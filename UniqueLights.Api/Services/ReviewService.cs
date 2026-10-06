using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.DTOs.Reviews;
using UniqueLights.Api.Entities;
using UniqueLights.Api.Exceptions;
using UniqueLights.Api.Services.Common;

namespace UniqueLights.Api.Services;

public interface IReviewService
{
    Task<PagedResult<ReviewDto>> GetForProductAsync(Guid productId, PagingQuery paging, Guid? currentUserId, CancellationToken ct);
    Task<ReviewSummaryDto> GetSummaryAsync(Guid productId, CancellationToken ct);
    Task<ReviewDto> CreateAsync(Guid userId, CreateReviewRequest request, CancellationToken ct);
    Task DeleteAsync(Guid reviewId, Guid userId, bool isAdmin, CancellationToken ct);
}

public sealed class ReviewService(AppDbContext db) : IReviewService
{
    private const string AlreadyReviewed = "You have already reviewed this product";

    private IQueryable<Review> VisibleReviews(Guid productId) =>
        db.Reviews.AsNoTracking().Where(r => r.ProductId == productId && r.IsApproved);

    public async Task<PagedResult<ReviewDto>> GetForProductAsync(Guid productId, PagingQuery paging, Guid? currentUserId, CancellationToken ct)
    {
        var query = VisibleReviews(productId);
        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip(paging.Skip).Take(paging.PageSize)
            .Select(r => new
            {
                r.Id, r.ProductId, r.Rating, r.Title, r.Comment, r.IsVerifiedPurchase, r.CreatedAt, r.UserId,
                r.User.FirstName, r.User.LastName
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new ReviewDto
        {
            Id = r.Id,
            ProductId = r.ProductId,
            Rating = r.Rating,
            Title = r.Title,
            Comment = r.Comment,
            AuthorName = Mappers.AuthorName(r.FirstName, r.LastName),
            IsVerifiedPurchase = r.IsVerifiedPurchase,
            CreatedAt = r.CreatedAt,
            IsMine = currentUserId.HasValue && r.UserId == currentUserId.Value
        }).ToList();

        return PagedResult<ReviewDto>.Create(items, paging.Page, paging.PageSize, total);
    }

    public async Task<ReviewSummaryDto> GetSummaryAsync(Guid productId, CancellationToken ct)
    {
        var counts = await VisibleReviews(productId)
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var distribution = Enumerable.Range(1, 5).ToDictionary(
            i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            i => counts.Where(c => c.Rating == i).Sum(c => c.Count));

        var total = distribution.Values.Sum();
        var average = total == 0 ? 0m : Pricing.Round(counts.Sum(c => (decimal)c.Rating * c.Count) / total);

        return new ReviewSummaryDto { AverageRating = average, ReviewCount = total, Distribution = distribution };
    }

    public async Task<ReviewDto> CreateAsync(Guid userId, CreateReviewRequest request, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId && p.IsActive, ct)
                      ?? throw new NotFoundException("Product not found");

        if (await db.Reviews.AnyAsync(r => r.ProductId == product.Id && r.UserId == userId, ct))
        {
            throw new ConflictException(AlreadyReviewed);
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new UnauthorizedException();

        var verified = await db.OrderItems.AnyAsync(i =>
            i.ProductId == product.Id && i.Order.UserId == userId && i.Order.Status != OrderStatus.Cancelled, ct);

        var review = new Review
        {
            ProductId = product.Id,
            UserId = userId,
            Rating = (short)request.Rating,
            Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
            IsApproved = true,
            IsVerifiedPurchase = verified
        };
        db.Reviews.Add(review);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException(AlreadyReviewed);
        }

        await RecalculateProductRatingAsync(product.Id, ct);

        return new ReviewDto
        {
            Id = review.Id,
            ProductId = review.ProductId,
            Rating = review.Rating,
            Title = review.Title,
            Comment = review.Comment,
            AuthorName = Mappers.AuthorName(user.FirstName, user.LastName),
            IsVerifiedPurchase = review.IsVerifiedPurchase,
            CreatedAt = review.CreatedAt,
            IsMine = true
        };
    }

    public async Task DeleteAsync(Guid reviewId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct)
                     ?? throw new NotFoundException("Review not found");
        if (review.UserId != userId && !isAdmin) throw new ForbiddenException("You can only delete your own reviews");

        review.MarkDeleted();
        await db.SaveChangesAsync(ct);
        await RecalculateProductRatingAsync(review.ProductId, ct);
    }

    /// <summary>Recomputes products.average_rating / review_count from approved, non-deleted reviews in one statement.</summary>
    private async Task RecalculateProductRatingAsync(Guid productId, CancellationToken ct)
    {
        var stats = await db.Reviews.AsNoTracking()
            .Where(r => r.ProductId == productId && r.IsApproved)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Average = g.Average(r => (decimal)r.Rating) })
            .FirstOrDefaultAsync(ct);

        var count = stats?.Count ?? 0;
        var average = stats is null ? 0m : Pricing.Round(stats.Average);

        await db.Products.IgnoreQueryFilters()
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.AverageRating, average)
                .SetProperty(p => p.ReviewCount, count), ct);
    }
}

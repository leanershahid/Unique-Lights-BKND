using Microsoft.EntityFrameworkCore;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.DTOs.Orders;
using UniqueLights.Api.Entities;

namespace UniqueLights.Api.Repositories;

public interface IOrderRepository
{
    /// <summary>Loads an order aggregate (items, payment, user) by number. Optionally restricted to an owner.</summary>
    Task<Order?> GetByNumberAsync(string orderNumber, Guid? ownerId, bool tracked, CancellationToken ct);

    Task<PagedResult<OrderSummaryDto>> GetSummariesAsync(IQueryable<Order> query, int page, int pageSize, bool includeCustomer, CancellationToken ct);

    Task<List<OrderSummaryDto>> GetRecentSummariesAsync(IQueryable<Order> query, int take, bool includeCustomer, CancellationToken ct);

    IQueryable<Order> Query();
}

public sealed class OrderRepository(AppDbContext db) : IOrderRepository
{
    public IQueryable<Order> Query() => db.Orders.AsNoTracking();

    public Task<Order?> GetByNumberAsync(string orderNumber, Guid? ownerId, bool tracked, CancellationToken ct)
    {
        IQueryable<Order> query = db.Orders
            .Include(o => o.Items)
            .Include(o => o.Payment)
            .Include(o => o.User)
            .AsSplitQuery();

        if (!tracked) query = query.AsNoTracking();
        if (ownerId.HasValue)
        {
            var owner = ownerId.Value;
            query = query.Where(o => o.UserId == owner);
        }

        var normalized = orderNumber.Trim().ToUpperInvariant();
        // IgnoreQueryFilters on User: a soft-deleted customer's orders must still be loadable by admins.
        return query.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.OrderNumber == normalized, ct);
    }

    private static IQueryable<OrderSummaryDto> Project(IQueryable<Order> query, bool includeCustomer) =>
        query.Select(o => new OrderSummaryDto
        {
            Id = o.Id,
            OrderNumber = o.OrderNumber,
            Status = o.Status,
            Total = o.Total,
            ItemCount = o.Items.Sum(i => i.Quantity),
            PlacedAt = o.PlacedAt,
            PreviewImages = o.Items
                .Where(i => i.ImageUrl != null)
                .OrderBy(i => i.ProductName)
                .Select(i => i.ImageUrl!)
                .Take(3)
                .ToList(),
            CustomerName = includeCustomer ? o.User.FirstName + " " + o.User.LastName : null,
            CustomerEmail = includeCustomer ? o.User.Email : null
        });

    public async Task<PagedResult<OrderSummaryDto>> GetSummariesAsync(IQueryable<Order> query, int page, int pageSize, bool includeCustomer, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = total == 0
            ? []
            : await Project(query.OrderByDescending(o => o.PlacedAt).ThenBy(o => o.Id).Skip((page - 1) * pageSize).Take(pageSize), includeCustomer)
                .ToListAsync(ct);
        return PagedResult<OrderSummaryDto>.Create(items, page, pageSize, total);
    }

    public Task<List<OrderSummaryDto>> GetRecentSummariesAsync(IQueryable<Order> query, int take, bool includeCustomer, CancellationToken ct) =>
        Project(query.OrderByDescending(o => o.PlacedAt).ThenBy(o => o.Id).Take(take), includeCustomer).ToListAsync(ct);
}

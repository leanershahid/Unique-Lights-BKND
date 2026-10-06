namespace UniqueLights.Api.DTOs.Reviews;

public sealed class ReviewDto
{
    public Guid Id { get; init; }
    public Guid ProductId { get; init; }
    public int Rating { get; init; }
    public string? Title { get; init; }
    public string? Comment { get; init; }
    public string AuthorName { get; init; } = string.Empty;
    public bool IsVerifiedPurchase { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool IsMine { get; init; }
}

public sealed class ReviewSummaryDto
{
    public decimal AverageRating { get; init; }
    public int ReviewCount { get; init; }
    public Dictionary<string, int> Distribution { get; init; } = new();
}

public sealed class CreateReviewRequest
{
    public Guid ProductId { get; init; }
    public int Rating { get; init; }
    public string? Title { get; init; }
    public string? Comment { get; init; }
}

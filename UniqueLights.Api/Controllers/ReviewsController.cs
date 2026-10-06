using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.DTOs.Reviews;
using UniqueLights.Api.Extensions;
using UniqueLights.Api.Services;

namespace UniqueLights.Api.Controllers;

[Route("api/reviews")]
public sealed class ReviewsController(IReviewService reviews) : ApiControllerBase
{
    /// <summary>Auth optional: when a valid Bearer token is sent, isMine is populated.</summary>
    [HttpGet("product/{productId:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<PagedResult<ReviewDto>>>> GetForProduct(
        Guid productId, [FromQuery] PagingQuery paging, CancellationToken ct) =>
        OkResponse(await reviews.GetForProductAsync(productId, paging, User.TryGetUserId(), ct));

    [HttpGet("product/{productId:guid}/summary")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<ReviewSummaryDto>>> Summary(Guid productId, CancellationToken ct) =>
        OkResponse(await reviews.GetSummaryAsync(productId, ct));

    [HttpPost]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<ReviewDto>>> Create(CreateReviewRequest request, CancellationToken ct) =>
        CreatedResponse(await reviews.CreateAsync(CurrentUserId, request, ct), "Thanks for your review");

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken ct)
    {
        await reviews.DeleteAsync(id, CurrentUserId, User.IsAdmin(), ct);
        return OkEmpty("Review deleted");
    }
}

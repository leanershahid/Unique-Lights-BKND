using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.DTOs.Orders;
using UniqueLights.Api.Services;

namespace UniqueLights.Api.Controllers;

[Route("api/orders")]
[Authorize]
public sealed class OrdersController(IOrderService orders) : ApiControllerBase
{
    [HttpPost("quote")]
    public async Task<ActionResult<ApiResponse<CheckoutQuoteDto>>> Quote(CheckoutQuoteRequest request, CancellationToken ct) =>
        OkResponse(await orders.QuoteAsync(CurrentUserId, request, ct));

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Create(CreateOrderRequest request, CancellationToken ct) =>
        CreatedResponse(await orders.CreateAsync(CurrentUserId, request, ct), "Order placed");

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderSummaryDto>>>> List([FromQuery] PagingQuery paging, CancellationToken ct) =>
        OkResponse(await orders.GetMyOrdersAsync(CurrentUserId, paging, ct));

    [HttpGet("{orderNumber}")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Get(string orderNumber, CancellationToken ct) =>
        OkResponse(await orders.GetMyOrderAsync(CurrentUserId, orderNumber, ct));

    [HttpPost("{orderNumber}/cancel")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Cancel(string orderNumber, CancellationToken ct) =>
        OkResponse(await orders.CancelMyOrderAsync(CurrentUserId, orderNumber, ct), "Order cancelled");
}

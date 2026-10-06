using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.DTOs.Shopping;
using UniqueLights.Api.Services;

namespace UniqueLights.Api.Controllers;

[Route("api/cart")]
[Authorize]
public sealed class CartController(ICartService cart) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<CartDto>>> Get(CancellationToken ct) =>
        OkResponse(await cart.GetAsync(CurrentUserId, ct));

    [HttpPost("items")]
    public async Task<ActionResult<ApiResponse<CartDto>>> AddItem(AddCartItemRequest request, CancellationToken ct) =>
        OkResponse(await cart.AddItemAsync(CurrentUserId, request, ct), "Added to cart");

    [HttpPatch("items/{itemId:guid}")]
    public async Task<ActionResult<ApiResponse<CartDto>>> UpdateItem(Guid itemId, UpdateCartItemRequest request, CancellationToken ct) =>
        OkResponse(await cart.UpdateItemAsync(CurrentUserId, itemId, request, ct));

    [HttpDelete("items/{itemId:guid}")]
    public async Task<ActionResult<ApiResponse<CartDto>>> RemoveItem(Guid itemId, CancellationToken ct) =>
        OkResponse(await cart.RemoveItemAsync(CurrentUserId, itemId, ct), "Removed from cart");

    [HttpDelete]
    public async Task<ActionResult<ApiResponse<CartDto>>> Clear(CancellationToken ct) =>
        OkResponse(await cart.ClearAsync(CurrentUserId, ct), "Cart cleared");

    [HttpPost("merge")]
    public async Task<ActionResult<ApiResponse<CartDto>>> Merge(MergeCartRequest request, CancellationToken ct) =>
        OkResponse(await cart.MergeAsync(CurrentUserId, request, ct));
}

[Route("api/wishlist")]
[Authorize]
public sealed class WishlistController(IWishlistService wishlist) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<WishlistDto>>> Get(CancellationToken ct) =>
        OkResponse(await wishlist.GetAsync(CurrentUserId, ct));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<WishlistDto>>> Add(AddWishlistItemRequest request, CancellationToken ct) =>
        OkResponse(await wishlist.AddAsync(CurrentUserId, request.ProductId, ct), "Saved to wishlist");

    [HttpDelete("{productId:guid}")]
    public async Task<ActionResult<ApiResponse<WishlistDto>>> Remove(Guid productId, CancellationToken ct) =>
        OkResponse(await wishlist.RemoveAsync(CurrentUserId, productId, ct), "Removed from wishlist");
}

[Route("api/coupons")]
[Authorize]
public sealed class CouponsController(ICouponService coupons) : ApiControllerBase
{
    [HttpPost("validate")]
    public async Task<ActionResult<ApiResponse<CouponValidationDto>>> Validate(ValidateCouponRequest request, CancellationToken ct) =>
        OkResponse(await coupons.ValidateAsync(request, ct), "Coupon applied");
}

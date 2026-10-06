using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniqueLights.Api.Authentication;
using UniqueLights.Api.DTOs.Admin;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.DTOs.Orders;
using UniqueLights.Api.Services;
using UniqueLights.Api.Services.Admin;

namespace UniqueLights.Api.Controllers.Admin;

[Authorize(Roles = Roles.Admin)]
public abstract class AdminControllerBase : ApiControllerBase;

[Authorize(Roles = Roles.Admin)]
[Route("api/admin/dashboard")]
public sealed class AdminDashboardController(IDashboardService dashboard) : AdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<DashboardDto>>> Get(CancellationToken ct) =>
        OkResponse(await dashboard.GetAsync(ct));
}

[Authorize(Roles = Roles.Admin)]
[Route("api/admin/products")]
public sealed class AdminProductsController(IAdminProductService products) : AdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminProductListItemDto>>>> List([FromQuery] AdminProductQuery query, CancellationToken ct) =>
        OkResponse(await products.SearchAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminProductDetailDto>>> Get(Guid id, CancellationToken ct) =>
        OkResponse(await products.GetAsync(id, ct));

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<AdminProductDetailDto>>> Create(UpsertProductRequest request, CancellationToken ct) =>
        CreatedResponse(await products.CreateAsync(request, ct), "Product created");

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminProductDetailDto>>> Update(Guid id, UpsertProductRequest request, CancellationToken ct) =>
        OkResponse(await products.UpdateAsync(id, request, ct), "Product updated");

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return OkEmpty("Product deleted");
    }

    [HttpPost("{id:guid}/variants")]
    public async Task<ActionResult<ApiResponse<AdminProductDetailDto>>> AddVariant(Guid id, UpsertVariantRequest request, CancellationToken ct) =>
        OkResponse(await products.AddVariantAsync(id, request, ct), "Variant added");

    [HttpPut("{id:guid}/variants/{variantId:guid}")]
    public async Task<ActionResult<ApiResponse<AdminProductDetailDto>>> UpdateVariant(Guid id, Guid variantId, UpsertVariantRequest request, CancellationToken ct) =>
        OkResponse(await products.UpdateVariantAsync(id, variantId, request, ct), "Variant updated");

    [HttpDelete("{id:guid}/variants/{variantId:guid}")]
    public async Task<ActionResult<ApiResponse<AdminProductDetailDto>>> DeleteVariant(Guid id, Guid variantId, CancellationToken ct) =>
        OkResponse(await products.DeleteVariantAsync(id, variantId, ct), "Variant deleted");

    /// <summary>multipart/form-data: file (jpg/png/webp, max 5 MB), altText, isPrimary.</summary>
    [HttpPost("{id:guid}/images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<AdminProductDetailDto>>> UploadImage(Guid id, [FromForm] UploadImageRequest request, CancellationToken ct) =>
        OkResponse(await products.UploadImageAsync(id, request, ct), "Image uploaded");

    [HttpPost("{id:guid}/images/url")]
    public async Task<ActionResult<ApiResponse<AdminProductDetailDto>>> AddImageUrl(Guid id, AddImageUrlRequest request, CancellationToken ct) =>
        OkResponse(await products.AddImageUrlAsync(id, request, ct), "Image added");

    [HttpPatch("{id:guid}/images/{imageId:guid}")]
    public async Task<ActionResult<ApiResponse<AdminProductDetailDto>>> UpdateImage(Guid id, Guid imageId, UpdateImageRequest request, CancellationToken ct) =>
        OkResponse(await products.UpdateImageAsync(id, imageId, request, ct), "Image updated");

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    public async Task<ActionResult<ApiResponse<AdminProductDetailDto>>> DeleteImage(Guid id, Guid imageId, CancellationToken ct) =>
        OkResponse(await products.DeleteImageAsync(id, imageId, ct), "Image deleted");
}

[Authorize(Roles = Roles.Admin)]
[Route("api/admin/categories")]
public sealed class AdminCategoriesController(IAdminCategoryService categories) : AdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminCategoryDto>>>> List(CancellationToken ct) =>
        OkResponse(await categories.GetAllAsync(ct));

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<AdminCategoryDto>>> Create(UpsertCategoryRequest request, CancellationToken ct) =>
        CreatedResponse(await categories.CreateAsync(request, ct), "Category created");

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminCategoryDto>>> Update(Guid id, UpsertCategoryRequest request, CancellationToken ct) =>
        OkResponse(await categories.UpdateAsync(id, request, ct), "Category updated");

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, ct);
        return OkEmpty("Category deleted");
    }
}

[Authorize(Roles = Roles.Admin)]
[Route("api/admin/orders")]
public sealed class AdminOrdersController(IOrderService orders) : AdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderSummaryDto>>>> List([FromQuery] AdminOrderQuery query, CancellationToken ct) =>
        OkResponse(await orders.SearchAsync(query, ct));

    [HttpGet("{orderNumber}")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Get(string orderNumber, CancellationToken ct) =>
        OkResponse(await orders.GetAsync(orderNumber, ct));

    [HttpPatch("{orderNumber}/status")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> UpdateStatus(string orderNumber, UpdateOrderStatusRequest request, CancellationToken ct) =>
        OkResponse(await orders.UpdateStatusAsync(orderNumber, request, ct), "Order status updated");
}

[Authorize(Roles = Roles.Admin)]
[Route("api/admin/customers")]
public sealed class AdminCustomersController(IAdminCustomerService customers) : AdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminCustomerDto>>>> List([FromQuery] AdminCustomerQuery query, CancellationToken ct) =>
        OkResponse(await customers.SearchAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminCustomerDetailDto>>> Get(Guid id, CancellationToken ct) =>
        OkResponse(await customers.GetAsync(id, ct));

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ApiResponse<AdminCustomerDto>>> UpdateStatus(Guid id, UpdateCustomerStatusRequest request, CancellationToken ct) =>
        OkResponse(await customers.UpdateStatusAsync(id, request.IsActive, CurrentUserId, ct),
            request.IsActive ? "Customer activated" : "Customer deactivated");
}

[Authorize(Roles = Roles.Admin)]
[Route("api/admin/inventory")]
public sealed class AdminInventoryController(IInventoryService inventory) : AdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<InventoryItemDto>>>> List([FromQuery] InventoryQuery query, CancellationToken ct) =>
        OkResponse(await inventory.SearchAsync(query, ct));

    [HttpPatch("{variantId:guid}")]
    public async Task<ActionResult<ApiResponse<InventoryItemDto>>> Update(Guid variantId, UpdateInventoryRequest request, CancellationToken ct) =>
        OkResponse(await inventory.UpdateAsync(variantId, request, ct), "Inventory updated");
}

[Authorize(Roles = Roles.Admin)]
[Route("api/admin/coupons")]
public sealed class AdminCouponsController(ICouponService coupons) : AdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CouponDto>>>> List(CancellationToken ct) =>
        OkResponse(await coupons.GetAllAsync(ct));

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<CouponDto>>> Create(UpsertCouponRequest request, CancellationToken ct) =>
        CreatedResponse(await coupons.CreateAsync(request, ct), "Coupon created");

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CouponDto>>> Update(Guid id, UpsertCouponRequest request, CancellationToken ct) =>
        OkResponse(await coupons.UpdateAsync(id, request, ct), "Coupon updated");

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken ct)
    {
        await coupons.DeleteAsync(id, ct);
        return OkEmpty("Coupon deleted");
    }
}

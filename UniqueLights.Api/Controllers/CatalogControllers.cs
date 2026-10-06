using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniqueLights.Api.DTOs.Catalog;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.Services;

namespace UniqueLights.Api.Controllers;

[Route("api/categories")]
[AllowAnonymous]
public sealed class CategoriesController(ICategoryService categories) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CategoryDto>>>> GetTree(CancellationToken ct) =>
        OkResponse(await categories.GetTreeAsync(ct));

    [HttpGet("{slug}")]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> GetBySlug(string slug, CancellationToken ct) =>
        OkResponse(await categories.GetBySlugAsync(slug, ct));
}

[Route("api/products")]
[AllowAnonymous]
public sealed class ProductsController(IProductService products) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductListItemDto>>>> Search(
        [FromQuery] ProductQueryParameters query, CancellationToken ct) =>
        OkResponse(await products.SearchAsync(query, ct));

    [HttpGet("filters")]
    public async Task<ActionResult<ApiResponse<ProductFiltersDto>>> Filters([FromQuery] string? category, CancellationToken ct) =>
        OkResponse(await products.GetFiltersAsync(category, ct));

    [HttpGet("suggest")]
    public async Task<ActionResult<ApiResponse<SearchSuggestionsDto>>> Suggest([FromQuery] string? q, CancellationToken ct) =>
        OkResponse(await products.SuggestAsync(q, ct));

    [HttpGet("{slug}")]
    public async Task<ActionResult<ApiResponse<ProductDetailDto>>> GetBySlug(string slug, CancellationToken ct) =>
        OkResponse(await products.GetBySlugAsync(slug, ct));

    [HttpGet("{slug}/related")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProductListItemDto>>>> Related(string slug, CancellationToken ct) =>
        OkResponse(await products.GetRelatedAsync(slug, ct));
}

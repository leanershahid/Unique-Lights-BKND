using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using UniqueLights.Api.Configurations;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.DTOs.Store;

namespace UniqueLights.Api.Controllers;

/// <summary>Public storefront settings, so the website never hard-codes prices, fees or delivery times.</summary>
[Route("api/store")]
[AllowAnonymous]
public sealed class StoreController(IOptions<StoreOptions> store, IOptions<CommerceOptions> commerce) : ApiControllerBase
{
    [HttpGet]
    [ResponseCache(Duration = 300)]
    public ActionResult<ApiResponse<StoreSettingsDto>> Get()
    {
        var s = store.Value;
        var c = commerce.Value;
        static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        return OkResponse(new StoreSettingsDto(
            s.Name, Clean(s.Tagline), c.Currency, c.TaxRate, c.FreeShippingThreshold, c.StandardShipping, c.ExpressShipping,
            c.StandardDeliveryDays, c.ExpressDeliveryDays, s.ReturnDays,
            Clean(s.SupportEmail), Clean(s.SupportPhone), Clean(s.Address), Clean(s.BusinessHours)));
    }
}

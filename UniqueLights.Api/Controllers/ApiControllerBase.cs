using Microsoft.AspNetCore.Mvc;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.Extensions;

namespace UniqueLights.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId => User.GetUserId();

    protected ActionResult<ApiResponse<T>> OkResponse<T>(T data, string? message = null) =>
        Ok(ApiResponse<T>.Ok(data, message));

    protected ActionResult<ApiResponse<T>> CreatedResponse<T>(T data, string? message = null) =>
        StatusCode(StatusCodes.Status201Created, ApiResponse<T>.Ok(data, message));

    /// <summary>200 with data: null (the API never uses 204).</summary>
    protected ActionResult<ApiResponse<object>> OkEmpty(string? message = null) =>
        Ok(ApiResponse<object>.Ok(null, message));
}

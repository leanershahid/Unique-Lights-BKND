using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniqueLights.Api.DTOs.Auth;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.DTOs.Customers;
using UniqueLights.Api.Services;

namespace UniqueLights.Api.Controllers;

[Route("api/customers/me")]
[Authorize]
public sealed class CustomersController(ICustomerService customers) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetProfile(CancellationToken ct) =>
        OkResponse(await customers.GetProfileAsync(CurrentUserId, ct));

    [HttpPut]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateProfile(UpdateProfileRequest request, CancellationToken ct) =>
        OkResponse(await customers.UpdateProfileAsync(CurrentUserId, request, ct), "Profile updated");

    [HttpPut("password")]
    public async Task<ActionResult<ApiResponse<object>>> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await customers.ChangePasswordAsync(CurrentUserId, request, ct);
        return OkEmpty("Password changed");
    }

    [HttpGet("addresses")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AddressDto>>>> GetAddresses(CancellationToken ct) =>
        OkResponse(await customers.GetAddressesAsync(CurrentUserId, ct));

    [HttpPost("addresses")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<AddressDto>>> CreateAddress(AddressRequest request, CancellationToken ct) =>
        CreatedResponse(await customers.CreateAddressAsync(CurrentUserId, request, ct), "Address added");

    [HttpPut("addresses/{id:guid}")]
    public async Task<ActionResult<ApiResponse<AddressDto>>> UpdateAddress(Guid id, AddressRequest request, CancellationToken ct) =>
        OkResponse(await customers.UpdateAddressAsync(CurrentUserId, id, request, ct), "Address updated");

    [HttpPatch("addresses/{id:guid}/default")]
    public async Task<ActionResult<ApiResponse<AddressDto>>> SetDefault(Guid id, CancellationToken ct) =>
        OkResponse(await customers.SetDefaultAddressAsync(CurrentUserId, id, ct), "Default address updated");

    [HttpDelete("addresses/{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteAddress(Guid id, CancellationToken ct)
    {
        await customers.DeleteAddressAsync(CurrentUserId, id, ct);
        return OkEmpty("Address deleted");
    }
}

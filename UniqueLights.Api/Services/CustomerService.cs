using Microsoft.EntityFrameworkCore;
using UniqueLights.Api.Authentication;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Auth;
using UniqueLights.Api.DTOs.Customers;
using UniqueLights.Api.Entities;
using UniqueLights.Api.Exceptions;
using UniqueLights.Api.Services.Common;

namespace UniqueLights.Api.Services;

public interface ICustomerService
{
    Task<UserDto> GetProfileAsync(Guid userId, CancellationToken ct);
    Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct);
    Task<IReadOnlyList<AddressDto>> GetAddressesAsync(Guid userId, CancellationToken ct);
    Task<AddressDto> CreateAddressAsync(Guid userId, AddressRequest request, CancellationToken ct);
    Task<AddressDto> UpdateAddressAsync(Guid userId, Guid addressId, AddressRequest request, CancellationToken ct);
    Task<AddressDto> SetDefaultAddressAsync(Guid userId, Guid addressId, CancellationToken ct);
    Task DeleteAddressAsync(Guid userId, Guid addressId, CancellationToken ct);
}

public sealed class CustomerService(AppDbContext db, IAuthService auth, IPasswordHashingService passwords) : ICustomerService
{
    private const int MaxAddresses = 20;

    public Task<UserDto> GetProfileAsync(Guid userId, CancellationToken ct) => auth.GetCurrentUserAsync(userId, ct);

    public async Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await GetUserAsync(userId, ct);
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        await db.SaveChangesAsync(ct);
        return user.ToDto(await auth.GetRolesAsync(userId, ct));
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await GetUserAsync(userId, ct);
        if (!passwords.Verify(user, request.CurrentPassword).Valid)
        {
            throw new ValidationException("currentPassword", "Current password is incorrect.");
        }

        user.PasswordHash = passwords.Hash(user, request.NewPassword);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AddressDto>> GetAddressesAsync(Guid userId, CancellationToken ct)
    {
        var addresses = await db.Addresses.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
        return addresses.Select(a => a.ToDto()).ToList();
    }

    public async Task<AddressDto> CreateAddressAsync(Guid userId, AddressRequest request, CancellationToken ct)
    {
        var existing = await db.Addresses.Where(a => a.UserId == userId).ToListAsync(ct);
        if (existing.Count >= MaxAddresses)
        {
            throw new BusinessRuleException($"You can save at most {MaxAddresses} addresses.");
        }

        var address = new Address { UserId = userId };
        Apply(address, request);
        address.IsDefault = request.IsDefault || existing.Count == 0;
        if (address.IsDefault) existing.ForEach(a => a.IsDefault = false);

        db.Addresses.Add(address);
        await db.SaveChangesAsync(ct);
        return address.ToDto();
    }

    public async Task<AddressDto> UpdateAddressAsync(Guid userId, Guid addressId, AddressRequest request, CancellationToken ct)
    {
        var addresses = await db.Addresses.Where(a => a.UserId == userId).ToListAsync(ct);
        var address = addresses.FirstOrDefault(a => a.Id == addressId) ?? throw new NotFoundException("Address not found");

        Apply(address, request);
        if (request.IsDefault)
        {
            MakeDefault(addresses, address);
        }
        else if (address.IsDefault)
        {
            // Un-defaulting: hand the default flag to the most recent other address, if any.
            var next = addresses.Where(a => a.Id != address.Id).OrderByDescending(a => a.CreatedAt).FirstOrDefault();
            if (next is not null)
            {
                address.IsDefault = false;
                next.IsDefault = true;
            }
        }

        await db.SaveChangesAsync(ct);
        return address.ToDto();
    }

    public async Task<AddressDto> SetDefaultAddressAsync(Guid userId, Guid addressId, CancellationToken ct)
    {
        var addresses = await db.Addresses.Where(a => a.UserId == userId).ToListAsync(ct);
        var address = addresses.FirstOrDefault(a => a.Id == addressId) ?? throw new NotFoundException("Address not found");
        MakeDefault(addresses, address);
        await db.SaveChangesAsync(ct);
        return address.ToDto();
    }

    public async Task DeleteAddressAsync(Guid userId, Guid addressId, CancellationToken ct)
    {
        var addresses = await db.Addresses.Where(a => a.UserId == userId).ToListAsync(ct);
        var address = addresses.FirstOrDefault(a => a.Id == addressId) ?? throw new NotFoundException("Address not found");

        address.MarkDeleted();
        if (address.IsDefault)
        {
            address.IsDefault = false;
            var next = addresses.Where(a => a.Id != address.Id).OrderByDescending(a => a.CreatedAt).FirstOrDefault();
            if (next is not null) next.IsDefault = true;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<User> GetUserAsync(Guid userId, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User not found");

    private static void MakeDefault(List<Address> addresses, Address target)
    {
        foreach (var a in addresses) a.IsDefault = a.Id == target.Id;
    }

    private static void Apply(Address address, AddressRequest r)
    {
        address.Label = string.IsNullOrWhiteSpace(r.Label) ? null : r.Label.Trim();
        address.FullName = r.FullName.Trim();
        address.Phone = r.Phone.Trim();
        address.Line1 = r.Line1.Trim();
        address.Line2 = string.IsNullOrWhiteSpace(r.Line2) ? null : r.Line2.Trim();
        address.City = r.City.Trim();
        address.State = r.State.Trim();
        address.PostalCode = r.PostalCode.Trim();
        address.Country = string.IsNullOrWhiteSpace(r.Country) ? "India" : r.Country.Trim();
    }
}

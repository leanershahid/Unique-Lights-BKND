using Microsoft.EntityFrameworkCore;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Admin;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.Entities;
using UniqueLights.Api.Exceptions;
using UniqueLights.Api.Repositories;
using UniqueLights.Api.Services.Common;

namespace UniqueLights.Api.Services.Admin;

public interface IAdminCustomerService
{
    Task<PagedResult<AdminCustomerDto>> SearchAsync(AdminCustomerQuery query, CancellationToken ct);
    Task<AdminCustomerDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task<AdminCustomerDto> UpdateStatusAsync(Guid id, bool isActive, Guid actingAdminId, CancellationToken ct);
}

public sealed class AdminCustomerService(AppDbContext db, IOrderRepository orders, IAuthService auth) : IAdminCustomerService
{
    private static IQueryable<AdminCustomerDto> Project(IQueryable<User> query) =>
        query.Select(u => new AdminCustomerDto
        {
            Id = u.Id,
            Email = u.Email,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Phone = u.Phone,
            IsActive = u.IsActive,
            Roles = u.UserRoles.Select(ur => ur.Role.Name).OrderBy(n => n).ToList(),
            OrdersCount = u.Orders.Count(),
            TotalSpent = u.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => (decimal?)o.Total) ?? 0m,
            CreatedAt = u.CreatedAt,
            LastOrderAt = u.Orders.Max(o => (DateTime?)o.PlacedAt)
        });

    public async Task<PagedResult<AdminCustomerDto>> SearchAsync(AdminCustomerQuery q, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = SearchPattern.Contains(q.Search);
            const string esc = SearchPattern.EscapeCharacter;
            query = query.Where(u =>
                EF.Functions.ILike(u.Email, pattern, esc) ||
                EF.Functions.ILike(u.FirstName + " " + u.LastName, pattern, esc) ||
                (u.Phone != null && EF.Functions.ILike(u.Phone, pattern, esc)));
        }

        var total = await query.CountAsync(ct);
        var items = await Project(query.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id).Skip(q.Skip).Take(q.PageSize))
            .ToListAsync(ct);
        return PagedResult<AdminCustomerDto>.Create(items, q.Page, q.PageSize, total);
    }

    public async Task<AdminCustomerDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var customer = await Project(db.Users.AsNoTracking().Where(u => u.Id == id)).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException("Customer not found");

        var addresses = await db.Addresses.AsNoTracking()
            .Where(a => a.UserId == id)
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        var recentOrders = await orders.GetRecentSummariesAsync(orders.Query().Where(o => o.UserId == id), 10, includeCustomer: true, ct);

        return new AdminCustomerDetailDto
        {
            Id = customer.Id,
            Email = customer.Email,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            Phone = customer.Phone,
            IsActive = customer.IsActive,
            Roles = customer.Roles,
            OrdersCount = customer.OrdersCount,
            TotalSpent = customer.TotalSpent,
            CreatedAt = customer.CreatedAt,
            LastOrderAt = customer.LastOrderAt,
            Addresses = addresses.Select(a => a.ToDto()).ToList(),
            RecentOrders = recentOrders
        };
    }

    public async Task<AdminCustomerDto> UpdateStatusAsync(Guid id, bool isActive, Guid actingAdminId, CancellationToken ct)
    {
        if (id == actingAdminId && !isActive)
        {
            throw new BusinessRuleException("You cannot deactivate your own account");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("Customer not found");
        user.IsActive = isActive;
        await db.SaveChangesAsync(ct);

        if (!isActive)
        {
            // Kill existing sessions; access tokens expire within AccessTokenMinutes.
            await auth.RevokeAllAsync(id, ct);
        }

        return await Project(db.Users.AsNoTracking().Where(u => u.Id == id)).FirstAsync(ct);
    }
}

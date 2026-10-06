using System.Globalization;
using Microsoft.EntityFrameworkCore;
using UniqueLights.Api.Authentication;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Admin;
using UniqueLights.Api.Entities;
using UniqueLights.Api.Repositories;
using UniqueLights.Api.Services.Common;

namespace UniqueLights.Api.Services.Admin;

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken ct);
}

public sealed class DashboardService(AppDbContext db, IOrderRepository orders, IInventoryService inventory, TimeProvider time)
    : IDashboardService
{
    private const int Days = 30;

    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        var today = time.GetUtcNow().UtcDateTime.Date;
        var start = DateTime.SpecifyKind(today.AddDays(-(Days - 1)), DateTimeKind.Utc);
        var previousStart = start.AddDays(-Days);

        var valid = db.Orders.AsNoTracking().Where(o => o.Status != OrderStatus.Cancelled);

        var totals = await valid
            .GroupBy(_ => 1)
            .Select(g => new { Revenue = g.Sum(o => o.Total), Count = g.Count() })
            .FirstOrDefaultAsync(ct);
        var totalRevenue = totals?.Revenue ?? 0m;
        var ordersCount = totals?.Count ?? 0;

        var revenueLast30 = await valid.Where(o => o.PlacedAt >= start).SumAsync(o => (decimal?)o.Total, ct) ?? 0m;
        var revenuePrevious30 = await valid.Where(o => o.PlacedAt >= previousStart && o.PlacedAt < start)
            .SumAsync(o => (decimal?)o.Total, ct) ?? 0m;

        var customersCount = await db.Users.CountAsync(u => u.UserRoles.Any(r => r.RoleId == Roles.CustomerId), ct);
        var productsCount = await db.Products.CountAsync(ct);

        // Group by UTC calendar day in SQL.
        var daily = await valid
            .Where(o => o.PlacedAt >= start)
            .GroupBy(o => o.PlacedAt.Date)
            .Select(g => new { Day = g.Key, Revenue = g.Sum(o => o.Total), Orders = g.Count() })
            .ToListAsync(ct);
        var dailyLookup = daily.ToDictionary(d => d.Day.Date);

        var salesByDay = Enumerable.Range(0, Days)
            .Select(i => start.AddDays(i).Date)
            .Select(day => dailyLookup.TryGetValue(day, out var d)
                ? new SalesByDayDto(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), d.Revenue, d.Orders)
                : new SalesByDayDto(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 0m, 0))
            .ToList();

        var statusCounts = await db.Orders.AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var ordersByStatus = Enum.GetValues<OrderStatus>()
            .Select(s => new OrdersByStatusDto(s, statusCounts.FirstOrDefault(c => c.Status == s)?.Count ?? 0))
            .ToList();

        var topProducts = await db.OrderItems.AsNoTracking()
            .Where(i => i.Order.Status != OrderStatus.Cancelled)
            .GroupBy(i => i.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Name = g.Max(i => i.ProductName) ?? "",
                ImageUrl = g.Max(i => i.ImageUrl),
                Units = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.LineTotal)
            })
            .OrderByDescending(x => x.Units).ThenByDescending(x => x.Revenue)
            .Take(5)
            .ToListAsync(ct);

        var recentOrders = await orders.GetRecentSummariesAsync(orders.Query(), 5, includeCustomer: true, ct);
        var lowStock = await inventory.GetLowStockAsync(10, ct);

        return new DashboardDto
        {
            TotalRevenue = totalRevenue,
            OrdersCount = ordersCount,
            CustomersCount = customersCount,
            ProductsCount = productsCount,
            AverageOrderValue = ordersCount == 0 ? 0m : Pricing.Round(totalRevenue / ordersCount),
            RevenueLast30Days = revenueLast30,
            RevenuePrevious30Days = revenuePrevious30,
            SalesByDay = salesByDay,
            OrdersByStatus = ordersByStatus,
            TopProducts = topProducts.Select(t => new TopProductDto(t.ProductId, t.Name, t.ImageUrl, t.Units, t.Revenue)).ToList(),
            RecentOrders = recentOrders,
            LowStock = lowStock
        };
    }
}

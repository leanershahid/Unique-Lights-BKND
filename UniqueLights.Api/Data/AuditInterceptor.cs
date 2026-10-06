using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using UniqueLights.Api.Entities;

namespace UniqueLights.Api.Data;

/// <summary>Sets CreatedAt / UpdatedAt (UTC) on every save.</summary>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Apply(DbContext? context)
    {
        if (context is null) return;
        var now = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case BaseEntity entity when entry.State == EntityState.Added:
                    if (entity.CreatedAt == default) entity.CreatedAt = now;
                    entity.UpdatedAt = now;
                    break;
                case BaseEntity entity when entry.State == EntityState.Modified:
                    entity.UpdatedAt = now;
                    entry.Property(nameof(BaseEntity.CreatedAt)).IsModified = false;
                    break;
                case RefreshToken token when entry.State == EntityState.Added && token.CreatedAt == default:
                    token.CreatedAt = now;
                    break;
            }
        }
    }
}

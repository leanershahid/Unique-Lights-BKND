namespace UniqueLights.Api.Entities;

/// <summary>Base type for entities: app-generated Guid id + audit timestamps (set by AuditInterceptor).</summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Entities that are soft deleted. A global query filter hides rows where IsDeleted = true.</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}

public static class SoftDeleteExtensions
{
    public static void MarkDeleted(this ISoftDelete entity)
    {
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
    }
}

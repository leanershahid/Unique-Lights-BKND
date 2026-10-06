using System.Text.Json.Serialization;

namespace UniqueLights.Api.DTOs.Common;

/// <summary>Response envelope used by every endpoint (see docs/API.md).</summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceId { get; init; }

    public static ApiResponse<T> Ok(T? data, string? message = null) => new() { Success = true, Data = data, Message = message };
}

public static class ApiResponse
{
    public static ApiResponse<object> Fail(string message, string? traceId, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new() { Success = false, Data = null, Message = message, Errors = errors, TraceId = traceId };
}

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }

    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount) => new()
    {
        Items = items,
        Page = page,
        PageSize = pageSize,
        TotalCount = totalCount,
        TotalPages = pageSize <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
    };
}

/// <summary>Standard paging query (page default 1, pageSize default 12, max 100).</summary>
public class PagingQuery
{
    public const int DefaultPageSize = 12;
    public const int MaxPageSize = 100;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? DefaultPageSize : Math.Min(value, MaxPageSize);
    }

    public int Skip => (Page - 1) * PageSize;
}

public sealed record IdNameSlugDto(Guid Id, string Name, string Slug);

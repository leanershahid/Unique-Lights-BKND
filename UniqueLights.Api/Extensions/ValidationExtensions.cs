using System.Text;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace UniqueLights.Api.Extensions;

public static class ValidationExtensions
{
    public static IReadOnlyDictionary<string, string[]> ToCamelCaseDictionary(this IEnumerable<ValidationFailure> failures) =>
        failures
            .GroupBy(f => ToCamelCasePath(f.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray());

    public static IReadOnlyDictionary<string, string[]> ToCamelCaseDictionary(this ModelStateDictionary modelState) =>
        modelState
            .Where(kv => kv.Value is { Errors.Count: > 0 })
            .GroupBy(kv => ToCamelCasePath(kv.Key))
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(kv => kv.Value!.Errors)
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "The value is invalid." : e.ErrorMessage)
                    .Distinct()
                    .ToArray());

    /// <summary>"$.Items[0].VariantId" / "Items[0].VariantId" -> "items[0].variantId". Empty -> "request".</summary>
    public static string ToCamelCasePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "request";
        var trimmed = path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path.TrimStart('$');
        if (trimmed.Length == 0) return "request";

        var sb = new StringBuilder(trimmed.Length);
        var startOfSegment = true;
        foreach (var ch in trimmed)
        {
            if (startOfSegment && char.IsLetter(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                startOfSegment = false;
                continue;
            }
            sb.Append(ch);
            startOfSegment = ch == '.';
        }
        return sb.ToString();
    }
}

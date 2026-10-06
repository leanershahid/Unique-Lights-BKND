using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UniqueLights.Api.Services.Common;

public static class Pricing
{
    /// <summary>Round half away from zero to 2 decimals (money).</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Compare-at price only when it is actually higher than the selling price.</summary>
    public static decimal? EffectiveCompareAt(decimal price, decimal? compareAt) =>
        compareAt.HasValue && compareAt.Value > price ? compareAt : null;

    /// <summary>Integer discount percentage (1-99) when compareAt &gt; price, otherwise null.</summary>
    public static int? DiscountPercent(decimal price, decimal? compareAt)
    {
        if (!compareAt.HasValue || compareAt.Value <= 0 || compareAt.Value <= price) return null;
        var percent = (int)Math.Round((compareAt.Value - price) / compareAt.Value * 100m, MidpointRounding.AwayFromZero);
        return Math.Clamp(percent, 1, 99);
    }
}

public static partial class SlugHelper
{
    public static string Slugify(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }

        var slug = NonAlphanumeric().Replace(sb.ToString(), "-").Trim('-');
        slug = MultipleHyphens().Replace(slug, "-");
        if (slug.Length > 200) slug = slug[..200].Trim('-');
        return slug.Length == 0 ? "item" : slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultipleHyphens();
}

public static class SearchPattern
{
    public const string EscapeCharacter = "\\";

    /// <summary>Builds a "%term%" ILIKE pattern with %, _ and \ escaped.</summary>
    public static string Contains(string term)
    {
        var escaped = term.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    public static List<string> SplitList(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Take(50)
                .ToList();
}

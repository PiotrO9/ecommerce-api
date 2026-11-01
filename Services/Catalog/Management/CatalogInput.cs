using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using E_commerce_backend.Contracts.CatalogManagement;

namespace E_commerce_backend.Services.Catalog.Management;

public static partial class CatalogInput
{
    // Identical list is installed in PostgreSQL; do not replace it with btrim(value).
    public const string Whitespace = "\u0009\u000A\u000B\u000C\u000D\u0020\u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000";
    public static string Canonical(string text) => text.Trim(Whitespace.ToCharArray()).Normalize(NormalizationForm.FormC);
    public static string? Text(string? text, int max, string field, bool required = true)
    {
        var value = text is null ? null : Canonical(text);
        if (value is { Length: 0 }) value = null;
        if ((required && value is null) || value?.Length > max) throw new CatalogValidation("invalid_" + field, field);
        return value;
    }
    public static string Slug(string? slug, string name)
    {
        var value = slug is null ? GenerateSlug(name) : slug.Trim().ToLowerInvariant();
        if (value.Length is < 1 or > 160 || !SlugPattern().IsMatch(value)) throw new CatalogValidation("invalid_slug", "slug");
        return value;
    }
    public static string GenerateSlug(string name)
    {
        var value = new StringBuilder();
        foreach (var c in name.ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') value.Append(c);
            else if (value.Length > 0 && value[^1] != '-') value.Append('-');
        }
        return value.ToString().TrimEnd('-');
    }
    public static Guid[] Ids(Guid[]? ids, string field, int max, bool optional = false)
    {
        if (ids is null && !optional) throw new CatalogValidation("invalid_" + field, field);
        ids ??= [];
        if (ids.Length > max || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Length) throw new CatalogValidation("invalid_" + field, field);
        return ids.Order().ToArray();
    }
    public static VariantDraftRequest Variant(VariantDraftRequest? request)
    {
        if (request is null) throw new CatalogValidation("first_variant_required", "firstVariant");
        var urls = request.ImageUrls ?? [];
        if (urls.Length > 20 || urls.Any(u => u is null || u.Length > 2048 || !Uri.TryCreate(u, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || string.IsNullOrEmpty(uri.Host))) throw new CatalogValidation("invalid_image_urls", "imageUrls");
        return request with { Sku = Text(request.Sku, 100, "sku"), Description = Text(request.Description, 20000, "description", false),
            ShortDescription = Text(request.ShortDescription, 500, "shortDescription", false), ImageUrls = urls,
            AttributePairIds = Ids(request.AttributePairIds, "attributePairIds", 100, true) };
    }
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}

public sealed class CatalogValidation(string code, string? field = null, int status = 400) : Exception(code)
{
    public string Code { get; } = code;
    public string? Field { get; } = field;
    public int Status { get; } = status;
}

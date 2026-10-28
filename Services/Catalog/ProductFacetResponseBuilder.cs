using E_commerce_backend.Contracts.Catalog;

namespace E_commerce_backend.Services.Catalog;

public static class ProductFacetResponseBuilder
{
    public static ProductSearchFilterResponse[] Build(
        IReadOnlyCollection<(string Code, string DisplayName)> keys,
        IReadOnlyCollection<(string Code, string Value, int Count)> rows,
        IReadOnlyCollection<(string Code, string Value)> selectedAttributes)
    {
        var selectedCodes = selectedAttributes.Select(attribute => attribute.Code).Distinct().ToArray();
        var selectedValues = selectedAttributes.GroupBy(attribute => attribute.Code)
            .ToDictionary(group => group.Key,
                group => group.Select(attribute => attribute.Value).ToHashSet(StringComparer.Ordinal));
        var displayNames = keys.ToDictionary(key => key.Code, key => key.DisplayName);
        foreach (var code in selectedCodes)
        {
            displayNames.TryAdd(code, code);
        }

        var countsByCode = rows.GroupBy(row => row.Code)
            .ToDictionary(group => group.Key, group => group.ToDictionary(row => row.Value,
                row => row.Count, StringComparer.Ordinal));
        return displayNames
            .Select(key =>
            {
                var counts = countsByCode.GetValueOrDefault(key.Key) ??
                    new Dictionary<string, int>(StringComparer.Ordinal);
                var selected = selectedValues.GetValueOrDefault(key.Key) ??
                    new HashSet<string>(StringComparer.Ordinal);
                var values = counts.Keys.Concat(selected).Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(value => value, StringComparer.Ordinal)
                    .Select(value => new ProductSearchFilterValueResponse(value,
                        counts.GetValueOrDefault(value), selected.Contains(value)))
                    .ToArray();
                return new ProductSearchFilterResponse(key.Key, key.Value, values);
            })
            .Where(filter => filter.Values.Count > 0)
            .OrderBy(filter => filter.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(filter => filter.Code, StringComparer.Ordinal)
            .ToArray();
    }
}

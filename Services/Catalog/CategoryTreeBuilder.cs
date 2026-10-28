using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Entities;

namespace E_commerce_backend.Services.Catalog;

public static class CategoryTreeBuilder
{
    public static IReadOnlyList<CategoryPathItemResponse>? GetPath(
        IReadOnlyCollection<Category> categories, Guid categoryId)
    {
        var byId = categories.ToDictionary(category => category.Id);
        if (!byId.TryGetValue(categoryId, out var current))
        {
            return null;
        }

        var path = new List<CategoryPathItemResponse>();
        var visited = new HashSet<Guid>();
        while (current is not null && visited.Add(current.Id))
        {
            path.Add(new CategoryPathItemResponse(current.Id, current.DisplayName, current.Slug));
            current = current.ParentId is Guid parentId && byId.TryGetValue(parentId, out var parent)
                ? parent : null;
        }

        path.Reverse();
        return path;
    }

    public static IReadOnlySet<Guid>? GetBranchIds(IReadOnlyCollection<Category> categories, Guid rootId)
    {
        var trees = Build(categories, rootId);
        if (trees is null)
        {
            return null;
        }

        var ids = new HashSet<Guid>();
        var pending = new Stack<CategoryTreeResponse>(trees);
        while (pending.TryPop(out var category))
        {
            ids.Add(category.Id);
            foreach (var child in category.Children)
            {
                pending.Push(child);
            }
        }

        return ids;
    }

    public static IReadOnlyList<CategoryTreeResponse>? Build(
        IReadOnlyCollection<Category> categories, Guid? rootId)
    {
        var nodes = categories.ToDictionary(category => category.Id,
            category => new CategoryTreeResponse(
                category.Id, category.ParentId, category.DisplayName, category.Slug, []));

        if (rootId is Guid selectedId && !nodes.ContainsKey(selectedId))
        {
            return null;
        }

        var roots = new List<CategoryTreeResponse>();
        foreach (var category in categories
                     .OrderBy(category => category.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(category => category.Id))
        {
            var node = nodes[category.Id];
            if (category.ParentId is Guid parentId)
            {
                nodes[parentId].Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        return rootId is Guid id ? [nodes[id]] : roots;
    }
}

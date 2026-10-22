using E_commerce_backend.Entities;
using E_commerce_backend.Services.Catalog;

namespace E_commerce_backend.Tests.Unit;

public sealed class CategoryTreeBuilderTests
{
    private readonly Guid _aId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private readonly Guid _bId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private readonly Guid _cId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private readonly Guid _otherId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    [Fact]
    public void WithoutRootReturnsAllTrees()
    {
        var trees = CategoryTreeBuilder.Build(Categories(), null)!;

        Assert.Equal(2, trees.Count);
        Assert.Equal(_aId, trees[0].Id);
        Assert.Equal(_bId, Assert.Single(trees[0].Children).Id);
        Assert.Equal(_cId, Assert.Single(trees[0].Children[0].Children).Id);
        Assert.Empty(trees[0].Children[0].Children[0].Children);
        Assert.Equal(_otherId, trees[1].Id);
    }

    [Fact]
    public void SelectedMiddleCategoryReturnsOnlyItsSubtree()
    {
        var trees = CategoryTreeBuilder.Build(Categories(), _bId)!;

        var tree = Assert.Single(trees);
        Assert.Equal(_bId, tree.Id);
        Assert.Equal(_aId, tree.ParentId);
        Assert.Equal(_cId, Assert.Single(tree.Children).Id);
    }

    [Fact]
    public void UnknownCategoryReturnsNullAndEmptyCatalogReturnsEmptyList()
    {
        Assert.Null(CategoryTreeBuilder.Build(Categories(), Guid.NewGuid()));
        Assert.Empty(CategoryTreeBuilder.Build([], null)!);
    }

    private List<Category> Categories() =>
    [
        new() { Id = _cId, ParentId = _bId, DisplayName = "Kategoria C", Slug = "c" },
        new() { Id = _otherId, DisplayName = "Z inna", Slug = "z-inna" },
        new() { Id = _bId, ParentId = _aId, DisplayName = "Kategoria B", Slug = "b" },
        new() { Id = _aId, DisplayName = "Kategoria A", Slug = "a" }
    ];
}

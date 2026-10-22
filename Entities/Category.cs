namespace E_commerce_backend.Entities;

public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string DisplayName { get; set; }
    public required string Slug { get; set; }
    public Guid? ParentId { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();
    public ICollection<ProductCategory> ProductCategories { get; set; } = new List<ProductCategory>();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shop.Catalog.Products;

namespace Shop.Catalog.Data;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var category = modelBuilder.Entity<Category>();
        category.ToTable("categories");
        category.HasKey(c => c.Id);
        category.Property(c => c.Id).ValueGeneratedNever();
        category.Property(c => c.Name).HasMaxLength(60);
        category.Property(c => c.Slug).HasMaxLength(80);
        category.HasIndex(c => c.Slug).IsUnique();

        var product = modelBuilder.Entity<Product>();
        product.ToTable("products");
        product.HasKey(p => p.Id);
        product.Property(p => p.Id).ValueGeneratedNever();
        product.Property(p => p.Sku).HasMaxLength(32);
        product.Property(p => p.Name).HasMaxLength(Product.NameMaxLength);
        product.Property(p => p.Slug).HasMaxLength(120);
        product.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength);
        product.Property(p => p.Price).HasPrecision(12, 2);
        product.Property(p => p.Version).IsRowVersion();
        product.HasOne<Category>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        product.HasIndex(p => p.Sku).IsUnique();
        product.HasIndex(p => p.Slug).IsUnique();
        product.HasIndex(p => new { p.IsActive, p.CategoryId, p.Price });
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContext.Configure(options, "Host=localhost;Database=catalog;Username=design;Password=design");
        return new CatalogDbContext(options.Options);
    }
}

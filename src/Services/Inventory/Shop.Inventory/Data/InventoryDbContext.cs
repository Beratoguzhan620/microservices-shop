using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shop.Inventory.Stock;
using Shop.Messaging;

namespace Shop.Inventory.Data;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> StockItems => Set<StockItem>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var item = modelBuilder.Entity<StockItem>();
        item.ToTable("stock_items", table => table.HasCheckConstraint("ck_stock_items_counts", "reserved >= 0 AND on_hand >= reserved"));
        item.HasKey(i => i.ProductId);
        item.Property(i => i.ProductId).ValueGeneratedNever();
        item.Property(i => i.Sku).HasMaxLength(32);
        item.Property(i => i.Name).HasMaxLength(120);
        item.Property(i => i.Version).IsRowVersion();
        item.Ignore(i => i.Available);

        var reservation = modelBuilder.Entity<Reservation>();
        reservation.ToTable("reservations");
        reservation.HasKey(r => new { r.OrderId, r.ProductId });
        reservation.Property(r => r.Status).HasConversion<string>().HasMaxLength(10);
        reservation.HasOne<StockItem>().WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Restrict);
        reservation.HasIndex(r => new { r.ProductId, r.Status });

        modelBuilder.AddOutboxTables();
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>();
        InventoryDbContext.Configure(options, "Host=localhost;Database=inventory;Username=design;Password=design");
        return new InventoryDbContext(options.Options);
    }
}

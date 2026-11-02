using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Messaging;
using Shop.Ordering.Orders;
using Shop.Ordering.Saga;

namespace Shop.Ordering.Data;

public sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : SagaDbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override IEnumerable<ISagaClassMap> Configurations => [new OrderStateMap()];

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var order = modelBuilder.Entity<Order>();
        order.ToTable("orders");
        order.HasKey(o => o.Id);
        order.Property(o => o.Id).ValueGeneratedNever();
        order.Property(o => o.CustomerEmail).HasMaxLength(254);
        order.Property(o => o.CustomerName).HasMaxLength(60);
        order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        order.Property(o => o.Total).HasPrecision(12, 2);
        order.Property(o => o.Reason).HasMaxLength(300);
        order.HasIndex(o => new { o.CustomerId, o.CreatedAt });
        order.HasIndex(o => new { o.Status, o.CreatedAt });
        order.OwnsOne(o => o.ShippingAddress, address =>
        {
            address.Property(a => a.FullName).HasMaxLength(100);
            address.Property(a => a.Line1).HasMaxLength(200);
            address.Property(a => a.City).HasMaxLength(60);
            address.Property(a => a.PostalCode).HasMaxLength(5);
            address.Property(a => a.Phone).HasMaxLength(20);
        });
        order.OwnsMany(o => o.Items, item =>
        {
            item.ToTable("order_items");
            item.WithOwner().HasForeignKey("OrderId");
            item.HasKey("OrderId", nameof(OrderItem.ProductId));
            item.Property(i => i.Sku).HasMaxLength(32);
            item.Property(i => i.Name).HasMaxLength(120);
            item.Property(i => i.UnitPrice).HasPrecision(12, 2);
        });

        modelBuilder.AddOutboxTables();
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}

internal sealed class OrderStateMap : SagaClassMap<OrderState>
{
    protected override void Configure(EntityTypeBuilder<OrderState> entity, ModelBuilder model)
    {
        entity.ToTable("order_states");
        entity.Property(x => x.CurrentState).HasMaxLength(40);
        entity.Property(x => x.Total).HasPrecision(12, 2);
    }
}

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>();
        OrderingDbContext.Configure(options, "Host=localhost;Database=ordering;Username=design;Password=design");
        return new OrderingDbContext(options.Options);
    }
}

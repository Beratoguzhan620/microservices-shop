using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shop.Messaging;
using Shop.Payment.Payments;

namespace Shop.Payment.Data;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Payments.Payment> Payments => Set<Payments.Payment>();

    public DbSet<CardToken> CardTokens => Set<CardToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var payment = modelBuilder.Entity<Payments.Payment>();
        payment.ToTable("payments");
        payment.HasKey(p => p.Id);
        payment.Property(p => p.Id).ValueGeneratedNever();
        payment.Property(p => p.Amount).HasPrecision(12, 2);
        payment.Property(p => p.Status).HasConversion<string>().HasMaxLength(10);
        payment.Property(p => p.CardLast4).HasMaxLength(4);
        payment.Property(p => p.Reason).HasMaxLength(200);
        payment.HasIndex(p => p.OrderId).IsUnique();

        var token = modelBuilder.Entity<CardToken>();
        token.ToTable("card_tokens");
        token.HasKey(t => t.Token);
        token.Property(t => t.Token).HasMaxLength(40);
        token.Property(t => t.Last4).HasMaxLength(4);
        token.Property(t => t.Brand).HasMaxLength(12);
        token.Property(t => t.Behaviour).HasConversion<string>().HasMaxLength(20);

        modelBuilder.AddOutboxTables();
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>();
        PaymentDbContext.Configure(options, "Host=localhost;Database=payment;Username=design;Password=design");
        return new PaymentDbContext(options.Options);
    }
}

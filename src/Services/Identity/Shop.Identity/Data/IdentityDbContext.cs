using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shop.Identity.Users;

namespace Shop.Identity.Data;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var user = modelBuilder.Entity<User>();
        user.ToTable("users");
        user.HasKey(u => u.Id);
        user.Property(u => u.Id).ValueGeneratedNever();
        user.Property(u => u.Email).HasMaxLength(User.EmailMaxLength);
        user.Property(u => u.DisplayName).HasMaxLength(User.DisplayNameMaxLength);
        user.Property(u => u.PasswordHash).HasMaxLength(200);
        user.Property(u => u.Role).HasMaxLength(20);
        user.HasIndex(u => u.Email).IsUnique();
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}

/// <summary>Lets <c>dotnet ef</c> create migrations without a running database.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>();
        IdentityDbContext.Configure(options, "Host=localhost;Database=identity;Username=design;Password=design");
        return new IdentityDbContext(options.Options);
    }
}

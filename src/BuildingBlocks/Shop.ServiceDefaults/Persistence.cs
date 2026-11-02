using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Shop.ServiceDefaults;

public static class Persistence
{
    /// <summary>The service's own database; every service has one and no other service reads it.</summary>
    public static string ConnectionString(IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"ConnectionStrings:{name} is not configured. Use 'dotnet user-secrets' or the ConnectionStrings__{name} environment variable.");

    public static bool IsUniqueViolation(this DbUpdateException exception, string? constraint = null) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && (constraint is null || postgres.ConstraintName == constraint);
}

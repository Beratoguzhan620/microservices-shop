using Xunit;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Shop.Testing;

/// <summary>One PostgreSQL container per test assembly; every service under test gets its own database in it.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task<string> CreateDatabaseAsync(string name)
    {
        var database = $"{name}_{Guid.NewGuid():N}"[..(name.Length + 9)];
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;
    }

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

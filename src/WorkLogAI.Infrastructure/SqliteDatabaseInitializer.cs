using System.Reflection;
using Microsoft.Data.Sqlite;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed class SqliteDatabaseInitializer(SqliteConnectionFactory connectionFactory)
    : IDatabaseInitializer
{
    private static readonly (long Version, string Resource)[] Migrations =
    [
        (1, "WorkLogAI.Infrastructure.Migrations.001_initial.sql"),
        (2, "WorkLogAI.Infrastructure.Migrations.002_phase3_review.sql")
    ];

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var version = await ReadUserVersionAsync(connection, transaction, cancellationToken);
        foreach (var migration in Migrations.Where(item => item.Version > version))
        {
            var sql = ReadMigration(migration.Resource);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<long> ReadUserVersionAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    private static string ReadMigration(string resource)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded migration '{resource}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

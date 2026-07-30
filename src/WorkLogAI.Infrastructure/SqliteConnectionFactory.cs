using Microsoft.Data.Sqlite;

namespace WorkLogAI.Infrastructure;

public sealed class SqliteConnectionFactory(IDatabasePathProvider pathProvider)
{
    public string DatabasePath => pathProvider.GetDatabasePath();

    public SqliteConnection Create()
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = false
        };
        return new SqliteConnection(builder.ToString());
    }
}

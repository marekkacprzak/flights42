using Microsoft.Data.Sqlite;

namespace AiCsServer.Data;

public static class LibSqlDatabase
{
    private static readonly object Gate = new();
    private static bool _initialized;
    private static string _dbPath = "";

    public static string Backend => "Microsoft.Data.Sqlite";

    public static string DbPath => _dbPath;

    public static void Initialize(IConfiguration configuration, IWebHostEnvironment environment)
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            string? url = configuration["LIBSQL_URL"]
                ?? configuration["LibSql:Url"]
                ?? Environment.GetEnvironmentVariable("LIBSQL_URL");

            _dbPath = ResolveDbPath(url, environment.ContentRootPath);
            string? directory = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            EnsureSchemaSqlite();
            _initialized = true;
        }
    }

    private static string ResolveDbPath(string? url, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Path.GetFullPath(Path.Combine(contentRoot, "flights42.db"));
        }

        string trimmed = url.Trim();
        if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            string rest = trimmed["file:".Length..];
            int q = rest.IndexOf('?', StringComparison.Ordinal);
            if (q >= 0)
            {
                rest = rest[..q];
            }

            if (rest.StartsWith("//", StringComparison.Ordinal))
            {
                rest = rest[2..];
            }

            if (Path.IsPathRooted(rest))
            {
                return Path.GetFullPath(rest);
            }

            return Path.GetFullPath(Path.Combine(contentRoot, rest.TrimStart('.', '/', '\\')));
        }

        if (trimmed.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            string path = trimmed["Data Source=".Length..].Trim();
            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            return Path.GetFullPath(Path.Combine(contentRoot, path));
        }

        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        return Path.GetFullPath(Path.Combine(contentRoot, trimmed));
    }

    private static void EnsureSchemaSqlite()
    {
        using SqliteConnection connection = OpenSqlite();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS booked_flights (
              flight_id INTEGER PRIMARY KEY
            );
            CREATE TABLE IF NOT EXISTS travel_plans (
              session_key TEXT PRIMARY KEY,
              summary TEXT NOT NULL,
              payload_json TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS dashboard_cache (
              hash TEXT PRIMARY KEY,
              spec_json TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public static SqliteConnection OpenSqlite()
    {
        SqliteConnection connection = new($"Data Source={_dbPath}");
        connection.Open();
        return connection;
    }

    private static (string Sql, object[] Args) NormalizeSqlite(string sql, object[] args)
    {
        if (args.Length == 0)
        {
            return (sql, args);
        }

        string rewritten = sql;
        for (int i = 0; i < args.Length; i++)
        {
            int idx = rewritten.IndexOf('?', StringComparison.Ordinal);
            if (idx < 0)
            {
                break;
            }

            rewritten = rewritten[..idx] + $"$p{i}" + rewritten[(idx + 1)..];
        }

        return (rewritten, args);
    }

    public static async Task ExecuteAsync(string sql, params object[] args)
    {
        (string rewritten, object[] normalized) = NormalizeSqlite(sql, args);
        using SqliteConnection connection = OpenSqlite();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = rewritten;
        for (int i = 0; i < normalized.Length; i++)
        {
            command.Parameters.AddWithValue($"$p{i}", normalized[i] ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<object[]>> QueryAsync(string sql, params object[] args)
    {
        (string rewritten, object[] normalized) = NormalizeSqlite(sql, args);
        using SqliteConnection connection = OpenSqlite();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = rewritten;
        for (int i = 0; i < normalized.Length; i++)
        {
            command.Parameters.AddWithValue($"$p{i}", normalized[i] ?? DBNull.Value);
        }

        List<object[]> sqliteRows = new();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            object[] cells = new object[reader.FieldCount];
            reader.GetValues(cells);
            sqliteRows.Add(cells);
        }

        return sqliteRows;
    }
}

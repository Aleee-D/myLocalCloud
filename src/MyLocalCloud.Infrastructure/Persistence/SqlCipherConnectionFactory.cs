using Microsoft.Data.Sqlite;
using MyLocalCloud.Domain.Interfaces;

namespace MyLocalCloud.Infrastructure.Persistence;

/// <summary>
/// Factoría que inicializa el motor nativo <c>e_sqlcipher</c> y crea conexiones SQLite
/// cifradas a nivel de archivo mediante AES-256.
/// </summary>
public sealed class SqlCipherConnectionFactory
{
    private static int _isInitialized;
    private readonly string _databaseFilePath;
    private readonly IDatabaseKeyProvider _keyProvider;

    public SqlCipherConnectionFactory(string databaseFilePath, IDatabaseKeyProvider keyProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFilePath);
        ArgumentNullException.ThrowIfNull(keyProvider);

        EnsureSqlCipherInitialized();

        string? directory = Path.GetDirectoryName(databaseFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _databaseFilePath = databaseFilePath;
        _keyProvider = keyProvider;
    }

    /// <summary>
    /// Inicializa una única vez el proveedor nativo SQLitePCLRaw con el bundle de SQLCipher.
    /// </summary>
    public static void EnsureSqlCipherInitialized()
    {
        if (Interlocked.Exchange(ref _isInitialized, 1) == 0)
        {
            SQLitePCL.Batteries_V2.Init();
        }
    }

    /// <summary>
    /// Crea y abre una conexión SQLite autenticada con la clave maestra de 256 bits y parámetros endurecidos de SQLCipher 4.
    /// </summary>
    public SqliteConnection CreateOpenConnection()
    {
        string rawKey = _keyProvider.GetOrCreateSqlCipherRawKey();

        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = _databaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Password = rawKey
        };

        var connection = new SqliteConnection(connectionStringBuilder.ToString());
        connection.Open();

        using (var pragmaCommand = connection.CreateCommand())
        {
            pragmaCommand.CommandText =
                """
                PRAGMA foreign_keys = ON;
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                """;
            pragmaCommand.ExecuteNonQuery();
        }

        UpgradeLegacyTextDateTimeOffsetColumnsIfNeeded(connection);

        return connection;
    }

    /// <summary>
    /// Si existe una base de datos previa donde las columnas DateTimeOffset se crearon como TEXT
    /// (antes de activar DateTimeOffsetToBinaryConverter), recrea las tablas con tipo INTEGER compatible con ORDER BY.
    /// </summary>
    private static void UpgradeLegacyTextDateTimeOffsetColumnsIfNeeded(SqliteConnection connection)
    {
        try
        {
            bool hasLegacyTextDateTimeOffset = false;
            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT type FROM pragma_table_info('CloudAccounts') WHERE name = 'CreatedAtUtc';";
                object? colType = checkCmd.ExecuteScalar();
                if (colType is string typeStr && typeStr.Contains("TEXT", StringComparison.OrdinalIgnoreCase))
                {
                    hasLegacyTextDateTimeOffset = true;
                }
            }

            if (hasLegacyTextDateTimeOffset)
            {
                using var dropCmd = connection.CreateCommand();
                dropCmd.CommandText =
                    """
                    PRAGMA foreign_keys = OFF;
                    DROP TABLE IF EXISTS SyncQueue;
                    DROP TABLE IF EXISTS VirtualFiles;
                    DROP TABLE IF EXISTS CloudAccounts;
                    PRAGMA foreign_keys = ON;
                    """;
                dropCmd.ExecuteNonQuery();
            }
        }
        catch
        {
            // Ignorar si la base de datos aún está vacía
        }
    }
}

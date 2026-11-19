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

        UpgradeSchemaToV11IfNeeded(connection);

        return connection;
    }

    /// <summary>
    /// Actualiza automáticamente esquemas de versiones previas (v1.0 -> v1.1):
    /// 1) Convierte tablas con DateTimeOffset TEXT heredado si existieran.
    /// 2) Añade las columnas VirtualDriveLetter y DeliveryMode a CloudAccounts si aún no existen.
    /// </summary>
    private static void UpgradeSchemaToV11IfNeeded(SqliteConnection connection)
    {
        try
        {
            var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasLegacyTextDateTimeOffset = false;

            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT name, type FROM pragma_table_info('CloudAccounts');";
                using SqliteDataReader reader = checkCmd.ExecuteReader();
                while (reader.Read())
                {
                    string colName = reader.GetString(0);
                    string colType = reader.GetString(1);
                    existingColumns.Add(colName);

                    if (string.Equals(colName, "CreatedAtUtc", StringComparison.OrdinalIgnoreCase) &&
                        colType.Contains("TEXT", StringComparison.OrdinalIgnoreCase))
                    {
                        hasLegacyTextDateTimeOffset = true;
                    }
                }
            }

            if (existingColumns.Count == 0)
                return;

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
                return;
            }

            if (!existingColumns.Contains("VirtualDriveLetter"))
            {
                using var alterDriveCmd = connection.CreateCommand();
                alterDriveCmd.CommandText = "ALTER TABLE CloudAccounts ADD COLUMN VirtualDriveLetter TEXT NOT NULL DEFAULT 'M:';";
                alterDriveCmd.ExecuteNonQuery();
            }

            if (!existingColumns.Contains("DeliveryMode"))
            {
                using var alterModeCmd = connection.CreateCommand();
                alterModeCmd.CommandText = "ALTER TABLE CloudAccounts ADD COLUMN DeliveryMode INTEGER NOT NULL DEFAULT 0;";
                alterModeCmd.ExecuteNonQuery();
            }
        }
        catch
        {
            // Ignorar si la base de datos aún no tiene tablas creadas
        }
    }
}

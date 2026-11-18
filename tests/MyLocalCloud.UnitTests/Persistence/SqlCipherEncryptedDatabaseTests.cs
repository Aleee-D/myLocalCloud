using System.Runtime.Versioning;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MyLocalCloud.Domain.Entities;
using MyLocalCloud.Domain.Enums;
using MyLocalCloud.Domain.Models;
using MyLocalCloud.Infrastructure.Persistence;
using MyLocalCloud.Infrastructure.Security;
using Xunit;

namespace MyLocalCloud.UnitTests.Persistence;

[SupportedOSPlatform("windows")]
public sealed class SqlCipherEncryptedDatabaseTests : IDisposable
{
    private readonly string _tempWorkspaceDir;
    private readonly string _databaseFilePath;
    private readonly string _keyStorageDir;

    public SqlCipherEncryptedDatabaseTests()
    {
        _tempWorkspaceDir = Path.Combine(
            Path.GetTempPath(),
            "MyLocalCloud_Tests_" + Guid.NewGuid().ToString("N"));

        _databaseFilePath = Path.Combine(_tempWorkspaceDir, "mylocalcloud_vfs.db");
        _keyStorageDir = Path.Combine(_tempWorkspaceDir, "Security");
        Directory.CreateDirectory(_tempWorkspaceDir);
    }

    [Fact]
    public async Task DatabaseFile_IsFullyEncryptedWithAes256_And_CredentialsAreRecovered()
    {
        // Arrange
        var dpapiService = new DpapiDataProtectionService();
        var keyProvider = new DpapiDatabaseKeyProvider(dpapiService, _keyStorageDir);
        var connectionFactory = new SqlCipherConnectionFactory(_databaseFilePath, keyProvider);

        var secret = new CredentialSecret
        {
            AccessToken = "ya29.GoogleDrivePlaintextTokenCheck_MUST_NOT_APPEAR_ON_DISK",
            RefreshToken = "1//RefreshTokenPlaintextCheck_MUST_NOT_APPEAR_ON_DISK"
        };

        Guid accountId;

        // Act 1: Crear esquema, guardar cuenta y archivo virtual, y cerrar conexión
        await using (SqliteConnection openConnection = connectionFactory.CreateOpenConnection())
        {
            var options = new DbContextOptionsBuilder<MyLocalCloudDbContext>()
                .UseSqlite(openConnection)
                .Options;

            await using var dbContext = new MyLocalCloudDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var account = new CloudAccount
            {
                ProviderType = CloudProviderType.GoogleDrive,
                DisplayName = "Cuenta Corporativa UltraSecreta",
                AccountEmailOrUser = "arquitecto@corporacion-secreta.com",
                LocalSyncRootPath = @"C:\Users\Test\myLocalCloud\GoogleDrive",
                EncryptedCredentialBlob = dpapiService.ProtectSecret(secret),
                RemoteDeltaCursor = "StartPageToken_998877"
            };

            account.VirtualFiles.Add(new VirtualFileItem
            {
                RemoteItemId = "gdrive_file_id_12345",
                RelativeLocalPath = @"\Confidencial\Contrato_Adquisicion_2026.pdf",
                FileName = "Contrato_Adquisicion_2026.pdf",
                FileSizeBytes = 10485760,
                HydrationState = HydrationState.PlaceholderOnly,
                SyncState = FileSyncState.InSync
            });

            dbContext.CloudAccounts.Add(account);
            await dbContext.SaveChangesAsync();
            accountId = account.Id;
        }

        SqliteConnection.ClearAllPools();

        // Assert 1: Inspección forense del archivo físico .db en disco
        byte[] rawDbFileBytes = await File.ReadAllBytesAsync(_databaseFilePath);
        Assert.NotEmpty(rawDbFileBytes);

        // Un archivo SQLite estándar sin cifrar comienza con los 16 bytes "SQLite format 3\0".
        // Con SQLCipher AES-256, la cabecera es indistinguible de ruido aleatorio (sal criptográfica).
        string headerAscii = Encoding.ASCII.GetString(rawDbFileBytes.AsSpan(0, Math.Min(16, rawDbFileBytes.Length)));
        Assert.NotEqual("SQLite format 3\0", headerAscii);

        // Ningún metadato sensible ni token debe existir en texto plano dentro del archivo .db
        string rawFileText = Encoding.UTF8.GetString(rawDbFileBytes);
        Assert.DoesNotContain("arquitecto@corporacion-secreta.com", rawFileText, StringComparison.Ordinal);
        Assert.DoesNotContain("Contrato_Adquisicion_2026.pdf", rawFileText, StringComparison.Ordinal);
        Assert.DoesNotContain(secret.AccessToken, rawFileText, StringComparison.Ordinal);

        // Assert 2: Intentar abrir la base de datos sin la clave maestra o con clave errónea debe fallar (SQLITE_NOTADB = 26)
        var wrongConnString = new SqliteConnectionStringBuilder
        {
            DataSource = _databaseFilePath,
            Password = "ClaveIncorrecta123!"
        }.ToString();

        await using (var wrongConn = new SqliteConnection(wrongConnString))
        {
            var ex = await Assert.ThrowsAsync<SqliteException>(async () =>
            {
                await wrongConn.OpenAsync();
                await using var cmd = wrongConn.CreateCommand();
                cmd.CommandText = "SELECT count(*) FROM CloudAccounts;";
                await cmd.ExecuteScalarAsync();
            });
            Assert.Equal(26, ex.SqliteErrorCode); // SQLITE_NOTADB: File opened that is not a database file
        }

        // Act & Assert 3: Reabrir con la clave maestra legítima (descifrada por DPAPI) y recuperar el token OAuth2
        await using (SqliteConnection validConnection = connectionFactory.CreateOpenConnection())
        {
            var options = new DbContextOptionsBuilder<MyLocalCloudDbContext>()
                .UseSqlite(validConnection)
                .Options;

            await using var dbContext = new MyLocalCloudDbContext(options);
            CloudAccount loadedAccount = await dbContext.CloudAccounts
                .Include(a => a.VirtualFiles)
                .SingleAsync(a => a.Id == accountId);

            CredentialSecret decryptedSecret = dpapiService.UnprotectSecret<CredentialSecret>(loadedAccount.EncryptedCredentialBlob);

            Assert.Equal(secret.AccessToken, decryptedSecret.AccessToken);
            Assert.Equal(secret.RefreshToken, decryptedSecret.RefreshToken);
            Assert.Single(loadedAccount.VirtualFiles);

            // Verificar que SQLite + EF Core soporta ORDER BY y WHERE sobre propiedades DateTimeOffset
            // gracias a DateTimeOffsetToBinaryConverter sin lanzar System.NotSupportedException
            List<CloudAccount> orderedAccounts = await dbContext.CloudAccounts
                .OrderBy(a => a.CreatedAtUtc)
                .ThenByDescending(a => a.LastSyncedAtUtc)
                .ToListAsync();

            Assert.Single(orderedAccounts);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempWorkspaceDir))
        {
            try
            {
                Directory.Delete(_tempWorkspaceDir, recursive: true);
            }
            catch
            {
                // Ignorar bloqueos transitorios del sistema de archivos en limpieza de tests
            }
        }
    }
}

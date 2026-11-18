using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyLocalCloud.Domain.Entities;
using MyLocalCloud.Domain.Enums;
using MyLocalCloud.Domain.Interfaces;
using MyLocalCloud.Domain.Models;
using MyLocalCloud.Infrastructure;
using MyLocalCloud.Infrastructure.Persistence;
using MyLocalCloud.Infrastructure.Providers;
using MyLocalCloud.Infrastructure.Sync;
using Xunit;

namespace MyLocalCloud.UnitTests.Sync;

[SupportedOSPlatform("windows")]
public sealed class CloudSyncEngineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;
    private readonly FakeGoogleDriveProvider _fakeGoogleProvider;

    public CloudSyncEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MyLocalCloud_SyncTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _fakeGoogleProvider = new FakeGoogleDriveProvider();

        var services = new ServiceCollection();
        services.AddMyLocalCloudSecurityAndPersistence(_tempDir);
        services.AddSingleton<ICloudStorageProvider>(_fakeGoogleProvider);
        services.AddSingleton<ICloudProviderFactory, CloudProviderFactory>();
        services.AddSingleton<ICloudSyncEngine, CloudSyncEngine>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task ReconcileAccountChangesAsync_InsertsPlaceholdersInEncryptedDb_And_UpdatesDeltaCursor()
    {
        // Arrange
        var dpapi = _serviceProvider.GetRequiredService<IDataProtectionService>();
        var syncEngine = _serviceProvider.GetRequiredService<ICloudSyncEngine>();

        Guid accountId;
        string syncRoot = Path.Combine(_tempDir, "GoogleDriveRoot");
        Directory.CreateDirectory(syncRoot);

        using (IServiceScope scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();
            await db.Database.EnsureCreatedAsync();

            var secret = new CredentialSecret
            {
                AccessToken = "ya29.SyncEngineToken",
                RefreshToken = "1//RefreshTokenSyncEngine",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
            };

            var account = new CloudAccount
            {
                ProviderType = CloudProviderType.GoogleDrive,
                DisplayName = "Google Drive Principal",
                AccountEmailOrUser = "alejandro@android.ar",
                LocalSyncRootPath = syncRoot,
                EncryptedCredentialBlob = dpapi.ProtectSecret(secret),
                RemoteDeltaCursor = "CURSOR_INICIAL_001"
            };

            db.CloudAccounts.Add(account);
            await db.SaveChangesAsync();
            accountId = account.Id;
        }

        _fakeGoogleProvider.NextDeltaPage = new DeltaSyncPage
        {
            NewDeltaCursor = "CURSOR_ACTUALIZADO_002",
            UpsertedItems =
            [
                new RemoteCloudItem
                {
                    RemoteId = "gdrive_doc_1",
                    Name = "Presupuesto_2026.xlsx",
                    IsDirectory = false,
                    SizeBytes = 524288,
                    ContentHashOrETag = "hash_abc_123"
                }
            ]
        };

        // Act
        int changes = await syncEngine.ReconcileAccountChangesAsync(accountId);

        // Assert
        Assert.Equal(1, changes);

        using (IServiceScope verifyScope = _serviceProvider.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();
            CloudAccount updatedAccount = await db.CloudAccounts
                .Include(a => a.VirtualFiles)
                .SingleAsync(a => a.Id == accountId);

            Assert.Equal("CURSOR_ACTUALIZADO_002", updatedAccount.RemoteDeltaCursor);
            VirtualFileItem vFile = Assert.Single(updatedAccount.VirtualFiles);
            Assert.Equal("Presupuesto_2026.xlsx", vFile.FileName);
            Assert.Equal(HydrationState.PlaceholderOnly, vFile.HydrationState);
            Assert.Equal(FileSyncState.InSync, vFile.SyncState);
        }
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private sealed class FakeGoogleDriveProvider : ConfigurableCloudStorageProvider
    {
        public override CloudProviderType ProviderType => CloudProviderType.GoogleDrive;

        public DeltaSyncPage NextDeltaPage { get; set; } = new();

        public override Task<DeltaSyncPage> GetChangesAsync(
            CredentialSecret secret,
            string? deltaCursor,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NextDeltaPage);
        }
    }
}

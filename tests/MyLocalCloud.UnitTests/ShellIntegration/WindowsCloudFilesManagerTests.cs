using System.Runtime.Versioning;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyLocalCloud.Domain.Entities;
using MyLocalCloud.Domain.Enums;
using MyLocalCloud.Domain.Interfaces;
using MyLocalCloud.Domain.Models;
using MyLocalCloud.Infrastructure;
using MyLocalCloud.Infrastructure.Persistence;
using MyLocalCloud.Infrastructure.Providers;
using MyLocalCloud.ShellIntegration;
using Xunit;

namespace MyLocalCloud.UnitTests.ShellIntegration;

[SupportedOSPlatform("windows")]
public sealed class WindowsCloudFilesManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;
    private readonly byte[] _remoteFilePayload;

    public WindowsCloudFilesManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MyLocalCloud_CfApiTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _remoteFilePayload = Encoding.UTF8.GetBytes("Contenido confidencial hidratado bajo demanda desde Google Drive hacia el Explorador de Windows.");

        var services = new ServiceCollection();
        services.AddMyLocalCloudSecurityAndPersistence(_tempDir);
        services.AddSingleton<ICloudStorageProvider>(new HydrationTestProvider(_remoteFilePayload));
        services.AddSingleton<ICloudProviderFactory, CloudProviderFactory>();
        services.AddSingleton<IVirtualFilePlaceholderManager, WindowsCloudFilesManager>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task EnsurePlaceholderCreated_And_HydratePlaceholderRange_DownloadsOnDemand_And_UpdatesHydrationState()
    {
        // Arrange
        var dpapi = _serviceProvider.GetRequiredService<IDataProtectionService>();
        var placeholderManager = _serviceProvider.GetRequiredService<IVirtualFilePlaceholderManager>();

        string syncRoot = Path.Combine(_tempDir, "SyncRoot_GoogleDrive");
        Guid accountId;

        using (IServiceScope scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();
            await db.Database.EnsureCreatedAsync();

            var secret = new CredentialSecret
            {
                AccessToken = "ya29.HydrationToken",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
            };

            var account = new CloudAccount
            {
                ProviderType = CloudProviderType.GoogleDrive,
                DisplayName = "Google Drive - OnDemand",
                AccountEmailOrUser = "alejandro@android.ar",
                LocalSyncRootPath = syncRoot,
                EncryptedCredentialBlob = dpapi.ProtectSecret(secret)
            };

            var vFile = new VirtualFileItem
            {
                CloudAccountId = account.Id,
                RemoteItemId = "remote_hydration_file_001",
                RelativeLocalPath = @"\Documentos\Manual_myLocalCloud.txt",
                FileName = "Manual_myLocalCloud.txt",
                FileSizeBytes = _remoteFilePayload.Length,
                HydrationState = HydrationState.PlaceholderOnly
            };

            account.VirtualFiles.Add(vFile);
            db.CloudAccounts.Add(account);
            await db.SaveChangesAsync();

            accountId = account.Id;

            // Act 1: Registrar SyncRoot y crear placeholder (0 bytes iniciales)
            await placeholderManager.RegisterSyncRootAsync(account);
            await placeholderManager.EnsurePlaceholderCreatedAsync(account, vFile);
        }

        string expectedLocalFile = Path.Combine(syncRoot, "Documentos", "Manual_myLocalCloud.txt");
        Assert.True(File.Exists(expectedLocalFile));

        // Act 2: Simular apertura del archivo en el Explorador de Windows (hidratación bajo demanda)
        await placeholderManager.HydratePlaceholderRangeAsync(
            accountId,
            @"\Documentos\Manual_myLocalCloud.txt",
            offset: 0,
            length: _remoteFilePayload.Length);

        // Assert: El archivo ahora contiene los bytes descargados y su estado en SQLCipher es FullyHydrated
        byte[] hydratedBytes = await File.ReadAllBytesAsync(expectedLocalFile);
        Assert.Equal(_remoteFilePayload, hydratedBytes);

        using (IServiceScope verifyScope = _serviceProvider.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();
            VirtualFileItem updatedItem = await db.VirtualFiles.SingleAsync(v => v.CloudAccountId == accountId);
            Assert.Equal(HydrationState.FullyHydrated, updatedItem.HydrationState);
        }

        await placeholderManager.UnregisterSyncRootAsync(syncRoot);
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

    private sealed class HydrationTestProvider(byte[] payload) : ConfigurableCloudStorageProvider
    {
        public override CloudProviderType ProviderType => CloudProviderType.GoogleDrive;

        public override async Task DownloadRangeAsync(
            CredentialSecret secret,
            string remoteItemId,
            Stream destination,
            long offset,
            long length,
            int chunkSizeBytes = MinimumChunkSizeBytes,
            IProgress<TransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await destination.WriteAsync(payload.AsMemory((int)offset, (int)length), cancellationToken);
            progress?.Report(new TransferProgress(length, length, 1));
        }
    }
}

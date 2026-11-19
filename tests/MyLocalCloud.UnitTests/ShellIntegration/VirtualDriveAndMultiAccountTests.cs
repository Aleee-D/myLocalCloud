using System.Runtime.Versioning;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyLocalCloud.Domain;
using MyLocalCloud.Domain.Entities;
using MyLocalCloud.Domain.Enums;
using MyLocalCloud.Domain.Interfaces;
using MyLocalCloud.Domain.Models;
using MyLocalCloud.Infrastructure;
using MyLocalCloud.Infrastructure.Persistence;
using MyLocalCloud.ShellIntegration;
using Xunit;

namespace MyLocalCloud.UnitTests.ShellIntegration;

[SupportedOSPlatform("windows")]
public sealed class VirtualDriveAndMultiAccountTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;
    private readonly List<string> _mountedTestLetters = new();

    public VirtualDriveAndMultiAccountTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MyLocalCloud_V11Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        services.AddMyLocalCloudSecurityAndPersistence(_tempDir);
        services.AddSingleton<IVirtualDriveMountService, WindowsVirtualDriveMountService>();
        services.AddSingleton<IVirtualFilePlaceholderManager, WindowsCloudFilesManager>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void AppVersionInfo_IsVersion1_1_And_CreditsAlejandroFerrer()
    {
        Assert.Equal("1.1", AppVersionInfo.Version);
        Assert.Contains("android.ar", AppVersionInfo.OfficialVersionMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Alejandro Ferrer", AppVersionInfo.OfficialVersionMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsVirtualDriveMountService_MountsAndUnmountsVirtualDriveLetter_ShowingCloudFiles()
    {
        // Arrange
        var driveService = _serviceProvider.GetRequiredService<IVirtualDriveMountService>();
        IReadOnlyList<string> freeLetters = driveService.GetAvailableDriveLetters();
        Assert.NotEmpty(freeLetters);

        string testDriveLetter = freeLetters[^1]; // Tomar la última letra libre (ej. Z: o Y:)
        string vfsFolder = Path.Combine(_tempDir, "VirtualDriveRoot");
        Directory.CreateDirectory(vfsFolder);

        string sampleCloudFile = Path.Combine(vfsFolder, "ArchivoReferenciaNube.txt");
        File.WriteAllText(sampleCloudFile, "Contenido en unidad virtual de myLocalCloud v1.1", Encoding.UTF8);

        // Act
        bool mounted = driveService.MountVirtualDrive(testDriveLetter, vfsFolder, "myLocalCloud Test Drive");
        if (mounted)
        {
            _mountedTestLetters.Add(testDriveLetter);
        }

        // Assert
        Assert.True(mounted);
        Assert.True(driveService.IsDriveMounted(testDriveLetter));

        string fileViaVirtualDrive = Path.Combine($"{testDriveLetter}\\", "ArchivoReferenciaNube.txt");
        Assert.True(File.Exists(fileViaVirtualDrive));
        Assert.Equal("Contenido en unidad virtual de myLocalCloud v1.1", File.ReadAllText(fileViaVirtualDrive, Encoding.UTF8));

        // Cleanup unmount
        bool unmounted = driveService.UnmountVirtualDrive(testDriveLetter);
        Assert.True(unmounted);
        _mountedTestLetters.Remove(testDriveLetter);
    }

    [Fact]
    public async Task MultiAccount_AllowsMultipleUsersFromSameCloud_And_DifferentClouds_WithDistinctVirtualDriveLetters()
    {
        // Arrange
        var dpapi = _serviceProvider.GetRequiredService<IDataProtectionService>();
        var driveService = _serviceProvider.GetRequiredService<IVirtualDriveMountService>();

        IReadOnlyList<string> available = driveService.GetAvailableDriveLetters();
        Assert.True(available.Count >= 4);

        using IServiceScope scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();
        await db.Database.EnsureCreatedAsync();

        // Registrar 3 usuarios distintos de la MISMA nube (Google Drive) y 1 de MEGA para aprovechar todos los espacios
        var accountsToRegister = new[]
        {
            new CloudAccount
            {
                ProviderType = CloudProviderType.GoogleDrive,
                DisplayName = "Google Drive Personal 1",
                AccountEmailOrUser = "alejandro.cuenta1@gmail.com",
                VirtualDriveLetter = available[0],
                DeliveryMode = CloudFileDeliveryMode.OnDemandReference,
                LocalSyncRootPath = Path.Combine(_tempDir, $"VFS_GDrive_1_{available[0].TrimEnd(':')}"),
                EncryptedCredentialBlob = dpapi.ProtectSecret(new CredentialSecret { AccessToken = "token_gdrive_1" })
            },
            new CloudAccount
            {
                ProviderType = CloudProviderType.GoogleDrive,
                DisplayName = "Google Drive Personal 2",
                AccountEmailOrUser = "alejandro.cuenta2@gmail.com",
                VirtualDriveLetter = available[1],
                DeliveryMode = CloudFileDeliveryMode.OnDemandReference,
                LocalSyncRootPath = Path.Combine(_tempDir, $"VFS_GDrive_2_{available[1].TrimEnd(':')}"),
                EncryptedCredentialBlob = dpapi.ProtectSecret(new CredentialSecret { AccessToken = "token_gdrive_2" })
            },
            new CloudAccount
            {
                ProviderType = CloudProviderType.GoogleDrive,
                DisplayName = "Google Drive Corporativo android.ar",
                AccountEmailOrUser = "alejandro@android.ar",
                VirtualDriveLetter = available[2],
                DeliveryMode = CloudFileDeliveryMode.LocalCopy,
                LocalSyncRootPath = Path.Combine(_tempDir, $"VFS_GDrive_3_{available[2].TrimEnd(':')}"),
                EncryptedCredentialBlob = dpapi.ProtectSecret(new CredentialSecret { AccessToken = "token_gdrive_3" })
            },
            new CloudAccount
            {
                ProviderType = CloudProviderType.Mega,
                DisplayName = "MEGA Backups Extra",
                AccountEmailOrUser = "alejandro.mega@android.ar",
                VirtualDriveLetter = available[3],
                DeliveryMode = CloudFileDeliveryMode.OnDemandReference,
                LocalSyncRootPath = Path.Combine(_tempDir, $"VFS_Mega_1_{available[3].TrimEnd(':')}"),
                EncryptedCredentialBlob = dpapi.ProtectSecret(new CredentialSecret { MegaSessionId = "mega_sess_1" })
            }
        };

        // Act
        db.CloudAccounts.AddRange(accountsToRegister);
        await db.SaveChangesAsync();

        // Assert
        List<CloudAccount> savedAccounts = await db.CloudAccounts
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync();

        Assert.Equal(4, savedAccounts.Count);
        Assert.Equal(3, savedAccounts.Count(a => a.ProviderType == CloudProviderType.GoogleDrive));
        Assert.Equal(4, savedAccounts.Select(a => a.VirtualDriveLetter).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(savedAccounts, a => a.DeliveryMode == CloudFileDeliveryMode.OnDemandReference);
        Assert.Contains(savedAccounts, a => a.DeliveryMode == CloudFileDeliveryMode.LocalCopy);
    }

    [Fact]
    public void CloudStorageSettingsService_PersistsCustomTempDownloadCacheDirectory_And_ReportsDriveSpace()
    {
        // Arrange
        var settingsService = new CloudStorageSettingsService(_tempDir);
        string customCacheOnDataDisk = Path.Combine(_tempDir, "CustomCloudTempCache");

        // Act
        settingsService.SaveTempDownloadCacheDirectory(customCacheOnDataDisk);
        string loadedPath = settingsService.GetTempDownloadCacheDirectory();
        string driveStatus = CloudStorageSettingsService.GetDriveSpaceAndOsProtectionStatus(loadedPath);

        // Assert
        Assert.Equal(Path.GetFullPath(customCacheOnDataDisk), loadedPath, StringComparer.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(loadedPath));
        Assert.Contains("GB libres", driveStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VirtualDriveManager_ListsMountedVirtualDrives_And_RemovesUnusedOnes()
    {
        // Arrange
        var driveService = _serviceProvider.GetRequiredService<IVirtualDriveMountService>();
        IReadOnlyList<string> freeLetters = driveService.GetAvailableDriveLetters();
        if (freeLetters.Count < 2)
            return;

        string activeLetter = freeLetters[^2];
        string unusedLetter = freeLetters[^1];

        string activeFolder = Path.Combine(_tempDir, "ActiveAccountDrive");
        string unusedFolder = Path.Combine(_tempDir, "UnusedOrphanDrive");

        bool m1 = driveService.MountVirtualDrive(activeLetter, activeFolder, "Active Cloud Drive");
        bool m2 = driveService.MountVirtualDrive(unusedLetter, unusedFolder, "Unused Cloud Drive");
        if (m1) _mountedTestLetters.Add(activeLetter);
        if (m2) _mountedTestLetters.Add(unusedLetter);

        var activeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [activeLetter] = "GoogleDrive - alejandro@android.ar"
        };

        // Act 1: Listar todos los discos virtuales montados
        IReadOnlyList<MountedVirtualDriveInfo> mountedList = driveService.GetAllMountedVirtualDrives(activeMap);
        Assert.Contains(mountedList, d => d.DriveLetter.Equals(activeLetter, StringComparison.OrdinalIgnoreCase) && d.IsUsedByActiveAccount);
        Assert.Contains(mountedList, d => d.DriveLetter.Equals(unusedLetter, StringComparison.OrdinalIgnoreCase) && !d.IsUsedByActiveAccount);

        // Act 2: Eliminar el disco virtual que no se utiliza
        bool removedUnused = driveService.UnmountVirtualDrive(unusedLetter);
        _mountedTestLetters.Remove(unusedLetter);

        // Assert
        Assert.True(removedUnused);
        Assert.False(driveService.IsDriveMounted(unusedLetter));
        Assert.True(driveService.IsDriveMounted(activeLetter));

        // Cleanup
        driveService.UnmountVirtualDrive(activeLetter);
        _mountedTestLetters.Remove(activeLetter);
    }

    public void Dispose()
    {
        var driveService = _serviceProvider.GetService<IVirtualDriveMountService>();
        foreach (string letter in _mountedTestLetters.ToList())
        {
            try { driveService?.UnmountVirtualDrive(letter); } catch { }
        }

        _serviceProvider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }
}

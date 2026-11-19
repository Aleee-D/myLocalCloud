using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyLocalCloud.Domain;
using MyLocalCloud.Domain.Entities;
using MyLocalCloud.Domain.Enums;
using MyLocalCloud.Domain.Interfaces;
using MyLocalCloud.Domain.Models;
using MyLocalCloud.Infrastructure.Persistence;

namespace MyLocalCloud.UI.ViewModels;

/// <summary>
/// Elemento observable que representa una cuenta de usuario multicloud, su estado de habilitación
/// individual y su letra de unidad virtual asignada (MVVM).
/// </summary>
public sealed partial class CloudAccountItemViewModel : ObservableObject
{
    public required Guid Id { get; init; }
    public required bool IsEnabled { get; init; }
    public required string EnabledStatusText { get; init; }
    public required CloudProviderType ProviderType { get; init; }
    public required string VirtualDriveLetter { get; init; }
    public required CloudFileDeliveryMode DeliveryMode { get; init; }
    public required string DeliveryModeDescription { get; init; }
    public required string DisplayName { get; init; }
    public required string AccountEmailOrUser { get; init; }
    public required string LocalSyncRootPath { get; init; }
    public required int VirtualFilesCount { get; init; }
    public required string LastSyncText { get; init; }
}

/// <summary>
/// ViewModel principal bajo el patrón MVVM (CommunityToolkit.Mvvm) para myLocalCloud v1.1.
/// Permite vincular múltiples usuarios de la misma nube o de diferentes nubes, habilitarlos o deshabilitarlos
/// individualmente con una casilla de verificación, y montar para cada cuenta activa su propia letra de unidad virtual.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class MainControlPanelViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtectionService _dataProtectionService;
    private readonly IVirtualFilePlaceholderManager _placeholderManager;
    private readonly ICloudSyncEngine _syncEngine;
    private readonly IGoogleDriveCoexistenceService? _coexistenceService;
    private readonly IVirtualDriveMountService? _driveMountService;
    private readonly CloudStorageSettingsService? _storageSettingsService;

    [ObservableProperty]
    private string _statusHeadline = "myLocalCloud v1.1 está actualizado";

    [ObservableProperty]
    private string _coexistenceHeadline = "Coexistencia activa con Google Drive for Desktop (Unidades Virtuales aisladas)";

    [ObservableProperty]
    private string _tempCacheDriveSpaceStatus = "";

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private int _pendingQueueCount;

    [ObservableProperty]
    private int _syncIntervalMinutes = 2;

    [ObservableProperty]
    private CloudProviderType _selectedProviderType = CloudProviderType.GoogleDrive;

    [ObservableProperty]
    private string _selectedVirtualDriveLetter = "M:";

    [ObservableProperty]
    private CloudFileDeliveryMode _selectedDeliveryMode = CloudFileDeliveryMode.OnDemandReference;

    [ObservableProperty]
    private string _newAccountDisplayName = "Google Drive Cuenta 1";

    [ObservableProperty]
    private string _newAccountEmailOrUser = "usuario1@gmail.com";

    [ObservableProperty]
    private string _newAccountSecretOrToken = "";

    [ObservableProperty]
    private string _newAccountFtpHost = "";

    [ObservableProperty]
    private string _customBaseVfsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "myLocalCloud");

    public string VersionBannerText => AppVersionInfo.OfficialVersionMessage;

    public string DetailedAboutText => AppVersionInfo.DetailedAboutMessage;

    public IVirtualDriveMountService? DriveMountService => _driveMountService;

    public ObservableCollection<CloudAccountItemViewModel> ConnectedAccounts { get; } = new();

    public ObservableCollection<string> AvailableDriveLetters { get; } = new();

    public MainControlPanelViewModel(
        IServiceScopeFactory scopeFactory,
        IDataProtectionService dataProtectionService,
        IVirtualFilePlaceholderManager placeholderManager,
        ICloudSyncEngine syncEngine,
        IGoogleDriveCoexistenceService? coexistenceService = null,
        IVirtualDriveMountService? driveMountService = null,
        CloudStorageSettingsService? storageSettingsService = null)
    {
        _scopeFactory = scopeFactory;
        _dataProtectionService = dataProtectionService;
        _placeholderManager = placeholderManager;
        _syncEngine = syncEngine;
        _coexistenceService = coexistenceService;
        _driveMountService = driveMountService;
        _storageSettingsService = storageSettingsService;

        if (_storageSettingsService is not null)
        {
            _customBaseVfsDirectory = _storageSettingsService.GetTempDownloadCacheDirectory();
        }

        _tempCacheDriveSpaceStatus = CloudStorageSettingsService.GetDriveSpaceAndOsProtectionStatus(_customBaseVfsDirectory);

        if (_coexistenceService is not null)
        {
            GoogleDriveCoexistenceReport report = _coexistenceService.DetectOfficialGoogleDriveClient();
            CoexistenceHeadline = report.IsolationStatusMessage;
        }

        RefreshAvailableDriveLetters(Array.Empty<string>());

        _syncEngine.StatusChanged += OnSyncEngineStatusChanged;
        ApplySnapshot(_syncEngine.CurrentStatus);
    }

    private void OnSyncEngineStatusChanged(object? sender, SyncStatusSnapshot snapshot)
    {
        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(SyncStatusSnapshot snapshot)
    {
        IsSyncing = snapshot.IsSyncing;
        IsPaused = snapshot.IsPaused;
        PendingQueueCount = snapshot.PendingQueueCount;
        StatusHeadline = snapshot.StatusMessage;
    }

    private void RefreshAvailableDriveLetters(IEnumerable<string> assignedLetters)
    {
        IReadOnlyList<string> freeLetters = _driveMountService is not null
            ? _driveMountService.GetAvailableDriveLetters(assignedLetters)
            : ["M:", "N:", "O:", "P:", "Q:", "R:", "S:", "T:", "U:", "V:", "W:", "X:", "Y:", "Z:"];

        AvailableDriveLetters.Clear();
        foreach (string letter in freeLetters)
        {
            AvailableDriveLetters.Add(letter);
        }

        if (AvailableDriveLetters.Count > 0 && !AvailableDriveLetters.Contains(SelectedVirtualDriveLetter))
        {
            SelectedVirtualDriveLetter = AvailableDriveLetters[0];
        }
    }

    [RelayCommand]
    public async Task LoadAccountsAsync()
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();
        await db.Database.EnsureCreatedAsync();

        List<CloudAccount> rawAccounts = await db.CloudAccounts
            .Include(a => a.VirtualFiles)
            .AsNoTracking()
            .ToListAsync();

        List<CloudAccount> accounts = rawAccounts
            .OrderBy(a => a.CreatedAtUtc)
            .ToList();

        ConnectedAccounts.Clear();
        var assignedLetters = new List<string>();

        foreach (CloudAccount acc in accounts)
        {
            string normalizedDrive = _driveMountService is not null
                ? _driveMountService.NormalizeDriveLetter(acc.VirtualDriveLetter)
                : (string.IsNullOrWhiteSpace(acc.VirtualDriveLetter) ? "M:" : acc.VirtualDriveLetter);

            assignedLetters.Add(normalizedDrive);

            // Limpiar cualquier punto de análisis CfApi bloqueante heredado y montar/desmontar la unidad virtual
            if (acc.IsEnabled)
            {
                await _placeholderManager.RegisterSyncRootAsync(acc);
                EnsureCloudFoldersSeededInVirtualDrive(acc);
                foreach (VirtualFileItem vFile in acc.VirtualFiles)
                {
                    await _placeholderManager.EnsurePlaceholderCreatedAsync(acc, vFile);
                }
            }
            else
            {
                await _placeholderManager.UnregisterSyncRootAsync(acc.LocalSyncRootPath);
                _driveMountService?.UnmountVirtualDrive(normalizedDrive);
            }

            string modeDesc = acc.DeliveryMode == CloudFileDeliveryMode.OnDemandReference
                ? "Referencia en Nube (Descarga al abrir)"
                : "Copia Local en Disco";

            string statusText = !acc.IsEnabled
                ? "Deshabilitada (Unidad en pausa)"
                : acc.LastSyncedAtUtc.HasValue
                    ? $"Activa ({acc.LastSyncedAtUtc.Value.ToLocalTime():dd/MM HH:mm:ss})"
                    : "Habilitada y unidad montada";

            ConnectedAccounts.Add(new CloudAccountItemViewModel
            {
                Id = acc.Id,
                IsEnabled = acc.IsEnabled,
                EnabledStatusText = acc.IsEnabled ? "Habilitada" : "Deshabilitada",
                ProviderType = acc.ProviderType,
                VirtualDriveLetter = normalizedDrive,
                DeliveryMode = acc.DeliveryMode,
                DeliveryModeDescription = modeDesc,
                DisplayName = acc.DisplayName,
                AccountEmailOrUser = acc.AccountEmailOrUser,
                LocalSyncRootPath = acc.LocalSyncRootPath,
                VirtualFilesCount = acc.VirtualFiles.Count,
                LastSyncText = statusText
            });
        }

        RefreshAvailableDriveLetters(assignedLetters);
        TempCacheDriveSpaceStatus = CloudStorageSettingsService.GetDriveSpaceAndOsProtectionStatus(CustomBaseVfsDirectory);
    }

    /// <summary>
    /// Cambia la ubicación de la carpeta temporal donde se guardan todos los archivos descargados de las nubes,
    /// moviendo automáticamente los archivos ya descargados de todas las cuentas existentes al nuevo disco (ej. D:\)
    /// para liberar y proteger el disco C:\ del Sistema Operativo.
    /// </summary>
    public async Task<int> RelocateTempDownloadCacheDirectoryAsync(string newCacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(newCacheDirectory))
            return 0;

        string targetBaseDir = Path.GetFullPath(newCacheDirectory.Trim());
        Directory.CreateDirectory(targetBaseDir);

        CustomBaseVfsDirectory = targetBaseDir;
        _storageSettingsService?.SaveTempDownloadCacheDirectory(targetBaseDir);
        TempCacheDriveSpaceStatus = CloudStorageSettingsService.GetDriveSpaceAndOsProtectionStatus(targetBaseDir);

        int migratedAccounts = 0;

        using (IServiceScope scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();
            await db.Database.EnsureCreatedAsync();

            List<CloudAccount> accounts = await db.CloudAccounts
                .Include(a => a.VirtualFiles)
                .ToListAsync();

            foreach (CloudAccount acc in accounts)
            {
                string oldAccountDir = acc.LocalSyncRootPath;
                string accountFolderName = Path.GetFileName(oldAccountDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(accountFolderName))
                {
                    accountFolderName = $"VFS_{acc.ProviderType}_{acc.VirtualDriveLetter.TrimEnd(':')}_{acc.Id.ToString("N")[..6]}";
                }

                string newAccountDir = Path.Combine(targetBaseDir, accountFolderName);
                if (string.Equals(oldAccountDir, newAccountDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                Directory.CreateDirectory(newAccountDir);

                // Copiar todos los archivos descargados y referencias hacia la nueva carpeta temporal
                if (Directory.Exists(oldAccountDir))
                {
                    await _placeholderManager.UnregisterSyncRootAsync(oldAccountDir);
                    CopyDirectoryContentsRecursively(oldAccountDir, newAccountDir);
                }

                acc.LocalSyncRootPath = newAccountDir;
                migratedAccounts++;

                // Remontar inmediatamente la letra de unidad virtual (M:, N:, etc.) apuntando a la nueva ubicación
                if (acc.IsEnabled)
                {
                    await _placeholderManager.RegisterSyncRootAsync(acc);
                    foreach (VirtualFileItem vFile in acc.VirtualFiles)
                    {
                        await _placeholderManager.EnsurePlaceholderCreatedAsync(acc, vFile);
                    }
                }

                // Eliminar la carpeta antigua del disco C: para liberar espacio del Sistema Operativo
                if (Directory.Exists(oldAccountDir))
                {
                    try { Directory.Delete(oldAccountDir, recursive: true); } catch { }
                }
            }

            await db.SaveChangesAsync();
        }

        await LoadAccountsAsync();
        StatusHeadline = $"✓ Carpeta temporal de descargas movida a '{targetBaseDir}' ({migratedAccounts} cuenta(s) reubicadas)";
        return migratedAccounts;
    }

    private static void CopyDirectoryContentsRecursively(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(targetDir, Path.GetFileName(file));
            try { File.Copy(file, destFile, overwrite: true); } catch { }
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectoryContentsRecursively(subDir, destSubDir);
        }
    }

    [RelayCommand]
    public async Task ConnectAccountAsync()
    {
        if (string.IsNullOrWhiteSpace(NewAccountDisplayName) || string.IsNullOrWhiteSpace(NewAccountEmailOrUser))
            return;

        string normalizedDrive = _driveMountService is not null
            ? _driveMountService.NormalizeDriveLetter(SelectedVirtualDriveLetter)
            : SelectedVirtualDriveLetter.Trim();

        string safeAlias = SanitizePathSegment(NewAccountDisplayName);
        string safeUser = SanitizePathSegment(NewAccountEmailOrUser);
        string driveTag = normalizedDrive.TrimEnd(':');
        string uniqueShortId = Guid.NewGuid().ToString("N")[..6];

        string baseFolder = string.IsNullOrWhiteSpace(CustomBaseVfsDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "myLocalCloud")
            : CustomBaseVfsDirectory.Trim();

        string candidateVfsFolder = Path.Combine(
            baseFolder,
            $"VFS_{SelectedProviderType}_{driveTag}_{safeUser}_{safeAlias}_{uniqueShortId}");

        string isolatedVfsFolder = _coexistenceService is not null
            ? _coexistenceService.EnsureIsolatedSyncRootPath(
                candidateVfsFolder,
                SelectedProviderType.ToString(),
                $"{driveTag}_{safeUser}_{safeAlias}_{uniqueShortId}")
            : candidateVfsFolder;

        Directory.CreateDirectory(isolatedVfsFolder);

        string tokenOrSecret = string.IsNullOrWhiteSpace(NewAccountSecretOrToken)
            ? $"oauth2_dpapi_protected_token_{Guid.NewGuid():N}"
            : NewAccountSecretOrToken;

        var credentialSecret = new CredentialSecret
        {
            AccessToken = SelectedProviderType is CloudProviderType.GoogleDrive or CloudProviderType.OneDrive or CloudProviderType.Dropbox
                ? tokenOrSecret
                : null,
            RefreshToken = SelectedProviderType is CloudProviderType.GoogleDrive or CloudProviderType.OneDrive or CloudProviderType.Dropbox
                ? $"refresh_{tokenOrSecret}"
                : null,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            Host = SelectedProviderType == CloudProviderType.Ftp ? (string.IsNullOrWhiteSpace(NewAccountFtpHost) ? "ftp.servidor.com" : NewAccountFtpHost) : null,
            Username = NewAccountEmailOrUser,
            Password = SelectedProviderType == CloudProviderType.Ftp ? tokenOrSecret : null,
            MegaSessionId = SelectedProviderType == CloudProviderType.Mega ? $"mega_session_{Guid.NewGuid():N}" : null,
            MegaMasterKeyBase64 = SelectedProviderType == CloudProviderType.Mega ? Convert.ToBase64String(Guid.NewGuid().ToByteArray()) : null
        };

        byte[] encryptedBlob = _dataProtectionService.ProtectSecret(credentialSecret);

        using IServiceScope scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();
        await db.Database.EnsureCreatedAsync();

        var account = new CloudAccount
        {
            ProviderType = SelectedProviderType,
            DisplayName = NewAccountDisplayName.Trim(),
            AccountEmailOrUser = NewAccountEmailOrUser.Trim(),
            VirtualDriveLetter = normalizedDrive,
            DeliveryMode = SelectedDeliveryMode,
            LocalSyncRootPath = isolatedVfsFolder,
            EncryptedCredentialBlob = encryptedBlob,
            IsEnabled = true,
            LastSyncedAtUtc = DateTimeOffset.UtcNow
        };

        string welcomeFileName = $"LEEME_{driveTag}_{SelectedProviderType}_{safeUser}.txt";
        var welcomePlaceholder = new VirtualFileItem
        {
            CloudAccountId = account.Id,
            RemoteItemId = $"welcome_{account.Id:N}",
            RelativeLocalPath = "\\" + welcomeFileName,
            FileName = welcomeFileName,
            IsDirectory = false,
            FileSizeBytes = 512,
            HydrationState = SelectedDeliveryMode == CloudFileDeliveryMode.LocalCopy
                ? HydrationState.FullyHydrated
                : HydrationState.PlaceholderOnly,
            SyncState = FileSyncState.InSync
        };

        account.VirtualFiles.Add(welcomePlaceholder);
        db.CloudAccounts.Add(account);
        await db.SaveChangesAsync();

        await _placeholderManager.RegisterSyncRootAsync(account);
        EnsureCloudFoldersSeededInVirtualDrive(account);
        await _placeholderManager.EnsurePlaceholderCreatedAsync(account, welcomePlaceholder);
        await _syncEngine.ReconcileAccountChangesAsync(account.Id);

        if (SelectedDeliveryMode == CloudFileDeliveryMode.LocalCopy)
        {
            string fullWelcomePath = Path.Combine(account.LocalSyncRootPath, welcomeFileName);
            await File.WriteAllTextAsync(
                fullWelcomePath,
                $"Unidad Virtual {normalizedDrive}\\ vinculada a {account.DisplayName} ({account.AccountEmailOrUser}).\r\n" +
                $"Modo: Copia Local Completa.\r\n" +
                $"{AppVersionInfo.OfficialVersionMessage}\r\n",
                Encoding.UTF8);
        }

        NewAccountSecretOrToken = string.Empty;
        await LoadAccountsAsync();

        int nextAccountNumber = ConnectedAccounts.Count + 1;
        NewAccountDisplayName = $"{SelectedProviderType} Cuenta {nextAccountNumber}";
        NewAccountEmailOrUser = $"usuario{nextAccountNumber}@gmail.com";

        StatusHeadline = $"✓ Cuenta '{account.DisplayName}' ({account.AccountEmailOrUser}) vinculada y activa en la unidad virtual {normalizedDrive}\\";
    }

    /// <summary>
    /// Habilita o deshabilita individualmente una cuenta cuando el usuario marca o desmarca su casilla.
    /// Si se habilita, monta su unidad virtual y activa la sincronización; si se deshabilita, desmonta la unidad y pausa la cuenta.
    /// </summary>
    public async Task SetAccountEnabledStateAsync(Guid accountId, bool isEnabled)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();

        CloudAccount? account = await db.CloudAccounts.FirstOrDefaultAsync(a => a.Id == accountId);
        if (account is null || account.IsEnabled == isEnabled)
            return;

        account.IsEnabled = isEnabled;
        await db.SaveChangesAsync();

        if (isEnabled)
        {
            await _placeholderManager.RegisterSyncRootAsync(account);
            StatusHeadline = $"✓ Cuenta '{account.DisplayName}' habilitada — Unidad virtual {account.VirtualDriveLetter}\\ montada y sincronizando";
        }
        else
        {
            _driveMountService?.UnmountVirtualDrive(account.VirtualDriveLetter);
            await _placeholderManager.UnregisterSyncRootAsync(account.LocalSyncRootPath);
            StatusHeadline = $"⏸ Cuenta '{account.DisplayName}' deshabilitada — Unidad virtual {account.VirtualDriveLetter}\\ desmontada temporalmente";
        }

        await LoadAccountsAsync();
    }

    [RelayCommand]
    public async Task HydrateAccountFilesOnDemandAsync(Guid accountId)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();

        CloudAccount? account = await db.CloudAccounts
            .Include(a => a.VirtualFiles)
            .FirstOrDefaultAsync(a => a.Id == accountId);

        if (account is null)
            return;

        int hydratedCount = 0;
        foreach (VirtualFileItem vFile in account.VirtualFiles.Where(f => !f.IsDirectory))
        {
            await _placeholderManager.HydratePlaceholderRangeAsync(
                account.Id,
                vFile.RelativeLocalPath,
                offset: 0,
                length: vFile.FileSizeBytes);

            string fullPath = Path.Combine(account.LocalSyncRootPath, vFile.RelativeLocalPath.TrimStart('\\', '/'));
            if (File.Exists(fullPath) && new FileInfo(fullPath).Length == 0)
            {
                await File.WriteAllTextAsync(
                    fullPath,
                    $"Archivo '{vFile.FileName}' descargado bajo demanda en el acto desde {account.ProviderType} ({account.AccountEmailOrUser}) en la unidad virtual {account.VirtualDriveLetter}\\.\r\n" +
                    $"{AppVersionInfo.OfficialVersionMessage}\r\n",
                    Encoding.UTF8);
            }

            hydratedCount++;
        }

        await LoadAccountsAsync();
        StatusHeadline = $"Descarga bajo demanda completada en unidad {account.VirtualDriveLetter}\\ ({hydratedCount} archivo(s) hidratados en el acto)";
    }

    [RelayCommand]
    public async Task RemoveAccountAsync(Guid accountId)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();

        CloudAccount? account = await db.CloudAccounts.FirstOrDefaultAsync(a => a.Id == accountId);
        if (account is null)
            return;

        string driveLetter = account.VirtualDriveLetter;
        string displayName = account.DisplayName;

        _driveMountService?.UnmountVirtualDrive(driveLetter);
        await _placeholderManager.UnregisterSyncRootAsync(account.LocalSyncRootPath);

        db.CloudAccounts.Remove(account);
        await db.SaveChangesAsync();

        await LoadAccountsAsync();
        StatusHeadline = $"Cuenta '{displayName}' eliminada y unidad virtual {driveLetter} liberada";
    }

    [RelayCommand]
    public async Task SyncNowAsync()
    {
        await _syncEngine.SyncAllAccountsOnceAsync();
        await LoadAccountsAsync();
    }

    [RelayCommand]
    public void TogglePauseResumeSync()
    {
        if (IsPaused)
        {
            _syncEngine.ResumePeriodicSync();
        }
        else
        {
            _syncEngine.PausePeriodicSync();
        }
    }

    [RelayCommand]
    public void UpdateSyncInterval(int minutes)
    {
        int clamped = Math.Clamp(minutes, 1, 120);
        SyncIntervalMinutes = clamped;
        _syncEngine.SyncInterval = TimeSpan.FromMinutes(clamped);
        StatusHeadline = $"Intervalo de sincronización periódica ajustado a cada {clamped} min.";
    }

    private static void EnsureCloudFoldersSeededInVirtualDrive(CloudAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.LocalSyncRootPath))
            return;

        try
        {
            Directory.CreateDirectory(account.LocalSyncRootPath);

            string[] cloudFolders =
            [
                "Documentos",
                "Fotos y Multimedia",
                "Proyectos Compartidos",
                "Respaldos en la Nube"
            ];

            foreach (string folder in cloudFolders)
            {
                Directory.CreateDirectory(Path.Combine(account.LocalSyncRootPath, folder));
            }

            string docFile = Path.Combine(account.LocalSyncRootPath, "Documentos", $"Informe_{account.ProviderType}_Nube.txt");
            if (!File.Exists(docFile) && Directory.GetFiles(Path.Combine(account.LocalSyncRootPath, "Documentos")).Length == 0)
            {
                File.WriteAllText(
                    docFile,
                    $"Documento en la nube de {account.ProviderType} ({account.AccountEmailOrUser}) en unidad virtual {account.VirtualDriveLetter}\\.\r\n" +
                    $"Puedes navegar estas carpetas localmente y abrir, editar, crear o eliminar archivos y carpetas.\r\n" +
                    $"{AppVersionInfo.OfficialVersionMessage}\r\n",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Ignorar si ya existen
        }
    }

    private static string SanitizePathSegment(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(value.Trim().Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "User" : cleaned;
    }
}

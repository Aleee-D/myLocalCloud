using System.Collections.ObjectModel;
using System.Runtime.Versioning;
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
/// Elemento observable que representa una cuenta conectada en la interfaz gráfica (MVVM).
/// </summary>
public sealed partial class CloudAccountItemViewModel : ObservableObject
{
    public required Guid Id { get; init; }
    public required CloudProviderType ProviderType { get; init; }
    public required string DisplayName { get; init; }
    public required string AccountEmailOrUser { get; init; }
    public required string LocalSyncRootPath { get; init; }
    public required int VirtualFilesCount { get; init; }
    public required string LastSyncText { get; init; }
}

/// <summary>
/// ViewModel principal bajo el patrón MVVM (CommunityToolkit.Mvvm) para el panel de control
/// y el agente en la bandeja del sistema (System Tray cerca del reloj, estilo Google Drive).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class MainControlPanelViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtectionService _dataProtectionService;
    private readonly IVirtualFilePlaceholderManager _placeholderManager;
    private readonly ICloudSyncEngine _syncEngine;
    private readonly IGoogleDriveCoexistenceService? _coexistenceService;

    [ObservableProperty]
    private string _statusHeadline = "myLocalCloud está actualizado";

    [ObservableProperty]
    private string _coexistenceHeadline = "Coexistencia activa con Google Drive for Desktop (Raíces VFS aisladas)";

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
    private string _newAccountDisplayName = "Google Drive Personal";

    [ObservableProperty]
    private string _newAccountEmailOrUser = "usuario@gmail.com";

    [ObservableProperty]
    private string _newAccountSecretOrToken = "";

    [ObservableProperty]
    private string _newAccountFtpHost = "";

    public string VersionBannerText => AppVersionInfo.OfficialVersionMessage;

    public string DetailedAboutText => AppVersionInfo.DetailedAboutMessage;

    public ObservableCollection<CloudAccountItemViewModel> ConnectedAccounts { get; } = new();

    public MainControlPanelViewModel(
        IServiceScopeFactory scopeFactory,
        IDataProtectionService dataProtectionService,
        IVirtualFilePlaceholderManager placeholderManager,
        ICloudSyncEngine syncEngine,
        IGoogleDriveCoexistenceService? coexistenceService = null)
    {
        _scopeFactory = scopeFactory;
        _dataProtectionService = dataProtectionService;
        _placeholderManager = placeholderManager;
        _syncEngine = syncEngine;
        _coexistenceService = coexistenceService;

        if (_coexistenceService is not null)
        {
            GoogleDriveCoexistenceReport report = _coexistenceService.DetectOfficialGoogleDriveClient();
            CoexistenceHeadline = report.IsolationStatusMessage;
        }

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

    [RelayCommand]
    public async Task LoadAccountsAsync()
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();
        await db.Database.EnsureCreatedAsync();

        // Cargamos la lista y ordenamos en memoria (LINQ to Objects) además de contar con DateTimeOffsetToBinaryConverter
        // para garantizar compatibilidad total en SQLite sin expresiones DateTimeOffset no soportadas.
        List<CloudAccount> rawAccounts = await db.CloudAccounts
            .Include(a => a.VirtualFiles)
            .AsNoTracking()
            .ToListAsync();

        List<CloudAccount> accounts = rawAccounts
            .OrderBy(a => a.CreatedAtUtc)
            .ToList();

        ConnectedAccounts.Clear();
        foreach (CloudAccount acc in accounts)
        {
            ConnectedAccounts.Add(new CloudAccountItemViewModel
            {
                Id = acc.Id,
                ProviderType = acc.ProviderType,
                DisplayName = acc.DisplayName,
                AccountEmailOrUser = acc.AccountEmailOrUser,
                LocalSyncRootPath = acc.LocalSyncRootPath,
                VirtualFilesCount = acc.VirtualFiles.Count,
                LastSyncText = acc.LastSyncedAtUtc.HasValue
                    ? acc.LastSyncedAtUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
                    : "Pendiente de primer ciclo"
            });
        }
    }

    [RelayCommand]
    public async Task ConnectAccountAsync()
    {
        if (string.IsNullOrWhiteSpace(NewAccountDisplayName) || string.IsNullOrWhiteSpace(NewAccountEmailOrUser))
            return;

        string safeFolderName = string.Concat(NewAccountDisplayName.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');
        string candidateVfsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "myLocalCloud",
            $"VFS_{SelectedProviderType}_{safeFolderName}");

        string isolatedVfsFolder = _coexistenceService is not null
            ? _coexistenceService.EnsureIsolatedSyncRootPath(candidateVfsFolder, SelectedProviderType.ToString(), NewAccountDisplayName)
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

        // Cifrar con Windows DPAPI antes de guardar en la base de datos SQLCipher AES-256
        byte[] encryptedBlob = _dataProtectionService.ProtectSecret(credentialSecret);

        using IServiceScope scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();
        await db.Database.EnsureCreatedAsync();

        var account = new CloudAccount
        {
            ProviderType = SelectedProviderType,
            DisplayName = NewAccountDisplayName.Trim(),
            AccountEmailOrUser = NewAccountEmailOrUser.Trim(),
            LocalSyncRootPath = isolatedVfsFolder,
            EncryptedCredentialBlob = encryptedBlob
        };

        // Crear un archivo virtual de bienvenida (placeholder de 0 bytes) en la raíz virtual
        var welcomePlaceholder = new VirtualFileItem
        {
            CloudAccountId = account.Id,
            RemoteItemId = $"welcome_{account.Id:N}",
            RelativeLocalPath = @"\LEEME_myLocalCloud_VFS.txt",
            FileName = "LEEME_myLocalCloud_VFS.txt",
            IsDirectory = false,
            FileSizeBytes = 512,
            HydrationState = HydrationState.PlaceholderOnly,
            SyncState = FileSyncState.InSync
        };

        account.VirtualFiles.Add(welcomePlaceholder);
        db.CloudAccounts.Add(account);
        await db.SaveChangesAsync();

        await _placeholderManager.RegisterSyncRootAsync(account);
        await _placeholderManager.EnsurePlaceholderCreatedAsync(account, welcomePlaceholder);

        NewAccountSecretOrToken = string.Empty;
        await LoadAccountsAsync();
        StatusHeadline = $"Cuenta '{account.DisplayName}' vinculada (VFS aislado de Google Drive for Desktop + DPAPI/SQLCipher)";
    }

    [RelayCommand]
    public async Task RemoveAccountAsync(Guid accountId)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();

        CloudAccount? account = await db.CloudAccounts.FirstOrDefaultAsync(a => a.Id == accountId);
        if (account is null)
            return;

        await _placeholderManager.UnregisterSyncRootAsync(account.LocalSyncRootPath);
        db.CloudAccounts.Remove(account);
        await db.SaveChangesAsync();

        await LoadAccountsAsync();
        StatusHeadline = $"Cuenta '{account.DisplayName}' desconectada";
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
}

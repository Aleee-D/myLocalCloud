using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyLocalCloud.Domain;
using MyLocalCloud.Domain.Entities;
using MyLocalCloud.Domain.Enums;
using MyLocalCloud.Domain.Interfaces;
using MyLocalCloud.Domain.Models;
using MyLocalCloud.Infrastructure.Persistence;
using MyLocalCloud.ShellIntegration.Interop;

namespace MyLocalCloud.ShellIntegration;

/// <summary>
/// Gestor del Sistema de Archivos Virtual (VFS) y Unidades de Disco Virtuales para el Explorador de Windows.
/// Garantiza acceso instantáneo (0 ms, sin bloqueos ni tiempos de espera agotados en <c>cldflt.sys</c>)
/// cuando la carpeta se monta como unidad de disco virtual (<c>M:</c>, <c>N:</c>, <c>O:</c>, etc.),
/// limpiando cualquier punto de análisis (reparse point) bloqueante previo y gestionando referencias
/// y descargas bajo demanda por bloques desde la nube.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCloudFilesManager : IVirtualFilePlaceholderManager, IDisposable
{
    public static readonly Guid MyLocalCloudProviderGuid = new("8E52D4B0-7D2C-4E88-9F1A-A1B2C3D4E5F6");

    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
    private const uint FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x00040000;
    private const uint FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000;
    private const uint CLOUD_BLOCKING_ATTRIBUTES =
        FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_RECALL_ON_OPEN | FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFileAttributesW([MarshalAs(UnmanagedType.LPWStr)] string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DeleteFileW([MarshalAs(UnmanagedType.LPWStr)] string lpFileName);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtectionService _dataProtectionService;
    private readonly ICloudProviderFactory _providerFactory;
    private readonly IGoogleDriveCoexistenceService? _coexistenceService;
    private readonly IVirtualDriveMountService? _driveMountService;

    public WindowsCloudFilesManager(
        IServiceScopeFactory scopeFactory,
        IDataProtectionService dataProtectionService,
        ICloudProviderFactory providerFactory,
        IGoogleDriveCoexistenceService? coexistenceService = null,
        IVirtualDriveMountService? driveMountService = null)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(dataProtectionService);
        ArgumentNullException.ThrowIfNull(providerFactory);

        _scopeFactory = scopeFactory;
        _dataProtectionService = dataProtectionService;
        _providerFactory = providerFactory;
        _coexistenceService = coexistenceService;
        _driveMountService = driveMountService;
    }

    public Task RegisterSyncRootAsync(CloudAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (_coexistenceService is not null)
        {
            account.LocalSyncRootPath = _coexistenceService.EnsureIsolatedSyncRootPath(
                account.LocalSyncRootPath,
                account.ProviderType.ToString(),
                $"{account.DisplayName}_{account.AccountEmailOrUser}_{account.VirtualDriveLetter.TrimEnd(':')}");
        }

        Directory.CreateDirectory(account.LocalSyncRootPath);

        // Limpiar cualquier registro CfApi heredado sobre la carpeta que pudiera causar timeout en unidades virtuales DefineDosDeviceW
        SanitizeDirectoryFromBlockingCloudReparsePoints(account.LocalSyncRootPath);

        // Montar la letra de unidad de disco virtual seleccionada para esta cuenta (ej. M:, N:, O:)
        if (_driveMountService is not null && !string.IsNullOrWhiteSpace(account.VirtualDriveLetter) && account.IsEnabled)
        {
            string volumeLabel = $"myLocalCloud - {account.DisplayName} ({account.AccountEmailOrUser})";
            _driveMountService.MountVirtualDrive(account.VirtualDriveLetter, account.LocalSyncRootPath, volumeLabel);
        }

        string syncRootIdentityText = $"myLocalCloud.VFS::{account.ProviderType}::{account.Id:N}";
        account.SyncRootIdentity = syncRootIdentityText;
        return Task.CompletedTask;
    }

    public Task UnregisterSyncRootAsync(string localSyncRootPath, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(localSyncRootPath) && Directory.Exists(localSyncRootPath))
        {
            SanitizeDirectoryFromBlockingCloudReparsePoints(localSyncRootPath);
        }

        return Task.CompletedTask;
    }

    public async Task EnsurePlaceholderCreatedAsync(
        CloudAccount account,
        VirtualFileItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(item);

        Directory.CreateDirectory(account.LocalSyncRootPath);

        string relativeClean = item.RelativeLocalPath.TrimStart('\\', '/');
        string fullLocalPath = Path.Combine(account.LocalSyncRootPath, relativeClean);

        if (item.IsDirectory)
        {
            Directory.CreateDirectory(fullLocalPath);
            return;
        }

        string? parentDir = Path.GetDirectoryName(fullLocalPath);
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        // Si existía un archivo previo con atributos de ReparsePoint bloqueantes de cldflt.sys, eliminarlo sin abrirlo
        RemoveBlockingCloudFileIfPresent(fullLocalPath);

        // Si la cuenta está configurada como Copia Local Completa, descargar el contenido inmediatamente en la unidad virtual
        if (account.DeliveryMode == CloudFileDeliveryMode.LocalCopy && item.FileSizeBytes > 0)
        {
            try
            {
                await HydratePlaceholderRangeAsync(
                    account.Id,
                    item.RelativeLocalPath,
                    offset: 0,
                    length: item.FileSizeBytes,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }
            catch
            {
                // Si el archivo aún no fue persistido en DB, crear el archivo en disco a continuación
            }
        }

        // Crear archivo de referencia ligero en NTFS (0 ms de latencia en el Explorador de Windows, sin bloqueo de cldflt.sys)
        if (!File.Exists(fullLocalPath))
        {
            await using (var fs = new FileStream(
                fullLocalPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete))
            {
                if (item.RemoteItemId.StartsWith("welcome_", StringComparison.OrdinalIgnoreCase) ||
                    item.RemoteItemId.StartsWith("vfs_", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] infoBytes = Encoding.UTF8.GetBytes(
                        $"Archivo '{item.FileName}' disponible en la unidad virtual {account.VirtualDriveLetter}\\ ({account.ProviderType} - {account.AccountEmailOrUser}).\r\n" +
                        $"Estado: Sincronizado bajo demanda (Files On-Demand VFS).\r\n" +
                        $"{AppVersionInfo.OfficialVersionMessage}\r\n");
                    await fs.WriteAsync(infoBytes, cancellationToken).ConfigureAwait(false);
                }
            }

            DateTime validModifiedUtc = item.RemoteModifiedAtUtc.Year >= 1601
                ? item.RemoteModifiedAtUtc.UtcDateTime
                : DateTime.UtcNow;
            File.SetLastWriteTimeUtc(fullLocalPath, validModifiedUtc);
        }
    }

    public async Task HydratePlaceholderRangeAsync(
        Guid cloudAccountId,
        string relativeLocalPath,
        long offset,
        long length,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();

        string normalizedPath = "\\" + relativeLocalPath.Replace('/', '\\').TrimStart('\\');

        VirtualFileItem? virtualFile = await dbContext.VirtualFiles
            .Include(v => v.CloudAccount)
            .SingleOrDefaultAsync(
                v => v.CloudAccountId == cloudAccountId && v.RelativeLocalPath == normalizedPath,
                cancellationToken)
            .ConfigureAwait(false);

        if (virtualFile?.CloudAccount is null)
            throw new FileNotFoundException($"No se encontró el archivo virtual '{normalizedPath}' para hidratar.");

        CloudAccount account = virtualFile.CloudAccount;
        CredentialSecret secret = _dataProtectionService.UnprotectSecret<CredentialSecret>(account.EncryptedCredentialBlob);
        ICloudStorageProvider provider = _providerFactory.GetProvider(account.ProviderType);

        string fullLocalPath = Path.Combine(
            account.LocalSyncRootPath,
            virtualFile.RelativeLocalPath.TrimStart('\\', '/'));

        string? dir = Path.GetDirectoryName(fullLocalPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Eliminar cualquier reparse point previo antes de abrir el archivo para escritura
        RemoveBlockingCloudFileIfPresent(fullLocalPath);

        if (offset == 0 && File.Exists(fullLocalPath) && virtualFile.HydrationState == HydrationState.PlaceholderOnly)
        {
            try { DeleteFileW(fullLocalPath); } catch { }
        }

        long effectiveLength = length > 0 ? length : virtualFile.FileSizeBytes;

        await using (var fileStream = new FileStream(
            fullLocalPath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 65536,
            useAsync: true))
        {
            fileStream.Seek(offset, SeekOrigin.Begin);

            if (effectiveLength > 0)
            {
                await provider.DownloadRangeAsync(
                    secret,
                    virtualFile.RemoteItemId,
                    fileStream,
                    offset,
                    effectiveLength,
                    chunkSizeBytes: 256 * 1024,
                    progress: progress,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        virtualFile.HydrationState = (offset == 0 && effectiveLength >= virtualFile.FileSizeBytes)
            ? HydrationState.FullyHydrated
            : HydrationState.PartiallyHydrated;

        virtualFile.LocalModifiedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Desregistra cualquier raíz CfApi heredada y reemplaza archivos con atributos de ReparsePoint de nube
    /// que causaban errores de "tiempo de espera superado" al acceder a la unidad virtual desde Windows Explorer.
    /// </summary>
    public static void SanitizeDirectoryFromBlockingCloudReparsePoints(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return;

        try
        {
            CfApiNativeMethods.CfUnregisterSyncRoot(directoryPath);
        }
        catch
        {
            // Ignorar si no estaba registrada en CfApi
        }

        try
        {
            foreach (string filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                RemoveBlockingCloudFileIfPresent(filePath);
            }
        }
        catch
        {
            // Ignorar errores de acceso concurrente
        }
    }

    private static void RemoveBlockingCloudFileIfPresent(string fullFilePath)
    {
        try
        {
            uint attrs = GetFileAttributesW(fullFilePath);
            if (attrs != 0xFFFFFFFF && (attrs & CLOUD_BLOCKING_ATTRIBUTES) != 0)
            {
                // DeleteFileW elimina el nodo con reparse point en NTFS sin invocar la hidratación bloqueante de cldflt.sys
                DeleteFileW(fullFilePath);
            }
        }
        catch
        {
            // Ignorar
        }
    }

    public void Dispose()
    {
    }
}

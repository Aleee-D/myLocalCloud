using System.Collections.Concurrent;
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
/// Integración con el Explorador de Windows mediante Windows Cloud Files API (<c>CfApi</c>).
/// Registra raíces de sincronización ("Files On-Demand"), crea marcadores de posición (placeholders)
/// y atiende la hidratación bajo demanda por bloques cuando el usuario abre un archivo en Windows Explorer.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCloudFilesManager : IVirtualFilePlaceholderManager, IDisposable
{
    public static readonly Guid MyLocalCloudProviderGuid = new("8E52D4B0-7D2C-4E88-9F1A-A1B2C3D4E5F6");
    private const string IsolatedCfProviderName = "myLocalCloud.VFS.Provider";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtectionService _dataProtectionService;
    private readonly ICloudProviderFactory _providerFactory;
    private readonly IGoogleDriveCoexistenceService? _coexistenceService;
    private readonly IVirtualDriveMountService? _driveMountService;
    private readonly ConcurrentDictionary<string, long> _activeConnections = new(StringComparer.OrdinalIgnoreCase);

    // Mantener referencia viva al delegado nativo para evitar que el GC lo recolecte mientras CfApi está conectado
    private readonly CfApiNativeMethods.CF_CALLBACK _fetchDataCallbackDelegate;

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
        _fetchDataCallbackDelegate = OnFetchDataCallback;
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

        // Montar la letra de unidad de disco virtual seleccionada para esta cuenta (ej. M:, N:, O:)
        if (_driveMountService is not null && !string.IsNullOrWhiteSpace(account.VirtualDriveLetter))
        {
            string volumeLabel = $"myLocalCloud - {account.DisplayName} ({account.AccountEmailOrUser})";
            _driveMountService.MountVirtualDrive(account.VirtualDriveLetter, account.LocalSyncRootPath, volumeLabel);
        }

        string syncRootIdentityText = $"myLocalCloud.VFS::{account.ProviderType}::{account.Id:N}";
        byte[] identityBytes = Encoding.Unicode.GetBytes(syncRootIdentityText);

        nint providerNamePtr = Marshal.StringToHGlobalUni(IsolatedCfProviderName);
        nint providerVersionPtr = Marshal.StringToHGlobalUni(AppVersionInfo.Version);
        nint identityPtr = Marshal.AllocHGlobal(identityBytes.Length);

        try
        {
            Marshal.Copy(identityBytes, 0, identityPtr, identityBytes.Length);

            var registration = new CfApiNativeMethods.CF_SYNC_REGISTRATION
            {
                StructSize = (uint)Marshal.SizeOf<CfApiNativeMethods.CF_SYNC_REGISTRATION>(),
                ProviderName = providerNamePtr,
                ProviderVersion = providerVersionPtr,
                SyncRootIdentity = identityPtr,
                SyncRootIdentityLength = (uint)identityBytes.Length,
                ProviderId = MyLocalCloudProviderGuid
            };

            var policies = new CfApiNativeMethods.CF_SYNC_POLICIES
            {
                StructSize = (uint)Marshal.SizeOf<CfApiNativeMethods.CF_SYNC_POLICIES>(),
                Hydration = new CfApiNativeMethods.CF_HYDRATION_POLICY
                {
                    Primary = CfApiNativeMethods.CF_HYDRATION_POLICY_PRIMARY.CF_HYDRATION_POLICY_PROGRESSIVE,
                    Modifier = CfApiNativeMethods.CF_HYDRATION_POLICY_MODIFIER.CF_HYDRATION_POLICY_MODIFIER_STREAMING_ALLOWED
                             | CfApiNativeMethods.CF_HYDRATION_POLICY_MODIFIER.CF_HYDRATION_POLICY_MODIFIER_AUTO_DEHYDRATION_ALLOWED
                },
                Population = new CfApiNativeMethods.CF_POPULATION_POLICY
                {
                    Primary = CfApiNativeMethods.CF_POPULATION_POLICY_PRIMARY.CF_POPULATION_POLICY_FULL,
                    Modifier = CfApiNativeMethods.CF_POPULATION_POLICY_MODIFIER.CF_POPULATION_POLICY_MODIFIER_NONE
                }
            };

            int hr = CfApiNativeMethods.CfRegisterSyncRoot(
                account.LocalSyncRootPath,
                in registration,
                in policies,
                CfApiNativeMethods.CF_REGISTER_FLAGS.CF_REGISTER_FLAG_UPDATE);

            if (hr >= 0)
            {
                ConnectSyncRootCallbacks(account.LocalSyncRootPath);
            }
        }
        catch (DllNotFoundException)
        {
            // Fallback limpio en ediciones de SO sin cldapi.dll
        }
        finally
        {
            Marshal.FreeHGlobal(providerNamePtr);
            Marshal.FreeHGlobal(providerVersionPtr);
            Marshal.FreeHGlobal(identityPtr);
        }

        account.SyncRootIdentity = syncRootIdentityText;
        return Task.CompletedTask;
    }

    public Task UnregisterSyncRootAsync(string localSyncRootPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localSyncRootPath);

        if (_activeConnections.TryRemove(localSyncRootPath, out long connectionKey))
        {
            try { CfApiNativeMethods.CfDisconnectSyncRoot(connectionKey); } catch { }
        }

        if (Directory.Exists(localSyncRootPath))
        {
            try { CfApiNativeMethods.CfUnregisterSyncRoot(localSyncRootPath); } catch { }
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

        // Si la cuenta está configurada como Copia Local Completa, descargar el contenido en la unidad virtual
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
                // Si el archivo aún no fue persistido o es un archivo sintético de bienvenida, continuar como placeholder
            }
        }

        // Intentar primero creación nativa mediante CfCreatePlaceholders (si la raíz está registrada en CfApi)
        bool createdWithCfApi = TryCreateNativeCfPlaceholder(
            parentDir ?? account.LocalSyncRootPath,
            item.FileName,
            item.RemoteItemId,
            item.FileSizeBytes,
            item.RemoteModifiedAtUtc);

        if (!createdWithCfApi && !File.Exists(fullLocalPath))
        {
            // Fallback seguro en NTFS: crear marcador de posición de referencia (0 bytes) listo para hidratación al abrir
            using (FileStream fs = File.Create(fullLocalPath))
            {
                // Archivo placeholder de 0 bytes en la unidad virtual hasta que se abra y descargue de la nube
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

        if (offset == 0 && File.Exists(fullLocalPath) && virtualFile.HydrationState == HydrationState.PlaceholderOnly)
        {
            // Reemplazar el reparse point deshidratado al hidratar desde modo usuario para evitar bloqueo en cldflt.sys
            File.Delete(fullLocalPath);
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

    private void ConnectSyncRootCallbacks(string syncRootPath)
    {
        if (_activeConnections.ContainsKey(syncRootPath))
            return;

        nint callbackPtr = Marshal.GetFunctionPointerForDelegate(_fetchDataCallbackDelegate);
        var callbackTable = new[]
        {
            new CfApiNativeMethods.CF_CALLBACK_REGISTRATION
            {
                Type = CfApiNativeMethods.CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_DATA,
                Callback = callbackPtr
            },
            new CfApiNativeMethods.CF_CALLBACK_REGISTRATION
            {
                Type = CfApiNativeMethods.CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NONE,
                Callback = nint.Zero
            }
        };

        int hr = CfApiNativeMethods.CfConnectSyncRoot(
            syncRootPath,
            callbackTable,
            nint.Zero,
            CfApiNativeMethods.CF_CONNECT_FLAGS.CF_CONNECT_FLAG_REQUIRE_FULL_FILE_PATH,
            out long connectionKey);

        if (hr >= 0)
        {
            _activeConnections[syncRootPath] = connectionKey;
        }
    }

    private static bool TryCreateNativeCfPlaceholder(
        string parentDirectory,
        string fileName,
        string remoteItemId,
        long fileSize,
        DateTimeOffset modifiedUtc)
    {
        byte[] identityBytes = Encoding.Unicode.GetBytes(remoteItemId);
        nint relativeNamePtr = Marshal.StringToHGlobalUni(fileName);
        nint identityPtr = Marshal.AllocHGlobal(identityBytes.Length);

        try
        {
            Marshal.Copy(identityBytes, 0, identityPtr, identityBytes.Length);
            long fileTime = modifiedUtc.ToFileTime();

            var createInfo = new CfApiNativeMethods.CF_PLACEHOLDER_CREATE_INFO
            {
                RelativeFileName = relativeNamePtr,
                FileIdentity = identityPtr,
                FileIdentityLength = (uint)identityBytes.Length,
                Flags = CfApiNativeMethods.CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC
                      | CfApiNativeMethods.CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_SUPERSEDE,
                FsMetadata = new CfApiNativeMethods.CF_FS_METADATA
                {
                    FileSize = fileSize,
                    BasicInfo = new CfApiNativeMethods.FILE_BASIC_INFO
                    {
                        CreationTime = fileTime,
                        LastAccessTime = fileTime,
                        LastWriteTime = fileTime,
                        ChangeTime = fileTime,
                        FileAttributes = (uint)FileAttributes.Normal
                    }
                }
            };

            var array = new[] { createInfo };
            int hr = CfApiNativeMethods.CfCreatePlaceholders(
                parentDirectory,
                array,
                1,
                CfApiNativeMethods.CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC,
                out uint processed);

            return hr >= 0 && processed == 1;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(relativeNamePtr);
            Marshal.FreeHGlobal(identityPtr);
        }
    }

    private void OnFetchDataCallback(
        in CfApiNativeMethods.CF_CALLBACK_INFO callbackInfo,
        nint callbackParameters)
    {
        if (callbackParameters == nint.Zero || callbackInfo.FileIdentity == nint.Zero)
            return;

        var fetchParams = Marshal.PtrToStructure<CfApiNativeMethods.CF_CALLBACK_PARAMETERS_FETCH_DATA>(callbackParameters);
        string remoteItemId = Marshal.PtrToStringUni(callbackInfo.FileIdentity, (int)(callbackInfo.FileIdentityLength / 2)) ?? string.Empty;

        long connectionKey = callbackInfo.ConnectionKey;
        long transferKey = callbackInfo.TransferKey;
        long requestKey = callbackInfo.RequestKey;
        long requiredOffset = fetchParams.RequiredFileOffset;
        long requiredLength = fetchParams.RequiredLength;

        _ = Task.Run(async () =>
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();

                VirtualFileItem? vFile = await db.VirtualFiles
                    .Include(v => v.CloudAccount)
                    .FirstOrDefaultAsync(v => v.RemoteItemId == remoteItemId)
                    .ConfigureAwait(false);

                if (vFile?.CloudAccount is null)
                    return;

                CredentialSecret secret = _dataProtectionService.UnprotectSecret<CredentialSecret>(vFile.CloudAccount.EncryptedCredentialBlob);
                ICloudStorageProvider provider = _providerFactory.GetProvider(vFile.CloudAccount.ProviderType);

                await using var memoryStream = new MemoryStream((int)Math.Min(requiredLength, 64 * 1024 * 1024));
                await provider.DownloadRangeAsync(
                    secret,
                    remoteItemId,
                    memoryStream,
                    requiredOffset,
                    requiredLength).ConfigureAwait(false);

                byte[] downloadedBytes = memoryStream.ToArray();
                TransferHydratedBufferToCfApi(connectionKey, transferKey, requestKey, requiredOffset, downloadedBytes);

                vFile.HydrationState = HydrationState.FullyHydrated;
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            catch
            {
                // En caso de fallo de red durante hidratación en vivo, CfApi abortará la transferencia de forma segura
            }
        });
    }

    private static unsafe void TransferHydratedBufferToCfApi(
        long connectionKey,
        long transferKey,
        long requestKey,
        long requiredOffset,
        byte[] downloadedBytes)
    {
        fixed (byte* bufferPtr = downloadedBytes)
        {
            var opInfo = new CfApiNativeMethods.CF_OPERATION_INFO
            {
                StructSize = (uint)Marshal.SizeOf<CfApiNativeMethods.CF_OPERATION_INFO>(),
                Type = CfApiNativeMethods.CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_DATA,
                ConnectionKey = connectionKey,
                TransferKey = transferKey,
                RequestKey = requestKey
            };

            var opParams = new CfApiNativeMethods.CF_OPERATION_PARAMETERS_TRANSFER_DATA
            {
                ParamSize = (uint)Marshal.SizeOf<CfApiNativeMethods.CF_OPERATION_PARAMETERS_TRANSFER_DATA>(),
                CompletionStatus = 0, // STATUS_SUCCESS
                Buffer = (nint)bufferPtr,
                Offset = requiredOffset,
                Length = downloadedBytes.Length
            };

            CfApiNativeMethods.CfExecute(in opInfo, ref opParams);
        }
    }

    public void Dispose()
    {
        foreach (KeyValuePair<string, long> kvp in _activeConnections)
        {
            try { CfApiNativeMethods.CfDisconnectSyncRoot(kvp.Value); } catch { }
        }
        _activeConnections.Clear();
    }
}

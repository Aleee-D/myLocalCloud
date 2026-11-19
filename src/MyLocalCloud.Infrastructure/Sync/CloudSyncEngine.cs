using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyLocalCloud.Domain.Entities;
using MyLocalCloud.Domain.Enums;
using MyLocalCloud.Domain.Interfaces;
using MyLocalCloud.Domain.Models;
using MyLocalCloud.Infrastructure.Persistence;

namespace MyLocalCloud.Infrastructure.Sync;

/// <summary>
/// Motor de sincronización en segundo plano de myLocalCloud.
/// Compara periódicamente el estado remoto de cada nube con la base de datos local cifrada (SQLCipher AES-256),
/// crea placeholders virtuales (0 bytes) para nuevos archivos y ejecuta la cola de transferencias por prioridad.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CloudSyncEngine : ICloudSyncEngine, IDisposable, IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtectionService _dataProtectionService;
    private readonly ICloudProviderFactory _providerFactory;
    private readonly IVirtualFilePlaceholderManager? _placeholderManager;
    private readonly SemaphoreSlim _syncMutex = new(1, 1);

    private CancellationTokenSource? _periodicCts;
    private Task? _periodicLoopTask;
    private SyncStatusSnapshot _currentStatus = new(
        IsSyncing: false,
        IsPaused: false,
        StatusMessage: "myLocalCloud está actualizado",
        PendingQueueCount: 0,
        LastSyncCompletedAtUtc: null,
        NextScheduledSyncAtUtc: null);

    public CloudSyncEngine(
        IServiceScopeFactory scopeFactory,
        IDataProtectionService dataProtectionService,
        ICloudProviderFactory providerFactory,
        IVirtualFilePlaceholderManager? placeholderManager = null)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(dataProtectionService);
        ArgumentNullException.ThrowIfNull(providerFactory);

        _scopeFactory = scopeFactory;
        _dataProtectionService = dataProtectionService;
        _providerFactory = providerFactory;
        _placeholderManager = placeholderManager;
    }

    public event EventHandler<SyncStatusSnapshot>? StatusChanged;

    public SyncStatusSnapshot CurrentStatus => _currentStatus;

    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromMinutes(2);

    public void StartPeriodicSync(TimeSpan? interval = null)
    {
        if (interval.HasValue && interval.Value > TimeSpan.Zero)
        {
            SyncInterval = interval.Value;
        }

        if (_periodicLoopTask is { IsCompleted: false })
            return;

        _periodicCts = new CancellationTokenSource();
        _periodicLoopTask = Task.Run(() => RunPeriodicSyncLoopAsync(_periodicCts.Token));
    }

    public void PausePeriodicSync()
    {
        UpdateStatus(_currentStatus with
        {
            IsPaused = true,
            StatusMessage = "Sincronización en pausa"
        });
    }

    public void ResumePeriodicSync()
    {
        UpdateStatus(_currentStatus with
        {
            IsPaused = false,
            StatusMessage = "Sincronización periódica activa",
            NextScheduledSyncAtUtc = DateTimeOffset.UtcNow.Add(SyncInterval)
        });
    }

    private async Task RunPeriodicSyncLoopAsync(CancellationToken cancellationToken)
    {
        // Primera sincronización al iniciar el servicio
        try
        {
            await SyncAllAccountsOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            // El estado de error ya fue reportado por SyncAllAccountsOnceAsync
        }

        using var timer = new PeriodicTimer(SyncInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_currentStatus.IsPaused)
                continue;

            try
            {
                await SyncAllAccountsOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Continuar el bucle periódico en el siguiente tick
            }
        }
    }

    public async Task<int> SyncAllAccountsOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!await _syncMutex.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        try
        {
            UpdateStatus(_currentStatus with
            {
                IsSyncing = true,
                StatusMessage = "Comprobando cambios en las nubes conectadas..."
            });

            int totalChangesProcessed = 0;

            using IServiceScope scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();
            await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

            List<Guid> activeAccountIds = await dbContext.CloudAccounts
                .Where(a => a.IsEnabled)
                .Select(a => a.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (Guid accountId in activeAccountIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                totalChangesProcessed += await ReconcileAccountChangesCoreAsync(dbContext, accountId, cancellationToken).ConfigureAwait(false);
                totalChangesProcessed += await ProcessPendingQueueCoreAsync(dbContext, accountId, cancellationToken).ConfigureAwait(false);
            }

            int remainingQueue = await dbContext.SyncQueue.CountAsync(cancellationToken).ConfigureAwait(false);
            DateTimeOffset now = DateTimeOffset.UtcNow;

            UpdateStatus(new SyncStatusSnapshot(
                IsSyncing: false,
                IsPaused: _currentStatus.IsPaused,
                StatusMessage: remainingQueue == 0
                    ? $"Sincronizado ({now.ToLocalTime():HH:mm:ss})"
                    : $"Pendientes en cola: {remainingQueue}",
                PendingQueueCount: remainingQueue,
                LastSyncCompletedAtUtc: now,
                NextScheduledSyncAtUtc: now.Add(SyncInterval)));

            return totalChangesProcessed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            UpdateStatus(_currentStatus with
            {
                IsSyncing = false,
                StatusMessage = $"Error de sincronización: {ex.Message}"
            });
            throw;
        }
        finally
        {
            _syncMutex.Release();
        }
    }

    public async Task<int> ReconcileAccountChangesAsync(Guid cloudAccountId, CancellationToken cancellationToken = default)
    {
        await _syncMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();
            await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

            return await ReconcileAccountChangesCoreAsync(dbContext, cloudAccountId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _syncMutex.Release();
        }
    }

    public async Task<int> ProcessPendingQueueAsync(Guid? cloudAccountId = null, CancellationToken cancellationToken = default)
    {
        await _syncMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MyLocalCloudDbContext>();
            await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

            return await ProcessPendingQueueCoreAsync(dbContext, cloudAccountId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _syncMutex.Release();
        }
    }

    private async Task<int> ReconcileAccountChangesCoreAsync(
        MyLocalCloudDbContext dbContext,
        Guid cloudAccountId,
        CancellationToken cancellationToken)
    {
        CloudAccount? account = await dbContext.CloudAccounts
            .Include(a => a.VirtualFiles)
            .SingleOrDefaultAsync(a => a.Id == cloudAccountId, cancellationToken)
            .ConfigureAwait(false);

        if (account is null || !account.IsEnabled)
            return 0;

        if (_placeholderManager is not null)
        {
            await _placeholderManager.RegisterSyncRootAsync(account, cancellationToken).ConfigureAwait(false);
        }

        CredentialSecret secret = _dataProtectionService.UnprotectSecret<CredentialSecret>(account.EncryptedCredentialBlob);
        ICloudStorageProvider provider = _providerFactory.GetProvider(account.ProviderType);

        // Si el token expiró, refrescarlo y re-encriptarlo con DPAPI
        if (secret.IsAccessTokenExpired(TimeSpan.FromMinutes(2)) && !string.IsNullOrWhiteSpace(secret.RefreshToken))
        {
            secret = await provider.RefreshAccessTokenAsync(secret, cancellationToken).ConfigureAwait(false);
            account.EncryptedCredentialBlob = _dataProtectionService.ProtectSecret(secret);
        }

        int changesApplied = 0;
        string? cursor = account.RemoteDeltaCursor;

        DeltaSyncPage deltaPage = await provider.GetChangesAsync(secret, cursor, cancellationToken).ConfigureAwait(false);

        // 1. Procesar altas y modificaciones remotas
        foreach (RemoteCloudItem remoteItem in deltaPage.UpsertedItems)
        {
            VirtualFileItem? existingItem = account.VirtualFiles
                .FirstOrDefault(v => v.RemoteItemId == remoteItem.RemoteId);

            string normalizedRelativePath = NormalizeRelativePath(remoteItem.Name);

            if (existingItem is null)
            {
                var newItem = new VirtualFileItem
                {
                    CloudAccountId = account.Id,
                    RemoteItemId = remoteItem.RemoteId,
                    RemoteParentId = remoteItem.ParentId,
                    RelativeLocalPath = normalizedRelativePath,
                    FileName = remoteItem.Name,
                    IsDirectory = remoteItem.IsDirectory,
                    FileSizeBytes = remoteItem.SizeBytes,
                    ContentHashOrETag = remoteItem.ContentHashOrETag,
                    HydrationState = HydrationState.PlaceholderOnly,
                    SyncState = FileSyncState.InSync,
                    RemoteModifiedAtUtc = remoteItem.ModifiedAtUtc,
                    LocalModifiedAtUtc = remoteItem.ModifiedAtUtc
                };

                dbContext.VirtualFiles.Add(newItem);
                account.VirtualFiles.Add(newItem);

                if (_placeholderManager is not null)
                {
                    await _placeholderManager.EnsurePlaceholderCreatedAsync(account, newItem, cancellationToken).ConfigureAwait(false);
                }

                changesApplied++;
            }
            else if (existingItem.ContentHashOrETag != remoteItem.ContentHashOrETag ||
                     existingItem.FileSizeBytes != remoteItem.SizeBytes ||
                     existingItem.FileName != remoteItem.Name)
            {
                // Detectar conflicto si el archivo local también tiene una subida pendiente
                if (existingItem.SyncState == FileSyncState.PendingUpload)
                {
                    existingItem.SyncState = FileSyncState.Conflict;
                }
                else
                {
                    existingItem.FileName = remoteItem.Name;
                    existingItem.RelativeLocalPath = normalizedRelativePath;
                    existingItem.FileSizeBytes = remoteItem.SizeBytes;
                    existingItem.ContentHashOrETag = remoteItem.ContentHashOrETag;
                    existingItem.RemoteModifiedAtUtc = remoteItem.ModifiedAtUtc;

                    // Si el archivo estaba hidratado o anclado localmente, encolar descarga de actualización
                    if (existingItem.HydrationState is HydrationState.FullyHydrated or HydrationState.PinnedLocally)
                    {
                        existingItem.SyncState = FileSyncState.PendingDownload;
                        dbContext.SyncQueue.Add(new SyncQueueItem
                        {
                            CloudAccountId = account.Id,
                            VirtualFileItemId = existingItem.Id,
                            OperationType = SyncOperationType.Download,
                            RelativePath = existingItem.RelativeLocalPath,
                            RemoteItemId = existingItem.RemoteItemId,
                            Priority = 5
                        });
                    }
                    else if (_placeholderManager is not null)
                    {
                        await _placeholderManager.EnsurePlaceholderCreatedAsync(account, existingItem, cancellationToken).ConfigureAwait(false);
                    }
                }

                changesApplied++;
            }
        }

        // 2. Procesar eliminaciones remotas
        foreach (string deletedRemoteId in deltaPage.DeletedRemoteIds)
        {
            VirtualFileItem? itemToRemove = account.VirtualFiles
                .FirstOrDefault(v => v.RemoteItemId == deletedRemoteId);

            if (itemToRemove is not null)
            {
                string fullLocalPath = Path.Combine(
                    account.LocalSyncRootPath,
                    itemToRemove.RelativeLocalPath.TrimStart('\\', '/'));

                if (File.Exists(fullLocalPath))
                {
                    try { File.Delete(fullLocalPath); } catch { /* Ignorar si está abierto */ }
                }

                dbContext.VirtualFiles.Remove(itemToRemove);
                changesApplied++;
            }
        }

        account.RemoteDeltaCursor = deltaPage.NewDeltaCursor;
        changesApplied += ScanLocalVirtualDriveFiles(dbContext, account);
        account.LastSyncedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return changesApplied;
    }

    /// <summary>
    /// Escanea los archivos colocados o creados por el usuario dentro de la unidad de disco virtual (ej. M:\, N:\)
    /// para indexarlos en la base de datos cifrada SQLCipher y encolar su sincronización.
    /// </summary>
    private static int ScanLocalVirtualDriveFiles(MyLocalCloudDbContext dbContext, CloudAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.LocalSyncRootPath) || !Directory.Exists(account.LocalSyncRootPath))
            return 0;

        int discovered = 0;
        try
        {
            string[] allFiles = Directory.GetFiles(account.LocalSyncRootPath, "*", SearchOption.AllDirectories);
            foreach (string fullFilePath in allFiles)
            {
                string relativePath = "\\" + Path.GetRelativePath(account.LocalSyncRootPath, fullFilePath).Replace('/', '\\').TrimStart('\\');

                bool alreadyTracked = account.VirtualFiles.Any(
                    v => string.Equals(v.RelativeLocalPath, relativePath, StringComparison.OrdinalIgnoreCase));

                if (!alreadyTracked)
                {
                    var fi = new FileInfo(fullFilePath);
                    var localItem = new VirtualFileItem
                    {
                        CloudAccountId = account.Id,
                        RemoteItemId = $"local_vfs_{Guid.NewGuid():N}",
                        RelativeLocalPath = relativePath,
                        FileName = fi.Name,
                        IsDirectory = false,
                        FileSizeBytes = fi.Length,
                        HydrationState = fi.Length > 0 ? HydrationState.FullyHydrated : HydrationState.PlaceholderOnly,
                        SyncState = fi.Length > 0 ? FileSyncState.PendingUpload : FileSyncState.InSync,
                        LocalModifiedAtUtc = new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero),
                        RemoteModifiedAtUtc = new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero)
                    };

                    dbContext.VirtualFiles.Add(localItem);
                    account.VirtualFiles.Add(localItem);

                    if (fi.Length > 0)
                    {
                        dbContext.SyncQueue.Add(new SyncQueueItem
                        {
                            CloudAccountId = account.Id,
                            VirtualFileItemId = localItem.Id,
                            OperationType = SyncOperationType.Upload,
                            RelativePath = relativePath,
                            Priority = 10
                        });
                    }

                    discovered++;
                }
            }
        }
        catch
        {
            // Ignorar archivos bloqueados temporalmente por otra aplicación
        }

        return discovered;
    }

    private async Task<int> ProcessPendingQueueCoreAsync(
        MyLocalCloudDbContext dbContext,
        Guid? cloudAccountId,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        IQueryable<SyncQueueItem> query = dbContext.SyncQueue
            .Include(q => q.CloudAccount)
            .Include(q => q.VirtualFileItem);

        if (cloudAccountId.HasValue)
        {
            query = query.Where(q => q.CloudAccountId == cloudAccountId.Value);
        }

        List<SyncQueueItem> rawQueueItems = await query
            .OrderBy(q => q.Priority)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<SyncQueueItem> pendingItems = rawQueueItems
            .Where(q => q.NextAttemptAtUtc == null || q.NextAttemptAtUtc <= now)
            .OrderBy(q => q.Priority)
            .ThenBy(q => q.EnqueuedAtUtc)
            .Take(50)
            .ToList();

        int completedCount = 0;

        foreach (SyncQueueItem queueItem in pendingItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (queueItem.CloudAccount is null || !queueItem.CloudAccount.IsEnabled)
                continue;

            CloudAccount account = queueItem.CloudAccount;
            CredentialSecret secret = _dataProtectionService.UnprotectSecret<CredentialSecret>(account.EncryptedCredentialBlob);
            ICloudStorageProvider provider = _providerFactory.GetProvider(account.ProviderType);

            string fullLocalPath = Path.Combine(
                account.LocalSyncRootPath,
                queueItem.RelativePath.TrimStart('\\', '/'));

            try
            {
                switch (queueItem.OperationType)
                {
                    case SyncOperationType.Upload:
                    {
                        if (!File.Exists(fullLocalPath))
                        {
                            dbContext.SyncQueue.Remove(queueItem);
                            break;
                        }

                        await using FileStream fs = new(fullLocalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, useAsync: true);
                        RemoteCloudItem uploaded = await provider.UploadChunkedAsync(
                            secret,
                            fs,
                            Path.GetFileName(fullLocalPath),
                            parentFolderId: queueItem.VirtualFileItem?.RemoteParentId,
                            cancellationToken: cancellationToken).ConfigureAwait(false);

                        if (queueItem.VirtualFileItem is not null)
                        {
                            queueItem.VirtualFileItem.RemoteItemId = uploaded.RemoteId;
                            queueItem.VirtualFileItem.ContentHashOrETag = uploaded.ContentHashOrETag;
                            queueItem.VirtualFileItem.FileSizeBytes = uploaded.SizeBytes;
                            queueItem.VirtualFileItem.SyncState = FileSyncState.InSync;
                            queueItem.VirtualFileItem.RemoteModifiedAtUtc = uploaded.ModifiedAtUtc;
                        }

                        dbContext.SyncQueue.Remove(queueItem);
                        completedCount++;
                        break;
                    }

                    case SyncOperationType.Download:
                    {
                        if (string.IsNullOrEmpty(queueItem.RemoteItemId))
                        {
                            dbContext.SyncQueue.Remove(queueItem);
                            break;
                        }

                        string? dir = Path.GetDirectoryName(fullLocalPath);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);

                        long targetSize = queueItem.VirtualFileItem?.FileSizeBytes ?? 0;
                        await using FileStream fs = new(fullLocalPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 65536, useAsync: true);
                        if (targetSize > 0)
                        {
                            await provider.DownloadRangeAsync(
                                secret,
                                queueItem.RemoteItemId,
                                fs,
                                offset: 0,
                                length: targetSize,
                                cancellationToken: cancellationToken).ConfigureAwait(false);
                        }

                        if (queueItem.VirtualFileItem is not null)
                        {
                            queueItem.VirtualFileItem.HydrationState = HydrationState.FullyHydrated;
                            queueItem.VirtualFileItem.SyncState = FileSyncState.InSync;
                        }

                        dbContext.SyncQueue.Remove(queueItem);
                        completedCount++;
                        break;
                    }

                    case SyncOperationType.DeleteRemote:
                    {
                        if (!string.IsNullOrEmpty(queueItem.RemoteItemId))
                        {
                            await provider.DeleteItemAsync(secret, queueItem.RemoteItemId, cancellationToken).ConfigureAwait(false);
                        }

                        dbContext.SyncQueue.Remove(queueItem);
                        completedCount++;
                        break;
                    }

                    default:
                        dbContext.SyncQueue.Remove(queueItem);
                        completedCount++;
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                queueItem.RetryCount++;
                queueItem.LastErrorMessage = ex.Message;
                int backoffSeconds = (int)Math.Min(300, Math.Pow(2, queueItem.RetryCount) * 5);
                queueItem.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(backoffSeconds);

                if (queueItem.VirtualFileItem is not null)
                {
                    queueItem.VirtualFileItem.SyncState = FileSyncState.Error;
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return completedCount;
    }

    private static string NormalizeRelativePath(string pathOrName)
    {
        string trimmed = pathOrName.Replace('/', '\\').TrimStart('\\');
        return "\\" + trimmed;
    }

    private void UpdateStatus(SyncStatusSnapshot newSnapshot)
    {
        _currentStatus = newSnapshot;
        StatusChanged?.Invoke(this, newSnapshot);
    }

    public void Dispose()
    {
        if (_periodicCts is not null)
        {
            try { _periodicCts.Cancel(); } catch { }
            _periodicCts.Dispose();
            _periodicCts = null;
        }
        _syncMutex.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_periodicCts is not null)
        {
            await _periodicCts.CancelAsync().ConfigureAwait(false);
            if (_periodicLoopTask is not null)
            {
                try { await _periodicLoopTask.ConfigureAwait(false); } catch { /* Ignorar */ }
            }
            _periodicCts.Dispose();
            _periodicCts = null;
        }
        _syncMutex.Dispose();
    }
}

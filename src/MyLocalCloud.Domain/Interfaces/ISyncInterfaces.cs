using MyLocalCloud.Domain.Entities;
using MyLocalCloud.Domain.Models;

namespace MyLocalCloud.Domain.Interfaces;

/// <summary>
/// Instantánea del estado en vivo del motor de sincronización para la bandeja del sistema (System Tray) y la GUI.
/// </summary>
public sealed record SyncStatusSnapshot(
    bool IsSyncing,
    bool IsPaused,
    string StatusMessage,
    int PendingQueueCount,
    DateTimeOffset? LastSyncCompletedAtUtc,
    DateTimeOffset? NextScheduledSyncAtUtc);

/// <summary>
/// Abstracción para el gestor de raíces virtuales y marcadores de posición (placeholders) de Windows CfApi.
/// </summary>
public interface IVirtualFilePlaceholderManager
{
    /// <summary>
    /// Registra una carpeta local como raíz de sincronización (Sync Root) en el Explorador de Windows.
    /// </summary>
    Task RegisterSyncRootAsync(CloudAccount account, CancellationToken cancellationToken = default);

    /// <summary>
    /// Desregistra una raíz de sincronización del Explorador de Windows.
    /// </summary>
    Task UnregisterSyncRootAsync(string localSyncRootPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea o actualiza un marcador de posición (placeholder de 0 bytes en disco) para un archivo o carpeta remota.
    /// </summary>
    Task EnsurePlaceholderCreatedAsync(
        CloudAccount account,
        VirtualFileItem item,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hidrata (descarga bajo demanda por chunks) un archivo solicitado por el Explorador de Windows.
    /// </summary>
    Task HydratePlaceholderRangeAsync(
        Guid cloudAccountId,
        string relativeLocalPath,
        long offset,
        long length,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Contrato del motor de sincronización periódica y reconciliación de estado remoto vs. SQLCipher local.
/// </summary>
public interface ICloudSyncEngine
{
    event EventHandler<SyncStatusSnapshot>? StatusChanged;

    SyncStatusSnapshot CurrentStatus { get; }

    TimeSpan SyncInterval { get; set; }

    void StartPeriodicSync(TimeSpan? interval = null);

    void PausePeriodicSync();

    void ResumePeriodicSync();

    /// <summary>
    /// Ejecuta inmediatamente un ciclo de reconciliación delta y procesamiento de cola para todas las cuentas activas.
    /// </summary>
    Task<int> SyncAllAccountsOnceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconcilia los cambios remotos de una cuenta específica contra la base de datos local cifrada.
    /// </summary>
    Task<int> ReconcileAccountChangesAsync(Guid cloudAccountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Procesa las tareas pendientes en la cola <see cref="SyncQueueItem"/> ordenadas por prioridad.
    /// </summary>
    Task<int> ProcessPendingQueueAsync(Guid? cloudAccountId = null, CancellationToken cancellationToken = default);
}

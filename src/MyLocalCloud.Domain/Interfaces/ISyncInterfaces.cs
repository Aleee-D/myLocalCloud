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
/// Información detallada de una letra de unidad de disco virtual montada en Windows.
/// </summary>
public sealed record MountedVirtualDriveInfo(
    string DriveLetter,
    string TargetFolderPath,
    string VolumeLabel,
    bool IsUsedByActiveAccount,
    string? AssignedAccountName);

/// <summary>
/// Servicio que monta, administra y desmonta letras de unidad de disco virtual en Windows (ej. M:, N:, O:, Z:)
/// asociadas a las raíces virtuales de cada cuenta de usuario multicloud.
/// </summary>
public interface IVirtualDriveMountService
{
    /// <summary>
    /// Devuelve las letras de unidad disponibles en el sistema (excluyendo unidades físicas y letras ya asignadas).
    /// </summary>
    IReadOnlyList<string> GetAvailableDriveLetters(IEnumerable<string>? currentlyAssignedLetters = null);

    /// <summary>
    /// Obtiene todas las unidades de disco virtuales (<c>DefineDosDeviceW</c> / <c>\??\...</c>) montadas actualmente en Windows,
    /// indicando cuáles pertenecen a cuentas activas y cuáles están sin uso (huérfanas) para poder eliminarlas.
    /// </summary>
    IReadOnlyList<MountedVirtualDriveInfo> GetAllMountedVirtualDrives(IReadOnlyDictionary<string, string>? activeAccountDrives = null);

    /// <summary>
    /// Desmonta y elimina todas las unidades de disco virtuales que no estén asignadas a ninguna cuenta activa.
    /// </summary>
    int RemoveAllUnusedVirtualDrives(IEnumerable<string> activeAccountDriveLetters);

    /// <summary>
    /// Normaliza una letra de unidad al formato "X:".
    /// </summary>
    string NormalizeDriveLetter(string driveLetter);

    /// <summary>
    /// Monta una carpeta de sincronización VFS como una letra de unidad de disco virtual en el Explorador de Windows.
    /// </summary>
    bool MountVirtualDrive(string driveLetter, string localSyncRootPath, string volumeLabel);

    /// <summary>
    /// Desmonta una letra de unidad de disco virtual previamente registrada.
    /// </summary>
    bool UnmountVirtualDrive(string driveLetter);

    /// <summary>
    /// Verifica si la letra de unidad indicada está montada actualmente.
    /// </summary>
    bool IsDriveMounted(string driveLetter);
}

/// <summary>
/// Abstracción para el gestor de raíces virtuales y marcadores de posición (placeholders) de Windows CfApi.
/// </summary>
public interface IVirtualFilePlaceholderManager
{
    /// <summary>
    /// Registra una carpeta local como raíz de sincronización (Sync Root) en el Explorador de Windows
    /// y activa la vigilancia de hidratación bajo demanda al abrir archivos.
    /// </summary>
    Task RegisterSyncRootAsync(CloudAccount account, CancellationToken cancellationToken = default);

    /// <summary>
    /// Desregistra una raíz de sincronización del Explorador de Windows.
    /// </summary>
    Task UnregisterSyncRootAsync(string localSyncRootPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea o actualiza un marcador de posición (referencia de 0 bytes en disco o copia local según configuración)
    /// para un archivo o carpeta remota dentro de la unidad virtual de la cuenta.
    /// </summary>
    Task EnsurePlaceholderCreatedAsync(
        CloudAccount account,
        VirtualFileItem item,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hidrata (descarga bajo demanda por chunks en el mismo momento en que se abre) un archivo de referencia.
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

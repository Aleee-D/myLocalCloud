namespace MyLocalCloud.Domain.Enums;

/// <summary>
/// Proveedores de almacenamiento en la nube soportados por myLocalCloud.
/// </summary>
public enum CloudProviderType
{
    GoogleDrive = 1,
    OneDrive = 2,
    Dropbox = 3,
    Ftp = 4,
    Mega = 5
}

/// <summary>
/// Estado de hidratación del archivo dentro del Sistema de Archivos Virtual (Windows CfApi).
/// </summary>
public enum HydrationState
{
    /// <summary>
    /// Solo existe el marcador de posición (placeholder) en el disco local; 0 bytes ocupados.
    /// </summary>
    PlaceholderOnly = 0,

    /// <summary>
    /// El archivo se ha descargado parcialmente (hidratación por rangos/chunks).
    /// </summary>
    PartiallyHydrated = 1,

    /// <summary>
    /// El archivo completo está disponible localmente en disco.
    /// </summary>
    FullyHydrated = 2,

    /// <summary>
    /// El usuario marcó "Mantener siempre en este dispositivo".
    /// </summary>
    PinnedLocally = 3
}

/// <summary>
/// Estado de sincronización de un ítem entre el VFS local y el proveedor remoto.
/// </summary>
public enum FileSyncState
{
    InSync = 0,
    PendingUpload = 1,
    PendingDownload = 2,
    Syncing = 3,
    Conflict = 4,
    Error = 5
}

/// <summary>
/// Tipo de operación encolada en el motor de sincronización.
/// </summary>
public enum SyncOperationType
{
    Upload = 1,
    Download = 2,
    DeleteRemote = 3,
    DeleteLocal = 4,
    RenameOrMove = 5,
    CreateDirectory = 6
}

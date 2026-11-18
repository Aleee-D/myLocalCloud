using MyLocalCloud.Domain.Enums;

namespace MyLocalCloud.Domain.Entities;

/// <summary>
/// Metadatos de un archivo o directorio dentro del Sistema de Archivos Virtual (VFS).
/// Mapea la identidad remota del proveedor con el placeholder local de Windows CfApi.
/// </summary>
public sealed class VirtualFileItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CloudAccountId { get; set; }

    public CloudAccount? CloudAccount { get; set; }

    /// <summary>
    /// ID único del archivo o carpeta en la nube remota (ej. FileId en Google Drive o NodeId en MEGA).
    /// </summary>
    public required string RemoteItemId { get; set; }

    /// <summary>
    /// ID del directorio padre en el proveedor remoto.
    /// </summary>
    public string? RemoteParentId { get; set; }

    /// <summary>
    /// Ruta relativa respecto a la raíz de sincronización de la cuenta (ej. "\Documentos\Informe.pdf").
    /// </summary>
    public required string RelativeLocalPath { get; set; }

    public required string FileName { get; set; }

    public bool IsDirectory { get; set; }

    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Hash o firma de versión del archivo remoto (SHA-256, MD5 o ETag según el proveedor).
    /// </summary>
    public string? ContentHashOrETag { get; set; }

    /// <summary>
    /// FileId local de NTFS asignado por el sistema de archivos cuando el placeholder es creado.
    /// </summary>
    public long? LocalNtfsFileId { get; set; }

    public HydrationState HydrationState { get; set; } = HydrationState.PlaceholderOnly;

    public FileSyncState SyncState { get; set; } = FileSyncState.InSync;

    public DateTimeOffset RemoteModifiedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LocalModifiedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

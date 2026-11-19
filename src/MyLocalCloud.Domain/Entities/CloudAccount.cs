using MyLocalCloud.Domain.Enums;

namespace MyLocalCloud.Domain.Entities;

/// <summary>
/// Representa una cuenta de usuario conectada a un proveedor (Google Drive, OneDrive, Dropbox, FTP o MEGA).
/// Permite registrar múltiples cuentas del mismo proveedor o de distintos proveedores, asignando a cada una
/// su propia letra de unidad de disco virtual (ej. M:, N:, O:) para aprovechar el espacio de cada cuenta.
/// </summary>
public sealed class CloudAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public CloudProviderType ProviderType { get; set; }

    /// <summary>
    /// Nombre descriptivo elegido por el usuario (ej. "Google Drive Personal 1", "Google Drive Trabajo 2", "MEGA Fotos").
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Identificador o correo electrónico del usuario en el proveedor remoto.
    /// </summary>
    public required string AccountEmailOrUser { get; set; }

    /// <summary>
    /// Letra de unidad de disco virtual asignada en Windows Explorer (ej. "M:", "N:", "O:", "Z:").
    /// </summary>
    public string VirtualDriveLetter { get; set; } = "M:";

    /// <summary>
    /// Modo de entrega de archivos en la unidad virtual: Referencia On-Demand (descarga al abrir) o Copia Local.
    /// </summary>
    public CloudFileDeliveryMode DeliveryMode { get; set; } = CloudFileDeliveryMode.OnDemandReference;

    /// <summary>
    /// Ruta absoluta de la raíz de sincronización local respaldada en NTFS y registrada en Windows CfApi.
    /// </summary>
    public required string LocalSyncRootPath { get; set; }

    /// <summary>
    /// Identificador de la raíz de sincronización registrado en Windows Cloud Files API.
    /// </summary>
    public string? SyncRootIdentity { get; set; }

    /// <summary>
    /// Payload cifrado con Windows DPAPI (CurrentUser + Entropía de Aplicación) que contiene el <see cref="Models.CredentialSecret"/>.
    /// </summary>
    public required byte[] EncryptedCredentialBlob { get; set; }

    /// <summary>
    /// Cursor o token delta del proveedor remoto (pageToken en Google Drive, deltaLink en OneDrive, cursor en Dropbox).
    /// </summary>
    public string? RemoteDeltaCursor { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastSyncedAtUtc { get; set; }

    public ICollection<VirtualFileItem> VirtualFiles { get; set; } = new List<VirtualFileItem>();

    public ICollection<SyncQueueItem> SyncQueueItems { get; set; } = new List<SyncQueueItem>();
}

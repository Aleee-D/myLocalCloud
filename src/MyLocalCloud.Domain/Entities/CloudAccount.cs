using MyLocalCloud.Domain.Enums;

namespace MyLocalCloud.Domain.Entities;

/// <summary>
/// Representa una cuenta conectada a un proveedor (Google Drive, OneDrive, Dropbox, FTP o MEGA).
/// El blob de credenciales (<see cref="EncryptedCredentialBlob"/>) está protegido con Windows DPAPI
/// además de residir en la base de datos SQLCipher cifrada con AES-256.
/// </summary>
public sealed class CloudAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public CloudProviderType ProviderType { get; set; }

    /// <summary>
    /// Nombre descriptivo elegido por el usuario (ej. "Google Drive Trabajo", "MEGA Backups").
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Identificador o correo electrónico de la cuenta en el proveedor remoto.
    /// </summary>
    public required string AccountEmailOrUser { get; set; }

    /// <summary>
    /// Ruta absoluta de la raíz de sincronización local registrada en Windows CfApi
    /// (ej. C:\Users\Usuario\myLocalCloud\GoogleDrive_Trabajo).
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

using MyLocalCloud.Domain.Enums;
using MyLocalCloud.Domain.Models;

namespace MyLocalCloud.Domain.Interfaces;

/// <summary>
/// Contrato unificado para los conectores de almacenamiento multicloud (Google Drive, OneDrive, Dropbox, FTP, MEGA).
/// </summary>
public interface ICloudStorageProvider
{
    CloudProviderType ProviderType { get; }

    /// <summary>
    /// Intercambia un código de autorización OAuth 2.0 por tokens de acceso y refresco.
    /// </summary>
    Task<CredentialSecret> ExchangeAuthorizationCodeAsync(
        string authorizationCode,
        string redirectUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresca un AccessToken expirado utilizando el RefreshToken almacenado.
    /// </summary>
    Task<CredentialSecret> RefreshAccessTokenAsync(
        CredentialSecret currentSecret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista todos los elementos hijos de un directorio remoto (maneja paginación automáticamente).
    /// </summary>
    Task<IReadOnlyList<RemoteCloudItem>> ListDirectoryAsync(
        CredentialSecret secret,
        string? remoteFolderId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene una página de cambios incrementales desde el último cursor delta conocido.
    /// </summary>
    Task<DeltaSyncPage> GetChangesAsync(
        CredentialSecret secret,
        string? deltaCursor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Descarga asíncronamente un rango de bytes de un archivo remoto en bloques (chunks).
    /// Diseñado para alimentar directamente las llamadas de hidratación de Windows CfApi.
    /// </summary>
    Task DownloadRangeAsync(
        CredentialSecret secret,
        string remoteItemId,
        Stream destination,
        long offset,
        long length,
        int chunkSizeBytes = 262_144,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sube un archivo grande al proveedor remoto mediante sesiones de subida resumible por chunks.
    /// </summary>
    Task<RemoteCloudItem> UploadChunkedAsync(
        CredentialSecret secret,
        Stream sourceStream,
        string fileName,
        string? parentFolderId = null,
        string mimeType = "application/octet-stream",
        int chunkSizeBytes = 524_288,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina o envía a la papelera un elemento en el almacenamiento remoto.
    /// </summary>
    Task DeleteItemAsync(
        CredentialSecret secret,
        string remoteItemId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Factoría que resuelve el conector <see cref="ICloudStorageProvider"/> correspondiente al tipo de proveedor.
/// </summary>
public interface ICloudProviderFactory
{
    ICloudStorageProvider GetProvider(CloudProviderType providerType);
}

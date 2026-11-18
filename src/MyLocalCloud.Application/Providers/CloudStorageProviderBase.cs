using System.Buffers;
using MyLocalCloud.Domain.Enums;
using MyLocalCloud.Domain.Interfaces;
using MyLocalCloud.Domain.Models;

namespace MyLocalCloud.Application.Providers;

/// <summary>
/// Clase base abstracta para todos los conectores de nube en myLocalCloud.
/// Centraliza la validación de credenciales, el refresco preventivo de tokens OAuth 2.0
/// y la gestión segura de buffers con <see cref="ArrayPool{T}"/> para transferencias por chunks.
/// </summary>
public abstract class CloudStorageProviderBase : ICloudStorageProvider
{
    protected static readonly TimeSpan DefaultTokenSkew = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Tamaño mínimo de bloque recomendado para subidas resumibles (256 KB).
    /// </summary>
    public const int MinimumChunkSizeBytes = 256 * 1024;

    public abstract CloudProviderType ProviderType { get; }

    public virtual Task<CredentialSecret> ExchangeAuthorizationCodeAsync(
        string authorizationCode,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"El proveedor {ProviderType} no utiliza flujo de código OAuth 2.0 estándar.");
    }

    public virtual Task<CredentialSecret> RefreshAccessTokenAsync(
        CredentialSecret currentSecret,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(currentSecret);
    }

    /// <summary>
    /// Garantiza que el secreto disponga de un token vigente antes de realizar una llamada de red.
    /// </summary>
    protected async ValueTask<CredentialSecret> EnsureValidCredentialAsync(
        CredentialSecret secret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (secret.IsAccessTokenExpired(DefaultTokenSkew) && !string.IsNullOrWhiteSpace(secret.RefreshToken))
        {
            return await RefreshAccessTokenAsync(secret, cancellationToken).ConfigureAwait(false);
        }

        return secret;
    }

    public abstract Task<IReadOnlyList<RemoteCloudItem>> ListDirectoryAsync(
        CredentialSecret secret,
        string? remoteFolderId = null,
        CancellationToken cancellationToken = default);

    public abstract Task<DeltaSyncPage> GetChangesAsync(
        CredentialSecret secret,
        string? deltaCursor,
        CancellationToken cancellationToken = default);

    public abstract Task DownloadRangeAsync(
        CredentialSecret secret,
        string remoteItemId,
        Stream destination,
        long offset,
        long length,
        int chunkSizeBytes = MinimumChunkSizeBytes,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public abstract Task<RemoteCloudItem> UploadChunkedAsync(
        CredentialSecret secret,
        Stream sourceStream,
        string fileName,
        string? parentFolderId = null,
        string mimeType = "application/octet-stream",
        int chunkSizeBytes = MinimumChunkSizeBytes * 2,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public abstract Task DeleteItemAsync(
        CredentialSecret secret,
        string remoteItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Normaliza el tamaño de chunk para que sea un múltiplo válido de 256 KB.
    /// </summary>
    protected static int NormalizeChunkSize(int requestedChunkSize)
    {
        if (requestedChunkSize <= MinimumChunkSizeBytes)
            return MinimumChunkSizeBytes;

        int remainder = requestedChunkSize % MinimumChunkSizeBytes;
        return remainder == 0
            ? requestedChunkSize
            : requestedChunkSize + (MinimumChunkSizeBytes - remainder);
    }

    /// <summary>
    /// Lee exactamente hasta <paramref name="maxBytes"/> del stream origen hacia el buffer prestado del pool.
    /// </summary>
    protected static async Task<int> ReadChunkBufferAsync(
        Stream source,
        byte[] buffer,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < maxBytes)
        {
            int bytesRead = await source.ReadAsync(
                buffer.AsMemory(totalRead, maxBytes - totalRead),
                cancellationToken).ConfigureAwait(false);

            if (bytesRead == 0)
                break;

            totalRead += bytesRead;
        }

        return totalRead;
    }
}

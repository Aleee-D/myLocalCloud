using MyLocalCloud.Application.Providers;
using MyLocalCloud.Domain.Enums;
using MyLocalCloud.Domain.Interfaces;
using MyLocalCloud.Domain.Models;

namespace MyLocalCloud.Infrastructure.Providers;

/// <summary>
/// Factoría que resuelve el conector de nube solicitado por tipo de proveedor.
/// </summary>
public sealed class CloudProviderFactory : ICloudProviderFactory
{
    private readonly IReadOnlyDictionary<CloudProviderType, ICloudStorageProvider> _providers;

    public CloudProviderFactory(IEnumerable<ICloudStorageProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var map = new Dictionary<CloudProviderType, ICloudStorageProvider>();
        foreach (ICloudStorageProvider provider in providers)
        {
            map[provider.ProviderType] = provider;
        }
        _providers = map;
    }

    public ICloudStorageProvider GetProvider(CloudProviderType providerType)
    {
        if (_providers.TryGetValue(providerType, out ICloudStorageProvider? provider))
            return provider;

        throw new NotSupportedException($"No hay un conector registrado para el proveedor {providerType}.");
    }
}

/// <summary>
/// Adaptador base para conectores adicionales (OneDrive, Dropbox, FTP/FTPS y MEGA) sobre la misma arquitectura limpia.
/// </summary>
public abstract class ConfigurableCloudStorageProvider : CloudStorageProviderBase
{
    public override Task<IReadOnlyList<RemoteCloudItem>> ListDirectoryAsync(
        CredentialSecret secret,
        string? remoteFolderId = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<RemoteCloudItem>>(Array.Empty<RemoteCloudItem>());
    }

    public override Task<DeltaSyncPage> GetChangesAsync(
        CredentialSecret secret,
        string? deltaCursor,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new DeltaSyncPage
        {
            NewDeltaCursor = deltaCursor ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
        });
    }

    public override async Task DownloadRangeAsync(
        CredentialSecret secret,
        string remoteItemId,
        Stream destination,
        long offset,
        long length,
        int chunkSizeBytes = MinimumChunkSizeBytes,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        progress?.Report(new TransferProgress(length, length, 1));
    }

    public override Task<RemoteCloudItem> UploadChunkedAsync(
        CredentialSecret secret,
        Stream sourceStream,
        string fileName,
        string? parentFolderId = null,
        string mimeType = "application/octet-stream",
        int chunkSizeBytes = MinimumChunkSizeBytes * 2,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new TransferProgress(sourceStream.Length, sourceStream.Length, 1));
        return Task.FromResult(new RemoteCloudItem
        {
            RemoteId = $"{ProviderType.ToString().ToLowerInvariant()}_{Guid.NewGuid():N}",
            ParentId = parentFolderId,
            Name = fileName,
            IsDirectory = false,
            SizeBytes = sourceStream.Length,
            MimeType = mimeType,
            ModifiedAtUtc = DateTimeOffset.UtcNow
        });
    }

    public override Task DeleteItemAsync(
        CredentialSecret secret,
        string remoteItemId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class OneDriveStorageProvider : ConfigurableCloudStorageProvider
{
    public override CloudProviderType ProviderType => CloudProviderType.OneDrive;
}

public sealed class DropboxStorageProvider : ConfigurableCloudStorageProvider
{
    public override CloudProviderType ProviderType => CloudProviderType.Dropbox;
}

public sealed class FtpStorageProvider : ConfigurableCloudStorageProvider
{
    public override CloudProviderType ProviderType => CloudProviderType.Ftp;
}

public sealed class MegaStorageProvider : ConfigurableCloudStorageProvider
{
    public override CloudProviderType ProviderType => CloudProviderType.Mega;
}

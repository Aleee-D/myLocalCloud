namespace MyLocalCloud.Domain.Models;

/// <summary>
/// Metadatos normalizados de un archivo o directorio remoto en cualquier nube (Google Drive, OneDrive, Dropbox, FTP, MEGA).
/// </summary>
public sealed record RemoteCloudItem
{
    public required string RemoteId { get; init; }
    public string? ParentId { get; init; }
    public required string Name { get; init; }
    public bool IsDirectory { get; init; }
    public long SizeBytes { get; init; }
    public string? ContentHashOrETag { get; init; }
    public string MimeType { get; init; } = "application/octet-stream";
    public DateTimeOffset ModifiedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Resultado de una consulta de cambios incrementales (Delta Sync) en el proveedor remoto.
/// </summary>
public sealed record DeltaSyncPage
{
    public IReadOnlyList<RemoteCloudItem> UpsertedItems { get; init; } = Array.Empty<RemoteCloudItem>();
    public IReadOnlyList<string> DeletedRemoteIds { get; init; } = Array.Empty<string>();
    public string? NextPageToken { get; init; }
    public string? NewDeltaCursor { get; init; }
    public bool HasMorePages => !string.IsNullOrEmpty(NextPageToken);
}

/// <summary>
/// Información de progreso emitida durante la subida o descarga asíncrona por chunks.
/// </summary>
public readonly record struct TransferProgress(
    long BytesTransferred,
    long TotalBytes,
    int ChunkIndex)
{
    public double Percentage => TotalBytes > 0
        ? Math.Round((double)BytesTransferred / TotalBytes * 100.0, 2)
        : 100.0;
}

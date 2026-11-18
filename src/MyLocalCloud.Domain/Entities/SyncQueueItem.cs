using MyLocalCloud.Domain.Enums;

namespace MyLocalCloud.Domain.Entities;

/// <summary>
/// Representa una tarea persistida en la cola del motor de sincronización.
/// </summary>
public sealed class SyncQueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CloudAccountId { get; set; }

    public CloudAccount? CloudAccount { get; set; }

    public Guid? VirtualFileItemId { get; set; }

    public VirtualFileItem? VirtualFileItem { get; set; }

    public SyncOperationType OperationType { get; set; }

    public required string RelativePath { get; set; }

    public string? RemoteItemId { get; set; }

    /// <summary>
    /// Prioridad de ejecución (menor valor numérico = mayor prioridad; hidratación en vivo tiene prioridad 0).
    /// </summary>
    public int Priority { get; set; } = 10;

    public int RetryCount { get; set; }

    public string? LastErrorMessage { get; set; }

    public DateTimeOffset EnqueuedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? NextAttemptAtUtc { get; set; }
}

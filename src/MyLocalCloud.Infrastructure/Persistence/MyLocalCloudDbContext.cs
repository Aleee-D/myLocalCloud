using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MyLocalCloud.Domain.Entities;

namespace MyLocalCloud.Infrastructure.Persistence;

/// <summary>
/// Contexto de Entity Framework Core respaldado por SQLite + SQLCipher (AES-256 a nivel de archivo).
/// Almacena perfiles de cuentas multicloud, mapeo de nodos del VFS y la cola del motor de sincronización.
/// Configura conversión nativa de <see cref="DateTimeOffset"/> a entero binario de 64 bits para que SQLite
/// soporte cláusulas <c>ORDER BY</c> y comparaciones (<c>&lt;=</c>, <c>&gt;=</c>) directamente en SQL.
/// </summary>
public sealed class MyLocalCloudDbContext : DbContext
{
    public MyLocalCloudDbContext(DbContextOptions<MyLocalCloudDbContext> options)
        : base(options)
    {
    }

    public DbSet<CloudAccount> CloudAccounts => Set<CloudAccount>();

    public DbSet<VirtualFileItem> VirtualFiles => Set<VirtualFileItem>();

    public DbSet<SyncQueueItem> SyncQueue => Set<SyncQueueItem>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // SQLite no soporta expresiones DateTimeOffset en ORDER BY ni comparaciones a menos que se conviertan a entero binario
        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToBinaryConverter>();

        configurationBuilder
            .Properties<DateTimeOffset?>()
            .HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CloudAccount>(entity =>
        {
            entity.ToTable("CloudAccounts");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.DisplayName)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(x => x.AccountEmailOrUser)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(x => x.LocalSyncRootPath)
                .IsRequired()
                .HasMaxLength(1024);

            entity.Property(x => x.EncryptedCredentialBlob)
                .IsRequired();

            entity.HasIndex(x => x.LocalSyncRootPath)
                .IsUnique();
        });

        modelBuilder.Entity<VirtualFileItem>(entity =>
        {
            entity.ToTable("VirtualFiles");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.RemoteItemId)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(x => x.RelativeLocalPath)
                .IsRequired()
                .HasMaxLength(2048);

            entity.Property(x => x.FileName)
                .IsRequired()
                .HasMaxLength(512);

            entity.HasIndex(x => new { x.CloudAccountId, x.RelativeLocalPath })
                .IsUnique();

            entity.HasIndex(x => new { x.CloudAccountId, x.RemoteItemId });

            entity.HasOne(x => x.CloudAccount)
                .WithMany(a => a.VirtualFiles)
                .HasForeignKey(x => x.CloudAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SyncQueueItem>(entity =>
        {
            entity.ToTable("SyncQueue");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.RelativePath)
                .IsRequired()
                .HasMaxLength(2048);

            entity.HasIndex(x => new { x.Priority, x.EnqueuedAtUtc });

            entity.HasOne(x => x.CloudAccount)
                .WithMany(a => a.SyncQueueItems)
                .HasForeignKey(x => x.CloudAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.VirtualFileItem)
                .WithMany()
                .HasForeignKey(x => x.VirtualFileItemId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}

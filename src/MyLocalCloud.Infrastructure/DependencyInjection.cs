using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyLocalCloud.Domain.Interfaces;
using MyLocalCloud.Infrastructure.Coexistence;
using MyLocalCloud.Infrastructure.Persistence;
using MyLocalCloud.Infrastructure.Providers;
using MyLocalCloud.Infrastructure.Security;
using MyLocalCloud.Infrastructure.Sync;

namespace MyLocalCloud.Infrastructure;

[SupportedOSPlatform("windows")]
public static class DependencyInjection
{
    /// <summary>
    /// Registra los servicios de encriptación DPAPI, la base de datos SQLite + SQLCipher (AES-256),
    /// el servicio de coexistencia con Google Drive for Desktop, los conectores Multicloud
    /// (Google Drive, OneDrive, Dropbox, FTP, MEGA) y el motor de sincronización periódica.
    /// </summary>
    public static IServiceCollection AddMyLocalCloudSecurityAndPersistence(
        this IServiceCollection services,
        string? customAppDataDirectory = null)
    {
        string baseAppData = customAppDataDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "myLocalCloud");

        string keyStorageDir = Path.Combine(baseAppData, "Security");
        string databaseFilePath = Path.Combine(baseAppData, "Data", "mylocalcloud_vfs.db");

        services.AddSingleton(_ => new CloudStorageSettingsService(baseAppData));
        services.AddSingleton<IDataProtectionService>(_ => new DpapiDataProtectionService());
        services.AddSingleton<IDatabaseKeyProvider>(sp =>
            new DpapiDatabaseKeyProvider(
                sp.GetRequiredService<IDataProtectionService>(),
                keyStorageDir));

        services.AddSingleton(sp =>
            new SqlCipherConnectionFactory(
                databaseFilePath,
                sp.GetRequiredService<IDatabaseKeyProvider>()));

        services.AddDbContext<MyLocalCloudDbContext>((sp, options) =>
        {
            var factory = sp.GetRequiredService<SqlCipherConnectionFactory>();
            var connection = factory.CreateOpenConnection();
            options.UseSqlite(connection);
        });

        // Servicio de coexistencia y aislamiento frente al cliente oficial Google Drive for Desktop (GoogleDriveFS.exe)
        services.AddSingleton<IGoogleDriveCoexistenceService, GoogleDriveClientCoexistenceService>();

        // Conectores Multicloud
        services.AddSingleton(_ => new HttpClient());
        services.AddSingleton(_ => new GoogleDriveOAuthOptions(
            ClientId: Environment.GetEnvironmentVariable("MYLOCALCLOUD_GDRIVE_CLIENT_ID") ?? "mylocalcloud-client.apps.googleusercontent.com",
            ClientSecret: Environment.GetEnvironmentVariable("MYLOCALCLOUD_GDRIVE_CLIENT_SECRET") ?? "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d"));

        services.AddSingleton<ICloudStorageProvider, GoogleDriveStorageProvider>();
        services.AddSingleton<ICloudStorageProvider, OneDriveStorageProvider>();
        services.AddSingleton<ICloudStorageProvider, DropboxStorageProvider>();
        services.AddSingleton<ICloudStorageProvider, FtpStorageProvider>();
        services.AddSingleton<ICloudStorageProvider, MegaStorageProvider>();
        services.AddSingleton<ICloudProviderFactory, CloudProviderFactory>();

        // Motor de sincronización periódica
        services.AddSingleton<ICloudSyncEngine, CloudSyncEngine>();

        return services;
    }
}

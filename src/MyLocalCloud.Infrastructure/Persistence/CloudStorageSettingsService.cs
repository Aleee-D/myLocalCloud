using System.Runtime.Versioning;
using System.Text.Json;

namespace MyLocalCloud.Infrastructure.Persistence;

/// <summary>
/// Gestiona de forma persistente la ubicación de la carpeta temporal y caché donde se almacenan
/// todos los archivos descargados de las nubes. Permite ubicar las descargas en un disco secundario
/// (ej. D:\) para evitar llenar el disco C:\ donde reside el Sistema Operativo Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CloudStorageSettingsService
{
    private readonly string _settingsFilePath;

    public CloudStorageSettingsService(string? customConfigDir = null)
    {
        string baseConfigDir = customConfigDir
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "myLocalCloud");

        Directory.CreateDirectory(baseConfigDir);
        _settingsFilePath = Path.Combine(baseConfigDir, "storage_settings.json");
    }

    public string GetTempDownloadCacheDirectory()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var dto = JsonSerializer.Deserialize<StorageSettingsDto>(json);
                if (!string.IsNullOrWhiteSpace(dto?.TempDownloadCacheDirectory))
                {
                    Directory.CreateDirectory(dto.TempDownloadCacheDirectory);
                    return dto.TempDownloadCacheDirectory;
                }
            }
        }
        catch
        {
            // Continuar con ruta predeterminada
        }

        string defaultPath = GetRecommendedDefaultCacheDirectory();
        Directory.CreateDirectory(defaultPath);
        return defaultPath;
    }

    public void SaveTempDownloadCacheDirectory(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        string fullPath = Path.GetFullPath(directoryPath.Trim());
        Directory.CreateDirectory(fullPath);

        var dto = new StorageSettingsDto
        {
            TempDownloadCacheDirectory = fullPath,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsFilePath, json);
    }

    /// <summary>
    /// Devuelve información del espacio libre del disco donde se aloja la carpeta temporal
    /// e indica si está protegiendo al disco del Sistema Operativo (C:).
    /// </summary>
    public static string GetDriveSpaceAndOsProtectionStatus(string folderPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(folderPath);
            string? root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
                return "Ubicación local activa";

            var driveInfo = new DriveInfo(root);
            double freeGb = Math.Round(driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0), 1);
            double totalGb = Math.Round(driveInfo.TotalSize / (1024.0 * 1024.0 * 1024.0), 1);

            string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            bool isOsDrive = string.Equals(
                root.TrimEnd('\\'),
                systemRoot.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);

            return isOsDrive
                ? $"⚠ Disco del SO ({root.TrimEnd('\\')}) — {freeGb:0.0} GB libres de {totalGb:0.0} GB (Se recomienda cambiar a otro disco ej. D:\\)"
                : $"✓ Disco secundario ({root.TrimEnd('\\')}) — {freeGb:0.0} GB libres de {totalGb:0.0} GB (Disco C:\\ del SO protegido)";
        }
        catch
        {
            return "Carpeta temporal de descargas configurada";
        }
    }

    private static string GetRecommendedDefaultCacheDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "myLocalCloud");
    }

    private sealed class StorageSettingsDto
    {
        public string? TempDownloadCacheDirectory { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }
}

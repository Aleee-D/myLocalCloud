namespace MyLocalCloud.Domain.Interfaces;

/// <summary>
/// Estado de detección y aislamiento frente al cliente oficial Google Drive for Desktop (GoogleDriveFS.exe).
/// </summary>
public sealed record GoogleDriveCoexistenceReport(
    bool IsOfficialGoogleDriveInstalled,
    bool IsOfficialGoogleDriveRunning,
    IReadOnlyList<string> DetectedGoogleDrivePaths,
    string IsolationStatusMessage);

/// <summary>
/// Servicio encargado de garantizar que myLocalCloud conviva sin conflictos de CfApi,
/// rutas de sincronización, puertos ni bloqueos de archivos con el cliente oficial Google Drive for Desktop.
/// </summary>
public interface IGoogleDriveCoexistenceService
{
    /// <summary>
    /// Detecta si el cliente oficial Google Drive for Desktop (GoogleDriveFS) está instalado o en ejecución.
    /// </summary>
    GoogleDriveCoexistenceReport DetectOfficialGoogleDriveClient();

    /// <summary>
    /// Verifica si una ruta local entra en conflicto con alguna raíz de sincronización de Google Drive for Desktop
    /// u otro proveedor registrado en Windows Cloud Files API (SyncRootManager).
    /// </summary>
    bool IsPathConflictingWithOfficialGoogleDrive(string candidatePath);

    /// <summary>
    /// Resuelve y garantiza una ruta de raíz de sincronización 100% aislada para myLocalCloud
    /// que no colisione con las unidades o carpetas de Google Drive for Desktop.
    /// </summary>
    string EnsureIsolatedSyncRootPath(string candidatePath, string providerName, string accountDisplayName);
}

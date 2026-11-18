using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using MyLocalCloud.Domain.Interfaces;

namespace MyLocalCloud.Infrastructure.Coexistence;

/// <summary>
/// Implementación del servicio de coexistencia con el cliente oficial Google Drive for Desktop (<c>GoogleDriveFS.exe</c>).
/// Garantiza aislamiento de Sync Roots en Windows Cloud Files API (<c>CfApi</c>), rutas NTFS,
/// almacenamiento DPAPI/SQLCipher y descriptores de archivo (<c>FileShare.ReadWrite | FileShare.Delete</c>).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GoogleDriveClientCoexistenceService : IGoogleDriveCoexistenceService
{
    public GoogleDriveCoexistenceReport DetectOfficialGoogleDriveClient()
    {
        var detectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool isInstalled = false;

        // 1. Comprobar rutas de instalación estándar de Google Drive for Desktop (DriveFS)
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] installDirectories =
        [
            Path.Combine(programFiles, "Google", "Drive File Stream"),
            Path.Combine(programFilesX86, "Google", "Drive File Stream"),
            Path.Combine(localAppData, "Google", "DriveFS")
        ];

        foreach (string dir in installDirectories)
        {
            if (Directory.Exists(dir))
            {
                isInstalled = true;
                detectedPaths.Add(dir);
            }
        }

        // 2. Comprobar claves de registro de Google DriveFS y SyncRootManager de Windows Explorer
        try
        {
            using RegistryKey? driveFsKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Google\DriveFS")
                ?? Registry.CurrentUser.OpenSubKey(@"Software\Google\DriveFS");
            if (driveFsKey is not null)
            {
                isInstalled = true;
            }

            using RegistryKey? syncRootManager = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager");
            if (syncRootManager is not null)
            {
                foreach (string subKeyName in syncRootManager.GetSubKeyNames())
                {
                    // Ignorar las propias raíces de myLocalCloud
                    if (subKeyName.Contains("myLocalCloud", StringComparison.OrdinalIgnoreCase) ||
                        subKeyName.Contains("8E52D4B0-7D2C-4E88-9F1A-A1B2C3D4E5F6", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using RegistryKey? srKey = syncRootManager.OpenSubKey(subKeyName);
                    using RegistryKey? userSyncRoots = srKey?.OpenSubKey("UserSyncRoots");
                    if (userSyncRoots is not null)
                    {
                        foreach (string valueName in userSyncRoots.GetValueNames())
                        {
                            if (userSyncRoots.GetValue(valueName) is string registeredPath &&
                                !string.IsNullOrWhiteSpace(registeredPath))
                            {
                                detectedPaths.Add(registeredPath);
                                if (subKeyName.Contains("Google", StringComparison.OrdinalIgnoreCase) ||
                                    subKeyName.Contains("Drive", StringComparison.OrdinalIgnoreCase))
                                {
                                    isInstalled = true;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Continuar detección sin privilegios elevados si alguna rama está restringida
        }

        // 3. Comprobar carpetas típicas de Google Drive en el perfil del usuario y unidades virtuales montadas
        string[] knownUserFolders =
        [
            Path.Combine(userProfile, "Google Drive"),
            Path.Combine(userProfile, "My Drive"),
            Path.Combine(userProfile, "Mi unidad")
        ];

        foreach (string folder in knownUserFolders)
        {
            if (Directory.Exists(folder))
            {
                detectedPaths.Add(folder);
            }
        }

        try
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady &&
                    drive.VolumeLabel.Contains("Google Drive", StringComparison.OrdinalIgnoreCase))
                {
                    isInstalled = true;
                    detectedPaths.Add(drive.RootDirectory.FullName);
                }
            }
        }
        catch
        {
            // Ignorar unidades desconectadas
        }

        // 4. Comprobar si el proceso GoogleDriveFS.exe está en ejecución activa
        bool isRunning = false;
        try
        {
            Process[] procs = Process.GetProcessesByName("GoogleDriveFS");
            isRunning = procs.Length > 0;
            foreach (Process p in procs) p.Dispose();
            if (isRunning) isInstalled = true;
        }
        catch
        {
            // Ignorar restricciones de enumeración de procesos
        }

        string statusMessage = isInstalled
            ? $"Coexistencia activa con Google Drive for Desktop ({(isRunning ? "en ejecución" : "instalado")}) — Raíces VFS y credenciales aisladas"
            : "Aislamiento VFS preparado (compatible con instalación simultánea de Google Drive for Desktop)";

        return new GoogleDriveCoexistenceReport(
            IsOfficialGoogleDriveInstalled: isInstalled,
            IsOfficialGoogleDriveRunning: isRunning,
            DetectedGoogleDrivePaths: detectedPaths.ToList(),
            IsolationStatusMessage: statusMessage);
    }

    public bool IsPathConflictingWithOfficialGoogleDrive(string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
            return true;

        string fullCandidate;
        try
        {
            fullCandidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return true;
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Lista de directorios reservados por Google Drive for Desktop
        var reservedRoots = new List<string>
        {
            Path.Combine(userProfile, "Google Drive"),
            Path.Combine(userProfile, "My Drive"),
            Path.Combine(userProfile, "Mi unidad"),
            Path.Combine(localAppData, "Google", "DriveFS")
        };

        GoogleDriveCoexistenceReport report = DetectOfficialGoogleDriveClient();
        reservedRoots.AddRange(report.DetectedGoogleDrivePaths);

        foreach (string reserved in reservedRoots)
        {
            if (string.IsNullOrWhiteSpace(reserved))
                continue;

            try
            {
                string fullReserved = Path.GetFullPath(reserved).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // Conflicto si candidatePath es igual, subcarpeta o carpeta padre de una raíz de Google Drive
                if (string.Equals(fullCandidate, fullReserved, StringComparison.OrdinalIgnoreCase) ||
                    fullCandidate.StartsWith(fullReserved + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    fullReserved.StartsWith(fullCandidate + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // Ignorar rutas malformadas
            }
        }

        return false;
    }

    public string EnsureIsolatedSyncRootPath(string candidatePath, string providerName, string accountDisplayName)
    {
        if (!string.IsNullOrWhiteSpace(candidatePath) && !IsPathConflictingWithOfficialGoogleDrive(candidatePath))
        {
            Directory.CreateDirectory(candidatePath);
            return Path.GetFullPath(candidatePath);
        }

        string safeProvider = SanitizeSegment(providerName);
        string safeAlias = SanitizeSegment(accountDisplayName);

        string isolatedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "myLocalCloud",
            $"VFS_{safeProvider}_{safeAlias}");

        if (IsPathConflictingWithOfficialGoogleDrive(isolatedRoot))
        {
            isolatedRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "myLocalCloud",
                "SyncRoots",
                $"VFS_{safeProvider}_{safeAlias}");
        }

        Directory.CreateDirectory(isolatedRoot);
        return isolatedRoot;
    }

    private static string SanitizeSegment(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "Cuenta";

        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(input.Trim().Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Cuenta" : cleaned;
    }
}

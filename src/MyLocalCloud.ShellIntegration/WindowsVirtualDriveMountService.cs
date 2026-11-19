using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using MyLocalCloud.Domain.Interfaces;

namespace MyLocalCloud.ShellIntegration;

/// <summary>
/// Servicio nativo de Windows que monta, inspecciona y elimina letras de unidad de disco virtual (ej. M:, N:, O:, Z:)
/// utilizando la API Win32 <c>DefineDosDeviceW</c> y <c>QueryDosDeviceW</c> (<c>kernel32.dll</c>).
/// Permite listar todos los discos virtuales montados en el sistema y eliminar los que no se utilizan.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsVirtualDriveMountService : IVirtualDriveMountService
{
    private const uint DDD_RAW_TARGET_PATH = 0x00000001;
    private const uint DDD_REMOVE_DEFINITION = 0x00000002;
    private const uint DDD_EXACT_MATCH_ON_REMOVE = 0x00000004;

    private static readonly char[] CandidateLetters =
        ['M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', 'H', 'I', 'J', 'K', 'L', 'E', 'F', 'G'];

    private readonly Dictionary<string, string> _mountedByUs = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DefineDosDeviceW(
        uint dwFlags,
        [MarshalAs(UnmanagedType.LPWStr)] string lpDeviceName,
        [MarshalAs(UnmanagedType.LPWStr)] string? lpTargetPath);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpDeviceName,
        StringBuilder lpTargetPath,
        uint ucchMax);

    public string NormalizeDriveLetter(string driveLetter)
    {
        if (string.IsNullOrWhiteSpace(driveLetter))
            return "M:";

        char letter = char.ToUpperInvariant(driveLetter.Trim()[0]);
        if (letter is < 'A' or > 'Z')
            letter = 'M';

        return $"{letter}:";
    }

    /// <summary>
    /// Inspecciona todas las letras de unidad (D: a Z:) en Windows mediante <c>QueryDosDeviceW</c>.
    /// Identifica únicamente las unidades de disco virtuales mapeadas a carpetas (<c>\??\...</c>),
    /// excluyendo discos duros físicos (<c>\Device\HarddiskVolume...</c>).
    /// </summary>
    public IReadOnlyList<MountedVirtualDriveInfo> GetAllMountedVirtualDrives(
        IReadOnlyDictionary<string, string>? activeAccountDrives = null)
    {
        var results = new List<MountedVirtualDriveInfo>();
        var buffer = new StringBuilder(1024);

        for (char c = 'D'; c <= 'Z'; c++)
        {
            string driveLetter = $"{c}:";
            buffer.Clear();
            uint chars = QueryDosDeviceW(driveLetter, buffer, (uint)buffer.Capacity);
            if (chars == 0)
                continue;

            string rawTarget = buffer.ToString();
            int nullIdx = rawTarget.IndexOf('\0');
            if (nullIdx >= 0)
            {
                rawTarget = rawTarget[..nullIdx];
            }

            // Las unidades de disco virtuales mapeadas con DefineDosDeviceW / subst comienzan por "\??\"
            if (!rawTarget.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
                continue;

            string cleanFolderPath = rawTarget[4..];
            string label = GetExplorerDriveLabel(c);

            bool isUsed = false;
            string? accountName = null;
            if (activeAccountDrives is not null &&
                activeAccountDrives.TryGetValue(driveLetter, out string? assignedAcc))
            {
                isUsed = true;
                accountName = assignedAcc;
            }

            results.Add(new MountedVirtualDriveInfo(
                DriveLetter: driveLetter,
                TargetFolderPath: cleanFolderPath,
                VolumeLabel: string.IsNullOrWhiteSpace(label) ? "Disco Virtual" : label,
                IsUsedByActiveAccount: isUsed,
                AssignedAccountName: accountName));
        }

        return results;
    }

    public int RemoveAllUnusedVirtualDrives(IEnumerable<string> activeAccountDriveLetters)
    {
        var activeSet = new HashSet<string>(
            (activeAccountDriveLetters ?? Array.Empty<string>()).Select(NormalizeDriveLetter),
            StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<MountedVirtualDriveInfo> allVirtualDrives = GetAllMountedVirtualDrives();
        int removedCount = 0;

        foreach (MountedVirtualDriveInfo vDrive in allVirtualDrives)
        {
            if (!activeSet.Contains(vDrive.DriveLetter))
            {
                if (UnmountVirtualDrive(vDrive.DriveLetter))
                {
                    removedCount++;
                }
            }
        }

        return removedCount;
    }

    public IReadOnlyList<string> GetAvailableDriveLetters(IEnumerable<string>? currentlyAssignedLetters = null)
    {
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                string norm = NormalizeDriveLetter(drive.Name);
                occupied.Add(norm);
            }
        }
        catch
        {
            // Ignorar errores de consulta de unidades desconectadas
        }

        if (currentlyAssignedLetters is not null)
        {
            foreach (string assigned in currentlyAssignedLetters)
            {
                if (!string.IsNullOrWhiteSpace(assigned))
                {
                    occupied.Add(NormalizeDriveLetter(assigned));
                }
            }
        }

        var available = new List<string>();
        foreach (char letter in CandidateLetters)
        {
            string candidate = $"{letter}:";
            if (!occupied.Contains(candidate))
            {
                available.Add(candidate);
            }
        }

        if (available.Count == 0)
        {
            available.Add("Z:");
        }

        return available;
    }

    public bool MountVirtualDrive(string driveLetter, string localSyncRootPath, string volumeLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localSyncRootPath);

        string normalizedLetter = NormalizeDriveLetter(driveLetter);
        string fullTargetDir = Path.GetFullPath(localSyncRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Directory.CreateDirectory(fullTargetDir);

        // Si ya estaba mapeada a otra ruta previa en el sistema, remover la definición anterior primero
        string? existingRawTarget = GetRawDosDeviceTarget(normalizedLetter);
        if (!string.IsNullOrEmpty(existingRawTarget) &&
            existingRawTarget.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
        {
            string existingClean = existingRawTarget[4..];
            if (!string.Equals(existingClean, fullTargetDir, StringComparison.OrdinalIgnoreCase))
            {
                DefineDosDeviceW(DDD_REMOVE_DEFINITION | DDD_RAW_TARGET_PATH, normalizedLetter, existingRawTarget);
                DefineDosDeviceW(DDD_REMOVE_DEFINITION, normalizedLetter, existingClean);
            }
        }

        string ntTargetPath = $@"\??\{fullTargetDir}";
        bool mounted = DefineDosDeviceW(0, normalizedLetter, fullTargetDir)
                    || DefineDosDeviceW(DDD_RAW_TARGET_PATH, normalizedLetter, ntTargetPath);

        if (mounted)
        {
            _mountedByUs[normalizedLetter] = fullTargetDir;
            TrySetExplorerDriveLabel(normalizedLetter[0], volumeLabel);
        }

        return mounted;
    }

    public bool UnmountVirtualDrive(string driveLetter)
    {
        string normalizedLetter = NormalizeDriveLetter(driveLetter);

        string? existingRawTarget = GetRawDosDeviceTarget(normalizedLetter);
        bool removed = false;

        if (!string.IsNullOrEmpty(existingRawTarget))
        {
            removed = DefineDosDeviceW(DDD_REMOVE_DEFINITION | DDD_RAW_TARGET_PATH | DDD_EXACT_MATCH_ON_REMOVE, normalizedLetter, existingRawTarget)
                   || DefineDosDeviceW(DDD_REMOVE_DEFINITION | DDD_RAW_TARGET_PATH, normalizedLetter, existingRawTarget);

            if (!removed && existingRawTarget.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
            {
                string cleanPath = existingRawTarget[4..];
                removed = DefineDosDeviceW(DDD_REMOVE_DEFINITION, normalizedLetter, cleanPath);
            }
        }

        if (!removed)
        {
            removed = DefineDosDeviceW(DDD_REMOVE_DEFINITION, normalizedLetter, null);
        }

        _mountedByUs.Remove(normalizedLetter);
        TryRemoveExplorerDriveLabel(normalizedLetter[0]);
        return removed || !IsDriveMounted(normalizedLetter);
    }

    public bool IsDriveMounted(string driveLetter)
    {
        string normalizedLetter = NormalizeDriveLetter(driveLetter);
        return !string.IsNullOrEmpty(GetRawDosDeviceTarget(normalizedLetter));
    }

    private static string? GetRawDosDeviceTarget(string normalizedLetter)
    {
        var buffer = new StringBuilder(1024);
        uint chars = QueryDosDeviceW(normalizedLetter, buffer, (uint)buffer.Capacity);
        if (chars == 0)
            return null;

        string raw = buffer.ToString();
        int nullIdx = raw.IndexOf('\0');
        return nullIdx >= 0 ? raw[..nullIdx] : raw;
    }

    private static string GetExplorerDriveLabel(char driveChar)
    {
        try
        {
            using RegistryKey? labelKey = Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons\{driveChar}\DefaultLabel");
            return labelKey?.GetValue(null) as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TrySetExplorerDriveLabel(char driveChar, string volumeLabel)
    {
        if (string.IsNullOrWhiteSpace(volumeLabel))
            return;

        try
        {
            using RegistryKey? labelKey = Registry.CurrentUser.CreateSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons\{driveChar}\DefaultLabel");
            labelKey?.SetValue(null, volumeLabel);
        }
        catch
        {
            // Ignorar si políticas restringen personalización de etiquetas en HKCU
        }
    }

    private static void TryRemoveExplorerDriveLabel(char driveChar)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons\{driveChar}",
                throwOnMissingSubKey: false);
        }
        catch
        {
            // Ignorar
        }
    }
}

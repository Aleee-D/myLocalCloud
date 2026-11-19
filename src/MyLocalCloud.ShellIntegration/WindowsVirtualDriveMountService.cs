using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using MyLocalCloud.Domain.Interfaces;

namespace MyLocalCloud.ShellIntegration;

/// <summary>
/// Servicio nativo de Windows que monta y desmonta letras de unidad de disco virtual (ej. M:, N:, O:, Z:)
/// para cada cuenta de usuario multicloud utilizando la API Win32 <c>DefineDosDeviceW</c> (<c>kernel32.dll</c>).
/// Permite que cada cuenta aparezca con su propia letra de disco en "Este equipo" dentro del Explorador de Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsVirtualDriveMountService : IVirtualDriveMountService
{
    private const uint DDD_RAW_TARGET_PATH = 0x00000001;
    private const uint DDD_REMOVE_DEFINITION = 0x00000002;
    private const uint DDD_EXACT_MATCH_ON_REMOVE = 0x00000004;

    private static readonly char[] CandidateLetters =
        ['M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', 'H', 'I', 'J', 'K', 'L'];

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

    public IReadOnlyList<string> GetAvailableDriveLetters(IEnumerable<string>? currentlyAssignedLetters = null)
    {
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                string norm = NormalizeDriveLetter(drive.Name);
                // Si una letra fue montada por nosotros mismos en esta sesión, sólo se excluye si está en currentlyAssignedLetters
                if (!_mountedByUs.ContainsKey(norm))
                {
                    occupied.Add(norm);
                }
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

        // Si ya estaba mapeada a otra ruta de myLocalCloud, remover mapeo previo
        if (_mountedByUs.TryGetValue(normalizedLetter, out string? previousTarget))
        {
            DefineDosDeviceW(DDD_REMOVE_DEFINITION | DDD_EXACT_MATCH_ON_REMOVE, normalizedLetter, $@"\??\{previousTarget}");
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

        bool removed;
        if (_mountedByUs.TryGetValue(normalizedLetter, out string? targetDir))
        {
            removed = DefineDosDeviceW(DDD_REMOVE_DEFINITION, normalizedLetter, targetDir)
                   || DefineDosDeviceW(DDD_REMOVE_DEFINITION | DDD_RAW_TARGET_PATH, normalizedLetter, $@"\??\{targetDir}")
                   || DefineDosDeviceW(DDD_REMOVE_DEFINITION, normalizedLetter, null);
            _mountedByUs.Remove(normalizedLetter);
        }
        else
        {
            removed = DefineDosDeviceW(DDD_REMOVE_DEFINITION, normalizedLetter, null);
        }

        TryRemoveExplorerDriveLabel(normalizedLetter[0]);
        return removed;
    }

    public bool IsDriveMounted(string driveLetter)
    {
        string normalizedLetter = NormalizeDriveLetter(driveLetter);
        var buffer = new StringBuilder(512);
        uint chars = QueryDosDeviceW(normalizedLetter, buffer, (uint)buffer.Capacity);
        return chars > 0;
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
            // Ignorar si políticas de grupo restringen personalización de etiquetas en HKCU
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

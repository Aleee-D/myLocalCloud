using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MyLocalCloud.Installer;

[SupportedOSPlatform("windows")]
internal static class InstallerProgram
{
    public const string AppName = "myLocalCloud";
    public const string AppVersion = "1.0";
    public const string Organization = "android.ar";
    public const string Developer = "Alejandro Ferrer";
    public const string FullPublisher = "android.ar (Desarrollador: Alejandro Ferrer)";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        string defaultInstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            AppName);

        if (args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            PerformUninstall(defaultInstallDir);
            return 0;
        }

        if (args.Any(a => string.Equals(a, "/SILENT", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase)))
        {
            InstallApplication(defaultInstallDir, createDesktopShortcut: true, startWithWindows: true, launchNow: false);
            return 0;
        }

        using var wizard = new SetupWizardForm(defaultInstallDir);
        Application.Run(wizard);
        return 0;
    }

    public static void InstallApplication(
        string targetDir,
        bool createDesktopShortcut,
        bool startWithWindows,
        bool launchNow)
    {
        // Detener cualquier instancia previa de myLocalCloud (sin tocar jamás GoogleDriveFS.exe)
        try
        {
            foreach (Process proc in Process.GetProcessesByName("myLocalCloud"))
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch { }

        Directory.CreateDirectory(targetDir);

        // 1. Extraer el paquete embebido de myLocalCloud
        using Stream? payloadStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MyLocalCloud.Payload.zip")
            ?? throw new InvalidOperationException("No se encontró el paquete embebido de la aplicación dentro del instalador.");

        using (var archive = new ZipArchive(payloadStream, ZipArchiveMode.Read))
        {
            archive.ExtractToDirectory(targetDir, overwriteFiles: true);
        }

        string mainExePath = Path.Combine(targetDir, "myLocalCloud.exe");
        string vfsRootFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "myLocalCloud");
        Directory.CreateDirectory(vfsRootFolder);

        // 2. Copiar el propio instalador como desinstalador
        string currentProcessPath = Environment.ProcessPath ?? string.Empty;
        string uninstallerPath = Path.Combine(targetDir, "Uninstall-myLocalCloud.exe");
        if (!string.IsNullOrEmpty(currentProcessPath) && File.Exists(currentProcessPath))
        {
            try { File.Copy(currentProcessPath, uninstallerPath, overwrite: true); } catch { }
        }

        // 3. Crear accesos directos (.lnk) en Menú Inicio y Escritorio
        string startMenuShortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            "myLocalCloud.lnk");
        CreateWindowsShortcut(
            startMenuShortcut,
            mainExePath,
            "",
            "myLocalCloud v1.0 — Desarrollada por android.ar (Alejandro Ferrer)");

        if (createDesktopShortcut)
        {
            string desktopShortcut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "myLocalCloud.lnk");
            CreateWindowsShortcut(
                desktopShortcut,
                mainExePath,
                "",
                "myLocalCloud v1.0 — Desarrollada por android.ar (Alejandro Ferrer)");
        }

        // 4. Registrar inicio automático junto al reloj de Windows (como Google Drive)
        using (RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
        {
            if (runKey is not null)
            {
                if (startWithWindows)
                {
                    runKey.SetValue(AppName, $"\"{mainExePath}\" --tray");
                }
                else
                {
                    runKey.DeleteValue(AppName, throwOnMissingValue: false);
                }
            }
        }

        // 5. Registrar en "Aplicaciones instaladas" de Windows con versión 1.0 y autoría android.ar / Alejandro Ferrer
        using (RegistryKey? uninstallKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\myLocalCloud"))
        {
            if (uninstallKey is not null)
            {
                uninstallKey.SetValue("DisplayName", "myLocalCloud (Files On-Demand VFS)");
                uninstallKey.SetValue("DisplayVersion", AppVersion);
                uninstallKey.SetValue("Publisher", FullPublisher);
                uninstallKey.SetValue("DisplayIcon", mainExePath);
                uninstallKey.SetValue("InstallLocation", targetDir);
                uninstallKey.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
                uninstallKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                uninstallKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                uninstallKey.SetValue("Comments", "Versión 1.0 - Desarrollada por android.ar | Desarrollador: Alejandro Ferrer");
            }
        }

        // 6. Iniciar la aplicación si el usuario lo solicitó
        if (launchNow && File.Exists(mainExePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = mainExePath,
                WorkingDirectory = targetDir,
                UseShellExecute = true
            });
        }
    }

    private static void PerformUninstall(string installDir)
    {
        using (RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
        {
            runKey?.DeleteValue(AppName, throwOnMissingValue: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\myLocalCloud", throwOnMissingSubKey: false);

        string desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "myLocalCloud.lnk");
        string startMenuShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "myLocalCloud.lnk");
        if (File.Exists(desktopShortcut)) try { File.Delete(desktopShortcut); } catch { }
        if (File.Exists(startMenuShortcut)) try { File.Delete(startMenuShortcut); } catch { }

        MessageBox.Show(
            "myLocalCloud v1.0 se ha desinstalado correctamente de este equipo.\n\n" +
            "Desarrollada por android.ar — Desarrollador: Alejandro Ferrer",
            "Desinstalación de myLocalCloud",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void CreateWindowsShortcut(string shortcutPath, string targetPath, string arguments, string description)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null)
                return;

            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.Arguments = arguments;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.Description = description;
            shortcut.IconLocation = $"{targetPath},0";
            shortcut.Save();
        }
        catch
        {
            // Ignorar si WScript.Shell está restringido por políticas locales
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class SetupWizardForm : Form
{
    private readonly TextBox _txtInstallPath;
    private readonly CheckBox _chkDesktopShortcut;
    private readonly CheckBox _chkStartWithWindows;
    private readonly CheckBox _chkLaunchAfterInstall;
    private readonly ProgressBar _progressBar;
    private readonly Button _btnInstall;
    private readonly Label _lblStatus;

    public SetupWizardForm(string defaultInstallDir)
    {
        Text = "Instalador de myLocalCloud — Versión 1.0 (android.ar)";
        Size = new Size(640, 470);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(15, 23, 42);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        var bannerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 95,
            BackColor = Color.FromArgb(30, 41, 59)
        };

        var lblTitle = new Label
        {
            Text = "Instalar myLocalCloud para Windows",
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(24, 16)
        };

        var lblSubtitle = new Label
        {
            Text = "Versión 1.0 — Desarrollada por android.ar | Desarrollador: Alejandro Ferrer",
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 189, 248),
            AutoSize = true,
            Location = new Point(26, 54)
        };

        bannerPanel.Controls.Add(lblTitle);
        bannerPanel.Controls.Add(lblSubtitle);

        var lblDesc = new Label
        {
            Text = "Este asistente instalará myLocalCloud v1.0 en tu equipo. La aplicación se ubica junto al reloj\n" +
                   "en la barra de tareas (como Google Drive) y sincroniza periódicamente tus nubes con\n" +
                   "cifrado local Windows DPAPI + SQLCipher AES-256.",
            ForeColor = Color.FromArgb(203, 213, 225),
            AutoSize = true,
            Location = new Point(24, 112)
        };

        var lblPath = new Label
        {
            Text = "Carpeta de instalación:",
            ForeColor = Color.FromArgb(148, 163, 184),
            AutoSize = true,
            Location = new Point(24, 182)
        };

        _txtInstallPath = new TextBox
        {
            Text = defaultInstallDir,
            Location = new Point(24, 206),
            Width = 570,
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        _chkDesktopShortcut = new CheckBox
        {
            Text = "Crear icono de acceso directo en el Escritorio",
            Checked = true,
            AutoSize = true,
            Location = new Point(24, 248),
            ForeColor = Color.White
        };

        _chkStartWithWindows = new CheckBox
        {
            Text = "Iniciar automáticamente en la barra de tareas cerca del reloj al iniciar Windows",
            Checked = true,
            AutoSize = true,
            Location = new Point(24, 278),
            ForeColor = Color.White
        };

        _chkLaunchAfterInstall = new CheckBox
        {
            Text = "Ejecutar myLocalCloud v1.0 al finalizar la instalación",
            Checked = true,
            AutoSize = true,
            Location = new Point(24, 308),
            ForeColor = Color.White
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(24, 345),
            Width = 570,
            Height = 18,
            Style = ProgressBarStyle.Continuous
        };

        _lblStatus = new Label
        {
            Text = "Listo para instalar myLocalCloud v1.0 (android.ar - Alejandro Ferrer)",
            ForeColor = Color.FromArgb(148, 163, 184),
            AutoSize = true,
            Location = new Point(24, 370)
        };

        _btnInstall = new Button
        {
            Text = "Instalar myLocalCloud v1.0",
            BackColor = Color.FromArgb(14, 165, 233),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(210, 38),
            Location = new Point(384, 382),
            Cursor = Cursors.Hand
        };
        _btnInstall.Click += async (_, _) => await RunInstallAsync();

        Controls.AddRange([
            bannerPanel,
            lblDesc,
            lblPath,
            _txtInstallPath,
            _chkDesktopShortcut,
            _chkStartWithWindows,
            _chkLaunchAfterInstall,
            _progressBar,
            _lblStatus,
            _btnInstall
        ]);
    }

    private async Task RunInstallAsync()
    {
        _btnInstall.Enabled = false;
        _progressBar.Value = 25;
        _lblStatus.Text = "Extrayendo archivos cifrados y registrando servicios...";

        try
        {
            string targetDir = _txtInstallPath.Text.Trim();
            bool desktop = _chkDesktopShortcut.Checked;
            bool autorun = _chkStartWithWindows.Checked;
            bool launch = _chkLaunchAfterInstall.Checked;

            await Task.Run(() => InstallerProgram.InstallApplication(targetDir, desktop, autorun, launch));

            _progressBar.Value = 100;
            _lblStatus.Text = "¡Instalación completada con éxito!";

            MessageBox.Show(
                this,
                "myLocalCloud — Versión 1.0 se instaló correctamente.\n\n" +
                "Desarrollada por: android.ar\n" +
                "Desarrollador: Alejandro Ferrer\n\n" +
                "Encontrarás el icono de myLocalCloud en la barra de tareas cerca del reloj de Windows.",
                "Instalación completada — myLocalCloud v1.0",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            Close();
        }
        catch (Exception ex)
        {
            _btnInstall.Enabled = true;
            MessageBox.Show(this, $"Error durante la instalación: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

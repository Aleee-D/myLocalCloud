using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MyLocalCloud.Installer;

[SupportedOSPlatform("windows")]
internal static class InstallerProgram
{
    public const string AppName = "myLocalCloud";
    public const string AppVersion = "1.1";
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

        // 1. Extraer el paquete embebido de myLocalCloud v1.1
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
            "myLocalCloud v1.1 — Desarrollada por android.ar (Alejandro Ferrer)");

        if (createDesktopShortcut)
        {
            string desktopShortcut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "myLocalCloud.lnk");
            CreateWindowsShortcut(
                desktopShortcut,
                mainExePath,
                "",
                "myLocalCloud v1.1 — Desarrollada por android.ar (Alejandro Ferrer)");
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

        // 5. Registrar en "Aplicaciones instaladas" de Windows con versión 1.1 y autoría android.ar / Alejandro Ferrer
        using (RegistryKey? uninstallKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\myLocalCloud"))
        {
            if (uninstallKey is not null)
            {
                uninstallKey.SetValue("DisplayName", "myLocalCloud v1.1 (Unidades Virtuales Multicloud VFS)");
                uninstallKey.SetValue("DisplayVersion", AppVersion);
                uninstallKey.SetValue("Publisher", FullPublisher);
                uninstallKey.SetValue("DisplayIcon", mainExePath);
                uninstallKey.SetValue("InstallLocation", targetDir);
                uninstallKey.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
                uninstallKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                uninstallKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                uninstallKey.SetValue("Comments", "Versión 1.1 - Desarrollada por android.ar | Desarrollador: Alejandro Ferrer");
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
            "myLocalCloud v1.1 se ha desinstalado correctamente de este equipo.\n\n" +
            "Desarrollada por android.ar — Desarrollador: Alejandro Ferrer",
            "Desinstalación de myLocalCloud v1.1",
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
        // Paleta Primaria Celeste y Gris
        Color grayPrimary = Color.FromArgb(71, 85, 105);
        Color graySurface = Color.FromArgb(226, 232, 240);
        Color skyBluePrimary = Color.FromArgb(14, 165, 233);
        Color skyBlueLight = Color.FromArgb(56, 189, 248);
        Color textDark = Color.FromArgb(30, 41, 59);

        Text = "Instalador de myLocalCloud — Versión 1.1 (android.ar)";
        Size = new Size(660, 485);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = graySurface;
        ForeColor = textDark;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        var bannerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 98,
            BackColor = grayPrimary
        };

        var logoBox = new PictureBox
        {
            Image = CreateCloudTreeInstallerLogo(68),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(20, 15),
            Size = new Size(68, 68),
            BackColor = Color.Transparent
        };

        var lblTitle = new Label
        {
            Text = "Instalar myLocalCloud v1.1 para Windows",
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(224, 242, 254),
            AutoSize = true,
            Location = new Point(100, 16)
        };

        var lblSubtitle = new Label
        {
            Text = "Versión 1.1 — Desarrollada por android.ar | Desarrollador: Alejandro Ferrer",
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            ForeColor = skyBlueLight,
            AutoSize = true,
            Location = new Point(102, 54)
        };

        bannerPanel.Controls.Add(logoBox);
        bannerPanel.Controls.Add(lblTitle);
        bannerPanel.Controls.Add(lblSubtitle);

        var lblDesc = new Label
        {
            Text = "Este asistente instalará myLocalCloud v1.1 (Paleta Celeste y Gris, Logo Árbol-Nube).\n" +
                   "Permite registrar múltiples usuarios de la misma nube o distintas nubes y montar para cada uno\n" +
                   "una letra de unidad de disco virtual (M:, N:, O:...) con descarga en el acto al abrir archivos.",
            ForeColor = textDark,
            AutoSize = true,
            Location = new Point(24, 112)
        };

        var lblPath = new Label
        {
            Text = "Carpeta de instalación:",
            ForeColor = grayPrimary,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 184)
        };

        _txtInstallPath = new TextBox
        {
            Text = defaultInstallDir,
            Location = new Point(24, 208),
            Width = 465,
            BackColor = Color.White,
            ForeColor = textDark,
            BorderStyle = BorderStyle.FixedSingle
        };

        var btnBrowseInstallPath = new Button
        {
            Text = "📂 Examinar...",
            BackColor = grayPrimary,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Location = new Point(500, 205),
            Size = new Size(115, 29),
            Cursor = Cursors.Hand
        };
        btnBrowseInstallPath.FlatAppearance.BorderSize = 0;
        btnBrowseInstallPath.Click += (_, _) =>
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = "Selecciona la carpeta donde deseas instalar la aplicación myLocalCloud v1.1:",
                SelectedPath = Directory.Exists(_txtInstallPath.Text)
                    ? _txtInstallPath.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ShowNewFolderButton = true
            };

            if (folderDialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
            {
                string chosen = folderDialog.SelectedPath.Trim();
                if (!chosen.EndsWith("myLocalCloud", StringComparison.OrdinalIgnoreCase))
                {
                    chosen = Path.Combine(chosen, "myLocalCloud");
                }
                _txtInstallPath.Text = chosen;
            }
        };

        _chkDesktopShortcut = new CheckBox
        {
            Text = "Crear icono de acceso directo en el Escritorio",
            Checked = true,
            AutoSize = true,
            Location = new Point(24, 250),
            ForeColor = textDark
        };

        _chkStartWithWindows = new CheckBox
        {
            Text = "Iniciar automáticamente en la barra de tareas cerca del reloj al iniciar Windows",
            Checked = true,
            AutoSize = true,
            Location = new Point(24, 280),
            ForeColor = textDark
        };

        _chkLaunchAfterInstall = new CheckBox
        {
            Text = "Ejecutar myLocalCloud v1.1 al finalizar la instalación",
            Checked = true,
            AutoSize = true,
            Location = new Point(24, 310),
            ForeColor = textDark
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(24, 348),
            Width = 590,
            Height = 18,
            Style = ProgressBarStyle.Continuous
        };

        _lblStatus = new Label
        {
            Text = "Listo para instalar myLocalCloud v1.1 (android.ar - Alejandro Ferrer)",
            ForeColor = grayPrimary,
            AutoSize = true,
            Location = new Point(24, 374)
        };

        _btnInstall = new Button
        {
            Text = "Instalar myLocalCloud v1.1",
            BackColor = skyBluePrimary,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            Size = new Size(220, 40),
            Location = new Point(394, 390),
            Cursor = Cursors.Hand
        };
        _btnInstall.FlatAppearance.BorderSize = 0;
        _btnInstall.Click += async (_, _) => await RunInstallAsync();

        Controls.AddRange([
            bannerPanel,
            lblDesc,
            lblPath,
            _txtInstallPath,
            btnBrowseInstallPath,
            _chkDesktopShortcut,
            _chkStartWithWindows,
            _chkLaunchAfterInstall,
            _progressBar,
            _lblStatus,
            _btnInstall
        ]);
    }

    private static Bitmap CreateCloudTreeInstallerLogo(int size)
    {
        var bmp = new Bitmap(size, size);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        float scale = size / 64f;
        g.ScaleTransform(scale, scale);

        // Tronco y ramas del árbol en Gris Plata
        using (var trunkBrush = new SolidBrush(Color.FromArgb(203, 213, 225)))
        {
            PointF[] trunk =
            [
                new PointF(29.5f, 34f),
                new PointF(34.5f, 34f),
                new PointF(37f, 52f),
                new PointF(41f, 56f),
                new PointF(23f, 56f),
                new PointF(27f, 52f)
            ];
            g.FillPolygon(trunkBrush, trunk);
        }

        // Copa del árbol en forma de Nube Celeste
        using (var cloudBrush = new LinearGradientBrush(
            new RectangleF(10, 10, 44, 28),
            Color.FromArgb(224, 242, 254),
            Color.FromArgb(14, 165, 233),
            90f))
        {
            g.FillEllipse(cloudBrush, 12, 20, 17, 15);
            g.FillEllipse(cloudBrush, 18, 14, 16, 16);
            g.FillEllipse(cloudBrush, 24, 11, 19, 19);
            g.FillEllipse(cloudBrush, 35, 19, 17, 16);
            g.FillRectangle(cloudBrush, 19, 23, 26, 12);
        }

        return bmp;
    }

    private async Task RunInstallAsync()
    {
        _btnInstall.Enabled = false;
        _progressBar.Value = 25;
        _lblStatus.Text = "Extrayendo archivos cifrados y registrando servicios v1.1...";

        try
        {
            string targetDir = _txtInstallPath.Text.Trim();
            bool desktop = _chkDesktopShortcut.Checked;
            bool autorun = _chkStartWithWindows.Checked;
            bool launch = _chkLaunchAfterInstall.Checked;

            await Task.Run(() => InstallerProgram.InstallApplication(targetDir, desktop, autorun, launch));

            _progressBar.Value = 100;
            _lblStatus.Text = "¡Instalación de v1.1 completada con éxito!";

            MessageBox.Show(
                this,
                "myLocalCloud — Versión 1.1 se instaló correctamente.\n\n" +
                "Desarrollada por: android.ar\n" +
                "Desarrollador: Alejandro Ferrer\n\n" +
                "Encontrarás el nuevo icono del Árbol con copa de Nube cerca del reloj de Windows.",
                "Instalación completada — myLocalCloud v1.1",
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

using System.Diagnostics;
using System.Runtime.Versioning;
using MyLocalCloud.Domain;
using MyLocalCloud.Domain.Enums;
using MyLocalCloud.UI.Tray;
using MyLocalCloud.UI.ViewModels;

namespace MyLocalCloud.UI.Views;

/// <summary>
/// Ventana principal del Panel de Control de myLocalCloud v1.1.
/// Diseñada con paleta de colores primarios Celeste y Gris y el nuevo logotipo del Árbol con copa de Nube.
/// Permite registrar múltiples usuarios de diferentes nubes o de la misma nube, asignando a cada uno
/// una letra de unidad de disco virtual (M:, N:, O:, etc.) con archivos en modo referencia (descarga al abrir) o copia local.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ControlPanelForm : Form
{
    // --- Paleta de Colores Primarios: Celeste y Gris ---
    private static readonly Color PrimaryGrayDark = Color.FromArgb(71, 85, 105);      // Gris Pizarra Primario (#475569)
    private static readonly Color PrimaryGrayMedium = Color.FromArgb(100, 116, 139);  // Gris Acero Medio (#64748B)
    private static readonly Color PrimaryGrayLight = Color.FromArgb(226, 232, 240);   // Gris Claro Superficie (#E2E8F0)
    private static readonly Color PrimaryGrayCard = Color.FromArgb(241, 245, 249);    // Gris Perla Tarjeta (#F1F5F9)
    private static readonly Color PrimarySkyBlue = Color.FromArgb(14, 165, 233);      // Celeste Primario (#0EA5E9)
    private static readonly Color PrimarySkyBlueLight = Color.FromArgb(56, 189, 248); // Celeste Brillante (#38BDF8)
    private static readonly Color PrimarySkyBlueSoft = Color.FromArgb(224, 242, 254);  // Celeste Hielo Suave (#E0F2FE)
    private static readonly Color PrimarySkyBlueDeep = Color.FromArgb(2, 132, 199);   // Celeste Profundo (#0284C7)
    private static readonly Color TextDarkGray = Color.FromArgb(30, 41, 59);          // Gris Carbón para lectura nítida

    private readonly MainControlPanelViewModel _viewModel;
    private readonly Action<string> _showTrayNotification;

    private readonly Label _lblStatus;
    private readonly ListView _accountsListView;
    private readonly ComboBox _cmbProvider;
    private readonly ComboBox _cmbDriveLetter;
    private readonly ComboBox _cmbDeliveryMode;
    private readonly TextBox _txtDisplayName;
    private readonly TextBox _txtEmailOrUser;
    private readonly TextBox _txtSecretToken;
    private readonly NumericUpDown _numSyncMinutes;
    private readonly Button _btnPauseResume;

    public bool AllowFullExit { get; set; }

    public ControlPanelForm(
        MainControlPanelViewModel viewModel,
        Action<string> showTrayNotification)
    {
        _viewModel = viewModel;
        _showTrayNotification = showTrayNotification;

        Text = $"{AppVersionInfo.AppName} v{AppVersionInfo.Version} — Unidades Virtuales Multicloud (Paleta Celeste y Gris)";
        Size = new Size(1060, 700);
        MinimumSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = PrimaryGrayLight;
        ForeColor = TextDarkGray;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        Icon = CloudIconGenerator.CreateCloudTrayIcon();

        // --- 1. Encabezado (Gris Pizarra Primario con acentos Celestes y Logo del Árbol-Nube) ---
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 98,
            BackColor = PrimaryGrayDark,
            Padding = new Padding(18, 12, 18, 10)
        };

        var logoPictureBox = new PictureBox
        {
            Image = CloudIconGenerator.CreateCloudTreeLogoBitmap(68),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(18, 14),
            Size = new Size(68, 68),
            BackColor = Color.Transparent
        };

        var lblTitle = new Label
        {
            Text = "myLocalCloud v1.1 — Unidades de Disco Virtuales Multicloud",
            Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold),
            ForeColor = PrimarySkyBlueSoft,
            AutoSize = true,
            Location = new Point(98, 12)
        };

        _lblStatus = new Label
        {
            Text = _viewModel.StatusHeadline,
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            ForeColor = PrimarySkyBlueLight,
            AutoSize = true,
            Location = new Point(100, 42)
        };

        var lblCoexistence = new Label
        {
            Text = $"✓ {_viewModel.CoexistenceHeadline} | Multi-usuario misma nube o distintas nubes",
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(226, 232, 240),
            AutoSize = true,
            Location = new Point(100, 66)
        };

        var btnAbout = CreateActionButton("🌳☁ Acerca de v1.1", PrimarySkyBlue, Color.White, new Point(875, 28), new Size(150, 38));
        btnAbout.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnAbout.Click += (_, _) => ShowOfficialVersionDialog(this);

        headerPanel.Controls.Add(logoPictureBox);
        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(_lblStatus);
        headerPanel.Controls.Add(lblCoexistence);
        headerPanel.Controls.Add(btnAbout);

        // --- 2. Barra de Sincronización Periódica (Celeste Hielo + Gris Acero) ---
        var syncBarPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 58,
            BackColor = PrimarySkyBlueSoft,
            Padding = new Padding(20, 10, 20, 10)
        };

        var btnSyncNow = CreateActionButton("⟳ Sincronizar ahora", PrimarySkyBlueDeep, Color.White, new Point(20, 11), new Size(170, 36));
        btnSyncNow.Click += async (_, _) => await TriggerSyncNowAsync();

        _btnPauseResume = CreateActionButton("⏸ Pausar sincronización", PrimaryGrayMedium, Color.White, new Point(200, 11), new Size(195, 36));
        _btnPauseResume.Click += (_, _) =>
        {
            _viewModel.TogglePauseResumeSyncCommand.Execute(null);
            RefreshUiFromViewModel();
        };

        var lblInterval = new Label
        {
            Text = "Sincronización periódica cada (min):",
            ForeColor = TextDarkGray,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(415, 19)
        };

        _numSyncMinutes = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 120,
            Value = _viewModel.SyncIntervalMinutes,
            Location = new Point(650, 15),
            Width = 65,
            BackColor = Color.White,
            ForeColor = TextDarkGray
        };
        _numSyncMinutes.ValueChanged += (_, _) =>
        {
            _viewModel.UpdateSyncInterval((int)_numSyncMinutes.Value);
            RefreshUiFromViewModel();
        };

        var btnOpenVfsFolder = CreateActionButton("📂 Abrir raíz myLocalCloud", PrimarySkyBlue, Color.White, new Point(845, 11), new Size(180, 36));
        btnOpenVfsFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnOpenVfsFolder.Click += (_, _) => OpenVfsRootInExplorer();

        syncBarPanel.Controls.Add(btnSyncNow);
        syncBarPanel.Controls.Add(_btnPauseResume);
        syncBarPanel.Controls.Add(lblInterval);
        syncBarPanel.Controls.Add(_numSyncMinutes);
        syncBarPanel.Controls.Add(btnOpenVfsFolder);

        // --- 3. Panel de Registro Multi-Usuario y Selección de Letra de Unidad Virtual ---
        var addAccountGroup = new Panel
        {
            Dock = DockStyle.Top,
            Height = 148,
            BackColor = PrimaryGrayCard,
            Padding = new Padding(20, 10, 20, 10)
        };

        var lblAddHeader = new Label
        {
            Text = "Registrar Usuario de Nube y Montar Letra de Unidad Virtual (Permite múltiples cuentas de la misma nube o de distintas nubes):",
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            ForeColor = PrimarySkyBlueDeep,
            AutoSize = true,
            Location = new Point(20, 10)
        };

        // Fila 1 de controles de alta
        var lblProv = new Label { Text = "Nube:", Location = new Point(20, 38), AutoSize = true, ForeColor = PrimaryGrayDark };
        _cmbProvider = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(20, 60),
            Width = 125,
            BackColor = Color.White,
            ForeColor = TextDarkGray
        };
        _cmbProvider.Items.AddRange(["GoogleDrive", "OneDrive", "Dropbox", "Ftp", "Mega"]);
        _cmbProvider.SelectedIndex = 0;
        _cmbProvider.SelectedIndexChanged += (_, _) =>
        {
            string? selectedText = _cmbProvider.SelectedItem as string;
            if (!string.IsNullOrEmpty(selectedText) && Enum.TryParse<CloudProviderType>(selectedText, out var parsed))
            {
                _viewModel.SelectedProviderType = parsed;
                int nextIdx = _viewModel.ConnectedAccounts.Count + 1;
                _txtDisplayName.Text = $"{parsed} Cuenta {nextIdx}";
            }
        };

        var lblDrive = new Label { Text = "Unidad Virtual:", Location = new Point(158, 38), AutoSize = true, ForeColor = PrimaryGrayDark };
        _cmbDriveLetter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(158, 60),
            Width = 90,
            BackColor = PrimarySkyBlueSoft,
            ForeColor = PrimarySkyBlueDeep,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold)
        };
        foreach (string letter in _viewModel.AvailableDriveLetters)
        {
            _cmbDriveLetter.Items.Add(letter);
        }
        if (_cmbDriveLetter.Items.Count > 0)
            _cmbDriveLetter.SelectedIndex = 0;

        _cmbDriveLetter.SelectedIndexChanged += (_, _) =>
        {
            if (_cmbDriveLetter.SelectedItem is string letter)
            {
                _viewModel.SelectedVirtualDriveLetter = letter;
            }
        };

        var lblMode = new Label { Text = "Modo de Archivos en la Unidad:", Location = new Point(262, 38), AutoSize = true, ForeColor = PrimaryGrayDark };
        _cmbDeliveryMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(262, 60),
            Width = 250,
            BackColor = Color.White,
            ForeColor = TextDarkGray
        };
        _cmbDeliveryMode.Items.Add("Referencia en Nube (Descarga al abrir)");
        _cmbDeliveryMode.Items.Add("Copia Local Completa en Disco");
        _cmbDeliveryMode.SelectedIndex = 0;
        _cmbDeliveryMode.SelectedIndexChanged += (_, _) =>
        {
            _viewModel.SelectedDeliveryMode = _cmbDeliveryMode.SelectedIndex == 1
                ? CloudFileDeliveryMode.LocalCopy
                : CloudFileDeliveryMode.OnDemandReference;
        };

        var lblName = new Label { Text = "Alias de la Cuenta:", Location = new Point(526, 38), AutoSize = true, ForeColor = PrimaryGrayDark };
        _txtDisplayName = new TextBox
        {
            Text = _viewModel.NewAccountDisplayName,
            Location = new Point(526, 60),
            Width = 160,
            BackColor = Color.White,
            ForeColor = TextDarkGray,
            BorderStyle = BorderStyle.FixedSingle
        };

        var lblUser = new Label { Text = "Usuario / Correo:", Location = new Point(698, 38), AutoSize = true, ForeColor = PrimaryGrayDark };
        _txtEmailOrUser = new TextBox
        {
            Text = _viewModel.NewAccountEmailOrUser,
            Location = new Point(698, 60),
            Width = 175,
            BackColor = Color.White,
            ForeColor = TextDarkGray,
            BorderStyle = BorderStyle.FixedSingle
        };

        // Fila 2 de controles de alta (Token DPAPI + Botón Vincular)
        var lblToken = new Label
        {
            Text = "Token OAuth2 / Contraseña FTP / Clave MEGA (Se cifra con Windows DPAPI):",
            Location = new Point(20, 102),
            AutoSize = true,
            ForeColor = PrimaryGrayDark
        };

        _txtSecretToken = new TextBox
        {
            Location = new Point(465, 99),
            Width = 240,
            UseSystemPasswordChar = true,
            BackColor = Color.White,
            ForeColor = TextDarkGray,
            BorderStyle = BorderStyle.FixedSingle
        };

        var btnConnect = CreateActionButton("➕ Vincular Usuario y Montar Unidad", PrimarySkyBlue, Color.White, new Point(725, 95), new Size(300, 36));
        btnConnect.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnConnect.Click += async (_, _) =>
        {
            _viewModel.NewAccountDisplayName = _txtDisplayName.Text;
            _viewModel.NewAccountEmailOrUser = _txtEmailOrUser.Text;
            _viewModel.NewAccountSecretOrToken = _txtSecretToken.Text;
            if (_cmbDriveLetter.SelectedItem is string letter)
            {
                _viewModel.SelectedVirtualDriveLetter = letter;
            }
            await _viewModel.ConnectAccountCommand.ExecuteAsync(null);
            _txtSecretToken.Clear();
            _txtDisplayName.Text = _viewModel.NewAccountDisplayName;
            _txtEmailOrUser.Text = _viewModel.NewAccountEmailOrUser;
            RefreshUiFromViewModel();
        };

        addAccountGroup.Controls.AddRange([
            lblAddHeader,
            lblProv, _cmbProvider,
            lblDrive, _cmbDriveLetter,
            lblMode, _cmbDeliveryMode,
            lblName, _txtDisplayName,
            lblUser, _txtEmailOrUser,
            lblToken, _txtSecretToken,
            btnConnect
        ]);

        // --- 4. Lista de Usuarios Multicloud y Unidades Virtuales ---
        var listContainer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 10, 20, 10),
            BackColor = PrimaryGrayLight
        };

        _accountsListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = Color.White,
            ForeColor = TextDarkGray,
            BorderStyle = BorderStyle.FixedSingle
        };
        _accountsListView.Columns.Add("Unidad Virtual", 105);
        _accountsListView.Columns.Add("Nube", 110);
        _accountsListView.Columns.Add("Usuario / Correo", 180);
        _accountsListView.Columns.Add("Alias de Cuenta", 155);
        _accountsListView.Columns.Add("Modo de Archivos", 215);
        _accountsListView.Columns.Add("Archivos", 75);
        _accountsListView.Columns.Add("Estado / Sincronización", 165);

        // Al hacer doble clic sobre una cuenta, abrir su unidad virtual (y descargar bajo demanda si es referencia)
        _accountsListView.DoubleClick += async (_, _) => await OpenSelectedVirtualDriveAndHydrateOnDemandAsync();

        listContainer.Controls.Add(_accountsListView);

        // --- 5. Pie de Página (Gris Pizarra + Botones Celestes de Unidad Virtual + Autoría v1.1) ---
        var footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            BackColor = PrimaryGrayDark,
            Padding = new Padding(20, 8, 20, 8)
        };

        var lblVersionFooter = new Label
        {
            Text = "Versión 1.1 — Fue desarrollada por android.ar | Desarrollador: Alejandro Ferrer",
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            ForeColor = PrimarySkyBlueLight,
            AutoSize = true,
            Location = new Point(20, 19)
        };

        var btnOpenDrive = CreateActionButton("💿 Abrir Unidad Virtual (Descargar al abrir)", PrimarySkyBlue, Color.White, new Point(485, 12), new Size(300, 36));
        btnOpenDrive.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnOpenDrive.Click += async (_, _) => await OpenSelectedVirtualDriveAndHydrateOnDemandAsync();

        var btnRemoveSelected = CreateActionButton("Desconectar y Liberar Unidad", PrimaryGrayMedium, Color.White, new Point(795, 12), new Size(230, 36));
        btnRemoveSelected.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnRemoveSelected.Click += async (_, _) =>
        {
            if (_accountsListView.SelectedItems.Count > 0 &&
                _accountsListView.SelectedItems[0].Tag is Guid id)
            {
                await _viewModel.RemoveAccountCommand.ExecuteAsync(id);
                RefreshUiFromViewModel();
            }
        };

        footerPanel.Controls.Add(lblVersionFooter);
        footerPanel.Controls.Add(btnOpenDrive);
        footerPanel.Controls.Add(btnRemoveSelected);

        Controls.Add(listContainer);
        Controls.Add(addAccountGroup);
        Controls.Add(syncBarPanel);
        Controls.Add(headerPanel);
        Controls.Add(footerPanel);

        _viewModel.PropertyChanged += (_, _) =>
        {
            if (IsHandleCreated)
            {
                BeginInvoke(RefreshUiFromViewModel);
            }
        };

        Shown += async (_, _) =>
        {
            try
            {
                await _viewModel.LoadAccountsCommand.ExecuteAsync(null);
                RefreshUiFromViewModel();
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"Aviso al cargar cuentas: {ex.Message}";
            }
        };
    }

    private async Task OpenSelectedVirtualDriveAndHydrateOnDemandAsync()
    {
        if (_accountsListView.SelectedItems.Count == 0)
        {
            OpenVfsRootInExplorer();
            return;
        }

        if (_accountsListView.SelectedItems[0].Tag is not Guid accountId)
            return;

        CloudAccountItemViewModel? item = _viewModel.ConnectedAccounts.FirstOrDefault(a => a.Id == accountId);
        if (item is null)
            return;

        // Si está en modo Referencia en Nube, al abrir desde aquí se hidratan los archivos solicitados en el acto
        await _viewModel.HydrateAccountFilesOnDemandCommand.ExecuteAsync(accountId);

        string driveRoot = $"{item.VirtualDriveLetter}\\";
        string pathToOpen = Directory.Exists(driveRoot) ? driveRoot : item.LocalSyncRootPath;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{pathToOpen}\"",
            UseShellExecute = true
        });
    }

    public async Task TriggerSyncNowAsync()
    {
        try
        {
            await _viewModel.SyncNowCommand.ExecuteAsync(null);
            RefreshUiFromViewModel();
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"Error al sincronizar: {ex.Message}";
        }
    }

    public void RefreshUiFromViewModel()
    {
        _lblStatus.Text = _viewModel.StatusHeadline;
        _btnPauseResume.Text = _viewModel.IsPaused
            ? "▶ Reanudar sincronización"
            : "⏸ Pausar sincronización";

        // Actualizar selector de letras de unidad virtual disponibles
        _cmbDriveLetter.BeginUpdate();
        _cmbDriveLetter.Items.Clear();
        foreach (string letter in _viewModel.AvailableDriveLetters)
        {
            _cmbDriveLetter.Items.Add(letter);
        }
        if (_cmbDriveLetter.Items.Count > 0)
        {
            int idx = _cmbDriveLetter.Items.IndexOf(_viewModel.SelectedVirtualDriveLetter);
            _cmbDriveLetter.SelectedIndex = idx >= 0 ? idx : 0;
        }
        _cmbDriveLetter.EndUpdate();

        // Actualizar tabla de usuarios y unidades virtuales
        _accountsListView.BeginUpdate();
        _accountsListView.Items.Clear();
        foreach (CloudAccountItemViewModel item in _viewModel.ConnectedAccounts)
        {
            var lvi = new ListViewItem($"{item.VirtualDriveLetter}\\")
            {
                Tag = item.Id,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = PrimarySkyBlueDeep
            };
            lvi.SubItems.Add(item.ProviderType.ToString());
            lvi.SubItems.Add(item.AccountEmailOrUser);
            lvi.SubItems.Add(item.DisplayName);
            lvi.SubItems.Add(item.DeliveryModeDescription);
            lvi.SubItems.Add(item.VirtualFilesCount.ToString());
            lvi.SubItems.Add(item.LastSyncText);
            _accountsListView.Items.Add(lvi);
        }
        _accountsListView.EndUpdate();
    }

    public static void ShowOfficialVersionDialog(IWin32Window? owner = null)
    {
        MessageBox.Show(
            owner,
            "myLocalCloud — Versión 1.1\n" +
            "Desarrollada por: android.ar\n" +
            "Desarrollador: Alejandro Ferrer\n\n" +
            "Novedades de la Versión 1.1:\n" +
            "• Unidades de disco virtuales dedicadas (M:, N:, O:, Z:) por cada usuario registrado.\n" +
            "• Registro de múltiples usuarios de la misma nube o de diferentes nubes para sumar y aprovechar sus espacios gratuitos/contratados.\n" +
            "• Archivos como referencia en la nube (se descargan en el mismo momento de abrirlos) o copia local.\n" +
            "• Paleta visual Celeste y Gris y nuevo logotipo (Árbol cuya copa es una Nube).\n" +
            "• Seguridad Windows DPAPI + SQLCipher AES-256 y coexistencia con Google Drive for Desktop.",
            "Acerca de myLocalCloud v1.1",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    public static void OpenVfsRootInExplorer()
    {
        string rootFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "myLocalCloud");

        Directory.CreateDirectory(rootFolder);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{rootFolder}\"",
            UseShellExecute = true
        });
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!AllowFullExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _showTrayNotification(
                "myLocalCloud v1.1 sigue activo junto al reloj de Windows manteniendo tus unidades virtuales sincronizadas.");
            return;
        }

        base.OnFormClosing(e);
    }

    private static Button CreateActionButton(string text, Color backColor, Color foreColor, Point location, Size size)
    {
        var btn = new Button
        {
            Text = text,
            BackColor = backColor,
            ForeColor = foreColor,
            FlatStyle = FlatStyle.Flat,
            Location = location,
            Size = size,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold)
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }
}

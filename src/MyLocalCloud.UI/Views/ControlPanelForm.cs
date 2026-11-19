using System.Diagnostics;
using System.Runtime.Versioning;
using MyLocalCloud.Domain;
using MyLocalCloud.Domain.Enums;
using MyLocalCloud.UI.Tray;
using MyLocalCloud.UI.ViewModels;

namespace MyLocalCloud.UI.Views;

/// <summary>
/// Ventana principal del Panel de Control de myLocalCloud v1.1.
/// Diseñada con paleta de colores primarios Celeste y Gris y el logotipo del Árbol con copa de Nube.
/// Incluye:
/// - Botón explícito "✓ Aceptar Cuenta y Vincular con la Nube" para registrar y dejar funcionando cada cuenta.
/// - Casilla de verificación (CheckBox) en cada cuenta para habilitarla o deshabilitarla individualmente.
/// - Selector de letra de unidad de disco virtual (M:, N:, O:...) y botón "📂 Examinar..." para ubicación local.
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
    private static readonly Color TextDarkGray = Color.FromArgb(30, 41, 59);          // Gris Carbón

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
    private readonly TextBox _txtBaseVfsPath;
    private readonly NumericUpDown _numSyncMinutes;
    private readonly Button _btnPauseResume;

    private bool _isUpdatingListViewProgrammatically;

    public bool AllowFullExit { get; set; }

    public ControlPanelForm(
        MainControlPanelViewModel viewModel,
        Action<string> showTrayNotification)
    {
        _viewModel = viewModel;
        _showTrayNotification = showTrayNotification;

        Text = $"{AppVersionInfo.AppName} v{AppVersionInfo.Version} — Unidades Virtuales Multicloud (Paleta Celeste y Gris)";
        Size = new Size(1060, 740);
        MinimumSize = new Size(960, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = PrimaryGrayLight;
        ForeColor = TextDarkGray;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        Icon = CloudIconGenerator.CreateCloudTrayIcon();

        // --- 1. Encabezado (Gris Pizarra Primario con acentos Celestes y Logo del Árbol-Nube) ---
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Width = 1040,
            Height = 96,
            BackColor = PrimaryGrayDark,
            Padding = new Padding(18, 12, 18, 10)
        };

        var logoPictureBox = new PictureBox
        {
            Image = CloudIconGenerator.CreateCloudTreeLogoBitmap(66),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(18, 14),
            Size = new Size(66, 66),
            BackColor = Color.Transparent
        };

        var lblTitle = new Label
        {
            Text = "myLocalCloud v1.1 — Unidades de Disco Virtuales Multicloud",
            Font = new Font("Segoe UI Semibold", 14.5f, FontStyle.Bold),
            ForeColor = PrimarySkyBlueSoft,
            AutoSize = true,
            Location = new Point(96, 10)
        };

        _lblStatus = new Label
        {
            Text = _viewModel.StatusHeadline,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            ForeColor = PrimarySkyBlueLight,
            AutoSize = true,
            Location = new Point(98, 40)
        };

        var lblCoexistence = new Label
        {
            Text = $"✓ {_viewModel.CoexistenceHeadline}",
            Font = new Font("Segoe UI", 8.8f, FontStyle.Regular),
            ForeColor = Color.FromArgb(226, 232, 240),
            AutoSize = true,
            Location = new Point(98, 64)
        };

        var btnAbout = CreateActionButton("🌳☁ Acerca de v1.1", PrimarySkyBlue, Color.White, new Point(780, 26), new Size(160, 38));
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
            Width = 1040,
            Height = 56,
            BackColor = PrimarySkyBlueSoft,
            Padding = new Padding(20, 10, 20, 10)
        };

        var btnSyncNow = CreateActionButton("⟳ Sincronizar ahora", PrimarySkyBlueDeep, Color.White, new Point(20, 10), new Size(165, 36));
        btnSyncNow.Click += async (_, _) => await TriggerSyncNowAsync();

        _btnPauseResume = CreateActionButton("⏸ Pausar sincronización", PrimaryGrayMedium, Color.White, new Point(195, 10), new Size(190, 36));
        _btnPauseResume.Click += (_, _) =>
        {
            _viewModel.TogglePauseResumeSyncCommand.Execute(null);
            RefreshUiFromViewModel();
        };

        var lblInterval = new Label
        {
            Text = "Sincronizar cada (min):",
            ForeColor = TextDarkGray,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(400, 18)
        };

        _numSyncMinutes = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 120,
            Value = _viewModel.SyncIntervalMinutes,
            Location = new Point(560, 14),
            Width = 62,
            BackColor = Color.White,
            ForeColor = TextDarkGray
        };
        _numSyncMinutes.ValueChanged += (_, _) =>
        {
            _viewModel.UpdateSyncInterval((int)_numSyncMinutes.Value);
            RefreshUiFromViewModel();
        };

        var btnOpenVfsFolder = CreateActionButton("📂 Abrir carpeta raíz VFS", PrimarySkyBlue, Color.White, new Point(645, 10), new Size(195, 36));
        btnOpenVfsFolder.Click += (_, _) => OpenVfsRootInExplorer(_txtBaseVfsPath?.Text);

        syncBarPanel.Controls.Add(btnSyncNow);
        syncBarPanel.Controls.Add(_btnPauseResume);
        syncBarPanel.Controls.Add(lblInterval);
        syncBarPanel.Controls.Add(_numSyncMinutes);
        syncBarPanel.Controls.Add(btnOpenVfsFolder);

        // --- 3. Panel de Registro Multi-Usuario, Unidad Virtual y Botón Aceptar/Vincular ---
        var addAccountGroup = new Panel
        {
            Dock = DockStyle.Top,
            Width = 1040,
            Height = 192,
            BackColor = PrimaryGrayCard,
            Padding = new Padding(20, 10, 20, 10)
        };

        var lblAddHeader = new Label
        {
            Text = "1) Selecciona la Nube, la Letra de Unidad Virtual y los datos del usuario; luego pulsa '✓ Aceptar Cuenta y Vincular con la Nube':",
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            ForeColor = PrimarySkyBlueDeep,
            AutoSize = true,
            Location = new Point(20, 8)
        };

        // Fila 1: Nube, Unidad Virtual, Modo de Archivos, Alias y Correo/Usuario
        var lblProv = new Label { Text = "Nube:", Location = new Point(20, 34), AutoSize = true, ForeColor = PrimaryGrayDark };
        _cmbProvider = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(20, 55),
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

        var lblDrive = new Label { Text = "Unidad Virtual:", Location = new Point(155, 34), AutoSize = true, ForeColor = PrimaryGrayDark };
        _cmbDriveLetter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(155, 55),
            Width = 88,
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

        var lblMode = new Label { Text = "Modo en Unidad Virtual:", Location = new Point(255, 34), AutoSize = true, ForeColor = PrimaryGrayDark };
        _cmbDeliveryMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(255, 55),
            Width = 240,
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

        var lblName = new Label { Text = "Alias de la Cuenta:", Location = new Point(508, 34), AutoSize = true, ForeColor = PrimaryGrayDark };
        _txtDisplayName = new TextBox
        {
            Text = _viewModel.NewAccountDisplayName,
            Location = new Point(508, 55),
            Width = 175,
            BackColor = Color.White,
            ForeColor = TextDarkGray,
            BorderStyle = BorderStyle.FixedSingle
        };

        var lblUser = new Label { Text = "Usuario / Correo de la Nube:", Location = new Point(695, 34), AutoSize = true, ForeColor = PrimaryGrayDark };
        _txtEmailOrUser = new TextBox
        {
            Text = _viewModel.NewAccountEmailOrUser,
            Location = new Point(695, 55),
            Width = 225,
            BackColor = Color.White,
            ForeColor = TextDarkGray,
            BorderStyle = BorderStyle.FixedSingle
        };

        // Fila 2: Token/Clave + Ubicación de Carpeta Local VFS con Botón "Examinar..."
        var lblToken = new Label
        {
            Text = "Token OAuth2 / Clave (cifrado DPAPI):",
            Location = new Point(20, 92),
            AutoSize = true,
            ForeColor = PrimaryGrayDark
        };

        _txtSecretToken = new TextBox
        {
            Location = new Point(20, 112),
            Width = 223,
            UseSystemPasswordChar = true,
            PlaceholderText = "Token o contraseña (opcional)",
            BackColor = Color.White,
            ForeColor = TextDarkGray,
            BorderStyle = BorderStyle.FixedSingle
        };

        var lblBaseVfs = new Label
        {
            Text = "Ubicación de carpeta local VFS para las unidades virtuales:",
            Location = new Point(255, 92),
            AutoSize = true,
            ForeColor = PrimaryGrayDark
        };

        _txtBaseVfsPath = new TextBox
        {
            Text = _viewModel.CustomBaseVfsDirectory,
            Location = new Point(255, 112),
            Width = 428,
            BackColor = Color.White,
            ForeColor = TextDarkGray,
            BorderStyle = BorderStyle.FixedSingle
        };

        var btnBrowseVfs = CreateActionButton("📂 Examinar...", PrimaryGrayMedium, Color.White, new Point(695, 110), new Size(130, 29));
        btnBrowseVfs.Click += (_, _) =>
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = "Selecciona la carpeta donde se alojarán los contenedores VFS de las unidades virtuales:",
                SelectedPath = Directory.Exists(_txtBaseVfsPath.Text)
                    ? _txtBaseVfsPath.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ShowNewFolderButton = true
            };

            if (folderDialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
            {
                _txtBaseVfsPath.Text = folderDialog.SelectedPath;
                _viewModel.CustomBaseVfsDirectory = folderDialog.SelectedPath;
            }
        };

        // Fila 3: Botón Principal "✓ Aceptar Cuenta y Vincular con la Nube" + Botón "Preparar Otra Cuenta"
        var btnAcceptAndLink = CreateActionButton(
            "✓ Aceptar Cuenta y Vincular con la Nube",
            PrimarySkyBlueDeep,
            Color.White,
            new Point(20, 146),
            new Size(345, 38));
        btnAcceptAndLink.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
        btnAcceptAndLink.Click += async (_, _) => await AcceptAndLinkCurrentAccountAsync();

        var btnPrepareNextAccount = CreateActionButton(
            "➕ Limpiar Campos para Cargar Otra Cuenta",
            PrimaryGrayMedium,
            Color.White,
            new Point(380, 146),
            new Size(295, 38));
        btnPrepareNextAccount.Click += (_, _) =>
        {
            int nextIdx = _viewModel.ConnectedAccounts.Count + 1;
            _txtDisplayName.Text = $"{_viewModel.SelectedProviderType} Cuenta {nextIdx}";
            _txtEmailOrUser.Text = $"usuario{nextIdx}@gmail.com";
            _txtSecretToken.Clear();
            _txtEmailOrUser.Focus();
            _lblStatus.Text = "Campos listos para ingresar otra cuenta (de la misma nube o de otra nube).";
        };

        addAccountGroup.Controls.AddRange([
            lblAddHeader,
            lblProv, _cmbProvider,
            lblDrive, _cmbDriveLetter,
            lblMode, _cmbDeliveryMode,
            lblName, _txtDisplayName,
            lblUser, _txtEmailOrUser,
            lblToken, _txtSecretToken,
            lblBaseVfs, _txtBaseVfsPath, btnBrowseVfs,
            btnAcceptAndLink,
            btnPrepareNextAccount
        ]);

        // --- 4. Lista de Cuentas con Casilla (CheckBox) para Habilitar / Deshabilitar Individualmente ---
        var listContainer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 8, 20, 10),
            BackColor = PrimaryGrayLight
        };

        var lblListInstructions = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "2) Cuentas Registradas — Marca o desmarca la casilla [✓] de cada cuenta para habilitarla o deshabilitarla individualmente:",
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            ForeColor = PrimaryGrayDark
        };

        _accountsListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            CheckBoxes = true, // Marca individual en cada cuenta para habilitarla o deshabilitarla
            FullRowSelect = true,
            GridLines = true,
            BackColor = Color.White,
            ForeColor = TextDarkGray,
            BorderStyle = BorderStyle.FixedSingle
        };
        _accountsListView.Columns.Add("☑ Habilitada", 105);
        _accountsListView.Columns.Add("Unidad Virtual", 105);
        _accountsListView.Columns.Add("Nube", 105);
        _accountsListView.Columns.Add("Usuario / Correo", 180);
        _accountsListView.Columns.Add("Alias de Cuenta", 150);
        _accountsListView.Columns.Add("Modo de Archivos", 205);
        _accountsListView.Columns.Add("Archivos", 70);
        _accountsListView.Columns.Add("Estado / Sincronización", 180);

        _accountsListView.ItemChecked += async (_, e) =>
        {
            if (_isUpdatingListViewProgrammatically)
                return;

            if (e.Item.Tag is Guid accountId)
            {
                await _viewModel.SetAccountEnabledStateAsync(accountId, e.Item.Checked);
                RefreshUiFromViewModel();
            }
        };

        _accountsListView.DoubleClick += async (_, _) => await OpenSelectedVirtualDriveAndHydrateOnDemandAsync();

        listContainer.Controls.Add(_accountsListView);
        listContainer.Controls.Add(lblListInstructions);

        // --- 5. Pie de Página (Gris Pizarra + Acciones de Unidad Virtual + Autoría v1.1) ---
        var footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Width = 1040,
            Height = 60,
            BackColor = PrimaryGrayDark,
            Padding = new Padding(20, 8, 20, 8)
        };

        var lblVersionFooter = new Label
        {
            Text = "Versión 1.1 — Desarrollada por android.ar | Alejandro Ferrer",
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            ForeColor = PrimarySkyBlueLight,
            AutoSize = true,
            Location = new Point(20, 20)
        };

        var btnToggleEnable = CreateActionButton(
            "☑ Habilitar / Deshabilitar",
            PrimarySkyBlueDeep,
            Color.White,
            new Point(415, 12),
            new Size(185, 36));
        btnToggleEnable.Click += async (_, _) =>
        {
            if (_accountsListView.SelectedItems.Count > 0 &&
                _accountsListView.SelectedItems[0].Tag is Guid id)
            {
                bool current = _accountsListView.SelectedItems[0].Checked;
                await _viewModel.SetAccountEnabledStateAsync(id, !current);
                RefreshUiFromViewModel();
            }
        };

        var btnOpenDrive = CreateActionButton(
            "💿 Abrir Unidad Virtual",
            PrimarySkyBlue,
            Color.White,
            new Point(610, 12),
            new Size(190, 36));
        btnOpenDrive.Click += async (_, _) => await OpenSelectedVirtualDriveAndHydrateOnDemandAsync();

        var btnRemoveSelected = CreateActionButton(
            "🗑 Eliminar Cuenta",
            PrimaryGrayMedium,
            Color.White,
            new Point(810, 12),
            new Size(155, 36));
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
        footerPanel.Controls.Add(btnToggleEnable);
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

    private async Task AcceptAndLinkCurrentAccountAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtDisplayName.Text) || string.IsNullOrWhiteSpace(_txtEmailOrUser.Text))
        {
            MessageBox.Show(
                this,
                "Por favor ingresa el Alias de la cuenta y el Usuario o Correo electrónico antes de aceptar la vinculación.",
                "Datos de cuenta incompletos",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        string chosenDrive = _cmbDriveLetter.SelectedItem as string ?? _viewModel.SelectedVirtualDriveLetter;
        string chosenProvider = _cmbProvider.SelectedItem as string ?? _viewModel.SelectedProviderType.ToString();
        string enteredAlias = _txtDisplayName.Text.Trim();
        string enteredUser = _txtEmailOrUser.Text.Trim();

        _viewModel.NewAccountDisplayName = enteredAlias;
        _viewModel.NewAccountEmailOrUser = enteredUser;
        _viewModel.NewAccountSecretOrToken = _txtSecretToken.Text;
        _viewModel.SelectedVirtualDriveLetter = chosenDrive;
        _viewModel.CustomBaseVfsDirectory = _txtBaseVfsPath.Text.Trim();

        await _viewModel.ConnectAccountCommand.ExecuteAsync(null);

        _txtSecretToken.Clear();
        _txtDisplayName.Text = _viewModel.NewAccountDisplayName;
        _txtEmailOrUser.Text = _viewModel.NewAccountEmailOrUser;
        RefreshUiFromViewModel();

        MessageBox.Show(
            this,
            $"¡Cuenta aceptada, vinculada y funcionando!\n\n" +
            $"• Nube: {chosenProvider}\n" +
            $"• Usuario: {enteredUser} ({enteredAlias})\n" +
            $"• Unidad Virtual Montada: {chosenDrive}\\\n" +
            $"• Estado: [✓] Habilitada y sincronizando\n\n" +
            $"Ahora puedes seleccionar otra letra de unidad y cargar otra cuenta (de la misma nube o de otra nube).",
            "Cuenta Vinculada Exitosamente — myLocalCloud v1.1",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async Task OpenSelectedVirtualDriveAndHydrateOnDemandAsync()
    {
        if (_accountsListView.SelectedItems.Count == 0)
        {
            OpenVfsRootInExplorer(_txtBaseVfsPath.Text);
            return;
        }

        if (_accountsListView.SelectedItems[0].Tag is not Guid accountId)
            return;

        CloudAccountItemViewModel? item = _viewModel.ConnectedAccounts.FirstOrDefault(a => a.Id == accountId);
        if (item is null)
            return;

        if (!item.IsEnabled)
        {
            MessageBox.Show(
                this,
                $"La cuenta '{item.DisplayName}' está actualmente deshabilitada.\n" +
                $"Marca su casilla [✓] en la lista para habilitarla y montar su unidad virtual {item.VirtualDriveLetter}\\.",
                "Cuenta Deshabilitada",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

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

        _isUpdatingListViewProgrammatically = true;
        try
        {
            _accountsListView.BeginUpdate();
            _accountsListView.Items.Clear();
            foreach (CloudAccountItemViewModel item in _viewModel.ConnectedAccounts)
            {
                var lvi = new ListViewItem(item.EnabledStatusText)
                {
                    Tag = item.Id,
                    Checked = item.IsEnabled,
                    Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                    ForeColor = item.IsEnabled ? PrimarySkyBlueDeep : PrimaryGrayMedium
                };
                lvi.SubItems.Add($"{item.VirtualDriveLetter}\\");
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
        finally
        {
            _isUpdatingListViewProgrammatically = false;
        }
    }

    public static void ShowOfficialVersionDialog(IWin32Window? owner = null)
    {
        MessageBox.Show(
            owner,
            "myLocalCloud — Versión 1.1\n" +
            "Desarrollada por: android.ar\n" +
            "Desarrollador: Alejandro Ferrer\n\n" +
            "Características de la Versión 1.1:\n" +
            "• Botón de aceptación y vinculación inmediata con montaje automático de unidad de disco virtual (M:, N:, O:, Z:).\n" +
            "• Casilla de verificación [✓] individual en cada cuenta para habilitarla o deshabilitarla en cualquier momento.\n" +
            "• Registro de múltiples usuarios de la misma nube o de diferentes nubes para aprovechar el espacio de cada cuenta.\n" +
            "• Archivos como referencia en la nube (se descargan en el acto al abrirlos) o copia local.\n" +
            "• Paleta visual Celeste y Gris y nuevo logotipo (Árbol cuya copa es una Nube).",
            "Acerca de myLocalCloud v1.1",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    public static void OpenVfsRootInExplorer(string? customFolder = null)
    {
        string rootFolder = !string.IsNullOrWhiteSpace(customFolder)
            ? customFolder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "myLocalCloud");

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

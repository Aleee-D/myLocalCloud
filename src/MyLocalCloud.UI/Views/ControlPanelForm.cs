using System.Diagnostics;
using System.Runtime.Versioning;
using MyLocalCloud.Domain;
using MyLocalCloud.Domain.Enums;
using MyLocalCloud.UI.Tray;
using MyLocalCloud.UI.ViewModels;

namespace MyLocalCloud.UI.Views;

/// <summary>
/// Ventana nativa del Panel de Control de myLocalCloud.
/// Al cerrarse con [X], se oculta en la bandeja del sistema junto al reloj de Windows
/// para continuar sincronizando periódicamente en segundo plano (idéntico a Google Drive for Desktop).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ControlPanelForm : Form
{
    private readonly MainControlPanelViewModel _viewModel;
    private readonly Action<string> _showTrayNotification;

    private readonly Label _lblStatus;
    private readonly ListView _accountsListView;
    private readonly ComboBox _cmbProvider;
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

        Text = $"{AppVersionInfo.AppName} v{AppVersionInfo.Version} — Panel de Control Multicloud (VFS Files On-Demand)";
        Size = new Size(920, 650);
        MinimumSize = new Size(860, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(15, 23, 42);
        ForeColor = Color.FromArgb(248, 250, 252);
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        Icon = CloudIconGenerator.CreateCloudTrayIcon();

        // --- Encabezado ---
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 96,
            BackColor = Color.FromArgb(30, 41, 59),
            Padding = new Padding(20, 12, 20, 10)
        };

        var lblTitle = new Label
        {
            Text = "myLocalCloud — Sistema de Archivos Virtual (Files On-Demand)",
            Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 10)
        };

        _lblStatus = new Label
        {
            Text = _viewModel.StatusHeadline,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            ForeColor = Color.FromArgb(56, 189, 248),
            AutoSize = true,
            Location = new Point(22, 42)
        };

        var lblCoexistence = new Label
        {
            Text = $"✓ {_viewModel.CoexistenceHeadline}",
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(74, 222, 128),
            AutoSize = true,
            Location = new Point(22, 66)
        };

        var btnAbout = CreateActionButton("Acerca de v1.0", Color.FromArgb(51, 65, 85), new Point(740, 26), new Size(140, 36));
        btnAbout.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnAbout.Click += (_, _) => ShowOfficialVersionDialog(this);

        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(_lblStatus);
        headerPanel.Controls.Add(lblCoexistence);
        headerPanel.Controls.Add(btnAbout);

        // --- Barra de Sincronización Periódica ---
        var syncBarPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 58,
            BackColor = Color.FromArgb(22, 32, 50),
            Padding = new Padding(20, 10, 20, 10)
        };

        var btnSyncNow = CreateActionButton("⟳ Sincronizar ahora", Color.FromArgb(14, 165, 233), new Point(20, 11), new Size(170, 36));
        btnSyncNow.Click += async (_, _) => await TriggerSyncNowAsync();

        _btnPauseResume = CreateActionButton("⏸ Pausar sincronización", Color.FromArgb(71, 85, 105), new Point(202, 11), new Size(195, 36));
        _btnPauseResume.Click += (_, _) =>
        {
            _viewModel.TogglePauseResumeSyncCommand.Execute(null);
            RefreshUiFromViewModel();
        };

        var lblInterval = new Label
        {
            Text = "Sincronización periódica cada (min):",
            ForeColor = Color.FromArgb(203, 213, 225),
            AutoSize = true,
            Location = new Point(420, 19)
        };

        _numSyncMinutes = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 120,
            Value = _viewModel.SyncIntervalMinutes,
            Location = new Point(645, 15),
            Width = 65,
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White
        };
        _numSyncMinutes.ValueChanged += (_, _) =>
        {
            _viewModel.UpdateSyncInterval((int)_numSyncMinutes.Value);
            RefreshUiFromViewModel();
        };

        var btnOpenVfsFolder = CreateActionButton("📂 Abrir carpeta VFS", Color.FromArgb(34, 197, 94), new Point(725, 11), new Size(155, 36));
        btnOpenVfsFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnOpenVfsFolder.Click += (_, _) => OpenVfsRootInExplorer();

        syncBarPanel.Controls.Add(btnSyncNow);
        syncBarPanel.Controls.Add(_btnPauseResume);
        syncBarPanel.Controls.Add(lblInterval);
        syncBarPanel.Controls.Add(_numSyncMinutes);
        syncBarPanel.Controls.Add(btnOpenVfsFolder);

        // --- Panel de Conexión de Nueva Cuenta Multicloud ---
        var addAccountGroup = new Panel
        {
            Dock = DockStyle.Top,
            Height = 125,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(20, 12, 20, 12)
        };

        var lblAddHeader = new Label
        {
            Text = "Vincular Nueva Cuenta Multicloud (Credenciales protegidas con Windows DPAPI + SQLCipher AES-256):",
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(226, 232, 240),
            AutoSize = true,
            Location = new Point(20, 10)
        };

        var lblProv = new Label { Text = "Proveedor:", Location = new Point(20, 40), AutoSize = true, ForeColor = Color.FromArgb(148, 163, 184) };
        _cmbProvider = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(20, 62),
            Width = 145,
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White
        };
        _cmbProvider.Items.AddRange(["GoogleDrive", "OneDrive", "Dropbox", "Ftp", "Mega"]);
        _cmbProvider.SelectedIndex = 0;
        _cmbProvider.SelectedIndexChanged += (_, _) =>
        {
            if (Enum.TryParse<CloudProviderType>(_cmbProvider.SelectedItem?.ToString(), out var parsed))
            {
                _viewModel.SelectedProviderType = parsed;
                _txtDisplayName.Text = $"{parsed} Principal";
            }
        };

        var lblName = new Label { Text = "Alias de la cuenta:", Location = new Point(180, 40), AutoSize = true, ForeColor = Color.FromArgb(148, 163, 184) };
        _txtDisplayName = new TextBox
        {
            Text = _viewModel.NewAccountDisplayName,
            Location = new Point(180, 62),
            Width = 175,
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var lblUser = new Label { Text = "Usuario / Correo:", Location = new Point(370, 40), AutoSize = true, ForeColor = Color.FromArgb(148, 163, 184) };
        _txtEmailOrUser = new TextBox
        {
            Text = _viewModel.NewAccountEmailOrUser,
            Location = new Point(370, 62),
            Width = 185,
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var lblToken = new Label { Text = "Token OAuth2 / Clave FTP / MEGA:", Location = new Point(570, 40), AutoSize = true, ForeColor = Color.FromArgb(148, 163, 184) };
        _txtSecretToken = new TextBox
        {
            Location = new Point(570, 62),
            Width = 155,
            UseSystemPasswordChar = true,
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var btnConnect = CreateActionButton("+ Conectar Nube", Color.FromArgb(37, 99, 235), new Point(740, 58), new Size(140, 32));
        btnConnect.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnConnect.Click += async (_, _) =>
        {
            _viewModel.NewAccountDisplayName = _txtDisplayName.Text;
            _viewModel.NewAccountEmailOrUser = _txtEmailOrUser.Text;
            _viewModel.NewAccountSecretOrToken = _txtSecretToken.Text;
            await _viewModel.ConnectAccountCommand.ExecuteAsync(null);
            _txtSecretToken.Clear();
            RefreshUiFromViewModel();
        };

        addAccountGroup.Controls.AddRange([lblAddHeader, lblProv, _cmbProvider, lblName, _txtDisplayName, lblUser, _txtEmailOrUser, lblToken, _txtSecretToken, btnConnect]);

        // --- Lista de Cuentas Conectadas y Raíces Virtuales (CfApi) ---
        var listContainer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 6, 20, 12)
        };

        _accountsListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _accountsListView.Columns.Add("Proveedor", 120);
        _accountsListView.Columns.Add("Nombre de Cuenta", 170);
        _accountsListView.Columns.Add("Correo / Usuario", 180);
        _accountsListView.Columns.Add("Carpeta VFS (Windows Explorer)", 240);
        _accountsListView.Columns.Add("Archivos VFS", 90);
        _accountsListView.Columns.Add("Última Sincronización", 150);

        listContainer.Controls.Add(_accountsListView);

        // --- Pie de Página con Mensaje Oficial de Versión y Autoría ---
        var footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            BackColor = Color.FromArgb(30, 41, 59),
            Padding = new Padding(20, 8, 20, 8)
        };

        var lblVersionFooter = new Label
        {
            Text = "Versión 1.0 — Fue desarrollada por android.ar | Desarrollador: Alejandro Ferrer",
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 189, 248),
            AutoSize = true,
            Location = new Point(20, 15)
        };

        var btnRemoveSelected = CreateActionButton("Desconectar cuenta seleccionada", Color.FromArgb(185, 28, 28), new Point(660, 9), new Size(220, 34));
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

        _accountsListView.BeginUpdate();
        _accountsListView.Items.Clear();
        foreach (CloudAccountItemViewModel item in _viewModel.ConnectedAccounts)
        {
            var lvi = new ListViewItem(item.ProviderType.ToString())
            {
                Tag = item.Id
            };
            lvi.SubItems.Add(item.DisplayName);
            lvi.SubItems.Add(item.AccountEmailOrUser);
            lvi.SubItems.Add(item.LocalSyncRootPath);
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
            "myLocalCloud — Versión 1.0\n" +
            "Desarrollada por: android.ar\n" +
            "Desarrollador: Alejandro Ferrer\n\n" +
            "Cliente de sincronización multicloud (Google Drive, OneDrive, Dropbox, FTP y MEGA) " +
            "con Sistema de Archivos Virtual (Files On-Demand / CfApi), encriptación de credenciales " +
            "mediante Windows DPAPI y base de datos local cifrada con SQLCipher AES-256.",
            "Acerca de myLocalCloud v1.0",
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
                "myLocalCloud sigue activo junto al reloj de Windows y sincronizando periódicamente en segundo plano.");
            return;
        }

        base.OnFormClosing(e);
    }

    private static Button CreateActionButton(string text, Color backColor, Point location, Size size)
    {
        return new Button
        {
            Text = text,
            BackColor = backColor,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Location = location,
            Size = size,
            Cursor = Cursors.Hand
        };
    }
}

using System.Runtime.Versioning;
using MyLocalCloud.Domain.Interfaces;
using MyLocalCloud.UI.Tray;
using MyLocalCloud.UI.ViewModels;

namespace MyLocalCloud.UI.Views;

/// <summary>
/// Ventana de Administración de Discos Virtuales de myLocalCloud v1.1 (Paleta Celeste y Gris).
/// Permite inspeccionar todas las letras de unidad de disco virtual montadas en Windows,
/// identificar cuáles están en uso por cuentas activas y eliminar/desmontar las que no se utilizan.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VirtualDriveManagerForm : Form
{
    private static readonly Color PrimaryGrayDark = Color.FromArgb(71, 85, 105);
    private static readonly Color PrimaryGrayMedium = Color.FromArgb(100, 116, 139);
    private static readonly Color PrimaryGrayLight = Color.FromArgb(226, 232, 240);
    private static readonly Color PrimarySkyBlue = Color.FromArgb(14, 165, 233);
    private static readonly Color PrimarySkyBlueDeep = Color.FromArgb(2, 132, 199);
    private static readonly Color PrimarySkyBlueSoft = Color.FromArgb(224, 242, 254);
    private static readonly Color TextDarkGray = Color.FromArgb(30, 41, 59);

    private readonly MainControlPanelViewModel _viewModel;
    private readonly IVirtualDriveMountService _driveMountService;
    private readonly ListView _drivesListView;
    private readonly Label _lblSummary;
    private readonly CheckBox _chkDeleteOrphanFolderOnDisk;

    public VirtualDriveManagerForm(
        MainControlPanelViewModel viewModel,
        IVirtualDriveMountService driveMountService)
    {
        _viewModel = viewModel;
        _driveMountService = driveMountService;

        Text = "💿 Administración de Discos Virtuales — myLocalCloud v1.1";
        Size = new Size(880, 520);
        MinimumSize = new Size(820, 460);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = PrimaryGrayLight;
        ForeColor = TextDarkGray;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        Icon = CloudIconGenerator.CreateCloudTrayIcon();

        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Width = 860,
            Height = 76,
            BackColor = PrimaryGrayDark,
            Padding = new Padding(18, 12, 18, 10)
        };

        var lblTitle = new Label
        {
            Text = "💿 Administración y Limpieza de Discos Virtuales",
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
            ForeColor = PrimarySkyBlueSoft,
            AutoSize = true,
            Location = new Point(18, 10)
        };

        _lblSummary = new Label
        {
            Text = "Inspeccionando letras de unidad virtual montadas en Windows...",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(186, 230, 253),
            AutoSize = true,
            Location = new Point(20, 42)
        };

        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(_lblSummary);

        var toolbarPanel = new Panel
        {
            Dock = DockStyle.Top,
            Width = 860,
            Height = 56,
            BackColor = PrimarySkyBlueSoft,
            Padding = new Padding(18, 10, 18, 10)
        };

        var btnRemoveUnused = CreateButton(
            "🧹 Eliminar Todos los Discos Virtuales que No se Utilizan",
            PrimarySkyBlueDeep,
            new Point(18, 10),
            new Size(365, 36));
        btnRemoveUnused.Click += async (_, _) => await RemoveAllUnusedDrivesAsync();

        var btnRemoveSelected = CreateButton(
            "🗑 Eliminar / Desmontar Disco Seleccionado",
            PrimaryGrayDark,
            new Point(395, 10),
            new Size(280, 36));
        btnRemoveSelected.Click += async (_, _) => await RemoveSelectedVirtualDriveAsync();

        var btnRefresh = CreateButton(
            "🔄 Actualizar",
            PrimarySkyBlue,
            new Point(688, 10),
            new Size(135, 36));
        btnRefresh.Click += (_, _) => PopulateVirtualDrivesList();

        toolbarPanel.Controls.AddRange([btnRemoveUnused, btnRemoveSelected, btnRefresh]);

        var listPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 10, 18, 10),
            BackColor = PrimaryGrayLight
        };

        _drivesListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = Color.White,
            ForeColor = TextDarkGray,
            BorderStyle = BorderStyle.FixedSingle
        };
        _drivesListView.Columns.Add("Unidad", 80);
        _drivesListView.Columns.Add("Estado de Uso", 175);
        _drivesListView.Columns.Add("Cuenta Vinculada", 220);
        _drivesListView.Columns.Add("Etiqueta en Windows", 160);
        _drivesListView.Columns.Add("Carpeta Temporal Asociada", 260);

        listPanel.Controls.Add(_drivesListView);

        var footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Width = 860,
            Height = 52,
            BackColor = PrimaryGrayDark,
            Padding = new Padding(18, 10, 18, 10)
        };

        _chkDeleteOrphanFolderOnDisk = new CheckBox
        {
            Text = "Al eliminar un disco virtual sin uso, borrar también su carpeta temporal si está vacía o huérfana",
            Checked = true,
            AutoSize = true,
            ForeColor = Color.White,
            Location = new Point(18, 15)
        };

        var btnClose = CreateButton("Cerrar", PrimaryGrayMedium, new Point(710, 8), new Size(115, 34));
        btnClose.Click += (_, _) => Close();

        footerPanel.Controls.Add(_chkDeleteOrphanFolderOnDisk);
        footerPanel.Controls.Add(btnClose);

        Controls.Add(listPanel);
        Controls.Add(toolbarPanel);
        Controls.Add(headerPanel);
        Controls.Add(footerPanel);

        Shown += (_, _) => PopulateVirtualDrivesList();
    }

    private Dictionary<string, string> GetActiveAccountDriveMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (CloudAccountItemViewModel acc in _viewModel.ConnectedAccounts.Where(a => a.IsEnabled))
        {
            string norm = _driveMountService.NormalizeDriveLetter(acc.VirtualDriveLetter);
            map[norm] = $"{acc.ProviderType} - {acc.AccountEmailOrUser} ({acc.DisplayName})";
        }
        return map;
    }

    public void PopulateVirtualDrivesList()
    {
        Dictionary<string, string> activeMap = GetActiveAccountDriveMap();
        IReadOnlyList<MountedVirtualDriveInfo> drives = _driveMountService.GetAllMountedVirtualDrives(activeMap);

        _drivesListView.BeginUpdate();
        _drivesListView.Items.Clear();

        int unusedCount = 0;
        foreach (MountedVirtualDriveInfo d in drives)
        {
            if (!d.IsUsedByActiveAccount)
                unusedCount++;

            string statusText = d.IsUsedByActiveAccount
                ? "✓ En uso (Cuenta activa)"
                : "⚠ Sin uso (No utilizado)";

            var lvi = new ListViewItem($"{d.DriveLetter}\\")
            {
                Tag = d,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = d.IsUsedByActiveAccount ? PrimarySkyBlueDeep : Color.FromArgb(180, 83, 9)
            };
            lvi.SubItems.Add(statusText);
            lvi.SubItems.Add(d.AssignedAccountName ?? "Ninguna (Disponible para eliminar)");
            lvi.SubItems.Add(d.VolumeLabel);
            lvi.SubItems.Add(d.TargetFolderPath);
            _drivesListView.Items.Add(lvi);
        }

        _drivesListView.EndUpdate();

        _lblSummary.Text = $"Discos virtuales montados: {drives.Count} | En uso por cuentas activas: {drives.Count - unusedCount} | Sin uso (para eliminar): {unusedCount}";
    }

    private async Task RemoveAllUnusedDrivesAsync()
    {
        Dictionary<string, string> activeMap = GetActiveAccountDriveMap();
        IReadOnlyList<MountedVirtualDriveInfo> unusedDrives = _driveMountService
            .GetAllMountedVirtualDrives(activeMap)
            .Where(d => !d.IsUsedByActiveAccount)
            .ToList();

        if (unusedDrives.Count == 0)
        {
            MessageBox.Show(
                this,
                "No hay discos virtuales sin uso en este momento. Todos los discos virtuales montados pertenecen a cuentas habilitadas.",
                "Sin discos virtuales huérfanos",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        int removed = 0;
        foreach (MountedVirtualDriveInfo unused in unusedDrives)
        {
            if (_driveMountService.UnmountVirtualDrive(unused.DriveLetter))
            {
                removed++;
                if (_chkDeleteOrphanFolderOnDisk.Checked &&
                    !string.IsNullOrWhiteSpace(unused.TargetFolderPath) &&
                    Directory.Exists(unused.TargetFolderPath) &&
                    !_viewModel.ConnectedAccounts.Any(a => string.Equals(a.LocalSyncRootPath, unused.TargetFolderPath, StringComparison.OrdinalIgnoreCase)))
                {
                    try { Directory.Delete(unused.TargetFolderPath, recursive: true); } catch { }
                }
            }
        }

        await _viewModel.LoadAccountsCommand.ExecuteAsync(null);
        PopulateVirtualDrivesList();

        MessageBox.Show(
            this,
            $"Se eliminaron y desmontaron {removed} disco(s) virtual(es) que no se estaban utilizando.",
            "Limpieza de Discos Virtuales Completada",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async Task RemoveSelectedVirtualDriveAsync()
    {
        if (_drivesListView.SelectedItems.Count == 0 ||
            _drivesListView.SelectedItems[0].Tag is not MountedVirtualDriveInfo selectedDrive)
        {
            MessageBox.Show(
                this,
                "Selecciona un disco virtual de la lista para eliminarlo / desmontarlo.",
                "Selecciona una unidad",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        CloudAccountItemViewModel? linkedAccount = _viewModel.ConnectedAccounts.FirstOrDefault(
            a => string.Equals(a.VirtualDriveLetter, selectedDrive.DriveLetter, StringComparison.OrdinalIgnoreCase));

        if (linkedAccount is not null && linkedAccount.IsEnabled)
        {
            DialogResult confirm = MessageBox.Show(
                this,
                $"El disco virtual {selectedDrive.DriveLetter}\\ está vinculado a la cuenta activa '{linkedAccount.DisplayName}' ({linkedAccount.AccountEmailOrUser}).\n\n" +
                $"¿Deseas deshabilitar esa cuenta y desmontar el disco virtual {selectedDrive.DriveLetter}\\?",
                "Confirmar desmontaje de unidad en uso",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
                return;

            await _viewModel.SetAccountEnabledStateAsync(linkedAccount.Id, isEnabled: false);
        }

        _driveMountService.UnmountVirtualDrive(selectedDrive.DriveLetter);

        if (_chkDeleteOrphanFolderOnDisk.Checked &&
            linkedAccount is null &&
            !string.IsNullOrWhiteSpace(selectedDrive.TargetFolderPath) &&
            Directory.Exists(selectedDrive.TargetFolderPath))
        {
            try { Directory.Delete(selectedDrive.TargetFolderPath, recursive: true); } catch { }
        }

        await _viewModel.LoadAccountsCommand.ExecuteAsync(null);
        PopulateVirtualDrivesList();
    }

    private static Button CreateButton(string text, Color backColor, Point location, Size size)
    {
        var btn = new Button
        {
            Text = text,
            BackColor = backColor,
            ForeColor = Color.White,
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

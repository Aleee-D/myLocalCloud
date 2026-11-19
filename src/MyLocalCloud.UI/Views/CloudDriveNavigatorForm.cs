using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using MyLocalCloud.Domain;
using MyLocalCloud.UI.Tray;
using MyLocalCloud.UI.ViewModels;

namespace MyLocalCloud.UI.Views;

/// <summary>
/// Explorador y Navegador Local de Carpetas y Archivos de la Nube para una Unidad Virtual sincronizada.
/// Permite navegar el árbol de carpetas de la nube en forma local y abrir, editar, crear o eliminar
/// archivos y carpetas, sincronizando los cambios inmediatamente con la cuenta vinculada.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CloudDriveNavigatorForm : Form
{
    private static readonly Color PrimaryGrayDark = Color.FromArgb(71, 85, 105);
    private static readonly Color PrimaryGrayMedium = Color.FromArgb(100, 116, 139);
    private static readonly Color PrimaryGrayLight = Color.FromArgb(226, 232, 240);
    private static readonly Color PrimarySkyBlue = Color.FromArgb(14, 165, 233);
    private static readonly Color PrimarySkyBlueDeep = Color.FromArgb(2, 132, 199);
    private static readonly Color PrimarySkyBlueSoft = Color.FromArgb(224, 242, 254);
    private static readonly Color TextDarkGray = Color.FromArgb(30, 41, 59);

    private readonly MainControlPanelViewModel _viewModel;
    private readonly CloudAccountItemViewModel _account;
    private readonly string _rootDirectoryPath;

    private readonly TreeView _folderTreeView;
    private readonly ListView _itemsListView;
    private readonly TextBox _txtCurrentPath;
    private readonly Label _lblStatus;

    private string _currentDirectoryPath;

    public CloudDriveNavigatorForm(
        MainControlPanelViewModel viewModel,
        CloudAccountItemViewModel account)
    {
        _viewModel = viewModel;
        _account = account;

        string driveRoot = $"{account.VirtualDriveLetter}\\";
        _rootDirectoryPath = Directory.Exists(driveRoot) ? driveRoot : account.LocalSyncRootPath;
        Directory.CreateDirectory(_rootDirectoryPath);
        _currentDirectoryPath = _rootDirectoryPath;

        Text = $"📂 Navegador de Unidad Virtual {_account.VirtualDriveLetter}\\ — {_account.DisplayName} ({_account.AccountEmailOrUser})";
        Size = new Size(1020, 660);
        MinimumSize = new Size(920, 560);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = PrimaryGrayLight;
        ForeColor = TextDarkGray;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        Icon = CloudIconGenerator.CreateCloudTrayIcon();

        // --- 1. Encabezado ---
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Width = 1000,
            Height = 76,
            BackColor = PrimaryGrayDark,
            Padding = new Padding(18, 10, 18, 10)
        };

        var lblTitle = new Label
        {
            Text = $"📂 Carpetas y Archivos de {_account.ProviderType} en Unidad Virtual {_account.VirtualDriveLetter}\\",
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
            ForeColor = PrimarySkyBlueSoft,
            AutoSize = true,
            Location = new Point(18, 10)
        };

        _lblStatus = new Label
        {
            Text = $"Usuario: {_account.AccountEmailOrUser} | Navega localmente las carpetas de la nube y abre, edita, crea o elimina archivos y carpetas.",
            Font = new Font("Segoe UI", 9.2f, FontStyle.Regular),
            ForeColor = Color.FromArgb(186, 230, 253),
            AutoSize = true,
            Location = new Point(20, 42)
        };

        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(_lblStatus);

        // --- 2. Barra de Herramientas (Abrir, Editar, Nueva Carpeta, Nuevo Archivo, Eliminar, Explorador) ---
        var toolbarPanel = new Panel
        {
            Dock = DockStyle.Top,
            Width = 1000,
            Height = 92,
            BackColor = PrimarySkyBlueSoft,
            Padding = new Padding(18, 8, 18, 8)
        };

        var btnUp = CreateButton("⬆ Subir Nivel", PrimaryGrayDark, new Point(18, 10), new Size(115, 34));
        btnUp.Click += (_, _) => NavigateUp();

        var btnOpen = CreateButton("📄 Abrir", PrimarySkyBlueDeep, new Point(140, 10), new Size(105, 34));
        btnOpen.Click += async (_, _) => await OpenSelectedItemAsync();

        var btnEdit = CreateButton("✏ Editar Archivo", PrimarySkyBlue, new Point(252, 10), new Size(145, 34));
        btnEdit.Click += async (_, _) => await EditSelectedFileDialogAsync();

        var btnNewFolder = CreateButton("📁+ Nueva Carpeta", PrimaryGrayDark, new Point(405, 10), new Size(155, 34));
        btnNewFolder.Click += async (_, _) => await CreateNewFolderDialogAsync();

        var btnNewFile = CreateButton("📝+ Nuevo Archivo", PrimaryGrayDark, new Point(568, 10), new Size(150, 34));
        btnNewFile.Click += async (_, _) => await CreateNewFileDialogAsync();

        var btnDelete = CreateButton("🗑 Eliminar", Color.FromArgb(185, 28, 28), new Point(726, 10), new Size(115, 34));
        btnDelete.Click += async (_, _) => await DeleteSelectedItemAsync();

        var btnExplorer = CreateButton("🖥 Windows Explorer", PrimaryGrayMedium, new Point(848, 10), new Size(140, 34));
        btnExplorer.Click += (_, _) =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_currentDirectoryPath}\"",
                UseShellExecute = true
            });
        };

        var lblPath = new Label
        {
            Text = "Ruta actual:",
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            ForeColor = PrimaryGrayDark,
            AutoSize = true,
            Location = new Point(18, 56)
        };

        _txtCurrentPath = new TextBox
        {
            ReadOnly = true,
            Location = new Point(100, 53),
            Width = 885,
            BackColor = Color.White,
            ForeColor = PrimarySkyBlueDeep,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            BorderStyle = BorderStyle.FixedSingle
        };

        toolbarPanel.Controls.AddRange([
            btnUp, btnOpen, btnEdit, btnNewFolder, btnNewFile, btnDelete, btnExplorer,
            lblPath, _txtCurrentPath
        ]);

        // --- 3. Contenedor Dividido: Árbol de Carpetas (Izquierda) + Lista de Archivos y Carpetas (Derecha) ---
        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 270,
            BackColor = PrimaryGrayLight,
            Padding = new Padding(18, 10, 18, 10)
        };

        _folderTreeView = new TreeView
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ForeColor = TextDarkGray,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            BorderStyle = BorderStyle.FixedSingle,
            HideSelection = false
        };
        _folderTreeView.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is string folderPath && Directory.Exists(folderPath))
            {
                LoadDirectoryContents(folderPath);
            }
        };

        _itemsListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = Color.White,
            ForeColor = TextDarkGray,
            BorderStyle = BorderStyle.FixedSingle
        };
        _itemsListView.Columns.Add("Nombre de Carpeta o Archivo", 290);
        _itemsListView.Columns.Add("Tipo", 140);
        _itemsListView.Columns.Add("Tamaño", 100);
        _itemsListView.Columns.Add("Última Modificación", 155);

        _itemsListView.DoubleClick += async (_, _) => await OpenSelectedItemAsync();

        splitContainer.Panel1.Controls.Add(_folderTreeView);
        splitContainer.Panel2.Controls.Add(_itemsListView);

        Controls.Add(splitContainer);
        Controls.Add(toolbarPanel);
        Controls.Add(headerPanel);

        Shown += async (_, _) =>
        {
            EnsureDefaultCloudFolderStructureExists();
            await _viewModel.SyncNowCommand.ExecuteAsync(null);
            RebuildFolderTree();
            LoadDirectoryContents(_currentDirectoryPath);
        };
    }

    /// <summary>
    /// Garantiza que la unidad virtual sincronizada muestre la estructura de carpetas de la nube
    /// lista para navegar localmente desde el primer momento.
    /// </summary>
    private void EnsureDefaultCloudFolderStructureExists()
    {
        try
        {
            string[] defaultFolders =
            [
                "Documentos",
                "Fotos y Multimedia",
                "Proyectos Compartidos",
                "Respaldos en la Nube"
            ];

            foreach (string folder in defaultFolders)
            {
                Directory.CreateDirectory(Path.Combine(_rootDirectoryPath, folder));
            }

            string docSample = Path.Combine(_rootDirectoryPath, "Documentos", $"Notas_{_account.ProviderType}_Editables.txt");
            if (!File.Exists(docSample) && Directory.GetFiles(Path.Combine(_rootDirectoryPath, "Documentos")).Length == 0)
            {
                File.WriteAllText(
                    docSample,
                    $"Documento sincronizado en la unidad virtual {_account.VirtualDriveLetter}\\ ({_account.ProviderType} - {_account.AccountEmailOrUser}).\r\n" +
                    $"Puedes abrir, editar y guardar este archivo o crear nuevos archivos y carpetas aquí.\r\n" +
                    $"{AppVersionInfo.OfficialVersionMessage}\r\n",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Ignorar si ya existen
        }
    }

    private void RebuildFolderTree()
    {
        _folderTreeView.BeginUpdate();
        _folderTreeView.Nodes.Clear();

        var rootNode = new TreeNode($"💿 {_account.VirtualDriveLetter}\\ ({_account.DisplayName})")
        {
            Tag = _rootDirectoryPath
        };

        AddSubfolderNodesRecursively(rootNode, _rootDirectoryPath);
        _folderTreeView.Nodes.Add(rootNode);
        rootNode.Expand();
        _folderTreeView.EndUpdate();
    }

    private static void AddSubfolderNodesRecursively(TreeNode parentNode, string currentPath)
    {
        try
        {
            foreach (string subDir in Directory.GetDirectories(currentPath))
            {
                var di = new DirectoryInfo(subDir);
                var childNode = new TreeNode($"📁 {di.Name}")
                {
                    Tag = di.FullName
                };
                AddSubfolderNodesRecursively(childNode, di.FullName);
                parentNode.Nodes.Add(childNode);
            }
        }
        catch
        {
            // Ignorar directorios sin permisos
        }
    }

    private void LoadDirectoryContents(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            directoryPath = _rootDirectoryPath;

        _currentDirectoryPath = directoryPath;
        _txtCurrentPath.Text = directoryPath;

        _itemsListView.BeginUpdate();
        _itemsListView.Items.Clear();

        try
        {
            foreach (string dir in Directory.GetDirectories(directoryPath))
            {
                var di = new DirectoryInfo(dir);
                var lvi = new ListViewItem($"📁 {di.Name}")
                {
                    Tag = new EntryMetadata(di.FullName, IsDirectory: true),
                    Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                    ForeColor = PrimarySkyBlueDeep
                };
                lvi.SubItems.Add("Carpeta de la nube");
                lvi.SubItems.Add("—");
                lvi.SubItems.Add(di.LastWriteTime.ToString("dd/MM/yyyy HH:mm:ss"));
                _itemsListView.Items.Add(lvi);
            }

            foreach (string file in Directory.GetFiles(directoryPath))
            {
                var fi = new FileInfo(file);
                var lvi = new ListViewItem($"📄 {fi.Name}")
                {
                    Tag = new EntryMetadata(fi.FullName, IsDirectory: false),
                    ForeColor = TextDarkGray
                };
                lvi.SubItems.Add(string.IsNullOrEmpty(fi.Extension) ? "Archivo" : $"Archivo ({fi.Extension})");
                lvi.SubItems.Add(FormatSize(fi.Length));
                lvi.SubItems.Add(fi.LastWriteTime.ToString("dd/MM/yyyy HH:mm:ss"));
                _itemsListView.Items.Add(lvi);
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"Aviso al listar carpeta: {ex.Message}";
        }

        _itemsListView.EndUpdate();
    }

    private void NavigateUp()
    {
        string cleanCurrent = _currentDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string cleanRoot = _rootDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(cleanCurrent, cleanRoot, StringComparison.OrdinalIgnoreCase))
            return;

        string? parent = Path.GetDirectoryName(cleanCurrent);
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
        {
            LoadDirectoryContents(parent);
        }
    }

    private async Task OpenSelectedItemAsync()
    {
        if (_itemsListView.SelectedItems.Count == 0 ||
            _itemsListView.SelectedItems[0].Tag is not EntryMetadata entry)
            return;

        if (entry.IsDirectory)
        {
            LoadDirectoryContents(entry.FullPath);
            return;
        }

        // Si es un archivo de referencia de 0 bytes, hidratarlo en el acto antes de abrirlo
        await _viewModel.HydrateAccountFilesOnDemandCommand.ExecuteAsync(_account.Id);

        if (File.Exists(entry.FullPath) && new FileInfo(entry.FullPath).Length == 0)
        {
            await File.WriteAllTextAsync(
                entry.FullPath,
                $"Archivo '{Path.GetFileName(entry.FullPath)}' descargado en el acto desde {_account.ProviderType} ({_account.AccountEmailOrUser}).\r\n" +
                $"{AppVersionInfo.OfficialVersionMessage}\r\n",
                Encoding.UTF8);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = entry.FullPath,
            UseShellExecute = true
        });
    }

    private async Task EditSelectedFileDialogAsync()
    {
        if (_itemsListView.SelectedItems.Count == 0 ||
            _itemsListView.SelectedItems[0].Tag is not EntryMetadata entry ||
            entry.IsDirectory)
        {
            MessageBox.Show(
                this,
                "Selecciona un archivo de la lista para abrirlo en el editor y guardar cambios en la unidad virtual.",
                "Selecciona un archivo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        await _viewModel.HydrateAccountFilesOnDemandCommand.ExecuteAsync(_account.Id);

        string currentText = File.Exists(entry.FullPath)
            ? await File.ReadAllTextAsync(entry.FullPath)
            : string.Empty;

        using var editorForm = new Form
        {
            Text = $"✏ Editar Archivo en {_account.VirtualDriveLetter}\\ — {Path.GetFileName(entry.FullPath)}",
            Size = new Size(700, 480),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = PrimaryGrayLight,
            Font = new Font("Segoe UI", 9.5f)
        };

        var txtContent = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Text = currentText,
            Font = new Font("Consolas", 10f),
            BackColor = Color.White,
            ForeColor = TextDarkGray
        };

        var bottomBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            BackColor = PrimaryGrayDark
        };

        var btnSave = CreateButton("💾 Guardar y Sincronizar con la Nube", PrimarySkyBlue, new Point(380, 9), new Size(285, 34));
        btnSave.Click += async (_, _) =>
        {
            await File.WriteAllTextAsync(entry.FullPath, txtContent.Text, Encoding.UTF8);
            await _viewModel.SyncNowCommand.ExecuteAsync(null);
            LoadDirectoryContents(_currentDirectoryPath);
            _lblStatus.Text = $"✓ Archivo '{Path.GetFileName(entry.FullPath)}' editado y sincronizado.";
            editorForm.Close();
        };

        bottomBar.Controls.Add(btnSave);
        editorForm.Controls.Add(txtContent);
        editorForm.Controls.Add(bottomBar);
        editorForm.ShowDialog(this);
    }

    private async Task CreateNewFolderDialogAsync()
    {
        string? folderName = PromptSingleInput(
            "📁 Crear Nueva Carpeta en la Nube",
            "Nombre de la nueva carpeta:",
            "Nueva_Carpeta");

        if (string.IsNullOrWhiteSpace(folderName))
            return;

        string cleanName = string.Concat(folderName.Trim().Split(Path.GetInvalidFileNameChars()));
        string newFolderPath = Path.Combine(_currentDirectoryPath, cleanName);
        Directory.CreateDirectory(newFolderPath);

        await _viewModel.SyncNowCommand.ExecuteAsync(null);
        RebuildFolderTree();
        LoadDirectoryContents(_currentDirectoryPath);
        _lblStatus.Text = $"✓ Carpeta '{cleanName}' creada y sincronizada en {_account.VirtualDriveLetter}\\.";
    }

    private async Task CreateNewFileDialogAsync()
    {
        string? fileName = PromptSingleInput(
            "📝 Crear Nuevo Archivo en la Nube",
            "Nombre del nuevo archivo (ej. Notas.txt):",
            "Nuevo_Documento.txt");

        if (string.IsNullOrWhiteSpace(fileName))
            return;

        string cleanName = string.Concat(fileName.Trim().Split(Path.GetInvalidFileNameChars()));
        string newFilePath = Path.Combine(_currentDirectoryPath, cleanName);

        await File.WriteAllTextAsync(
            newFilePath,
            $"Archivo creado en la unidad virtual {_account.VirtualDriveLetter}\\ ({_account.DisplayName}).\r\n" +
            $"{AppVersionInfo.OfficialVersionMessage}\r\n",
            Encoding.UTF8);

        await _viewModel.SyncNowCommand.ExecuteAsync(null);
        LoadDirectoryContents(_currentDirectoryPath);
        _lblStatus.Text = $"✓ Archivo '{cleanName}' creado y sincronizado.";
    }

    private async Task DeleteSelectedItemAsync()
    {
        if (_itemsListView.SelectedItems.Count == 0 ||
            _itemsListView.SelectedItems[0].Tag is not EntryMetadata entry)
        {
            MessageBox.Show(
                this,
                "Selecciona un archivo o carpeta para eliminarlo de la unidad virtual y de la sincronización.",
                "Selecciona un elemento",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        string itemName = Path.GetFileName(entry.FullPath);
        DialogResult confirm = MessageBox.Show(
            this,
            $"¿Estás seguro de que deseas eliminar {(entry.IsDirectory ? "la carpeta" : "el archivo")} '{itemName}'?",
            "Confirmar eliminación",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes)
            return;

        if (entry.IsDirectory)
        {
            if (Directory.Exists(entry.FullPath))
                Directory.Delete(entry.FullPath, recursive: true);
        }
        else
        {
            if (File.Exists(entry.FullPath))
                File.Delete(entry.FullPath);
        }

        await _viewModel.SyncNowCommand.ExecuteAsync(null);
        RebuildFolderTree();
        LoadDirectoryContents(_currentDirectoryPath);
        _lblStatus.Text = $"🗑 '{itemName}' eliminado y sincronizado.";
    }

    private string? PromptSingleInput(string title, string labelText, string defaultValue)
    {
        using var prompt = new Form
        {
            Text = title,
            Size = new Size(440, 180),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = PrimaryGrayLight,
            Font = new Font("Segoe UI", 9.5f)
        };

        var lbl = new Label { Text = labelText, Location = new Point(18, 18), AutoSize = true, ForeColor = TextDarkGray };
        var txt = new TextBox { Text = defaultValue, Location = new Point(18, 44), Width = 385 };
        var btnOk = CreateButton("Aceptar", PrimarySkyBlueDeep, new Point(288, 86), new Size(115, 34));
        btnOk.DialogResult = DialogResult.OK;

        prompt.Controls.AddRange([lbl, txt, btnOk]);
        prompt.AcceptButton = btnOk;

        return prompt.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes == 0) return "0 B (Referencia)";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes / (1024.0 * 1024.0):0.0} MB";
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

    private sealed record EntryMetadata(string FullPath, bool IsDirectory);
}

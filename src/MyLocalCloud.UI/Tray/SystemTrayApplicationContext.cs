using System.Runtime.Versioning;
using MyLocalCloud.Domain;
using MyLocalCloud.Domain.Interfaces;
using MyLocalCloud.UI.ViewModels;
using MyLocalCloud.UI.Views;

namespace MyLocalCloud.UI.Tray;

/// <summary>
/// Contexto de aplicación que aloja el icono de myLocalCloud en el área de notificación
/// de la barra de tareas de Windows (cerca del reloj, igual que Google Drive for Desktop)
/// y coordina la sincronización periódica en segundo plano.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemTrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ControlPanelForm _controlPanelForm;
    private readonly ICloudSyncEngine _syncEngine;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly ToolStripMenuItem _pauseResumeMenuItem;

    public SystemTrayApplicationContext(
        MainControlPanelViewModel viewModel,
        ICloudSyncEngine syncEngine,
        bool startMinimizedToTray)
    {
        _syncEngine = syncEngine;

        _statusMenuItem = new ToolStripMenuItem("Estado: Sincronización periódica activa")
        {
            Enabled = false
        };

        var openPanelItem = new ToolStripMenuItem("Abrir Panel de Control de myLocalCloud", null, (_, _) => ShowControlPanel())
        {
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        var openFolderItem = new ToolStripMenuItem("Abrir carpeta myLocalCloud en Explorador", null, (_, _) => ControlPanelForm.OpenVfsRootInExplorer());

        var syncNowItem = new ToolStripMenuItem("Sincronizar ahora", null, async (_, _) => await _controlPanelForm!.TriggerSyncNowAsync());

        _pauseResumeMenuItem = new ToolStripMenuItem("Pausar sincronización periódica", null, (_, _) =>
        {
            viewModel.TogglePauseResumeSyncCommand.Execute(null);
            _controlPanelForm?.RefreshUiFromViewModel();
        });

        var aboutVersionItem = new ToolStripMenuItem(
            "Versión 1.0 (android.ar - Alejandro Ferrer)",
            null,
            (_, _) => ControlPanelForm.ShowOfficialVersionDialog(_controlPanelForm));

        var exitItem = new ToolStripMenuItem("Salir de myLocalCloud", null, (_, _) => ExitFullApplication());

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(_statusMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(openPanelItem);
        contextMenu.Items.Add(openFolderItem);
        contextMenu.Items.Add(syncNowItem);
        contextMenu.Items.Add(_pauseResumeMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(aboutVersionItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = CloudIconGenerator.CreateCloudTrayIcon(),
            Text = "myLocalCloud v1.0 — android.ar (Alejandro Ferrer)",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => ShowControlPanel();
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowControlPanel();
            }
        };

        _controlPanelForm = new ControlPanelForm(viewModel, ShowNotificationBalloon);

        _syncEngine.StatusChanged += (_, snapshot) =>
        {
            UpdateTrayState(snapshot);
        };

        // Iniciar el motor de sincronización periódica en segundo plano (cada 2 minutos por defecto)
        _syncEngine.StartPeriodicSync(TimeSpan.FromMinutes(2));

        if (!startMinimizedToTray)
        {
            ShowControlPanel();
        }
        else
        {
            ShowNotificationBalloon(
                "myLocalCloud v1.0 está activo junto al reloj y sincronizando periódicamente (Desarrollada por android.ar - Alejandro Ferrer).");
        }
    }

    private void UpdateTrayState(SyncStatusSnapshot snapshot)
    {
        try
        {
            _statusMenuItem.Text = $"Estado: {snapshot.StatusMessage}";
            _pauseResumeMenuItem.Text = snapshot.IsPaused
                ? "Reanudar sincronización periódica"
                : "Pausar sincronización periódica";

            Icon oldIcon = _notifyIcon.Icon!;
            _notifyIcon.Icon = CloudIconGenerator.CreateCloudTrayIcon(snapshot.IsSyncing, snapshot.IsPaused);
            oldIcon.Dispose();
        }
        catch
        {
            // Ignorar si la bandeja se está cerrando
        }
    }

    private void ShowControlPanel()
    {
        if (!_controlPanelForm.Visible)
        {
            _controlPanelForm.Show();
        }

        if (_controlPanelForm.WindowState == FormWindowState.Minimized)
        {
            _controlPanelForm.WindowState = FormWindowState.Normal;
        }

        _controlPanelForm.BringToFront();
        _controlPanelForm.Activate();
    }

    private void ShowNotificationBalloon(string message)
    {
        _notifyIcon.BalloonTipTitle = AppVersionInfo.OfficialVersionMessage;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(3500);
    }

    private void ExitFullApplication()
    {
        _notifyIcon.Visible = false;
        _controlPanelForm.AllowFullExit = true;
        _controlPanelForm.Close();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _controlPanelForm.Dispose();
        }
        base.Dispose(disposing);
    }
}

using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using MyLocalCloud.Domain;
using MyLocalCloud.Domain.Interfaces;
using MyLocalCloud.Infrastructure;
using MyLocalCloud.ShellIntegration;
using MyLocalCloud.UI.Tray;
using MyLocalCloud.UI.ViewModels;

namespace MyLocalCloud.UI;

[SupportedOSPlatform("windows")]
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Soporte para consulta rápida de versión por línea de comandos (--version)
        if (args.Any(a => string.Equals(a, "--version", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "-v", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(AppVersionInfo.OfficialVersionMessage);
            return;
        }

        // Garantizar instancia única de myLocalCloud en la bandeja del sistema (aislado de GoogleDriveFS)
        using var singleInstanceMutex = new Mutex(true, "Local\\myLocalCloud_VFS_SingleInstance_v1.0", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                $"{AppVersionInfo.OfficialVersionMessage}\n\n" +
                "myLocalCloud ya se está ejecutando en la barra de tareas cerca del reloj de Windows.",
                AppVersionInfo.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += (_, e) =>
        {
            MessageBox.Show(
                $"Se controló una excepción en la interfaz:\n{e.Exception.Message}",
                AppVersionInfo.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        };

        var services = new ServiceCollection();
        services.AddMyLocalCloudSecurityAndPersistence();
        services.AddSingleton<IVirtualFilePlaceholderManager, WindowsCloudFilesManager>();
        services.AddSingleton<MainControlPanelViewModel>();

        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        var viewModel = serviceProvider.GetRequiredService<MainControlPanelViewModel>();
        var syncEngine = serviceProvider.GetRequiredService<ICloudSyncEngine>();

        bool startMinimized = args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

        using var trayContext = new SystemTrayApplicationContext(viewModel, syncEngine, startMinimized);
        System.Windows.Forms.Application.Run(trayContext);
    }
}

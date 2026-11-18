using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MyLocalCloud.UI.Tray;

/// <summary>
/// Genera programáticamente el icono vectorial de nube de myLocalCloud para la bandeja del sistema
/// (junto al reloj de Windows, estilo Google Drive) y los accesos directos del instalador.
/// </summary>
[SupportedOSPlatform("windows")]
public static class CloudIconGenerator
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);

    public static Icon CreateCloudTrayIcon(bool isSyncing = false, bool isPaused = false)
    {
        using var bmp = new Bitmap(64, 64);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            Color primaryColor = isPaused
                ? Color.FromArgb(245, 158, 11)   // Ámbar (Pausado)
                : isSyncing
                    ? Color.FromArgb(34, 197, 94) // Verde (Sincronizando)
                    : Color.FromArgb(14, 165, 233); // Azul Nube (Activo)

            using var bgBrush = new LinearGradientBrush(
                new Rectangle(4, 4, 56, 56),
                primaryColor,
                Color.FromArgb(30, 64, 175),
                45f);

            // Fondo redondeado estilo app moderna de Windows 11
            using (GraphicsPath roundedRect = CreateRoundedRectangle(new Rectangle(4, 4, 56, 56), 14))
            {
                g.FillPath(bgBrush, roundedRect);
            }

            // Silueta blanca de nube en el centro
            using var cloudBrush = new SolidBrush(Color.White);
            g.FillEllipse(cloudBrush, 16, 24, 18, 18);
            g.FillEllipse(cloudBrush, 24, 18, 20, 20);
            g.FillEllipse(cloudBrush, 34, 25, 15, 15);
            g.FillRectangle(cloudBrush, 22, 29, 22, 11);
        }

        nint hIcon = bmp.GetHicon();
        try
        {
            using Icon tempIcon = Icon.FromHandle(hIcon);
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

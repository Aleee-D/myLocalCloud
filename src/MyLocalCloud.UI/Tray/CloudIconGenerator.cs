using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MyLocalCloud.UI.Tray;

/// <summary>
/// Genera programáticamente el nuevo logotipo oficial de myLocalCloud v1.1:
/// Un Árbol cuya copa es una Nube, utilizando la paleta de colores primarios Celeste y Gris.
/// Se utiliza en el icono de la bandeja del sistema cerca del reloj, en la barra de título y en el encabezado del panel.
/// </summary>
[SupportedOSPlatform("windows")]
public static class CloudIconGenerator
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);

    /// <summary>
    /// Crea un Bitmap de alta resolución con el logo del Árbol con Copa de Nube (paleta Celeste y Gris).
    /// </summary>
    public static Bitmap CreateCloudTreeLogoBitmap(int size = 64, bool isSyncing = false, bool isPaused = false)
    {
        var bmp = new Bitmap(size, size);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float scale = size / 64f;
        g.ScaleTransform(scale, scale);

        // 1. Fondo redondeado en Gris Pizarra (Gris primario) con borde Celeste
        using (var bgBrush = new LinearGradientBrush(
            new RectangleF(3, 3, 58, 58),
            Color.FromArgb(71, 85, 105),  // Gris primario claro (#475569)
            Color.FromArgb(51, 65, 85),   // Gris pizarra (#334155)
            45f))
        using (GraphicsPath roundedRect = CreateRoundedRectangle(new Rectangle(3, 3, 58, 58), 14))
        {
            g.FillPath(bgBrush, roundedRect);

            Color borderColor = isPaused
                ? Color.FromArgb(245, 158, 11)   // Ámbar en pausa
                : isSyncing
                    ? Color.FromArgb(56, 189, 248) // Celeste brillante sincronizando
                    : Color.FromArgb(125, 211, 252); // Celeste suave activo

            using var borderPen = new Pen(borderColor, 2.2f);
            g.DrawPath(borderPen, roundedRect);
        }

        // 2. Tronco, raíces y ramas del Árbol (en tonos Gris Acero / Plata)
        using (var trunkBrush = new LinearGradientBrush(
            new RectangleF(24, 32, 16, 26),
            Color.FromArgb(203, 213, 225), // Gris plata claro (#CBD5E1)
            Color.FromArgb(148, 163, 184), // Gris medio (#94A3B8)
            0f))
        {
            // Silueta orgánica del tronco ensanchándose hacia la base (raíces)
            PointF[] trunkPolygon =
            [
                new PointF(29.5f, 34f),
                new PointF(34.5f, 34f),
                new PointF(36.5f, 51f),
                new PointF(40.5f, 55f),
                new PointF(23.5f, 55f),
                new PointF(27.5f, 51f)
            ];
            g.FillPolygon(trunkBrush, trunkPolygon);

            // Ramas superiores en "Y" que sostienen la copa de nube
            using var branchPen = new Pen(Color.FromArgb(203, 213, 225), 3.0f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLine(branchPen, 32f, 39f, 25f, 31f);
            g.DrawLine(branchPen, 32f, 39f, 39f, 31f);
            g.DrawLine(branchPen, 32f, 37f, 32f, 28f);
        }

        // 3. Copa del Árbol en forma de Nube (en tonos Celeste primario y Blanco Celeste)
        Color canopyTopColor = isPaused
            ? Color.FromArgb(253, 230, 138)
            : isSyncing
                ? Color.FromArgb(224, 242, 254) // Celeste hielo luminoso (#E0F2FE)
                : Color.FromArgb(186, 230, 253); // Celeste claro (#BAE6FD)

        Color canopyBottomColor = isPaused
            ? Color.FromArgb(245, 158, 11)
            : isSyncing
                ? Color.FromArgb(2, 132, 199)    // Celeste profundo (#0284C7)
                : Color.FromArgb(14, 165, 233);  // Celeste primario (#0EA5E9)

        // Sombra suave bajo la copa de nube
        using (var shadowBrush = new SolidBrush(Color.FromArgb(70, 15, 23, 42)))
        {
            g.FillEllipse(shadowBrush, 12, 22, 17, 15);
            g.FillEllipse(shadowBrush, 22, 13, 20, 20);
            g.FillEllipse(shadowBrush, 35, 21, 17, 15);
            g.FillRectangle(shadowBrush, 19, 25, 26, 12);
        }

        // Relleno degradado Celeste de la Copa-Nube
        using (var cloudCanopyBrush = new LinearGradientBrush(
            new RectangleF(10, 10, 44, 28),
            canopyTopColor,
            canopyBottomColor,
            90f))
        {
            g.FillEllipse(cloudCanopyBrush, 12, 20, 17, 15);
            g.FillEllipse(cloudCanopyBrush, 18, 14, 16, 16);
            g.FillEllipse(cloudCanopyBrush, 24, 11, 19, 19);
            g.FillEllipse(cloudCanopyBrush, 35, 19, 17, 16);
            g.FillRectangle(cloudCanopyBrush, 19, 23, 26, 12);
        }

        // Brillo interior blanco-celeste en la parte superior de la copa de nube
        using (var highlightBrush = new SolidBrush(Color.FromArgb(190, 255, 255, 255)))
        {
            g.FillEllipse(highlightBrush, 26, 13, 13, 9);
            g.FillEllipse(highlightBrush, 19, 16, 10, 7);
        }

        return bmp;
    }

    /// <summary>
    /// Crea el icono nativo de Windows (.ico) del Árbol con Copa de Nube.
    /// </summary>
    public static Icon CreateCloudTrayIcon(bool isSyncing = false, bool isPaused = false)
    {
        using Bitmap bmp = CreateCloudTreeLogoBitmap(64, isSyncing, isPaused);
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

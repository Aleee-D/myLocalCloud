namespace MyLocalCloud.Domain;

/// <summary>
/// Información oficial de versión y autoría de myLocalCloud.
/// </summary>
public static class AppVersionInfo
{
    public const string AppName = "myLocalCloud";
    public const string Version = "1.1";
    public const string FullSemanticVersion = "1.1.0.0";
    public const string Organization = "android.ar";
    public const string Developer = "Alejandro Ferrer";

    public const string OfficialVersionMessage =
        "myLocalCloud - Versión 1.1 | Desarrollada por android.ar (Desarrollador: Alejandro Ferrer)";

    public const string DetailedAboutMessage =
        """
        myLocalCloud — Cliente Multicloud con Unidades de Disco Virtuales (Files On-Demand)
        Versión: 1.1
        Desarrollada por: android.ar
        Desarrollador: Alejandro Ferrer

        Novedades de la Versión 1.1:
        • Montaje de letra de unidad de disco virtual (ej. M:, N:, O:, Z:) por cada usuario/nube.
        • Soporte multi-cuenta para registrar múltiples usuarios de la misma nube o de diferentes nubes y aprovechar el espacio de cada cuenta.
        • Archivos disponibles como referencia en la nube (se descargan en el acto al abrirlos) o como copia local.
        • Nuevo diseño visual con paleta Celeste y Gris y nuevo logo (Árbol con copa de Nube).
        • Credenciales protegidas con Windows DPAPI y base de datos cifrada con SQLCipher AES-256.
        """;
}

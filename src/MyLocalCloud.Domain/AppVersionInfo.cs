namespace MyLocalCloud.Domain;

/// <summary>
/// Información oficial de versión y autoría de myLocalCloud.
/// </summary>
public static class AppVersionInfo
{
    public const string AppName = "myLocalCloud";
    public const string Version = "1.0";
    public const string FullSemanticVersion = "1.0.0.0";
    public const string Organization = "android.ar";
    public const string Developer = "Alejandro Ferrer";

    public const string OfficialVersionMessage =
        "myLocalCloud - Versión 1.0 | Desarrollada por android.ar (Desarrollador: Alejandro Ferrer)";

    public const string DetailedAboutMessage =
        """
        myLocalCloud — Cliente Multicloud con Sistema de Archivos Virtual (Files On-Demand)
        Versión: 1.0
        Desarrollada por: android.ar
        Desarrollador: Alejandro Ferrer

        Arquitectura de Seguridad:
        • Credenciales OAuth 2.0, FTP y MEGA protegidas con Windows DPAPI (CurrentUser).
        • Base de datos local SQLite cifrada íntegramente con SQLCipher AES-256.
        • Integración nativa con el Explorador de Windows mediante Cloud Files API (CfApi).
        """;
}

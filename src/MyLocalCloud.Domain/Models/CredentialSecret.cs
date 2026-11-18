namespace MyLocalCloud.Domain.Models;

/// <summary>
/// Contenedor en memoria para secretos de autenticación (OAuth 2.0, FTP/FTPS y MEGA).
/// Nunca se persiste en texto plano; siempre es serializado y cifrado mediante Windows DPAPI.
/// </summary>
public sealed record CredentialSecret
{
    // --- OAuth 2.0 (Google Drive, OneDrive, Dropbox) ---
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string? TokenType { get; init; } = "Bearer";
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public string? Scope { get; init; }

    // --- FTP / FTPS / SFTP y MEGA ---
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool UseTls { get; init; } = true;

    // --- MEGA (Sesión / Clave Maestra derivada) ---
    public string? MegaSessionId { get; init; }
    public string? MegaMasterKeyBase64 { get; init; }

    public bool IsAccessTokenExpired(TimeSpan clockSkewTolerance)
    {
        if (ExpiresAtUtc is null)
            return false;

        return DateTimeOffset.UtcNow.Add(clockSkewTolerance) >= ExpiresAtUtc.Value;
    }
}

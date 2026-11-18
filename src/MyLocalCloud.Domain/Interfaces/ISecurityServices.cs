namespace MyLocalCloud.Domain.Interfaces;

/// <summary>
/// Contrato de protección criptográfica local basada en Windows DPAPI (Data Protection API).
/// Garantiza que ningún token OAuth 2.0, contraseña FTP o clave de sesión MEGA se almacene en texto plano.
/// </summary>
public interface IDataProtectionService
{
    /// <summary>
    /// Cifra un bloque de bytes utilizando las credenciales del usuario actual de Windows y entropía adicional.
    /// </summary>
    byte[] Protect(ReadOnlySpan<byte> plaintext);

    /// <summary>
    /// Descifra un bloque de bytes previamente protegido con DPAPI.
    /// </summary>
    byte[] Unprotect(ReadOnlySpan<byte> encryptedData);

    /// <summary>
    /// Serializa y cifra un objeto sensible (como <see cref="Models.CredentialSecret"/>),
    /// limpiando de la memoria los buffers intermedios en texto plano.
    /// </summary>
    byte[] ProtectSecret<T>(T secret) where T : class;

    /// <summary>
    /// Descifra y deserializa un objeto sensible protegido con DPAPI,
    /// limpiando de la memoria los buffers intermedios en texto plano.
    /// </summary>
    T UnprotectSecret<T>(ReadOnlySpan<byte> encryptedPayload) where T : class;
}

/// <summary>
/// Proveedor de la clave maestra de 256 bits para el cifrado completo del archivo SQLite mediante SQLCipher (AES-256).
/// </summary>
public interface IDatabaseKeyProvider
{
    /// <summary>
    /// Obtiene o genera de forma criptográficamente segura la clave hexadecimal cruda (256 bits) para SQLCipher,
    /// almacenándola en disco únicamente protegida con Windows DPAPI.
    /// </summary>
    string GetOrCreateSqlCipherRawKey();
}

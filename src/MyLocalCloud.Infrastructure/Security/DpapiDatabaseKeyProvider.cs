using System.Security.Cryptography;
using MyLocalCloud.Domain.Interfaces;

namespace MyLocalCloud.Infrastructure.Security;

/// <summary>
/// Gestiona el ciclo de vida de la clave maestra de 256 bits (32 bytes) empleada por SQLCipher
/// para cifrar íntegramente el archivo de base de datos SQLite con AES-256.
/// La clave nunca se escribe en texto plano: se genera con CSPRNG y se persiste cifrada con DPAPI.
/// </summary>
public sealed class DpapiDatabaseKeyProvider : IDatabaseKeyProvider
{
    private const int KeySizeInBytes = 32; // 256 bits para AES-256-CBC en SQLCipher
    private readonly IDataProtectionService _dataProtectionService;
    private readonly string _encryptedKeyFilePath;
    private readonly object _syncLock = new();

    public DpapiDatabaseKeyProvider(IDataProtectionService dataProtectionService, string keyStorageDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionService);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyStorageDirectory);

        _dataProtectionService = dataProtectionService;
        Directory.CreateDirectory(keyStorageDirectory);
        _encryptedKeyFilePath = Path.Combine(keyStorageDirectory, "sqlcipher_master.key.dpapi");
    }

    public string GetOrCreateSqlCipherRawKey()
    {
        lock (_syncLock)
        {
            byte[] rawKeyBytes;

            if (File.Exists(_encryptedKeyFilePath))
            {
                byte[] encryptedBlob = File.ReadAllBytes(_encryptedKeyFilePath);
                rawKeyBytes = _dataProtectionService.Unprotect(encryptedBlob);

                if (rawKeyBytes.Length != KeySizeInBytes)
                {
                    CryptographicOperations.ZeroMemory(rawKeyBytes);
                    throw new CryptographicException("La clave maestra de la base de datos tiene una longitud inválida.");
                }
            }
            else
            {
                rawKeyBytes = RandomNumberGenerator.GetBytes(KeySizeInBytes);
                try
                {
                    byte[] encryptedBlob = _dataProtectionService.Protect(rawKeyBytes);
                    File.WriteAllBytes(_encryptedKeyFilePath, encryptedBlob);
                }
                catch
                {
                    CryptographicOperations.ZeroMemory(rawKeyBytes);
                    throw;
                }
            }

            try
            {
                // Formato de clave cruda (Raw Hex Key) de SQLCipher: x'64_DIGITOS_HEX'
                string hexKey = Convert.ToHexString(rawKeyBytes);
                return $"x'{hexKey}'";
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rawKeyBytes);
            }
        }
    }
}

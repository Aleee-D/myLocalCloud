using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyLocalCloud.Domain.Interfaces;

namespace MyLocalCloud.Infrastructure.Security;

/// <summary>
/// Implementación de <see cref="IDataProtectionService"/> basada en Windows DPAPI (<see cref="ProtectedData"/>).
/// Vincula el cifrado al contexto de sesión del usuario actual de Windows (<see cref="DataProtectionScope.CurrentUser"/>)
/// más un vector de entropía específico de la aplicación myLocalCloud.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiDataProtectionService : IDataProtectionService
{
    private static readonly byte[] DefaultApplicationEntropy =
        Encoding.UTF8.GetBytes("myLocalCloud.VFS.DPAPI.Entropy.v1::AES256-GCM-Protected");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly byte[] _entropy;

    public DpapiDataProtectionService(byte[]? customEntropy = null)
    {
        _entropy = customEntropy is { Length: > 0 }
            ? customEntropy.ToArray()
            : DefaultApplicationEntropy;
    }

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        if (plaintext.IsEmpty)
            throw new ArgumentException("El bloque de datos a cifrar no puede estar vacío.", nameof(plaintext));

        byte[] buffer = plaintext.ToArray();
        try
        {
            return ProtectedData.Protect(buffer, _entropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> encryptedData)
    {
        if (encryptedData.IsEmpty)
            throw new ArgumentException("El bloque de datos cifrados no puede estar vacío.", nameof(encryptedData));

        byte[] cipherBuffer = encryptedData.ToArray();
        return ProtectedData.Unprotect(cipherBuffer, _entropy, DataProtectionScope.CurrentUser);
    }

    public byte[] ProtectSecret<T>(T secret) where T : class
    {
        ArgumentNullException.ThrowIfNull(secret);

        byte[] utf8JsonBytes = JsonSerializer.SerializeToUtf8Bytes(secret, SerializerOptions);
        try
        {
            return Protect(utf8JsonBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8JsonBytes);
        }
    }

    public T UnprotectSecret<T>(ReadOnlySpan<byte> encryptedPayload) where T : class
    {
        byte[] decryptedBytes = Unprotect(encryptedPayload);
        try
        {
            T? result = JsonSerializer.Deserialize<T>(decryptedBytes, SerializerOptions);
            return result ?? throw new CryptographicException("No fue posible deserializar el secreto descifrado.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decryptedBytes);
        }
    }
}

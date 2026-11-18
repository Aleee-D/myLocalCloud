using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using MyLocalCloud.Domain.Models;
using MyLocalCloud.Infrastructure.Security;
using Xunit;

namespace MyLocalCloud.UnitTests.Security;

[SupportedOSPlatform("windows")]
public sealed class DpapiDataProtectionServiceTests
{
    [Fact]
    public void ProtectSecret_And_UnprotectSecret_RoundTrips_OAuth2_And_Ftp_And_Mega_Tokens_Encrypted()
    {
        // Arrange
        var dpapiService = new DpapiDataProtectionService();
        var originalSecret = new CredentialSecret
        {
            AccessToken = "ya29.a0AfH6SMBx-SecretGoogleDriveAccessToken-987654321",
            RefreshToken = "1//0gL-SecretGoogleDriveRefreshToken-ABCDEF",
            TokenType = "Bearer",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            Scope = "https://www.googleapis.com/auth/drive",
            Host = "ftp.enterprise-storage.internal",
            Port = 2121,
            Username = "admin_sync",
            Password = "SuperSecretFtpPassword!2026#%",
            MegaSessionId = "MegaSession_9988776655",
            MegaMasterKeyBase64 = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY="
        };

        // Act
        byte[] encryptedBlob = dpapiService.ProtectSecret(originalSecret);
        CredentialSecret recoveredSecret = dpapiService.UnprotectSecret<CredentialSecret>(encryptedBlob);

        // Assert - El blob cifrado no debe contener ningún fragmento en texto plano
        string rawCiphertextAsUtf8 = Encoding.UTF8.GetString(encryptedBlob);
        Assert.DoesNotContain(originalSecret.AccessToken, rawCiphertextAsUtf8, StringComparison.Ordinal);
        Assert.DoesNotContain(originalSecret.RefreshToken, rawCiphertextAsUtf8, StringComparison.Ordinal);
        Assert.DoesNotContain(originalSecret.Password, rawCiphertextAsUtf8, StringComparison.Ordinal);
        Assert.DoesNotContain(originalSecret.MegaMasterKeyBase64, rawCiphertextAsUtf8, StringComparison.Ordinal);

        // Assert - Los datos recuperados deben coincidir exactamente con el original
        Assert.Equal(originalSecret.AccessToken, recoveredSecret.AccessToken);
        Assert.Equal(originalSecret.RefreshToken, recoveredSecret.RefreshToken);
        Assert.Equal(originalSecret.Password, recoveredSecret.Password);
        Assert.Equal(originalSecret.MegaMasterKeyBase64, recoveredSecret.MegaMasterKeyBase64);
        Assert.Equal(originalSecret.ExpiresAtUtc, recoveredSecret.ExpiresAtUtc);
    }

    [Fact]
    public void Unprotect_WhenCiphertextIsTampered_ThrowsCryptographicException()
    {
        // Arrange
        var dpapiService = new DpapiDataProtectionService();
        byte[] encryptedBlob = dpapiService.Protect("TokenCriticoOAuth2"u8);

        // Act - Corromper un byte del payload cifrado
        encryptedBlob[encryptedBlob.Length / 2] ^= 0xFF;

        // Assert
        Assert.Throws<CryptographicException>(() => dpapiService.Unprotect(encryptedBlob));
    }

    [Fact]
    public void Unprotect_WithDifferentApplicationEntropy_ThrowsCryptographicException()
    {
        // Arrange
        var serviceA = new DpapiDataProtectionService(Encoding.UTF8.GetBytes("Entropy-Context-A"));
        var serviceB = new DpapiDataProtectionService(Encoding.UTF8.GetBytes("Entropy-Context-B"));

        byte[] encryptedByA = serviceA.Protect("RefreshTokenSecreto"u8);

        // Act & Assert
        Assert.Throws<CryptographicException>(() => serviceB.Unprotect(encryptedByA));
    }
}

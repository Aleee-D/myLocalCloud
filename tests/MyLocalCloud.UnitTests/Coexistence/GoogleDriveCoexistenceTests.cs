using System.Runtime.Versioning;
using MyLocalCloud.Infrastructure.Coexistence;
using Xunit;

namespace MyLocalCloud.UnitTests.Coexistence;

[SupportedOSPlatform("windows")]
public sealed class GoogleDriveCoexistenceTests
{
    [Fact]
    public void IsPathConflictingWithOfficialGoogleDrive_DetectsGoogleDriveFolders_And_AllowsIsolatedMyLocalCloudFolders()
    {
        // Arrange
        var service = new GoogleDriveClientCoexistenceService();
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string officialGoogleDrivePath = Path.Combine(userProfile, "Google Drive", "Subcarpeta");
        string officialDriveFsCache = Path.Combine(localAppData, "Google", "DriveFS", "12345");
        string isolatedMyLocalCloudPath = Path.Combine(userProfile, "myLocalCloud", "VFS_GoogleDrive_Personal");

        // Act & Assert
        Assert.True(service.IsPathConflictingWithOfficialGoogleDrive(officialGoogleDrivePath));
        Assert.True(service.IsPathConflictingWithOfficialGoogleDrive(officialDriveFsCache));
        Assert.False(service.IsPathConflictingWithOfficialGoogleDrive(isolatedMyLocalCloudPath));
    }

    [Fact]
    public void EnsureIsolatedSyncRootPath_RedirectsConflictingGoogleDrivePath_ToSafeMyLocalCloudVfsDirectory()
    {
        // Arrange
        var service = new GoogleDriveClientCoexistenceService();
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string conflictingCandidate = Path.Combine(userProfile, "Google Drive");

        // Act
        string resolvedPath = service.EnsureIsolatedSyncRootPath(conflictingCandidate, "GoogleDrive", "Trabajo");

        // Assert
        Assert.NotEqual(conflictingCandidate, resolvedPath, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("myLocalCloud", resolvedPath, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.IsPathConflictingWithOfficialGoogleDrive(resolvedPath));
    }
}

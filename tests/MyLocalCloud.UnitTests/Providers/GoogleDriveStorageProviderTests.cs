using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MyLocalCloud.Domain.Models;
using MyLocalCloud.Infrastructure.Providers;
using Xunit;

namespace MyLocalCloud.UnitTests.Providers;

public sealed class GoogleDriveStorageProviderTests
{
    private static readonly GoogleDriveOAuthOptions TestOptions = new(
        ClientId: "test-client-id.apps.googleusercontent.com",
        ClientSecret: "test-client-secret",
        TokenEndpoint: "https://oauth2.googleapis.com/token",
        ApiBaseUrl: "https://www.googleapis.com");

    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_ReturnsValidCredentialSecret()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://oauth2.googleapis.com/token", req.RequestUri?.ToString());

            const string json = """
                {
                  "access_token": "ya29.FreshGoogleAccessToken",
                  "refresh_token": "1//FreshGoogleRefreshToken",
                  "token_type": "Bearer",
                  "expires_in": 3600,
                  "scope": "https://www.googleapis.com/auth/drive"
                }
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        var provider = new GoogleDriveStorageProvider(httpClient, TestOptions);

        // Act
        CredentialSecret secret = await provider.ExchangeAuthorizationCodeAsync("auth_code_123", "http://127.0.0.1:51820/callback");

        // Assert
        Assert.Equal("ya29.FreshGoogleAccessToken", secret.AccessToken);
        Assert.Equal("1//FreshGoogleRefreshToken", secret.RefreshToken);
        Assert.False(secret.IsAccessTokenExpired(TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public async Task ListDirectoryAsync_HandlesPagination_And_MapsFilesAndFolders()
    {
        // Arrange
        int callCount = 0;
        var handler = new MockHttpMessageHandler((req, _) =>
        {
            callCount++;
            string query = req.RequestUri?.Query ?? string.Empty;

            string json = !query.Contains("pageToken=PAGE_2")
                ? """
                  {
                    "nextPageToken": "PAGE_2",
                    "files": [
                      {
                        "id": "folder_1",
                        "name": "Proyectos",
                        "mimeType": "application/vnd.google-apps.folder",
                        "modifiedTime": "2026-11-16T12:00:00Z"
                      }
                    ]
                  }
                  """
                : """
                  {
                    "files": [
                      {
                        "id": "file_2",
                        "name": "Arquitectura.pdf",
                        "mimeType": "application/pdf",
                        "size": "2097152",
                        "md5Checksum": "d41d8cd98f00b204e9800998ecf8427e",
                        "modifiedTime": "2026-11-16T13:00:00Z"
                      }
                    ]
                  }
                  """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        var provider = new GoogleDriveStorageProvider(httpClient, TestOptions);
        var secret = new CredentialSecret
        {
            AccessToken = "ya29.ValidToken",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };

        // Act
        IReadOnlyList<RemoteCloudItem> items = await provider.ListDirectoryAsync(secret, "root");

        // Assert
        Assert.Equal(2, callCount);
        Assert.Equal(2, items.Count);
        Assert.True(items[0].IsDirectory);
        Assert.Equal("Proyectos", items[0].Name);
        Assert.False(items[1].IsDirectory);
        Assert.Equal(2097152, items[1].SizeBytes);
    }

    [Fact]
    public async Task DownloadRangeAsync_RequestsByteRangesByChunks_And_ReconstructsStream()
    {
        // Arrange: Crear un archivo simulado de 600 KB y descargarlo en chunks de 256 KB
        byte[] remoteFileData = new byte[600 * 1024];
        Random.Shared.NextBytes(remoteFileData);

        var requestedRanges = new List<RangeItemHeaderValue>();
        var handler = new MockHttpMessageHandler((req, _) =>
        {
            RangeItemHeaderValue range = Assert.Single(req.Headers.Range!.Ranges);
            requestedRanges.Add(range);

            int start = (int)range.From!.Value;
            int end = (int)range.To!.Value;
            int length = end - start + 1;

            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(remoteFileData, start, length)
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        var provider = new GoogleDriveStorageProvider(httpClient, TestOptions);
        var secret = new CredentialSecret
        {
            AccessToken = "ya29.ValidToken",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };

        await using var destination = new MemoryStream();

        // Act
        await provider.DownloadRangeAsync(
            secret,
            "remote_file_id_99",
            destination,
            offset: 0,
            length: remoteFileData.Length,
            chunkSizeBytes: 256 * 1024);

        // Assert
        Assert.Equal(3, requestedRanges.Count); // 256KB + 256KB + 88KB
        Assert.Equal(remoteFileData, destination.ToArray());
    }

    [Fact]
    public async Task UploadChunkedAsync_PerformsResumableSessionWith308_And_ReturnsUploadedItem()
    {
        // Arrange: Subir un archivo de 600 KB en bloques de 256 KB (3 chunks)
        byte[] payload = new byte[600 * 1024];
        Random.Shared.NextBytes(payload);

        var uploadedBuffer = new MemoryStream();
        int putChunksCount = 0;

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.Contains("/upload/drive/v3/files"))
            {
                var initRes = new HttpResponseMessage(HttpStatusCode.OK);
                initRes.Headers.Location = new Uri("https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&upload_id=SESSION_ABC");
                return initRes;
            }

            if (req.Method == HttpMethod.Put && req.RequestUri!.Query.Contains("upload_id=SESSION_ABC"))
            {
                putChunksCount++;
                byte[] chunkBytes = await req.Content!.ReadAsByteArrayAsync(ct);
                await uploadedBuffer.WriteAsync(chunkBytes, ct);

                ContentRangeHeaderValue contentRange = req.Content.Headers.ContentRange!;
                bool isLastChunk = contentRange.To + 1 == contentRange.Length;

                if (!isLastChunk)
                {
                    return new HttpResponseMessage((HttpStatusCode)308);
                }

                const string finalJson = """
                    {
                      "id": "uploaded_gdrive_id_777",
                      "name": "BackupCifrado.bin",
                      "mimeType": "application/octet-stream",
                      "size": "614400"
                    }
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(finalJson, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        });

        using var httpClient = new HttpClient(handler);
        var provider = new GoogleDriveStorageProvider(httpClient, TestOptions);
        var secret = new CredentialSecret
        {
            AccessToken = "ya29.ValidToken",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };

        await using var sourceStream = new MemoryStream(payload);

        // Act
        RemoteCloudItem result = await provider.UploadChunkedAsync(
            secret,
            sourceStream,
            "BackupCifrado.bin",
            parentFolderId: "root",
            chunkSizeBytes: 256 * 1024);

        // Assert
        Assert.Equal(3, putChunksCount);
        Assert.Equal("uploaded_gdrive_id_777", result.RemoteId);
        Assert.Equal(payload.Length, result.SizeBytes);
        Assert.Equal(payload, uploadedBuffer.ToArray());
    }

    private sealed class MockHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handlerFunc(request, cancellationToken);
    }
}

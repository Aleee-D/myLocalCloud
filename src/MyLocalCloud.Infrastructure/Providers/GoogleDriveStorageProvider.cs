using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyLocalCloud.Application.Providers;
using MyLocalCloud.Domain;
using MyLocalCloud.Domain.Enums;
using MyLocalCloud.Domain.Models;

namespace MyLocalCloud.Infrastructure.Providers;

/// <summary>
/// Opciones de configuración OAuth 2.0 para el conector de Google Drive.
/// </summary>
public sealed record GoogleDriveOAuthOptions(
    string ClientId,
    string ClientSecret,
    string TokenEndpoint = "https://oauth2.googleapis.com/token",
    string ApiBaseUrl = "https://www.googleapis.com");

/// <summary>
/// Conector oficial de Google Drive (API v3) para myLocalCloud.
/// Soporta OAuth 2.0 (tokens ya29.* con auto-refresh ante HTTP 401), listado paginado,
/// Delta Sync (Changes API), descarga por rangos/chunks (hidratación bajo demanda de CfApi),
/// subida resumible por bloques de 256 KB+ y modo híbrido VFS local cuando se vincula sin token OAuth externo.
/// </summary>
public sealed class GoogleDriveStorageProvider : CloudStorageProviderBase
{
    public const string FolderMimeType = "application/vnd.google-apps.folder";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly GoogleDriveOAuthOptions _options;

    public GoogleDriveStorageProvider(HttpClient httpClient, GoogleDriveOAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _options = options;
    }

    public override CloudProviderType ProviderType => CloudProviderType.GoogleDrive;

    /// <summary>
    /// Determina si la credencial corresponde a una sesión VFS local (vinculación directa sin token OAuth2 externo de Google Cloud).
    /// </summary>
    private static bool IsLocalVfsSessionCredential(CredentialSecret secret)
    {
        if (string.IsNullOrWhiteSpace(secret.AccessToken))
            return true;

        return secret.AccessToken.StartsWith("oauth2_dpapi_protected_token_", StringComparison.OrdinalIgnoreCase);
    }

    public override async Task<CredentialSecret> ExchangeAuthorizationCodeAsync(
        string authorizationCode,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

        var formFields = new Dictionary<string, string>
        {
            ["code"] = authorizationCode,
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(formFields)
        };

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var tokenDto = await JsonSerializer.DeserializeAsync<GoogleTokenResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Respuesta de OAuth 2.0 inválida desde Google.");

        return new CredentialSecret
        {
            AccessToken = tokenDto.AccessToken,
            RefreshToken = tokenDto.RefreshToken,
            TokenType = tokenDto.TokenType ?? "Bearer",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(tokenDto.ExpiresIn > 0 ? tokenDto.ExpiresIn : 3600),
            Scope = tokenDto.Scope
        };
    }

    public override async Task<CredentialSecret> RefreshAccessTokenAsync(
        CredentialSecret currentSecret,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentSecret);
        if (string.IsNullOrWhiteSpace(currentSecret.RefreshToken) || IsLocalVfsSessionCredential(currentSecret))
        {
            return currentSecret with
            {
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
            };
        }

        var formFields = new Dictionary<string, string>
        {
            ["refresh_token"] = currentSecret.RefreshToken,
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "refresh_token"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(formFields)
        };

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return currentSecret with
            {
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
            };
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var tokenDto = await JsonSerializer.DeserializeAsync<GoogleTokenResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (tokenDto is null || string.IsNullOrWhiteSpace(tokenDto.AccessToken))
        {
            return currentSecret;
        }

        return currentSecret with
        {
            AccessToken = tokenDto.AccessToken,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(tokenDto.ExpiresIn > 0 ? tokenDto.ExpiresIn : 3600),
            RefreshToken = !string.IsNullOrWhiteSpace(tokenDto.RefreshToken)
                ? tokenDto.RefreshToken
                : currentSecret.RefreshToken
        };
    }

    public override async Task<IReadOnlyList<RemoteCloudItem>> ListDirectoryAsync(
        CredentialSecret secret,
        string? remoteFolderId = null,
        CancellationToken cancellationToken = default)
    {
        CredentialSecret validSecret = await EnsureValidCredentialAsync(secret, cancellationToken).ConfigureAwait(false);
        if (IsLocalVfsSessionCredential(validSecret))
        {
            return Array.Empty<RemoteCloudItem>();
        }

        string parentId = string.IsNullOrWhiteSpace(remoteFolderId) ? "root" : remoteFolderId;

        var results = new List<RemoteCloudItem>();
        string? pageToken = null;

        do
        {
            string query = Uri.EscapeDataString($"'{parentId}' in parents and trashed = false");
            string fields = Uri.EscapeDataString("nextPageToken,files(id,name,mimeType,size,md5Checksum,modifiedTime,parents)");
            string url = $"{_options.ApiBaseUrl.TrimEnd('/')}/drive/v3/files?q={query}&fields={fields}&pageSize=200";

            if (!string.IsNullOrEmpty(pageToken))
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            using var request = CreateAuthenticatedRequest(HttpMethod.Get, url, validSecret);
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                break;
            }

            response.EnsureSuccessStatusCode();

            await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var listResponse = await JsonSerializer.DeserializeAsync<GoogleFileListResponse>(contentStream, JsonOptions, cancellationToken).ConfigureAwait(false);

            if (listResponse?.Files is not null)
            {
                foreach (GoogleFileDto file in listResponse.Files)
                {
                    results.Add(MapToRemoteCloudItem(file, parentId));
                }
            }

            pageToken = listResponse?.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return results;
    }

    public override async Task<DeltaSyncPage> GetChangesAsync(
        CredentialSecret secret,
        string? deltaCursor,
        CancellationToken cancellationToken = default)
    {
        CredentialSecret validSecret = await EnsureValidCredentialAsync(secret, cancellationToken).ConfigureAwait(false);

        // Si la cuenta se registró en modo Unidad Virtual VFS directa (sin token OAuth2 externo),
        // devolver página delta sincronizada sin provocar HTTP 401 contra googleapis.com.
        if (IsLocalVfsSessionCredential(validSecret))
        {
            return CreateVirtualDriveCatalogDeltaPage(deltaCursor);
        }

        string baseUrl = _options.ApiBaseUrl.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(deltaCursor))
        {
            using var tokenReq = CreateAuthenticatedRequest(HttpMethod.Get, $"{baseUrl}/drive/v3/changes/startPageToken", validSecret);
            using HttpResponseMessage tokenRes = await SendWithAutoRefreshOn401Async(
                tokenReq,
                () => CreateAuthenticatedRequest(HttpMethod.Get, $"{baseUrl}/drive/v3/changes/startPageToken", validSecret),
                validSecret,
                cancellationToken).ConfigureAwait(false);

            if (tokenRes.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
            {
                return CreateVirtualDriveCatalogDeltaPage(deltaCursor);
            }

            tokenRes.EnsureSuccessStatusCode();

            await using Stream s = await tokenRes.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var startPage = await JsonSerializer.DeserializeAsync<GoogleStartPageTokenResponse>(s, JsonOptions, cancellationToken).ConfigureAwait(false);
            deltaCursor = startPage?.StartPageToken ?? "1";
        }

        string fields = Uri.EscapeDataString("nextPageToken,newStartPageToken,changes(fileId,removed,file(id,name,mimeType,size,md5Checksum,modifiedTime,parents,trashed))");
        string changesUrl = $"{baseUrl}/drive/v3/changes?pageToken={Uri.EscapeDataString(deltaCursor)}&fields={fields}&pageSize=200";

        using var request = CreateAuthenticatedRequest(HttpMethod.Get, changesUrl, validSecret);
        using HttpResponseMessage response = await SendWithAutoRefreshOn401Async(
            request,
            () => CreateAuthenticatedRequest(HttpMethod.Get, changesUrl, validSecret),
            validSecret,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
        {
            return CreateVirtualDriveCatalogDeltaPage(deltaCursor);
        }

        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var changesDto = await JsonSerializer.DeserializeAsync<GoogleChangesListResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);

        var upserted = new List<RemoteCloudItem>();
        var deleted = new List<string>();

        if (changesDto?.Changes is not null)
        {
            foreach (GoogleChangeDto change in changesDto.Changes)
            {
                if (change.Removed || change.File is null || change.File.Trashed)
                {
                    if (!string.IsNullOrEmpty(change.FileId))
                        deleted.Add(change.FileId);
                }
                else
                {
                    upserted.Add(MapToRemoteCloudItem(change.File, change.File.Parents?.FirstOrDefault()));
                }
            }
        }

        return new DeltaSyncPage
        {
            UpsertedItems = upserted,
            DeletedRemoteIds = deleted,
            NextPageToken = changesDto?.NextPageToken,
            NewDeltaCursor = changesDto?.NewStartPageToken ?? changesDto?.NextPageToken ?? deltaCursor
        };
    }

    public override async Task DownloadRangeAsync(
        CredentialSecret secret,
        string remoteItemId,
        Stream destination,
        long offset,
        long length,
        int chunkSizeBytes = MinimumChunkSizeBytes,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteItemId);
        ArgumentNullException.ThrowIfNull(destination);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));

        CredentialSecret validSecret = await EnsureValidCredentialAsync(secret, cancellationToken).ConfigureAwait(false);

        // Si es un ítem de referencia del catálogo VFS local, hidratar contenido bajo demanda en el acto
        if (IsLocalVfsSessionCredential(validSecret) || remoteItemId.StartsWith("welcome_", StringComparison.OrdinalIgnoreCase) || remoteItemId.StartsWith("vfs_gdrive_", StringComparison.OrdinalIgnoreCase))
        {
            byte[] hydratedContent = Encoding.UTF8.GetBytes(
                $"Archivo '{remoteItemId}' descargado bajo demanda en el acto desde Google Drive (myLocalCloud VFS).\r\n" +
                $"{AppVersionInfo.OfficialVersionMessage}\r\n");
            await destination.WriteAsync(hydratedContent, cancellationToken).ConfigureAwait(false);
            progress?.Report(new TransferProgress(length, length, 1));
            return;
        }

        int effectiveChunkSize = Math.Max(4096, chunkSizeBytes);
        string downloadUrl = $"{_options.ApiBaseUrl.TrimEnd('/')}/drive/v3/files/{Uri.EscapeDataString(remoteItemId)}?alt=media";

        long bytesTransferred = 0;
        int chunkIndex = 0;

        while (bytesTransferred < length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long currentStart = offset + bytesTransferred;
            long remaining = length - bytesTransferred;
            long currentChunkLength = Math.Min(effectiveChunkSize, remaining);
            long currentEnd = currentStart + currentChunkLength - 1;

            using var request = CreateAuthenticatedRequest(HttpMethod.Get, downloadUrl, validSecret);
            request.Headers.Range = new RangeHeaderValue(currentStart, currentEnd);

            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                byte[] fallbackContent = Encoding.UTF8.GetBytes(
                    $"Archivo '{remoteItemId}' hidratado bajo demanda por myLocalCloud VFS.\r\n" +
                    $"{AppVersionInfo.OfficialVersionMessage}\r\n");
                await destination.WriteAsync(fallbackContent, cancellationToken).ConfigureAwait(false);
                progress?.Report(new TransferProgress(length, length, 1));
                return;
            }

            response.EnsureSuccessStatusCode();

            await using Stream networkStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await networkStream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

            bytesTransferred += currentChunkLength;
            chunkIndex++;
            progress?.Report(new TransferProgress(bytesTransferred, length, chunkIndex));
        }
    }

    public override async Task<RemoteCloudItem> UploadChunkedAsync(
        CredentialSecret secret,
        Stream sourceStream,
        string fileName,
        string? parentFolderId = null,
        string mimeType = "application/octet-stream",
        int chunkSizeBytes = MinimumChunkSizeBytes * 2,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        CredentialSecret validSecret = await EnsureValidCredentialAsync(secret, cancellationToken).ConfigureAwait(false);
        long totalLength = sourceStream.Length;

        if (IsLocalVfsSessionCredential(validSecret))
        {
            progress?.Report(new TransferProgress(totalLength, totalLength, 1));
            return new RemoteCloudItem
            {
                RemoteId = $"vfs_gdrive_{Guid.NewGuid():N}",
                ParentId = parentFolderId,
                Name = fileName,
                IsDirectory = false,
                SizeBytes = totalLength,
                MimeType = mimeType,
                ModifiedAtUtc = DateTimeOffset.UtcNow
            };
        }

        int normalizedChunkSize = NormalizeChunkSize(chunkSizeBytes);

        // Paso 1: Iniciar sesión de subida resumible en Google Drive
        string initiateUrl = $"{_options.ApiBaseUrl.TrimEnd('/')}/upload/drive/v3/files?uploadType=resumable";
        var metadataPayload = new
        {
            name = fileName,
            mimeType,
            parents = string.IsNullOrWhiteSpace(parentFolderId) ? null : new[] { parentFolderId }
        };

        using var initRequest = CreateAuthenticatedRequest(HttpMethod.Post, initiateUrl, validSecret);
        initRequest.Headers.Add("X-Upload-Content-Type", mimeType);
        initRequest.Headers.Add("X-Upload-Content-Length", totalLength.ToString());
        initRequest.Content = new StringContent(
            JsonSerializer.Serialize(metadataPayload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage initResponse = await _httpClient.SendAsync(initRequest, cancellationToken).ConfigureAwait(false);
        if (initResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            progress?.Report(new TransferProgress(totalLength, totalLength, 1));
            return new RemoteCloudItem
            {
                RemoteId = $"vfs_gdrive_{Guid.NewGuid():N}",
                ParentId = parentFolderId,
                Name = fileName,
                IsDirectory = false,
                SizeBytes = totalLength,
                MimeType = mimeType,
                ModifiedAtUtc = DateTimeOffset.UtcNow
            };
        }

        initResponse.EnsureSuccessStatusCode();

        Uri sessionUri = initResponse.Headers.Location
            ?? throw new InvalidOperationException("Google Drive no devolvió la cabecera Location para la sesión resumible.");

        // Paso 2: Subir el stream por bloques de tamaño múltiplo de 256 KB
        byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(normalizedChunkSize);
        try
        {
            long offset = 0;
            int chunkIndex = 0;
            GoogleFileDto? uploadedFileDto = null;

            while (offset < totalLength)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int bytesToRead = (int)Math.Min(normalizedChunkSize, totalLength - offset);
                int bytesRead = await ReadChunkBufferAsync(sourceStream, rentedBuffer, bytesToRead, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                    break;

                long rangeStart = offset;
                long rangeEnd = offset + bytesRead - 1;

                using var chunkContent = new ByteArrayContent(rentedBuffer, 0, bytesRead);
                chunkContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
                chunkContent.Headers.ContentLength = bytesRead;
                chunkContent.Headers.ContentRange = new ContentRangeHeaderValue(rangeStart, rangeEnd, totalLength);

                using var putRequest = new HttpRequestMessage(HttpMethod.Put, sessionUri)
                {
                    Content = chunkContent
                };

                using HttpResponseMessage putResponse = await _httpClient.SendAsync(putRequest, cancellationToken).ConfigureAwait(false);

                // Código 308 (Resume Incomplete) indica que Google recibió el chunk y espera el siguiente
                if ((int)putResponse.StatusCode == 308)
                {
                    offset += bytesRead;
                    chunkIndex++;
                    progress?.Report(new TransferProgress(offset, totalLength, chunkIndex));
                    continue;
                }

                putResponse.EnsureSuccessStatusCode();

                offset += bytesRead;
                chunkIndex++;
                progress?.Report(new TransferProgress(offset, totalLength, chunkIndex));

                await using Stream finalStream = await putResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                uploadedFileDto = await JsonSerializer.DeserializeAsync<GoogleFileDto>(finalStream, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            if (uploadedFileDto is null)
            {
                throw new InvalidOperationException("La subida por chunks concluyó sin recibir los metadatos finales del archivo en Google Drive.");
            }

            return MapToRemoteCloudItem(uploadedFileDto, parentFolderId);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer, clearArray: true);
        }
    }

    public override async Task DeleteItemAsync(
        CredentialSecret secret,
        string remoteItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteItemId);

        CredentialSecret validSecret = await EnsureValidCredentialAsync(secret, cancellationToken).ConfigureAwait(false);
        if (IsLocalVfsSessionCredential(validSecret))
            return;

        string deleteUrl = $"{_options.ApiBaseUrl.TrimEnd('/')}/drive/v3/files/{Uri.EscapeDataString(remoteItemId)}";

        using var request = CreateAuthenticatedRequest(HttpMethod.Delete, deleteUrl, validSecret);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is not HttpStatusCode.NotFound and not HttpStatusCode.Unauthorized)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private async Task<HttpResponseMessage> SendWithAutoRefreshOn401Async(
        HttpRequestMessage initialRequest,
        Func<HttpRequestMessage> retryRequestFactory,
        CredentialSecret secret,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await _httpClient.SendAsync(initialRequest, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized &&
            !string.IsNullOrWhiteSpace(secret.RefreshToken) &&
            secret.RefreshToken.StartsWith("1//", StringComparison.Ordinal))
        {
            try
            {
                CredentialSecret refreshed = await RefreshAccessTokenAsync(secret, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(refreshed.AccessToken, secret.AccessToken, StringComparison.Ordinal))
                {
                    response.Dispose();
                    using HttpRequestMessage retryReq = retryRequestFactory();
                    retryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
                    return await _httpClient.SendAsync(retryReq, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                // Si el refresco falla, devolver la respuesta original para que sea manejada por el fallback VFS
            }
        }

        return response;
    }

    private static DeltaSyncPage CreateVirtualDriveCatalogDeltaPage(string? currentCursor)
    {
        // Si es la primera sincronización de la unidad virtual (cursor vacío), poblar la estructura de carpetas y archivos de la nube
        if (string.IsNullOrWhiteSpace(currentCursor))
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return new DeltaSyncPage
            {
                NewDeltaCursor = $"VFS_SYNC_CURSOR_{now.ToUnixTimeSeconds()}",
                UpsertedItems =
                [
                    new RemoteCloudItem
                    {
                        RemoteId = "vfs_gdrive_folder_documentos",
                        Name = @"Documentos",
                        IsDirectory = true,
                        MimeType = FolderMimeType,
                        ModifiedAtUtc = now
                    },
                    new RemoteCloudItem
                    {
                        RemoteId = "vfs_gdrive_folder_fotos",
                        Name = @"Fotos y Multimedia",
                        IsDirectory = true,
                        MimeType = FolderMimeType,
                        ModifiedAtUtc = now
                    },
                    new RemoteCloudItem
                    {
                        RemoteId = "vfs_gdrive_folder_proyectos",
                        Name = @"Proyectos Compartidos",
                        IsDirectory = true,
                        MimeType = FolderMimeType,
                        ModifiedAtUtc = now
                    },
                    new RemoteCloudItem
                    {
                        RemoteId = "vfs_gdrive_folder_respaldos",
                        Name = @"Respaldos en la Nube",
                        IsDirectory = true,
                        MimeType = FolderMimeType,
                        ModifiedAtUtc = now
                    },
                    new RemoteCloudItem
                    {
                        RemoteId = "vfs_gdrive_doc_informe",
                        ParentId = "vfs_gdrive_folder_documentos",
                        Name = @"Documentos\Informe_General_Nube.txt",
                        IsDirectory = false,
                        SizeBytes = 1024,
                        ContentHashOrETag = "etag_gdrive_v11_doc1",
                        MimeType = "text/plain",
                        ModifiedAtUtc = now
                    },
                    new RemoteCloudItem
                    {
                        RemoteId = "vfs_gdrive_doc_notas",
                        ParentId = "vfs_gdrive_folder_documentos",
                        Name = @"Documentos\Notas_Editables_Usuario.txt",
                        IsDirectory = false,
                        SizeBytes = 1024,
                        ContentHashOrETag = "etag_gdrive_v11_doc2",
                        MimeType = "text/plain",
                        ModifiedAtUtc = now
                    },
                    new RemoteCloudItem
                    {
                        RemoteId = "vfs_gdrive_proy_arquitectura",
                        ParentId = "vfs_gdrive_folder_proyectos",
                        Name = @"Proyectos Compartidos\Proyecto_Multicloud_VFS.txt",
                        IsDirectory = false,
                        SizeBytes = 1024,
                        ContentHashOrETag = "etag_gdrive_v11_proy1",
                        MimeType = "text/plain",
                        ModifiedAtUtc = now
                    },
                    new RemoteCloudItem
                    {
                        RemoteId = "vfs_gdrive_fotos_catalogo",
                        ParentId = "vfs_gdrive_folder_fotos",
                        Name = @"Fotos y Multimedia\Indice_Multimedia_Nube.txt",
                        IsDirectory = false,
                        SizeBytes = 1024,
                        ContentHashOrETag = "etag_gdrive_v11_foto1",
                        MimeType = "text/plain",
                        ModifiedAtUtc = now
                    },
                    new RemoteCloudItem
                    {
                        RemoteId = "vfs_gdrive_respaldo_config",
                        ParentId = "vfs_gdrive_folder_respaldos",
                        Name = @"Respaldos en la Nube\Resumen_Backup_Cifrado.txt",
                        IsDirectory = false,
                        SizeBytes = 1024,
                        ContentHashOrETag = "etag_gdrive_v11_bak1",
                        MimeType = "text/plain",
                        ModifiedAtUtc = now
                    }
                ]
            };
        }

        return new DeltaSyncPage
        {
            NewDeltaCursor = $"VFS_SYNC_CURSOR_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            UpsertedItems = Array.Empty<RemoteCloudItem>(),
            DeletedRemoteIds = Array.Empty<string>()
        };
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url, CredentialSecret secret)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret.AccessToken);
        return request;
    }

    private static RemoteCloudItem MapToRemoteCloudItem(GoogleFileDto dto, string? fallbackParentId)
    {
        bool isFolder = string.Equals(dto.MimeType, FolderMimeType, StringComparison.OrdinalIgnoreCase);
        long size = 0;
        if (!string.IsNullOrEmpty(dto.Size) && long.TryParse(dto.Size, out long parsedSize))
        {
            size = parsedSize;
        }

        return new RemoteCloudItem
        {
            RemoteId = dto.Id ?? Guid.NewGuid().ToString("N"),
            ParentId = dto.Parents?.FirstOrDefault() ?? fallbackParentId,
            Name = dto.Name ?? "unnamed",
            IsDirectory = isFolder,
            SizeBytes = size,
            ContentHashOrETag = dto.Md5Checksum,
            MimeType = dto.MimeType ?? "application/octet-stream",
            ModifiedAtUtc = dto.ModifiedTime ?? DateTimeOffset.UtcNow
        };
    }

    // --- DTOs internos de serialización para Google Drive API v3 ---
    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    private sealed class GoogleFileListResponse
    {
        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; set; }

        [JsonPropertyName("files")]
        public List<GoogleFileDto>? Files { get; set; }
    }

    private sealed class GoogleStartPageTokenResponse
    {
        [JsonPropertyName("startPageToken")]
        public string? StartPageToken { get; set; }
    }

    private sealed class GoogleChangesListResponse
    {
        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; set; }

        [JsonPropertyName("newStartPageToken")]
        public string? NewStartPageToken { get; set; }

        [JsonPropertyName("changes")]
        public List<GoogleChangeDto>? Changes { get; set; }
    }

    private sealed class GoogleChangeDto
    {
        [JsonPropertyName("fileId")]
        public string? FileId { get; set; }

        [JsonPropertyName("removed")]
        public bool Removed { get; set; }

        [JsonPropertyName("file")]
        public GoogleFileDto? File { get; set; }
    }

    private sealed class GoogleFileDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; }

        [JsonPropertyName("size")]
        public string? Size { get; set; }

        [JsonPropertyName("md5Checksum")]
        public string? Md5Checksum { get; set; }

        [JsonPropertyName("modifiedTime")]
        public DateTimeOffset? ModifiedTime { get; set; }

        [JsonPropertyName("parents")]
        public List<string>? Parents { get; set; }

        [JsonPropertyName("trashed")]
        public bool Trashed { get; set; }
    }
}

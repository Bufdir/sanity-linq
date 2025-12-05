// Copy-write 2018 Oslofjord Operations AS

// This file is part of Sanity LINQ (https://github.com/oslofjord/sanity-linq).

//  Sanity LINQ is free software: you can redistribute it and/or modify
//  it under the terms of the MIT License.

//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
//  MIT License for more details.

//  You should have received a copy of the MIT License
//  along with this program.

using Sanity.Linq.CommonTypes;
using Sanity.Linq.DTOs;
using Sanity.Linq.Enums;
using Sanity.Linq.Exceptions;
using Sanity.Linq.Internal;
using Sanity.Linq.JsonConverters;
using Sanity.Linq.Mutations;

namespace Sanity.Linq;

public class SanityClient
{
    protected SanityOptions Options;
    private readonly IHttpClientFactory? _factory;
    private HttpClient _httpClient = null!;
    private HttpClient _httpQueryClient = null!;

    public SanityClient(SanityOptions options, JsonSerializerSettings? serializerSettings = null, IHttpClientFactory? clientFactory = null) : this(options, serializerSettings, serializerSettings, clientFactory)
    { }

    public SanityClient(SanityOptions options, JsonSerializerSettings? serializerSettings, JsonSerializerSettings? deserializerSettings, IHttpClientFactory? clientFactory = null)
    {
        Options = options;
        _factory = clientFactory;

        var defaultSerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Converters = new List<JsonConverter> { new SanityReferenceTypeConverter() }
        };

        SerializerSettings = serializerSettings ?? defaultSerializerSettings;
        DeserializerSettings = deserializerSettings ?? defaultSerializerSettings;

        Initialize();
    }

    public JsonSerializerSettings DeserializerSettings { get; }
    public JsonSerializerSettings SerializerSettings { get; }

    public virtual Task<SanityMutationResponse> CommitMutationsAsync(object mutations, bool returnIds = false, bool returnDocuments = true, SanityMutationVisibility visibility = SanityMutationVisibility.Sync, CancellationToken cancellationToken = default)
    {
        return CommitMutationsInternalAsync<SanityMutationResponse>(mutations, returnIds, returnDocuments, visibility, cancellationToken);
    }

    public virtual Task<SanityMutationResponse<TDoc>> CommitMutationsAsync<TDoc>(object mutations, bool returnIds = false, bool returnDocuments = true, SanityMutationVisibility visibility = SanityMutationVisibility.Sync, CancellationToken cancellationToken = default)
    {
        return CommitMutationsInternalAsync<SanityMutationResponse<TDoc>>(mutations, returnIds, returnDocuments, visibility, cancellationToken);
    }

    public virtual async Task<SanityQueryResponse<TResult>> FetchAsync<TResult>(string query, object? parameters = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(query))
        {
            throw new ArgumentException("Query cannot be empty", nameof(query));
        }
        var oQuery = new SanityQuery
        {
            Query = query,
            Params = parameters
        };
        var json = new StringContent(JsonConvert.SerializeObject(oQuery, Formatting.None, SerializerSettings), Encoding.UTF8, "application/json");
        var response = await _httpQueryClient.PostAsync($"data/query/{WebUtility.UrlEncode(Options.Dataset)}", json, cancellationToken).ConfigureAwait(false);

        return await HandleHttpResponseAsync<SanityQueryResponse<TResult>>(response).ConfigureAwait(false);
    }

    public virtual async Task<SanityDocumentsResponse<TDoc>> GetDocumentAsync<TDoc>(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentException("Id cannot be empty", nameof(id));
        }
        var response = await _httpQueryClient.GetAsync($"data/doc/{WebUtility.UrlEncode(Options.Dataset)}/{WebUtility.UrlEncode(id)}", cancellationToken).ConfigureAwait(false);
        return await HandleHttpResponseAsync<SanityDocumentsResponse<TDoc>>(response).ConfigureAwait(false);
    }

    public void Initialize()
    {
        // Initialize serialization settings

        // Initialize query client
        _httpQueryClient = _factory?.CreateClient() ?? new HttpClient();
        _httpQueryClient.DefaultRequestHeaders.Accept.Clear();
        _httpQueryClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpQueryClient.BaseAddress = Options.UseCdn switch
        {
            true => new Uri($"https://{WebUtility.UrlEncode(Options.ProjectId)}.apicdn.sanity.io/{Options.ApiVersion}/"),
            _ => new Uri($"https://{WebUtility.UrlEncode(Options.ProjectId)}.api.sanity.io/{Options.ApiVersion}/")
        };
        if (!string.IsNullOrEmpty(Options.Token))
        {
            _httpQueryClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Options.Token);
        }

        // Initialize client for non-query requests (i.e. requests than never use CDN)
        if (!Options.UseCdn)
        {
            _httpClient = _httpQueryClient;
        }
        else
        {
            _httpClient = _factory?.CreateClient() ?? new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.BaseAddress = new Uri($"https://{WebUtility.UrlEncode(Options.ProjectId)}.api.sanity.io/{Options.ApiVersion}/");
            if (!string.IsNullOrEmpty(Options.Token))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Options.Token);
            }
        }
    }

    public virtual async Task<SanityDocumentResponse<SanityFileAsset>> UploadFileAsync(FileInfo file, string? label = null, CancellationToken cancellationToken = default)
    {
        var mimeType = MimeTypeMap.GetMimeType(file.Extension);
        await using var fs = file.OpenRead();
        return await UploadFileAsync(fs, file.Name, mimeType, label, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<SanityDocumentResponse<SanityFileAsset>> UploadFileAsync(Stream stream, string fileName, string? contentType = null, string? label = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(fileName))
        {
            query.Add($"filename={WebUtility.UrlEncode(fileName)}");
        }
        if (!string.IsNullOrEmpty(label))
        {
            query.Add($"label={WebUtility.UrlEncode(label)}");
        }
        var uri = $"assets/files/{WebUtility.UrlEncode(Options.Dataset)}{(query.Count > 0 ? "?" + query.Aggregate((c, n) => c + "&" + n) : "")}";
        var request = new HttpRequestMessage(HttpMethod.Post, uri);

        request.Content = new StreamContent(stream);
        if (!string.IsNullOrEmpty(contentType))
        {
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await HandleHttpResponseAsync<SanityDocumentResponse<SanityFileAsset>>(response).ConfigureAwait(false);
    }

    public virtual async Task<SanityDocumentResponse<SanityImageAsset>> UploadImageAsync(FileInfo image, string? label = null, CancellationToken cancellationToken = default)
    {
        var mimeType = MimeTypeMap.GetMimeType(image.Extension);
        await using var fs = image.OpenRead();
        return await UploadImageAsync(fs, image.Name, mimeType, label, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<SanityDocumentResponse<SanityImageAsset>> UploadImageAsync(Stream stream, string fileName, string? contentType = null, string? label = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(fileName))
        {
            query.Add($"filename={WebUtility.UrlEncode(fileName)}");
        }
        if (!string.IsNullOrEmpty(label))
        {
            query.Add($"label={WebUtility.UrlEncode(label)}");
        }
        var uri = $"assets/images/{WebUtility.UrlEncode(Options.Dataset)}{(query.Count > 0 ? "?" + query.Aggregate((c, n) => c + "&" + n) : "")}";
        var request = new HttpRequestMessage(HttpMethod.Post, uri);

        request.Content = new StreamContent(stream);
        if (!string.IsNullOrEmpty(contentType))
        {
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await HandleHttpResponseAsync<SanityDocumentResponse<SanityImageAsset>>(response).ConfigureAwait(false);
    }

    protected virtual async Task<TResult> CommitMutationsInternalAsync<TResult>(object mutations, bool returnIds = false, bool returnDocuments = false, SanityMutationVisibility visibility = SanityMutationVisibility.Sync, CancellationToken cancellationToken = default)
    {
        if (mutations == null)
        {
            throw new ArgumentNullException(nameof(mutations));
        }

        var json = mutations switch
        {
            string s => s,
            SanityMutationBuilder builder => builder.Build(SerializerSettings),
            _ => JsonConvert.SerializeObject(mutations, Formatting.None, SerializerSettings)
        };

        var response = await _httpClient.PostAsync($"data/mutate/{WebUtility.UrlEncode(Options.Dataset)}?returnIds={returnIds.ToString().ToLower()}&returnDocuments={returnDocuments.ToString().ToLower()}&visibility={visibility.ToString().ToLower()}", new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken).ConfigureAwait(false);
        return await HandleHttpResponseAsync<TResult>(response).ConfigureAwait(false);
    }

    protected virtual async Task<TResponse> HandleHttpResponseAsync<TResponse>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var requestUri = response.RequestMessage?.RequestUri;
        if (response.IsSuccessStatusCode)
        {
            try
            {
                var obj = JsonConvert.DeserializeObject<TResponse>(content, DeserializerSettings);
                if (obj != null)
                {
                    return obj;
                }

                var preview = Truncate(content, 2048);
                throw new SanityDeserializationException("Failed to deserialize Sanity response: deserialized to null", preview, requestUri);
            }
            catch (SanityDeserializationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var preview = Truncate(content, 2048);
                throw new SanityDeserializationException("Failed to deserialize Sanity response", preview, requestUri, ex);
            }
        }

        var httpEx = new SanityHttpException($"Sanity request failed with HTTP status {response.StatusCode}: {response.ReasonPhrase ?? ""}")
        {
            Content = content,
            StatusCode = response.StatusCode
        };
        throw httpEx;
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (value == null)
        {
            return string.Empty;
        }
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
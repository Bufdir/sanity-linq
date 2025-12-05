// Copy-write 2018 Oslofjord Operations AS

// This file is part of Sanity LINQ (https://github.com/oslofjord/sanity-linq).

//  Sanity LINQ is free software: you can redistribute it and/or modify
//  it under the terms of the MIT Licence.

//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
//  MIT Licence for more details.

//  You should have received a copy of the MIT Licence
//  along with this program.

using Sanity.Linq.CommonTypes;
using Sanity.Linq.DTOs;
using Sanity.Linq.Enums;
using Sanity.Linq.Internal;
using Sanity.Linq.Mutations;

namespace Sanity.Linq;

public static class SanityClientExtensions
{
    private const int DefaultTimeoutSeconds = 100;
    private const long DefaultMaxDownloadBytes = 50L * 1024L * 1024L; // 50 MB

    extension(SanityClient client)
    {
        public async Task<SanityDocumentResponse<SanityImageAsset>> UploadImageAsync(Uri imageUrl, string? label = null, CancellationToken cancellationToken = default)
            => await client.UploadImageAsync(imageUrl, null, null, null, label, cancellationToken).ConfigureAwait(false);

        public async Task<SanityDocumentResponse<SanityImageAsset>> UploadImageAsync(
            Uri imageUrl,
            HttpClient? httpClient,
            TimeSpan? timeout,
            long? maxDownloadBytes,
            string? label = null,
            CancellationToken cancellationToken = default)
        {
            if (imageUrl == null)
            {
                throw new ArgumentNullException(nameof(imageUrl));
            }
            //Default to JPG
            var mimeType = MimeTypeMap.GetMimeType(".jpg");
            var fileName = imageUrl.PathAndQuery.Split('?')[0].Split('#')[0].Split('/').Last();
            var extension = fileName.Split('.').Last();
            if (extension != fileName)
            {
                mimeType = MimeTypeMap.GetMimeType(extension);
            }

            var maxBytes = maxDownloadBytes ?? DefaultMaxDownloadBytes;
            var disposeClient = httpClient == null;
            httpClient ??= new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(DefaultTimeoutSeconds) };
            try
            {
                using var response = await httpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > maxBytes)
                {
                    throw new InvalidOperationException($"Remote content length {contentLength.Value} exceeds allowed maximum of {maxBytes} bytes");
                }

                await using var networkStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var limitedStream = new MemoryStream();
                var buffer = new byte[81_920];
                long total = 0;
                int read;
                while ((read = await networkStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new InvalidOperationException($"Downloaded content exceeded allowed maximum of {maxBytes} bytes");
                    }
                    await limitedStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                limitedStream.Position = 0;

                return await client.UploadImageAsync(limitedStream, fileName, mimeType, label ?? "Source:" + imageUrl.OriginalString, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (disposeClient)
                {
                    httpClient.Dispose();
                }
            }
        }

        public async Task<SanityDocumentResponse<SanityFileAsset>> UploadFileAsync(Uri fileUrl, string? label = null, CancellationToken cancellationToken = default)
            => await client.UploadFileAsync(fileUrl, null, null, null, label, cancellationToken).ConfigureAwait(false);

        public async Task<SanityDocumentResponse<SanityFileAsset>> UploadFileAsync(
            Uri fileUrl,
            HttpClient? httpClient,
            TimeSpan? timeout,
            long? maxDownloadBytes,
            string? label = null,
            CancellationToken cancellationToken = default)
        {
            if (fileUrl == null)
            {
                throw new ArgumentNullException(nameof(fileUrl));
            }

            var mimeType = "application/octet-stream";
            var fileName = fileUrl.PathAndQuery.Split('?')[0].Split('#')[0].Split('/').Last();
            var extension = fileName.Split('.').Last();
            if (extension != fileName)
            {
                mimeType = MimeTypeMap.GetMimeType(extension);
            }

            var maxBytes = maxDownloadBytes ?? DefaultMaxDownloadBytes;
            var disposeClient = httpClient == null;
            httpClient ??= new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(DefaultTimeoutSeconds) };
            try
            {
                using var response = await httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > maxBytes)
                {
                    throw new InvalidOperationException($"Remote content length {contentLength.Value} exceeds allowed maximum of {maxBytes} bytes");
                }

                await using var networkStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var limitedStream = new MemoryStream();
                var buffer = new byte[81_920];
                long total = 0;
                int read;
                while ((read = await networkStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new InvalidOperationException($"Downloaded content exceeded allowed maximum of {maxBytes} bytes");
                    }
                    await limitedStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                limitedStream.Position = 0;

                return await client.UploadFileAsync(limitedStream, fileName, mimeType, label ?? "Source:" + fileUrl.OriginalString, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (disposeClient)
                {
                    httpClient.Dispose();
                }
            }
        }

        public Task<SanityMutationResponse<TDoc>> CreateAsync<TDoc>(TDoc document, CancellationToken cancellationToken = default)
        {
            return client.BeginTransaction<TDoc>().Create(document).CommitAsync(false, true, SanityMutationVisibility.Sync, cancellationToken);
        }

        public Task<SanityMutationResponse<TDoc>> SetAsync<TDoc>(TDoc document, CancellationToken cancellationToken = default)
        {
            return client.BeginTransaction<TDoc>().SetValues(document).CommitAsync(false, true, SanityMutationVisibility.Sync, cancellationToken);
        }

        public Task<SanityMutationResponse> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            return client.BeginTransaction().DeleteById(id).CommitAsync(false, false, SanityMutationVisibility.Sync, cancellationToken);
        }

        public SanityMutationBuilder BeginTransaction()
        {
            return new SanityMutationBuilder(client);
        }

        public SanityMutationBuilder<TDoc> BeginTransaction<TDoc>()
        {
            return new SanityMutationBuilder(client).For<TDoc>();
        }
    }

    public static Task<SanityMutationResponse> CommitAsync(this SanityMutationBuilder transactionBuilder, bool returnIds = false, bool returnDocuments = true, SanityMutationVisibility visibility = SanityMutationVisibility.Sync, CancellationToken cancellationToken = default)
    {
        var result = transactionBuilder.Client.CommitMutationsAsync(transactionBuilder.Build(transactionBuilder.Client.SerializerSettings), returnIds, returnDocuments, visibility, cancellationToken);
        transactionBuilder.Clear();
        return result;
    }

    public static Task<SanityMutationResponse<TDoc>> CommitAsync<TDoc>(this SanityMutationBuilder<TDoc> transactionBuilder, bool returnIds = false, bool returnDocuments = true, SanityMutationVisibility visibility = SanityMutationVisibility.Sync, CancellationToken cancellationToken = default)
    {
        var result = transactionBuilder.InnerBuilder.Client.CommitMutationsAsync<TDoc>(transactionBuilder.Build(), returnIds, returnDocuments, visibility, cancellationToken);
        transactionBuilder.Clear();
        return result;
    }
}
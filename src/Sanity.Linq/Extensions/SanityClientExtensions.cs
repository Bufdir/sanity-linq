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
    private static readonly HttpClient HttpClient = new();

    extension(SanityClient client)
    {
        public async Task<SanityDocumentResponse<SanityImageAsset>> UploadImageAsync(Uri imageUrl, string? label = null, CancellationToken cancellationToken = default)
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

            using var response = await HttpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var fs = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var result = await client.UploadImageAsync(fs, fileName, mimeType, label ?? "Source:" + imageUrl.OriginalString, cancellationToken).ConfigureAwait(false);
            fs.Close();
            return result;
        }

        public async Task<SanityDocumentResponse<SanityFileAsset>> UploadFileAsync(Uri fileUrl, string? label = null, CancellationToken cancellationToken = default)
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

            using var response = await HttpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var fs = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var result = await client.UploadFileAsync(fs, fileName, mimeType, label ?? "Source:" + fileUrl.OriginalString, cancellationToken).ConfigureAwait(false);
            fs.Close();
            return result;
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
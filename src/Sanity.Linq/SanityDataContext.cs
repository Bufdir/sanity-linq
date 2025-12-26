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

using Microsoft.Extensions.Logging;
using Sanity.Linq.BlockContent;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.DTOs;
using Sanity.Linq.Enums;
using Sanity.Linq.JsonConverters;
using Sanity.Linq.Mutations;

namespace Sanity.Linq;

/// <summary>
///     Linq-to-Sanity Data Context.
///     Handles initialization of SanityDbSets defined in inherited classes.
/// </summary>
public class SanityDataContext
{
    private readonly ConcurrentDictionary<string, SanityDocumentSet> _documentSets = new();
    private readonly object _dsLock = new();

    /// <summary>
    ///     Create a new SanityDbContext using the specified options.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="serializerSettings"></param>
    /// <param name="htmlBuilderOptions"></param>
    /// <param name="clientFactory"></param>
    public SanityDataContext(SanityOptions options, JsonSerializerSettings? serializerSettings = null, SanityHtmlBuilderOptions? htmlBuilderOptions = null, IHttpClientFactory? clientFactory = null, ILogger? logger = null) : this(options, serializerSettings, serializerSettings, htmlBuilderOptions, clientFactory, logger)
    {
    }

    /// <summary>
    ///     Create a new SanityDbContext using the explicitly specified JsonSerializerSettings.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="serializerSettings"></param>
    /// <param name="deserializerSettings"></param>
    /// <param name="htmlBuilderOptions"></param>
    /// <param name="clientFactory"></param>
    public SanityDataContext(SanityOptions options, JsonSerializerSettings? serializerSettings, JsonSerializerSettings? deserializerSettings, SanityHtmlBuilderOptions? htmlBuilderOptions = null, IHttpClientFactory? clientFactory = null, ILogger? logger = null)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        var defaultSerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Converters = new List<JsonConverter> { new SanityReferenceTypeConverter() }
        };

        SerializerSettings = serializerSettings ?? defaultSerializerSettings;
        DeserializerSettings = deserializerSettings ?? defaultSerializerSettings;
        Client = new SanityClient(options, SerializerSettings, DeserializerSettings, clientFactory, logger);
        Mutations = new SanityMutationBuilder(Client);
        HtmlBuilder = new SanityHtmlBuilder(options, null, SerializerSettings, htmlBuilderOptions);
    }

    /// <summary>
    ///     Create a new SanityDbContext using the specified options.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="isShared">Indicates that the context can be used by multiple SanityDocumentSets</param>
    internal SanityDataContext(SanityOptions options, bool isShared) : this(options, null, null, null, clientFactory: null)
    {
        IsShared = isShared;
    }

    public SanityClient Client { get; }
    public JsonSerializerSettings DeserializerSettings { get; }
    public virtual SanityDocumentSet<SanityDocument> Documents => DocumentSet<SanityDocument>(2);
    public virtual SanityDocumentSet<SanityFileAsset> Files => DocumentSet<SanityFileAsset>(2);
    public SanityHtmlBuilder HtmlBuilder { get; set; }
    public virtual SanityDocumentSet<SanityImageAsset> Images => DocumentSet<SanityImageAsset>(2);
    public SanityMutationBuilder Mutations { get; }
    public JsonSerializerSettings SerializerSettings { get; }
    internal bool IsShared { get; }

    public virtual void ClearChanges()
    {
        Mutations.Clear();
    }

    /// <summary>
    ///     Sends all changes registered on Document sets to Sanity as a transactional set of mutations.
    /// </summary>
    /// <param name="returnIds"></param>
    /// <param name="returnDocuments"></param>
    /// <param name="visibility"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<SanityMutationResponse> CommitAsync(bool returnIds = false, bool returnDocuments = false, SanityMutationVisibility visibility = SanityMutationVisibility.Sync, CancellationToken cancellationToken = default)
    {
        var result = await Client.CommitMutationsAsync(Mutations.Build(Client.SerializerSettings), returnIds, returnDocuments, visibility, cancellationToken).ConfigureAwait(false);
        Mutations.Clear();
        return result;
    }

    /// <summary>
    ///     Sends all changes registered on document sets of specified type to Sanity as a transactional set of mutations.
    /// </summary>
    /// <param name="returnIds"></param>
    /// <param name="returnDocuments"></param>
    /// <param name="visibility"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<SanityMutationResponse<TDoc>> CommitAsync<TDoc>(bool returnIds = false, bool returnDocuments = false, SanityMutationVisibility visibility = SanityMutationVisibility.Sync, CancellationToken cancellationToken = default)
    {
        var mutations = Mutations.For<TDoc>();
        if (mutations.Mutations.Count > 0)
        {
            var result = await Client.CommitMutationsAsync<TDoc>(mutations.Build(), returnIds, returnDocuments, visibility, cancellationToken).ConfigureAwait(false);
            mutations.Clear();
            return result;
        }

        throw new Exception($"No pending changes for document type {typeof(TDoc)}");
    }

    /// <summary>
    ///     Returns an IQueryable document set for specified type
    /// </summary>
    /// <typeparam name="TDoc"></typeparam>
    /// <returns></returns>
    public virtual SanityDocumentSet<TDoc> DocumentSet<TDoc>(int maxNestingLevel = 7)
    {
        var key = $"{typeof(TDoc).FullName ?? ""}_{maxNestingLevel}";
        lock (_dsLock)
        {
            if (!_documentSets.ContainsKey(key)) _documentSets[key] = new SanityDocumentSet<TDoc>(this, maxNestingLevel);
        }

        lock (_dsLock)
        {
            return (_documentSets[key] as SanityDocumentSet<TDoc>)!;
        }
    }
}
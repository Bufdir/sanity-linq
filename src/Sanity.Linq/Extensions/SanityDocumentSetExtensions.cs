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
using Sanity.Linq.Mutations;
using Sanity.Linq.Mutations.Model;
using Sanity.Linq.QueryProvider;

namespace Sanity.Linq;

public static class SanityDocumentSetExtensions
{
    /// <param name="source"></param>
    /// <typeparam name="T"></typeparam>
    extension<T>(IQueryable<T> source)
    {
        /// <summary>
        /// Generates a Sanity GROQ query string based on the LINQ expression associated with the source.
        /// </summary>
        /// <typeparam name="T">
        /// The type of the elements in the source queryable.
        /// </typeparam>
        /// <returns>
        /// A <see cref="string"/> containing the Sanity GROQ query.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the <paramref name="source"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="Exception">
        /// Thrown when the <paramref name="source"/> is not a <see cref="SanityDocumentSet{T}"/>.
        /// </exception>
        public string GetSanityQuery()
        {
            return source switch
            {
                null => throw new ArgumentNullException(nameof(source)),
                SanityDocumentSet<T> { Provider: SanityQueryProvider provider } => provider.GetSanityQuery<T>(
                    source.Expression),
                _ => throw new Exception("Queryable source must be a SanityDbSet<T>.")
            };
        }

        /// <summary>
        /// Asynchronously converts the elements of the source queryable to a <see cref="List{T}"/>.
        /// </summary>
        /// <typeparam name="T">
        /// The type of the elements in the source queryable.
        /// </typeparam>
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> to observe while waiting for the task to complete. Defaults to <see cref="CancellationToken.None"/>.
        /// </param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains a <see cref="List{T}"/> of elements from the source queryable.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the source queryable is <c>null</c>.
        /// </exception>
        /// <exception cref="Exception">
        /// Thrown when the source queryable is not a <see cref="SanityDocumentSet{T}"/>.
        /// </exception>
        public async Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
        {
            return source switch
            {
                null => throw new ArgumentNullException(nameof(source)),
                SanityDocumentSet<T> dbSet => (await dbSet.ExecuteAsync(cancellationToken).ConfigureAwait(false))
                    .ToList(),
                _ => throw new Exception("Queryable source must be a SanityDbSet<T>.")
            };
        }

        /// <summary>
        /// Asynchronously converts the elements of the source queryable to an array.
        /// </summary>
        /// <typeparam name="T">
        /// The type of the elements in the source queryable.
        /// </typeparam>
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> to observe while waiting for the task to complete. Defaults to <see cref="CancellationToken.None"/>.
        /// </param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains an array of elements from the source queryable.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the source queryable is <c>null</c>.
        /// </exception>
        /// <exception cref="Exception">
        /// Thrown when the source queryable is not a <see cref="SanityDocumentSet{T}"/>.
        /// </exception>
        public async Task<T[]> ToArrayAsync(CancellationToken cancellationToken = default)
        {
            return source switch
            {
                null => throw new ArgumentNullException(nameof(source)),
                SanityDocumentSet<T> dbSet => (await dbSet.ExecuteAsync(cancellationToken).ConfigureAwait(false))
                    .ToArray(),
                _ => throw new Exception("Queryable source must be a SanityDbSet<T>.")
            };
        }

        /// <summary>
        /// Asynchronously returns the first element of the queryable source or a default value if the source contains no elements.
        /// </summary>
        /// <typeparam name="T">
        /// The type of the elements in the queryable source.
        /// </typeparam>
        /// <param name="source">
        /// The queryable source from which to retrieve the first element or default value.
        /// </param>
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> to observe while waiting for the task to complete.
        /// </param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains the first element of the sequence,
        /// or <c>null</c> if the source is empty.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the <paramref name="source"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="Exception">
        /// Thrown when the <paramref name="source"/> is not a <see cref="SanityDocumentSet{T}"/>.
        /// </exception>
        public async Task<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            return source switch
            {
                null => throw new ArgumentNullException(nameof(source)),
                SanityDocumentSet<T> dbSet => (await dbSet.Take(1).ExecuteSingleAsync(cancellationToken).ConfigureAwait(false)),
                _ => throw new Exception("Queryable source must be a SanityDbSet<T>.")
            };
        }

        public async Task<IEnumerable<T>> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            return source switch
            {
                null => throw new ArgumentNullException(nameof(source)),
                SanityDocumentSet<T> dbSet => await dbSet.ExecuteAsync(cancellationToken).ConfigureAwait(false),
                _ => throw new Exception("Queryable source must be a SanityDbSet<T>.")
            };
        }

        public async Task<T?> ExecuteSingleAsync(CancellationToken cancellationToken = default)
        {
            return source switch
            {
                null => throw new ArgumentNullException(nameof(source)),
                SanityDocumentSet<T> dbSet => await dbSet.ExecuteSingleAsync(cancellationToken).ConfigureAwait(false),
                _ => throw new Exception("Queryable source must be a SanityDbSet<T>.")
            };
        }

        public async Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            return source switch
            {
                null => throw new ArgumentNullException(nameof(source)),
                SanityDocumentSet<T> dbSet => (await dbSet.ExecuteCountAsync(cancellationToken).ConfigureAwait(false)),
                _ => throw new Exception("Queryable source must be a SanityDbSet<T>.")
            };
        }

        public async Task<long> LongCountAsync(CancellationToken cancellationToken = default)
        {
            return source switch
            {
                null => throw new ArgumentNullException(nameof(source)),
                SanityDocumentSet<T> dbSet => (await dbSet.ExecuteLongCountAsync(cancellationToken)
                    .ConfigureAwait(false)),
                _ => throw new Exception("Queryable source must be a SanityDbSet<T>.")
            };
        }

        public IQueryable<T> Include<TProperty>(Expression<Func<T, TProperty>> property)
        {
            switch (source)
            {
                case null:
                    throw new ArgumentNullException(nameof(source));
                case SanityDocumentSet<T> dbSet:
                    dbSet.Include(property);
                    return dbSet;

                default:
                    throw new Exception("Queryable source must be a SanityDbSet<T>.");
            }
        }

        public IQueryable<T> Include<TProperty>(Expression<Func<T, TProperty>> property, string sourceName)
        {
            switch (source)
            {
                case null:
                    throw new ArgumentNullException(nameof(source));
                case SanityDocumentSet<T> dbSet:
                    dbSet.Include(property, sourceName);
                    return dbSet;

                default:
                    throw new Exception("Queryable source must be a SanityDbSet<T>.");
            }
        }

        public SanityMutationBuilder<T> Patch(Action<SanityPatch> patch)
        {
            return source switch
            {
                null => throw new ArgumentNullException(nameof(source)),
                SanityDocumentSet<T> dbSet => dbSet.Mutations.PatchByQuery(dbSet.Expression, patch),
                _ => throw new Exception("Queryable source must be a SanityDbSet<T>.")
            };
        }

        public SanityMutationBuilder<T> Delete()
        {
            return source switch
            {
                null => throw new ArgumentNullException(nameof(source)),
                SanityDocumentSet<T> dbSet => dbSet.Mutations.DeleteByQuery(dbSet.Expression),
                _ => throw new Exception("Queryable source must be a SanityDbSet<T>.")
            };
        }
    }

    /// <param name="docs"></param>
    /// <typeparam name="TDoc"></typeparam>
    extension<TDoc>(SanityDocumentSet<TDoc> docs)
    {
        public SanityMutationBuilder<TDoc> Create(TDoc document)
        {
            return docs.Mutations.Create(document);
        }

        public SanityMutationBuilder<TDoc> CreateIfNotExists(TDoc document)
        {
            return docs.Mutations.CreateIfNotExists(document);
        }

        /// <summary>
        /// Sets only non-null values.
        /// </summary>
        /// <param name="document"></param>
        /// <returns></returns>
        public SanityMutationBuilder<TDoc> SetValues(TDoc document)
        {
            return docs.Mutations.SetValues(document);
        }

        public SanityMutationBuilder<TDoc> Update(TDoc document)
        {
            return docs.Mutations.Update(document);
        }

        public SanityMutationBuilder<TDoc> DeleteById(string id)
        {
            return docs.Mutations.DeleteById(id);
        }

        public SanityMutationBuilder<TDoc> DeleteByQuery(Expression<Func<TDoc, bool>> query)
        {
            return docs.Mutations.DeleteByQuery(query);
        }

        public SanityMutationBuilder<TDoc> PatchById(SanityPatchById<TDoc> patch)
        {
            return docs.Mutations.PatchById(patch);
        }

        public SanityMutationBuilder<TDoc> PatchById(string id, Action<SanityPatch> patch)
        {
            return docs.Mutations.PatchById(id, patch);
        }

        public SanityMutationBuilder<TDoc> PatchByQuery(SanityPatchByQuery<TDoc> patch)
        {
            return docs.Mutations.PatchByQuery(patch);
        }

        public SanityMutationBuilder<TDoc> PatchByQuery(Expression<Func<TDoc, bool>> query, Action<SanityPatch> patch)
        {
            return docs.Mutations.PatchByQuery(query, patch);
        }

        public void ClearChanges()
        {
            docs.Mutations.Clear();
        }

        public Task<SanityMutationResponse<TDoc>> CommitChangesAsync(bool returnIds = false, bool returnDocuments = false, SanityMutationVisibility visibility = SanityMutationVisibility.Sync, CancellationToken cancellationToken = default)
        {
            return docs.Context.CommitAsync<TDoc>(returnIds, returnDocuments, visibility, cancellationToken);
        }
    }

    extension(SanityDocumentSet<SanityImageAsset> images)
    {
        public Task<SanityDocumentResponse<SanityImageAsset>> UploadAsync(Stream stream, string filename, string? contentType = null, string? label = null, CancellationToken cancellationToken = default)
        {
            return images.Context.Client.UploadImageAsync(stream, filename, contentType, label, cancellationToken);
        }

        public Task<SanityDocumentResponse<SanityImageAsset>> UploadAsync(FileInfo file, string filename, string? contentType = null, string? label = null, CancellationToken cancellationToken = default)
        {
            return images.Context.Client.UploadImageAsync(file, label, cancellationToken);
        }

        public Task<SanityDocumentResponse<SanityImageAsset>> UploadAsync(Uri uri, string? label = null, CancellationToken cancellationToken = default)
        {
            return images.Context.Client.UploadImageAsync(uri, label, cancellationToken);
        }
    }

    extension(SanityDocumentSet<SanityFileAsset> images)
    {
        public Task<SanityDocumentResponse<SanityFileAsset>> UploadAsync(Stream stream, string filename, string? contentType = null, string? label = null, CancellationToken cancellationToken = default)
        {
            return images.Context.Client.UploadFileAsync(stream, filename, contentType, label, cancellationToken);
        }

        public Task<SanityDocumentResponse<SanityFileAsset>> UploadAsync(FileInfo file, string filename, string? contentType = null, string? label = null, CancellationToken cancellationToken = default)
        {
            return images.Context.Client.UploadFileAsync(file, label, cancellationToken);
        }

        public Task<SanityDocumentResponse<SanityFileAsset>> UploadAsync(Uri uri, string? label = null, CancellationToken cancellationToken = default)
        {
            return images.Context.Client.UploadFileAsync(uri, label, cancellationToken);
        }
    }
}
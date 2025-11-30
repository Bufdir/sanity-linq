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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
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
        /// Returns Sanity GROQ query for the expression. 
        /// </summary>
        /// <returns></returns>
        public string GetSanityQuery()
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source is SanityDocumentSet<T> { Provider: SanityQueryProvider provider })
            {
                return provider.GetSanityQuery<T>(source.Expression);
            }

            throw new Exception("Queryable source must be a SanityDbSet<T>.");
        }

        public async Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source is SanityDocumentSet<T> dbSet)
            {
                return (await dbSet.ExecuteAsync(cancellationToken).ConfigureAwait(false)).ToList();
            }

            throw new Exception("Queryable source must be a SanityDbSet<T>.");
        }

        public async Task<T[]> ToArrayAsync(CancellationToken cancellationToken = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source is SanityDocumentSet<T> dbSet)
            {
                return (await dbSet.ExecuteAsync(cancellationToken).ConfigureAwait(false)).ToArray();
            }

            throw new Exception("Queryable source must be a SanityDbSet<T>.");
        }

        public async Task<T> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source is SanityDocumentSet<T> dbSet)
            {
                return (await dbSet.Take(1).ExecuteSingleAsync(cancellationToken).ConfigureAwait(false));
            }

            throw new Exception("Queryable source must be a SanityDbSet<T>.");
        }

        public async Task<IEnumerable<T>> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source is SanityDocumentSet<T> dbSet)
            {
                return await dbSet.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }

            throw new Exception("Queryable source must be a SanityDbSet<T>.");
        }

        public async Task<T> ExecuteSingleAsync(CancellationToken cancellationToken = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source is SanityDocumentSet<T> dbSet)
            {
                return await dbSet.ExecuteSingleAsync(cancellationToken).ConfigureAwait(false);
            }

            throw new Exception("Queryable source must be a SanityDbSet<T>.");
        }

        public async Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source is SanityDocumentSet<T> dbSet)
            {
                return (await dbSet.ExecuteCountAsync(cancellationToken).ConfigureAwait(false));
            }

            throw new Exception("Queryable source must be a SanityDbSet<T>.");
        }

        public async Task<long> LongCountAsync(CancellationToken cancellationToken = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source is SanityDocumentSet<T> dbSet)
            {
                return (await dbSet.ExecuteLongCountAsync(cancellationToken).ConfigureAwait(false));
            }

            throw new Exception("Queryable source must be a SanityDbSet<T>.");
        }

        public IQueryable<T> Include<TProperty>(Expression<Func<T, TProperty>> property)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source is SanityDocumentSet<T> dbSet)
            {
                dbSet.Include(property);
                return dbSet;
            }

            throw new Exception("Queryable source must be a SanityDbSet<T>.");
        }

        public IQueryable<T> Include<TProperty>(Expression<Func<T, TProperty>> property, string sourceName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source is SanityDocumentSet<T> dbSet)
            {
                dbSet.Include(property, sourceName);
                return dbSet;
            }

            throw new Exception("Queryable source must be a SanityDbSet<T>.");
        }

        public SanityMutationBuilder<T> Patch(Action<SanityPatch> patch)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source is SanityDocumentSet<T> dbSet)
            {
                return dbSet.Mutations.PatchByQuery(dbSet.Expression, patch);
            }

            throw new Exception("Queryable source must be a SanityDbSet<T>.");
        }

        public SanityMutationBuilder<T> Delete()
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source is SanityDocumentSet<T> dbSet)
            {
                return dbSet.Mutations.DeleteByQuery(dbSet.Expression);
            }

            throw new Exception("Queryable source must be a SanityDbSet<T>.");
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

        public SanityMutationBuilder<TDoc> DeleteByQuery(Expression<Func<TDoc,bool>> query)
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

    //public static SanityTransactionBuilder<TDoc> PatchById<TDoc>(this SanityDocumentSet<TDoc> docs, string id, object patch)
    //{
    //    return docs.Mutations.PatchById(id, patch);
    //}

    //public static SanityTransactionBuilder<TDoc> PatchByQuery<TDoc>(this SanityDocumentSet<TDoc> docs, Expression<Func<TDoc,bool>> query, object patch)
    //{
    //    return docs.Mutations.PatchByQuery(query, patch);
    //}


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
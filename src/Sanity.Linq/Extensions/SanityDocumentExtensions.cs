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
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Sanity.Linq.BlockContent;

namespace Sanity.Linq;
#nullable disable
public static class SanityDocumentExtensions
{
    /// <param name="document"></param>
    extension(object document)
    {
        /// <summary>
        /// Determines if object is a Sanity draft document by inspecting the Id field.
        /// </summary>
        /// <returns></returns>
        public bool IsDraft()
        {
            if (document == null) return false;
            var id = document.SanityId();
            if (id == null) return false;
            return id.StartsWith("drafts.");
        }

        public bool IsDefined()
        {
            return document != null;
        }

        /// <summary>
        /// Returns Type of a document using reflection to find a field which serializes to "_id"
        /// </summary>
        /// <returns></returns>
        public string SanityId()
        {
            if (document == null) return null;

            // Return Id using reflection (based on conventions)
            var idProperty = document.GetType().GetIdProperty();
            if (idProperty != null)
            {
                return idProperty.GetValue(document)?.ToString();
            }

            // ID not found
            return null;
        }

        public void SetSanityId(string value)
        {
            if (document == null) return;

            // Return Id using reflection (based on conventions)
            var idProperty = document.GetType().GetIdProperty();
            if (idProperty != null)
            {
                idProperty.SetValue(document, value);
            }
        }

        /// <summary>
        /// Returns Type of a document using reflection to find a field which serializes to "_type"
        /// </summary>
        /// <returns></returns>
        public string SanityType()
        {
            if (document == null) return null;

            // Return type using reflection (based on conventions)
            var docTypeProperty = document.GetType().GetTypeProperty();
            if (docTypeProperty != null)
            {
                return docTypeProperty.GetValue(document)?.ToString();
            }
            return null;
        }

        /// <summary>
        /// Returns Type of a document using reflection to find a field which serializes to "_rev"
        /// </summary>
        /// <returns></returns>
        public string SanityRevision()
        {
            if (document == null) return null;

            // Return type using reflection (based on conventions)
            var revisionProperty = document.GetType().GetRevisionProperty();
            if (revisionProperty != null)
            {
                return revisionProperty.GetValue(document)?.ToString();
            }
            return null;
        }

        public DateTimeOffset? SanityCreatedAt()
        {
            if (document == null) return null;

            // Return type using reflection (based on conventions)
            var revisionProperty = document.GetType().GetCreatedAtProperty();
            if (revisionProperty != null)
            {
                var val = revisionProperty.GetValue(document);
                return Convert.ChangeType(val, typeof(DateTimeOffset)) as DateTimeOffset?;
            }
            return null;
        }

        public DateTimeOffset? SanityUpdatedAt()
        {
            if (document == null) return null;

            // Return type using reflection (based on conventions)
            var revisionProperty = document.GetType().GetUpdatedAtProperty();
            if (revisionProperty != null)
            {
                var val = revisionProperty.GetValue(document);
                return Convert.ChangeType(val, typeof(DateTimeOffset)) as DateTimeOffset?;
            }
            return null;
        }

        /// <summary>
        /// Indicates that documents has a Sanity _type field
        /// </summary>
        /// <returns></returns>
        internal bool HasDocumentTypeProperty()
        {
            return document?.GetType()?.GetTypeProperty() != null;
        }

        /// <summary>
        /// Indicates that documents has a Sanity _id field
        /// </summary>
        /// <returns></returns>
        internal bool HasIdProperty()
        {
            return document?.GetType()?.GetIdProperty() != null;
        }

        /// <summary>
        /// Indicates that documents has a Sanity _id field
        /// </summary>
        /// <returns></returns>
        internal bool HasRevisionProperty()
        {
            return document?.GetType()?.GetRevisionProperty() != null;
        }

        public object GetValue(string fieldName)
        {
            var prop = document.GetType().GetProperty(fieldName);
            if (prop != null && prop.CanRead)
            {
                return prop.GetValue(document);
            }
            var field = document.GetType().GetField(fieldName);
            if (field != null && field.IsPublic)
            {
                return field.GetValue(document);
            }
            return null;
        }

        public T GetValue<T>(string fieldName)
        {
            var val = GetValue(document, fieldName);
            if (val != null)
            {
                var converted = Convert.ChangeType(val, typeof(T));
                if (converted != null)
                {
                    return (T)converted;
                }
            }
            return default(T);
        }
    }


    private static ConcurrentDictionary<Type, PropertyInfo> _idPropertyCache = new();
    private static PropertyInfo GetIdProperty(this Type type)
    {
        if (!_idPropertyCache.ContainsKey(type))
        {
            var props = type.GetProperties();
            var idProperty = props.FirstOrDefault(p => p.Name.Equals("_id", StringComparison.InvariantCultureIgnoreCase) ||
                                                       (p.GetCustomAttribute<JsonPropertyAttribute>(true) != null && p.GetCustomAttribute<JsonPropertyAttribute>(true).PropertyName == "_id"));
            _idPropertyCache[type] = idProperty;
        }
        return _idPropertyCache[type];
    }


    private static ConcurrentDictionary<Type, PropertyInfo> _typePropertyCache = new();
    private static PropertyInfo GetTypeProperty(this Type type)
    {
        if (!_typePropertyCache.ContainsKey(type))
        {
            var props = type.GetProperties();
            var typeProperty = props.FirstOrDefault(p => p.Name.Equals("_type", StringComparison.InvariantCultureIgnoreCase) ||
                                                         (p.GetCustomAttribute<JsonPropertyAttribute>(true) != null && p.GetCustomAttribute<JsonPropertyAttribute>(true).PropertyName == "_type"));

            _typePropertyCache[type] = typeProperty;
        }
        return _typePropertyCache[type];
    }


    private static ConcurrentDictionary<Type, PropertyInfo> _revPropertyCache = new();
    private static PropertyInfo GetRevisionProperty(this Type type)
    {
        if (!_revPropertyCache.ContainsKey(type))
        {
            var props = type.GetProperties();
            var revProperty = props.FirstOrDefault(p => p.Name.Equals("_rev", StringComparison.InvariantCultureIgnoreCase) ||
                                                        (p.GetCustomAttribute<JsonPropertyAttribute>(true) != null && p.GetCustomAttribute<JsonPropertyAttribute>(true).PropertyName == "_rev"));

            _revPropertyCache[type] = revProperty;
        }
        return _revPropertyCache[type];
    }


    private static ConcurrentDictionary<Type, PropertyInfo> _createdAtPropertyCache = new();
    private static PropertyInfo GetCreatedAtProperty(this Type type)
    {
        if (!_createdAtPropertyCache.ContainsKey(type))
        {
            var props = type.GetProperties();
            var revProperty = props.FirstOrDefault(p => p.Name.Equals("_createdAt", StringComparison.InvariantCultureIgnoreCase) ||
                                                        (p.GetCustomAttribute<JsonPropertyAttribute>(true) != null && p.GetCustomAttribute<JsonPropertyAttribute>(true).PropertyName == "_createdAt"));

            _createdAtPropertyCache[type] = revProperty;
        }
        return _createdAtPropertyCache[type];
    }

    private static ConcurrentDictionary<Type, PropertyInfo> _updatedAtPropertyCache = new();
    private static PropertyInfo GetUpdatedAtProperty(this Type type)
    {
        if (!_updatedAtPropertyCache.ContainsKey(type))
        {
            var props = type.GetProperties();
            var revProperty = props.FirstOrDefault(p => p.Name.Equals("_updatedAt", StringComparison.InvariantCultureIgnoreCase) ||
                                                        (p.GetCustomAttribute<JsonPropertyAttribute>(true) != null && p.GetCustomAttribute<JsonPropertyAttribute>(true).PropertyName == "_updatedAt"));

            _updatedAtPropertyCache[type] = revProperty;
        }
        return _updatedAtPropertyCache[type];
    }


    extension(object blockContent)
    {
        public Task<string> ToHtmlAsync(SanityHtmlBuilder builder)
        {
            return blockContent.ToHtmlAsync(builder, null);
        }

        public Task<string> ToHtmlAsync(SanityHtmlBuilder builder, object buildContext = null)
        {
            return builder.BuildAsync(blockContent, buildContext);
        }

        public string ToHtml(SanityHtmlBuilder builder)
        {
            return blockContent.ToHtml(builder, null);
        }

        public string ToHtml(SanityHtmlBuilder builder, object buildContext)
        {
            return builder.BuildAsync(blockContent, buildContext).Result;
        }

        public Task<string> ToHtmlAsync(SanityDataContext context)
        {
            return blockContent.ToHtmlAsync(context, null);
        }

        public Task<string> ToHtmlAsync(SanityDataContext context, object buildContext)
        {
            return context.HtmlBuilder.BuildAsync(blockContent, buildContext);
        }

        public string ToHtml(SanityDataContext context)
        {
            return blockContent.ToHtml(context, null);
        }

        public string ToHtml(SanityDataContext context, object buildContext)
        {
            return context.HtmlBuilder.BuildAsync(blockContent, buildContext).Result;
        }
    }
}
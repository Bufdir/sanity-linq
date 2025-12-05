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

#nullable disable

namespace Sanity.Linq.CommonTypes;

public class SanityLocale<T> : Dictionary<string, object>
{
    public SanityLocale()
    {
    }

    public SanityLocale(string sanityTypeName)
    {
        Type = sanityTypeName;
    }

    [JsonIgnore]
    public string Type
    {
        get => ContainsKey("_type") ? this["_type"]?.ToString() : null;
        set => this["_type"] = value;
    }

    public IReadOnlyDictionary<string, T> Translations => this.Where(kv => kv.Key != "_type").ToDictionary(kv => kv.Key, kv =>
    {
        if (kv.Value == null) return default(T);
        if (kv.Value is T value) return value;
        if (kv.Value is JObject jObject) return jObject.ToObject<T>();
        return default(T);
    });

    public T Get(string languageCode)
    {
        if (ContainsKey(languageCode))
        {
            if (this[languageCode] is JObject)
            {
                return ((JObject)this[languageCode]).ToObject<T>();
            }

            if (this[languageCode] is JArray)
            {
                return ((JArray)this[languageCode]).ToObject<T>();
            }

            var sVal = this[languageCode]?.ToString();
            if (sVal != null && typeof(T) == typeof(string))
            {
                return (T)(object)sVal;
            }
            return sVal != null ? (T)Convert.ChangeType(sVal, typeof(T)) : default(T);
        }

        return default(T);
    }

    public void Set(string languageCode, T value)
    {
        this[languageCode] = value;
    }
}
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

namespace Sanity.Linq.JsonConverters;

public class SanityReferenceTypeConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return (objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(SanityReference<>));
    }

    public override object? ReadJson(JsonReader reader, Type type, object? existingValue, JsonSerializer serializer)
    {
        var elemType = type.GetGenericArguments()[0];
        if (serializer.Deserialize(reader) is not JObject obj)
        {
            return null;
        }

        var res = (SanityObject)Activator.CreateInstance(type)!;
        var refProp = type.GetProperty(nameof(SanityReference<>.Ref));
        var typeProp = type.GetProperty(nameof(SanityReference<>.SanityType));
        var keyProp = type.GetProperty(nameof(SanityReference<>.SanityKey));
        var weakProp = type.GetProperty(nameof(SanityReference<>.Weak));
        var valueProp = type.GetProperty(nameof(SanityReference<>.Value));

        if (refProp != null) refProp.SetValue(res, obj.GetValue("_ref")?.ToString() ?? obj.GetValue("_id")?.ToString());
        if (typeProp != null) typeProp.SetValue(res, obj.GetValue("_type")?.ToString());
        if (keyProp != null) keyProp.SetValue(res, obj.GetValue("_key")?.ToString());
        if (weakProp != null) weakProp.SetValue(res, obj.GetValue("_weak")?.ToObject<bool?>());

        // Decide if we should populate Value
        // If it's dereferenced, it usually has more fields than the basic reference ones.
        // Also if _ref is missing but it's a JObject, it's likely a dereferenced document.
        bool isDereferenced = obj.Properties().Any(p =>
            p.Name != "_ref" && p.Name != "_type" && p.Name != "_key" && p.Name != "_weak" && p.Name != "_rev" && p.Name != "_id");

        if (isDereferenced && valueProp != null)
        {
            using (var subReader = obj.CreateReader())
            {
                valueProp.SetValue(res, serializer.Deserialize(subReader, elemType));
            }
        }
        return res;
        // Unable to deserialize
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value != null)
        {
            var type = value.GetType();

            //Get reference from object
            var refProp = type.GetProperty("Ref");
            var valRef = refProp != null ? refProp.GetValue(value) as string : null;

            // Alternatively, get reference from Id on nested Value
            if (string.IsNullOrEmpty(valRef))
            {
                var propValue = type.GetProperty("Value");
                var valValue = propValue != null ? propValue.GetValue(value) : null;
                if (propValue != null && valValue != null)
                {
                    var valType = propValue.PropertyType;
                    var idProp = valType.GetProperties().FirstOrDefault(p => p.Name.ToLower() == "_id" || ((p.GetCustomAttributes(typeof(JsonPropertyAttribute), true).FirstOrDefault() as JsonPropertyAttribute)?.PropertyName?.Equals("_id")).GetValueOrDefault());
                    if (idProp != null)
                    {
                        valRef = idProp.GetValue(valValue) as string;
                    }
                }
            }

            // Get _key property (required for arrays in sanity editor)
            var keyProp = type.GetProperties().FirstOrDefault(p => p.Name.ToLower() == "_key" || ((p.GetCustomAttributes(typeof(JsonPropertyAttribute), true).FirstOrDefault() as JsonPropertyAttribute)?.PropertyName?.Equals("_key")).GetValueOrDefault());
            var weakProp = type.GetProperties().FirstOrDefault(p => p.Name.ToLower() == "_weak" || ((p.GetCustomAttributes(typeof(JsonPropertyAttribute), true).FirstOrDefault() as JsonPropertyAttribute)?.PropertyName?.Equals("_weak")).GetValueOrDefault());
            var valKey = keyProp != null ? keyProp.GetValue(value) as string : null;
            if (string.IsNullOrEmpty(valKey)) valKey = Guid.NewGuid().ToString();
            var valWeak = weakProp != null ? weakProp.GetValue(value) as bool? : null;

            if (!string.IsNullOrEmpty(valRef))
            {
                serializer.Serialize(writer, new { _ref = valRef, _type = "reference", _key = valKey, _weak = valWeak });
                return;
            }
        }
        serializer.Serialize(writer, null);
    }
}
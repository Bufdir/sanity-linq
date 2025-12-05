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

        if (obj.GetValue("_ref") != null)
        {
            // Normal reference
            return obj.ToObject(type);
        }

        var res = Activator.CreateInstance(type);
        var refProp = type.GetProperty(nameof(SanityReference<object>.Ref));
        var typeProp = type.GetProperty(nameof(SanityReference<object>.SanityType));
        var keyProp = type.GetProperty(nameof(SanityReference<object>.SanityKey));
        var weakProp = type.GetProperty(nameof(SanityReference<object>.Weak));
        var valueProp = type.GetProperty(nameof(SanityReference<object>.Value));

        if (refProp != null) refProp.SetValue(res, obj.GetValue("_id")?.ToString());
        if (typeProp != null) typeProp.SetValue(res, "reference");
        if (keyProp != null) keyProp.SetValue(res, obj.GetValue("_key"));
        if (weakProp != null) weakProp.SetValue(res, obj.GetValue("_weak"));
        if (valueProp != null) valueProp.SetValue(res, serializer.Deserialize(new StringReader(obj.ToString()), elemType));
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
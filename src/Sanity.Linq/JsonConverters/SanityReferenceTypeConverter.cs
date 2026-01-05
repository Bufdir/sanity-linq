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
using Sanity.Linq.QueryProvider;

namespace Sanity.Linq.JsonConverters;

public class SanityReferenceTypeConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(SanityReference<>);
    }

    public override object? ReadJson(JsonReader reader, Type type, object? existingValue, JsonSerializer serializer)
    {
        var elemType = type.GetGenericArguments()[0];
        if (serializer.Deserialize(reader) is not JObject obj) return null;

        var res = (SanityObject)Activator.CreateInstance(type)!;

        var refVal = obj.GetValue("_ref")?.ToString() ?? obj.GetValue("_id")?.ToString();
        var typeVal = obj.GetValue("_type")?.ToString();
        var keyVal = obj.GetValue("_key")?.ToString();

        type.GetProperty(nameof(SanityReference<>.Ref))?.SetValue(res, refVal);
        type.GetProperty(nameof(SanityReference<>.SanityType))?.SetValue(res, typeVal);
        type.GetProperty(nameof(SanityReference<>.SanityKey))?.SetValue(res, keyVal);
        type.GetProperty(nameof(SanityReference<>.Weak))?.SetValue(res, obj.GetValue("_weak")?.ToObject<bool?>());

        var derefToken = obj.Property(SanityConstants.DEREFERENCING_SWITCH)?.Value ??
                         obj.Property(SanityConstants.DEREFERENCING_OPERATOR)?.Value ??
                         obj.Properties().FirstOrDefault(p => p.Name.EndsWith("->"))?.Value;

        if (derefToken is JObject derefObj)
            foreach (var prop in derefObj.Properties())
                obj[prop.Name] = prop.Value;

        // Decide if we should populate Value
        if (!IsDereferenced(obj)) return res;

        var val = obj.ToObject(elemType, serializer);
        if (val is SanityDocument doc && string.IsNullOrEmpty(doc.Id)) doc.Id = (obj.GetValue("_id")?.ToString() ?? refVal ?? keyVal)!;
        type.GetProperty(nameof(SanityReference<>.Value))?.SetValue(res, val);
        return res;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value != null)
        {
            var type = value.GetType();

            //Get reference from an object
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
                    var idProp = valType.GetProperties().FirstOrDefault(p => p.GetJsonProperty() == "_id");
                    if (idProp != null) valRef = idProp.GetValue(valValue) as string;
                }
            }

            // Get _key property (required for arrays in sanity editor)
            var keyProp = type.GetProperties().FirstOrDefault(p => p.GetJsonProperty() == "_key");
            var weakProp = type.GetProperties().FirstOrDefault(p => p.GetJsonProperty() == "_weak");
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

    private static bool IsDereferenced(JObject obj)
    {
        return obj.Properties().Any(p =>
            p.Name != "_ref" && p.Name != "_type" && p.Name != "_key" && p.Name != "_weak" && p.Name != "_rev" && p.Name != "_id"
            && p.Name != SanityConstants.DEREFERENCING_SWITCH && p.Name != SanityConstants.DEREFERENCING_OPERATOR
            && !p.Name.EndsWith("->"));
    }
}
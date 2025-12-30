namespace Sanity.Linq;

internal static class JsonPropertyAttributeExtensions
{
    public static string GetJsonProperty(this MemberInfo member)
    {
        var attr = GetJsonPropertyAttribute(member);

        return attr?.PropertyName ?? member.Name.ToCamelCase();
    }
    
    private static JsonPropertyAttribute? GetJsonPropertyAttribute(MemberInfo member)
    {
        var attr = member.GetCustomAttributes(typeof(JsonPropertyAttribute), true)
            .Cast<JsonPropertyAttribute>().FirstOrDefault();
        if (attr != null) return attr;

        if (member is not PropertyInfo prop || prop.DeclaringType == null) return null;

        foreach (var @interface in prop.DeclaringType.GetInterfaces())
        {
            var interfaceProp = @interface.GetProperty(prop.Name);
            if (interfaceProp == null) continue;
            attr = interfaceProp.GetCustomAttributes(typeof(JsonPropertyAttribute), true)
                .Cast<JsonPropertyAttribute>().FirstOrDefault();
            if (attr != null) return attr;
        }
        
        return null;
    }
}
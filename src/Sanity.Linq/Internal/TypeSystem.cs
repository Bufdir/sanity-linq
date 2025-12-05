// References:
// https://msdn.microsoft.com/en-us/library/bb546158.aspx

namespace Sanity.Linq.Internal;

internal static class TypeSystem
{
    internal static Type GetElementType(Type seqType)
    {
        var ienum = FindIEnumerable(seqType);
        if (ienum == null) return seqType;
        return ienum.GetGenericArguments()[0];
    }

    // Copyright (c) .NET Foundation. All rights reserved.
    // Source: https://github.com/aspnet/EntityFrameworkCore/blob/dev/src/EFCore/EntityFrameworkQueryableExtensions.cs
    internal static MethodInfo GetMethod(string name, int parameterCount = 0, Func<MethodInfo, bool>? predicate = null)
        => typeof(Queryable).GetTypeInfo().GetDeclaredMethods(name)
            .Single(mi => (mi.GetParameters().Length == parameterCount + 1)
                          && ((predicate == null) || predicate(mi)));

    private static Type? FindIEnumerable(Type seqType)
    {
        if (seqType == null || seqType == typeof(string))
            return null;

        if (seqType.IsArray)
            return typeof(IEnumerable<>).MakeGenericType(seqType.GetElementType()!);

        if (seqType.IsGenericType)
        {
            foreach (var arg in seqType.GetGenericArguments())
            {
                var ienum = typeof(IEnumerable<>).MakeGenericType(arg);
                if (ienum.IsAssignableFrom(seqType))
                {
                    return ienum;
                }
            }
        }

        var ifaces = seqType.GetInterfaces();
        if (ifaces is { Length: > 0 })
        {
            foreach (var iface in ifaces)
            {
                var ienum = FindIEnumerable(iface);
                if (ienum != null) return ienum;
            }
        }

        if (seqType.BaseType != null && seqType.BaseType != typeof(object))
        {
            return FindIEnumerable(seqType.BaseType);
        }

        return null;
    }
}
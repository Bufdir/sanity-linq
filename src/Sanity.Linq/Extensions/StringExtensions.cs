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

namespace Sanity.Linq;

public static class StringExtensions
{
    public static string ToCamelCase(this string str)
    {
        if (string.IsNullOrEmpty(str)) return str;

        if (str.Length == 1) return str.ToLower();

        //Make first letter lowercase (i.e. camelCase)
        return char.ToLowerInvariant(str[0]) + str[1..];
    }
    
    // Try to pretty print JSON bodies if possible
    internal static string? ToPrettyPrintJson(this string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        try
        {
            var token = JToken.Parse(s);
            return token.ToString(Formatting.Indented);
        }
        catch
        {
            return s;
        }
    }
}
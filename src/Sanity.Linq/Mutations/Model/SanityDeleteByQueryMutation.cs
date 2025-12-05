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

namespace Sanity.Linq.Mutations.Model;

public class SanityDeleteByQueryMutation  : SanityMutation
{
    public SanityDeleteByQueryMutation(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            throw new ArgumentException("Query cannot be null when creating a delete by query mutation.", nameof(query));
        }

        Delete = new SanityQuery { Query = query };
    }

    public SanityQuery Delete { get; set; }

    public class SanityQuery
    {
        public string Query { get; set; } = string.Empty;
    }
}
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

public class SanityCreateOrReplaceMutation : SanityMutation
{
    public SanityCreateOrReplaceMutation(object document)
    {            
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (!document.HasIdProperty() || !document.HasDocumentTypeProperty())
        {
            throw new ArgumentException("Document must have an Id field which is represented as '_id' when serialized to JSON.", nameof(document));
        }

        CreateOrReplace = document;
    }

    public object CreateOrReplace { get; set; }
}
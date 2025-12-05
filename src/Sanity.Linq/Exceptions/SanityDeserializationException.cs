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

namespace Sanity.Linq.Exceptions;

public class SanityDeserializationException : Exception
{
    public string? ResponsePreview { get; }
    public Uri? RequestUri { get; }

    public SanityDeserializationException()
    { }

    public SanityDeserializationException(string message) : base(message)
    { }

    public SanityDeserializationException(string message, Exception innerException) : base(message, innerException)
    { }

    public SanityDeserializationException(string message, string? responsePreview, Uri? requestUri, Exception? inner = null)
        : base(message, inner)
    {
        ResponsePreview = responsePreview;
        RequestUri = requestUri;
    }
}

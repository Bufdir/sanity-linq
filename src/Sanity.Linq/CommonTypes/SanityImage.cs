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

namespace Sanity.Linq.CommonTypes;

public class SanityImage : SanityObject
{
    public SanityImage()
    {
        SanityType = "image";
    }

    public SanityReference<SanityImageAsset>? Asset { get; set; }

    public SanityImageCrop? Crop { get; set; }

    public SanityImageHotspot? Hotspot { get; set; }
}
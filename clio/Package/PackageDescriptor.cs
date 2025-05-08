﻿using System;
using System.Collections.Generic;
using Clio.Common;

namespace Clio.Package;

#region Class: PackageDescriptor

public class PackageDescriptor
{

    #region Properties: Public

    public IList<PackageDependency> DependsOn { get; set; }

    public string Maintainer { get; set; }

    public string ModifiedOnUtc { get; set; }

    public string Name { get; set; }

    public string PackageVersion { get; set; }

    public string ProjectPath { get; set; } = string.Empty;

    public PackageType Type { get; set; } = PackageType.General;

    public Guid UId { get; set; }

    #endregion

    #region Methods: Private

    private static DateTime ClearMilliseconds(DateTime dt)
    {
        return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second);
    }

    #endregion

    #region Methods: Public

    public static string ConvertToModifiedOnUtc(DateTime dateTime)
    {
        long unixDateTime = UnixTimeConverter.CovertToUnixDateTime(ClearMilliseconds(dateTime));
        return $"/Date({unixDateTime})/";
    }

    #endregion

}

#endregion

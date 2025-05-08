using System;
using System.Collections.Generic;

using Clio.Common;

namespace Clio.Package;

public class PackageDescriptor
{
    public Guid UId { get; set; }

    public string PackageVersion { get; set; }

    public string Name { get; set; }

    public PackageType Type { get; set; } = PackageType.General;

    public string ProjectPath { get; set; } = string.Empty;

    public string ModifiedOnUtc { get; set; }

    public string Maintainer { get; set; }

    public IList<PackageDependency> DependsOn { get; set; }

    private static DateTime ClearMilliseconds(DateTime dt) =>
        new (dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second);

    public static string ConvertToModifiedOnUtc(DateTime dateTime)
    {
        long unixDateTime = UnixTimeConverter.CovertToUnixDateTime(ClearMilliseconds(dateTime));
        return $"/Date({unixDateTime})/";
    }
}

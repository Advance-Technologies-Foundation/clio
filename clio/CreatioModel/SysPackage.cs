#pragma warning disable CS8618, // Non-nullable field is uninitialized.
using System;
using System.Diagnostics.CodeAnalysis;
using ATF.Repository;
using ATF.Repository.Attributes;

namespace CreatioModel;

[ExcludeFromCodeCoverage]
[Schema("SysPackageInInstalledApp")]
public class SysPackageInInstalledApp : BaseModel
{

    #region Properties: Public

    [SchemaProperty("SysInstalledApp")]
    public Guid SysInstalledAppId { get; set; }

    [SchemaProperty("SysPackage")]
    public Guid SysPackageId { get; set; }

    #endregion

}

#pragma warning restore CS8618 // Non-nullable field is uninitialized.

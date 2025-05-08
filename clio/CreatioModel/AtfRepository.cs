#pragma warning disable CS8618, // Non-nullable field is uninitialized.
using System;
using System.Diagnostics.CodeAnalysis;
using ATF.Repository;
using ATF.Repository.Attributes;

namespace CreatioModel;

[ExcludeFromCodeCoverage]
[Schema("AtfRepository")]
public class AtfRepository : BaseModel
{

    #region Properties: Public

    [SchemaProperty("AtfApplication")]
    public Guid AtfApplicationId { get; set; }

    [SchemaProperty("Name")]
    public string Name { get; set; }

    [SchemaProperty("SysInstalledApp")]
    public Guid SysInstalledAppId { get; set; }

    #endregion

}

#pragma warning restore CS8618 // Non-nullable field is uninitialized.

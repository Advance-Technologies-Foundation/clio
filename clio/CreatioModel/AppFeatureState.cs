#pragma warning disable CS8618, // Non-nullable field is uninitialized.
using System;
using System.Diagnostics.CodeAnalysis;
using ATF.Repository;
using ATF.Repository.Attributes;

namespace CreatioModel;

[ExcludeFromCodeCoverage]
[Schema("AppFeatureState")]
public class AppFeatureState : BaseModel
{

    #region Properties: Public

    [LookupProperty("AdminUnit")]
    public virtual SysAdminUnit AdminUnit { get; set; }

    [SchemaProperty("AdminUnit")]
    public Guid AdminUnitId { get; set; }

    [LookupProperty("Feature")]
    public virtual AppFeature Feature { get; set; }

    [SchemaProperty("Feature")]
    public Guid FeatureId { get; set; }

    [SchemaProperty("FeatureState")]
    public bool FeatureState { get; set; }

    #endregion

}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

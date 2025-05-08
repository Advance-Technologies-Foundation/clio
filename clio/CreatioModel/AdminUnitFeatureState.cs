#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using System;
using System.Diagnostics.CodeAnalysis;

using ATF.Repository;
using ATF.Repository.Attributes;

namespace CreatioModel;

[ExcludeFromCodeCoverage]
[Schema("AdminUnitFeatureState")]
public class AdminUnitFeatureState : BaseModel
{
    [SchemaProperty("FeatureState")]
    public bool FeatureState { get; set; }

    [SchemaProperty("SysAdminUnit")]
    public Guid AdminUnitId { get; set; }

    [LookupProperty("SysAdminUnit")]
    public virtual SysAdminUnit AdminUnit { get; set; }

    [SchemaProperty("Feature")]
    public Guid FeatureId { get; set; }

    [LookupProperty("Feature")]
    public virtual AppFeature Feature { get; set; }
}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

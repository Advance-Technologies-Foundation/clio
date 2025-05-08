#pragma warning disable CS8618, // Non-nullable field is uninitialized.
using System.Diagnostics.CodeAnalysis;
using ATF.Repository;
using ATF.Repository.Attributes;

namespace CreatioModel;

[ExcludeFromCodeCoverage]
[Schema("SysAdminUnit")]
public class SysAdminUnit : BaseModel
{

    #region Properties: Public

    [SchemaProperty("Name")]
    public string Name { get; set; }

    #endregion

}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

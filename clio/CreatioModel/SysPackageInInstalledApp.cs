#pragma warning disable CS8618, // Non-nullable field is uninitialized.
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ATF.Repository;
using ATF.Repository.Attributes;

namespace CreatioModel;

[ExcludeFromCodeCoverage]
[Schema("SysPackage")]
public class SysPackage : BaseModel
{

    #region Properties: Public

    [SchemaProperty("Maintainer")]
    public string Maintainer { get; set; }

    [SchemaProperty("ModifiedOn")]
    public DateTime ModifiedOn { get; set; }

    [SchemaProperty("Name")]
    public string Name { get; set; }

    [DetailProperty("SysPackageId")]
    public virtual List<SysSchema> SysSchemas { get; set; }

    #endregion

}

#pragma warning restore CS8618 // Non-nullable field is uninitialized.

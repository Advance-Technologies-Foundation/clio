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
    [SchemaProperty("Name")] public string Name { get; set; }

    [SchemaProperty("ModifiedOn")] public DateTime ModifiedOn { get; set; }

    [DetailProperty("SysPackageId")] public virtual List<SysSchema> SysSchemas { get; set; }

    [SchemaProperty("Maintainer")] public string Maintainer { get; set; }
}

#pragma warning restore CS8618 // Non-nullable field is uninitialized.

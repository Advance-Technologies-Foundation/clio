#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using ATF.Repository;
using ATF.Repository.Attributes;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("SysPackage")]
	public class SysPackage : BaseModel
	{

		[SchemaProperty("Name")]
		public string Name { get; set; }

		[SchemaProperty("ModifiedOn")]
		public DateTime ModifiedOn { get; set; }

		[DetailProperty("SysPackageId")]
		public virtual List<SysSchema> SysSchemas { get; set; }
	}
}

#pragma warning restore CS8618 // Non-nullable field is uninitialized.

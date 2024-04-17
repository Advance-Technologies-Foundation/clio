#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using System;
using ATF.Repository;
using ATF.Repository.Attributes;
using System.Diagnostics.CodeAnalysis;

namespace CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("SysPackageInInstalledApp")]
	public class SysPackageInInstalledApp : BaseModel
	{

		[SchemaProperty("SysPackage")]
		public Guid SysPackageId { get; set; }

		[SchemaProperty("SysInstalledApp")]
		public Guid SysInstalledAppId { get; set; }

	}
}
	
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

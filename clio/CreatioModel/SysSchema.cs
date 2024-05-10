#pragma warning disable CS8618, // Non-nullable field is uninitialized.
using System;
using System.Diagnostics.CodeAnalysis;
using ATF.Repository;
using ATF.Repository.Attributes;

namespace CreatioModel;

[ExcludeFromCodeCoverage]
[Schema("SysSchema")]
public class SysSchema : BaseModel
{

	#region Properties: Public

	[SchemaProperty("ManagerName")]
	public string ManagerName { get; set; }

	[SchemaProperty("Name")]
	public string Name { get; set; }
	
	[SchemaProperty("UId")]
	public Guid UId { get; set; }

	[SchemaProperty("ModifiedOn")]
	public DateTime ModifiedOn { get; set; }

	[SchemaProperty("Checksum")]
	public string Checksum { get; set; }

	[SchemaProperty("SysPackage")]
	public Guid SysPackageId { get; set; }
 
	[LookupProperty("SysPackage")]
	public virtual SysPackage SysPackage { get; set; }

	#endregion

}

#pragma warning restore CS8618 // Non-nullable field is uninitialized.
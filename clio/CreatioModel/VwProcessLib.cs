#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using System;
using ATF.Repository;
using ATF.Repository.Attributes;
using System.Diagnostics.CodeAnalysis;

namespace Clio.CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("VwProcessLib")]
	public class VwProcessLib: BaseModel
	{

		[SchemaProperty("CreatedOn")]
		public DateTime CreatedOn { get; set; }

		[SchemaProperty("ModifiedOn")]
		public DateTime ModifiedOn { get; set; }

		[SchemaProperty("UId")]
		public Guid UId { get; set; }

		[SchemaProperty("Name")]
		public string Name { get; set; }

		[SchemaProperty("Caption")]
		public string? Caption { get; set; }

		[SchemaProperty("ManagerName")]
		public string ManagerName { get; set; }

		[SchemaProperty("Parent")]
		public Guid ParentId { get; set; }

		[SchemaProperty("ExtendParent")]
		public bool ExtendParent { get; set; }

		[SchemaProperty("IsChanged")]
		public bool IsChanged { get; set; }

		[SchemaProperty("IsLocked")]
		public bool IsLocked { get; set; }

		[SchemaProperty("MetaData")]
		public byte[] MetaData { get; set; }

		[SchemaProperty("MetaDataModifiedOn")]
		public DateTime MetaDataModifiedOn { get; set; }
		
		[SchemaProperty("PackageUId")]
		public Guid PackageUId { get; set; }
		
		[SchemaProperty("Description")]
		public string Description { get; set; }

		[SchemaProperty("NeedUpdateSourceCode")]
		public bool NeedUpdateSourceCode { get; set; }

		[SchemaProperty("NeedUpdateStructure")]
		public bool NeedUpdateStructure { get; set; }

		[SchemaProperty("NeedInstall")]
		public bool NeedInstall { get; set; }

		[SchemaProperty("IsMaxVersion")]
		public bool IsMaxVersion { get; set; }

		[SchemaProperty("TagProperty")]
		public string TagProperty { get; set; }

		[SchemaProperty("Enabled")]
		public bool Enabled { get; set; }

		[SchemaProperty("Version")]
		public int Version { get; set; }

		[SchemaProperty("ProcessSchemaType")]
		public Guid ProcessSchemaTypeId { get; set; }

		[LookupProperty("ProcessSchemaType")]
		public virtual ProcessSchemaType ProcessSchemaType { get; set; }

		[SchemaProperty("SysSchemaId")]
		public Guid SysSchemaId { get; set; }

		[SchemaProperty("AddToRunButton")]
		public bool AddToRunButton { get; set; }

		[SchemaProperty("IsActiveVersion")]
		public bool IsActiveVersion { get; set; }

		[SchemaProperty("VersionParentId")]
		public Guid VersionParentId { get; set; }

		[SchemaProperty("HasStartEvent")]
		public bool HasStartEvent { get; set; }

		[SchemaProperty("VersionParentUId")]
		public Guid VersionParentUId { get; set; }

		[SchemaProperty("IsProcessTracing")]
		public bool IsProcessTracing { get; set; }

	}
}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

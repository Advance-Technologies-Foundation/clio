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

		// Nullable: the view passes SysSchema.ParentId through unchanged, and it is NULL for every
		// process that is not a version of another one. A non-nullable Guid turns that into Guid.Empty
		// before any caller can tell "no parent" from "parent unknown".
		[SchemaProperty("Parent")]
		public Guid? ParentId { get; set; }

		[SchemaProperty("ExtendParent")]
		public bool ExtendParent { get; set; }

		[SchemaProperty("IsChanged")]
		public bool IsChanged { get; set; }

		[SchemaProperty("IsLocked")]
		public bool IsLocked { get; set; }

		// MetaData / MetaDataModifiedOn are deliberately NOT mapped, and this note is about their ABSENCE —
		// not about the column declared below it. ATF builds the select from this type's properties, so
		// declaring the metadata blob makes every query over the process library carry the full serialized
		// schema for every row, including a version-family read of up to 50 members. No caller has ever read
		// them. Re-adding either column re-adds that cost everywhere.

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

		// Nullable: Version and IsActiveVersion both come from VwProcessSchemaVersion, whose subqueries
		// run against VwProcessSchemaInfo — and that view INNER JOINs SysPackage, so a schema whose
		// package does not resolve yields NULL here while the VwProcessLib row itself survives.
		// Non-nullable types collapse that into 0 / false, i.e. into "version 0, and it is the one
		// that runs" — the exact wrong answer this feature exists to stop reporting.
		[SchemaProperty("Version")]
		public int? Version { get; set; }

		[SchemaProperty("ProcessSchemaType")]
		public Guid ProcessSchemaTypeId { get; set; }

		[LookupProperty("ProcessSchemaType")]
		public virtual ProcessSchemaType ProcessSchemaType { get; set; }

		[SchemaProperty("SysSchemaId")]
		public Guid SysSchemaId { get; set; }

		[SchemaProperty("AddToRunButton")]
		public bool AddToRunButton { get; set; }

		[SchemaProperty("IsActiveVersion")]
		public bool? IsActiveVersion { get; set; }

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

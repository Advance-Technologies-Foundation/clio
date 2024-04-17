#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using System;
using ATF.Repository;
using ATF.Repository.Attributes;
using System.Diagnostics.CodeAnalysis;

namespace CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("SysPackage")]
	public class SysPackage: BaseModel
	{

		

		[SchemaProperty("Name")]
		public string Name { get; set; }

		[SchemaProperty("Description")]
		public string Description { get; set; }

		[SchemaProperty("ProcessListeners")]
		public int ProcessListeners { get; set; }

		[SchemaProperty("Position")]
		public int Position { get; set; }

		[SchemaProperty("SysWorkspace")]
		public Guid SysWorkspaceId { get; set; }

		[LookupProperty("SysWorkspace")]
		public virtual SysWorkspace SysWorkspace { get; set; }

		[SchemaProperty("UId")]
		public Guid UId { get; set; }

		[SchemaProperty("Version")]
		public string Version { get; set; }

		[SchemaProperty("Maintainer")]
		public string Maintainer { get; set; }

		[SchemaProperty("Essential")]
		public bool Essential { get; set; }

		[SchemaProperty("Annotation")]
		public string Annotation { get; set; }

		[SchemaProperty("IsChanged")]
		public bool IsChanged { get; set; }

		[SchemaProperty("IsLocked")]
		public bool IsLocked { get; set; }

		[SchemaProperty("InstallType")]
		public string InstallType { get; set; }

		[SchemaProperty("RepositoryRevisionNumber")]
		public int RepositoryRevisionNumber { get; set; }

		[SchemaProperty("SysRepository")]
		public Guid SysRepositoryId { get; set; }

		[LookupProperty("SysRepository")]
		public virtual SysRepository SysRepository { get; set; }

		/// <summary>
		/// 0 - General, 1 - Assembly
		/// </summary>
		[SchemaProperty("Type")]
		public int Type { get; set; }

		/// <summary>
		/// Path relative to location of package descriptor
		/// </summary>
		[SchemaProperty("ProjectPath")]
		public string ProjectPath { get; set; }

		/// <summary>
		/// Install behavior
		/// </summary>
		[SchemaProperty("InstallBehavior")]
		public int InstallBehavior { get; set; }

		[SchemaProperty("LicOperations")]
		public string LicOperations { get; set; }

		[SchemaProperty("HierarchyLevel")]
		public int HierarchyLevel { get; set; }

	}
}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

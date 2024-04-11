#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using System;
using ATF.Repository;
using ATF.Repository.Attributes;
using System.Diagnostics.CodeAnalysis;

namespace CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("SysWorkspace")]
	public class SysWorkspace: BaseModel
	{
		
		[SchemaProperty("Name")]
		public string Name { get; set; }

		[SchemaProperty("Description")]
		public string Description { get; set; }

		[SchemaProperty("ProcessListeners")]
		public int ProcessListeners { get; set; }

		[SchemaProperty("IsDefault")]
		public bool IsDefault { get; set; }

		[SchemaProperty("Number")]
		public int Number { get; set; }

		[SchemaProperty("Version")]
		public int Version { get; set; }

		[SchemaProperty("RepositoryUri")]
		public string RepositoryUri { get; set; }

		[SchemaProperty("WorkingCopyPath")]
		public string WorkingCopyPath { get; set; }

		[SchemaProperty("RepositoryRevisionNumber")]
		public int RepositoryRevisionNumber { get; set; }

		[SchemaProperty("BuildODataStartedBy")]
		public Guid BuildODataStartedById { get; set; }


	}
}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

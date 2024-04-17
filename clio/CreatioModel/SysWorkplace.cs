#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using System;
using ATF.Repository;
using ATF.Repository.Attributes;
using System.Diagnostics.CodeAnalysis;

namespace CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("SysWorkplace")]
	public class SysWorkplace: BaseModel
	{

		[SchemaProperty("Position")]
		public int Position { get; set; }

		[SchemaProperty("Name")]
		public string Name { get; set; }

		[SchemaProperty("IsPersonal")]
		public bool IsPersonal { get; set; }

		[SchemaProperty("LoaderId")]
		public Guid LoaderId { get; set; }

		[SchemaProperty("SysApplicationClientType")]
		public Guid SysApplicationClientTypeId { get; set; }

		
		[SchemaProperty("Type")]
		public Guid TypeId { get; set; }

		
		[SchemaProperty("HomePageUId")]
		public Guid HomePageUId { get; set; }

		[SchemaProperty("UseOnlyShell")]
		public bool UseOnlyShell { get; set; }

	}
}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

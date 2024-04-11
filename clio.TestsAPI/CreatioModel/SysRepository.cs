#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using System;
using ATF.Repository;
using ATF.Repository.Attributes;
using System.Diagnostics.CodeAnalysis;

namespace CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("SysRepository")]
	public class SysRepository: BaseModel
	{

		[SchemaProperty("Name")]
		public string Name { get; set; }

		[SchemaProperty("Address")]
		public string Address { get; set; }

		[SchemaProperty("IsActive")]
		public bool IsActive { get; set; }

	}
}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

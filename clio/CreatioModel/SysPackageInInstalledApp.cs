#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using ATF.Repository;
using ATF.Repository.Attributes;
using System.Diagnostics.CodeAnalysis;

namespace CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("SysPackage")]
	public class SysPackage : BaseModel
	{

		[SchemaProperty("Name")]
		public string Name { get; set; }

	}
}

#pragma warning restore CS8618 // Non-nullable field is uninitialized.

#pragma warning disable CS8618, // Non-nullable field is uninitialized.
using System.Diagnostics.CodeAnalysis;
using ATF.Repository;
using ATF.Repository.Attributes;

namespace CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("SysAdminUnit")]
	public class SysAdminUnit: BaseModel
	{
		

		[SchemaProperty("Name")]
		public string Name { get; set; }

		

	}
}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

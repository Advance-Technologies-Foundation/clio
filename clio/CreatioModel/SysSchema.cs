#pragma warning disable CS8618, // Non-nullable field is uninitialized.
using System.Diagnostics.CodeAnalysis;
using ATF.Repository;
using ATF.Repository.Attributes;

namespace CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("SysSchema")]
	public class SysSchema : BaseModel
	{

		[SchemaProperty("Name")]
		public string Name { get; set; }
		
		[SchemaProperty("ManagerName")]
		public string ManagerName { get; set; }
		
	}



}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
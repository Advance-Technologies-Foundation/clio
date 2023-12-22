#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using System;
using ATF.Repository;
using ATF.Repository.Attributes;
using System.Diagnostics.CodeAnalysis;

namespace CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("SysInstalledApp")]
	public class SysInstalledApp: BaseModel
	{

		[SchemaProperty("Name")]
		public string Name { get; set; }

		[SchemaProperty("Code")]
		public string Code { get; set; }

		
		[SchemaProperty("Description")]
		public string Description { get; set; }

		public override string ToString() {
			return $"\"Id: {Id}, Name: {Name}, Code: {Code}\"";
		}

	}



	[ExcludeFromCodeCoverage]
	[Schema("Contact")]
	public class Contact : BaseModel
	{

		[SchemaProperty("Name")]
		public string Name
		{
			get; set;
		}

	}

}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

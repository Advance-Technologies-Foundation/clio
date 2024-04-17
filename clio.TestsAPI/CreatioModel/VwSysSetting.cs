#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using System;
using ATF.Repository;
using ATF.Repository.Attributes;
using System.Diagnostics.CodeAnalysis;

namespace CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("VwSysSetting")]
	public class VwSysSetting: BaseModel
	{

		[SchemaProperty("Name")]
		public string Name { get; set; }

		[SchemaProperty("Description")]
		public string Description { get; set; }

		[SchemaProperty("Code")]
		public string Code { get; set; }

		[SchemaProperty("ValueTypeName")]
		public string ValueTypeName { get; set; }

		[SchemaProperty("ReferenceSchemaUId")]
		public Guid ReferenceSchemaUIdId { get; set; }

		[LookupProperty("ReferenceSchemaUId")]
		public virtual SysSchema ReferenceSchemaUId { get; set; }

		[SchemaProperty("IsPersonal")]
		public bool IsPersonal { get; set; }

		[SchemaProperty("IsCacheable")]
		public bool IsCacheable { get; set; }

		[SchemaProperty("IsSSPAvailable")]
		public bool IsSSPAvailable { get; set; }

	}
}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

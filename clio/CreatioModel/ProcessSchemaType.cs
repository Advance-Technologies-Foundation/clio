#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using System;
using System.Diagnostics.CodeAnalysis;
using ATF.Repository;
using ATF.Repository.Attributes;

namespace Clio.CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("ProcessSchemaType")]
	public class ProcessSchemaType: BaseModel
	{

		[SchemaProperty("CreatedOn")]
		public DateTime CreatedOn { get; set; }

		[SchemaProperty("ModifiedOn")]
		public DateTime ModifiedOn { get; set; }

		[SchemaProperty("Name")]
		public string Name { get; set; }

		[SchemaProperty("Description")]
		public string Description { get; set; }

		[SchemaProperty("Code")]
		public string Code { get; set; }

		[SchemaProperty("ProcessListeners")]
		public int ProcessListeners { get; set; }

	}
}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

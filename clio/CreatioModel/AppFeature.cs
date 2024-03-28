#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using System;
using ATF.Repository;
using ATF.Repository.Attributes;
using System.Diagnostics.CodeAnalysis;

namespace CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("AppFeature")]
	public class AppFeature: BaseModel
	{

		
		[SchemaProperty("Name")]
		public string Name { get; set; }

		[SchemaProperty("Description")]
		public string Description { get; set; }

		[SchemaProperty("Code")]
		public string Code { get; set; }
		
		[SchemaProperty("State")]
		public bool State { get; set; }

		[SchemaProperty("StateForCurrentUser")]
		public bool StateForCurrentUser { get; set; }

		[SchemaProperty("Source")]
		public string Source { get; set; }

	}
}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

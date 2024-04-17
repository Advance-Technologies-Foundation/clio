#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using System;
using ATF.Repository;
using ATF.Repository.Attributes;
using System.Diagnostics.CodeAnalysis;

namespace CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("AppFeatureState")]
	public class AppFeatureState: BaseModel
	{
		

		[SchemaProperty("FeatureState")]
		public bool FeatureState { get; set; }

		[SchemaProperty("AdminUnit")]
		public Guid AdminUnitId { get; set; }

		[LookupProperty("AdminUnit")]
		public virtual SysAdminUnit AdminUnit { get; set; }

		[SchemaProperty("Feature")]
		public Guid FeatureId { get; set; }

		[LookupProperty("Feature")]
		public virtual AppFeature Feature { get; set; }

	}
}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

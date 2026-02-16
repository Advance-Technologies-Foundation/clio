#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using System;
using System.Diagnostics.CodeAnalysis;
using ATF.Repository;
using ATF.Repository.Attributes;
using CreatioModel;

namespace Clio.CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("CompilationHistory")]
	public class CompilationHistory: BaseModel
	{

		[SchemaProperty("CreatedOn")]
		public DateTime CreatedOn { get; set; }

		[SchemaProperty("CreatedBy")]
		public Guid CreatedById { get; set; }

		[LookupProperty("CreatedBy")]
		public virtual Contact CreatedBy { get; set; }

		[SchemaProperty("ModifiedOn")]
		public DateTime ModifiedOn { get; set; }

		[SchemaProperty("ModifiedBy")]
		public Guid ModifiedById { get; set; }

		[LookupProperty("ModifiedBy")]
		public virtual Contact ModifiedBy { get; set; }

		[SchemaProperty("ProcessListeners")]
		public int ProcessListeners { get; set; }

		[SchemaProperty("ErrorsWarnings")]
		public string ErrorsWarnings { get; set; }

		[SchemaProperty("Result")]
		public bool Result { get; set; }

		[SchemaProperty("ProjectName")]
		public string ProjectName { get; set; }

		[SchemaProperty("DurationInSeconds")]
		public int DurationInSeconds { get; set; }

		[SchemaProperty("StartedBy")]
		public Guid StartedById { get; set; }

		[LookupProperty("StartedBy")]
		public virtual SysAdminUnit StartedBy { get; set; }

	}
}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

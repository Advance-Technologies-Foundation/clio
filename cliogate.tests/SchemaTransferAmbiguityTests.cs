using System.Collections.Generic;
using cliogate.Files.cs;
using cliogate.Files.cs.Dto;
using FluentAssertions;
using NUnit.Framework;

namespace cliogate.tests
{
	/// <summary>
	/// Covers the refusal message <c>ExportSchema</c> produces when a schema name resolves to more than one layer.
	/// </summary>
	/// <remarks>
	/// R7/AC3 requires the ambiguity to be reported and never guessed, and the message is the whole of that
	/// contract — it is what the operator (or an agent) reads to build the retry. The uniqueness constraint is
	/// <c>IU_Name_Manager_Package</c>, so a name can match twice inside ONE package under two managers; naming
	/// only the packages would then print the same package twice and advise the one option that cannot narrow
	/// the match, which is a dead-end refusal loop.
	/// </remarks>
	[TestFixture]
	[System.ComponentModel.Category("UnitTest")]
	public class SchemaTransferAmbiguityTests
	{

		[Test]
		[Description("Advises --manager-name and names the package once when every candidate lives in one package")]
		public void DescribeAmbiguity_Should_Advise_Manager_When_One_Package_Carries_Every_Layer()
		{
			// Arrange
			List<SchemaLayerInfo> layers = new List<SchemaLayerInfo> {
				NewLayer("UsrProbePackage", "SourceCodeSchemaManager"),
				NewLayer("UsrProbePackage", "AddonSchemaManager")
			};

			// Act
			string message = CreatioApiGateway.DescribeAmbiguity("UsrProbe", layers);

			// Assert
			message.Should().Contain("matches 2 layers",
				because: "two layers matched; describing them as two PACKAGES would be a count of the wrong thing");
			message.Should().Contain("'UsrProbePackage' (SourceCodeSchemaManager)")
				.And.Contain("'UsrProbePackage' (AddonSchemaManager)",
					because: "the manager is the only dimension that tells these two candidates apart");
			message.Should().Contain("--manager-name",
				because: "re-running with --package-name would match both rows again and refuse again");
			message.Should().NotContain("--package-name",
				because: "advising the option that cannot reduce the match is what makes the refusal a dead end");
		}

		[Test]
		[Description("Advises --package-name and lists every candidate when the layers span several packages")]
		public void DescribeAmbiguity_Should_Advise_Package_When_Layers_Span_Several_Packages()
		{
			// Arrange
			List<SchemaLayerInfo> layers = new List<SchemaLayerInfo> {
				NewLayer("CrtCoreBase", "EntitySchemaManager"),
				NewLayer("UsrProbePackage", "EntitySchemaManager")
			};

			// Act
			string message = CreatioApiGateway.DescribeAmbiguity("Contact", layers);

			// Assert
			message.Should().Contain("Schema 'Contact' matches 2 layers",
				because: "the operator needs to know how many candidates the retry has to narrow");
			message.Should().Contain("'CrtCoreBase' (EntitySchemaManager)")
				.And.Contain("'UsrProbePackage' (EntitySchemaManager)",
					because: "each candidate has to be nameable in the retry");
			message.Should().Contain("--package-name",
				because: "the package is what distinguishes these candidates");
		}

		[Test]
		[Description("Prints one entry per distinct package/manager pair rather than repeating identical candidates")]
		public void DescribeAmbiguity_Should_Deduplicate_Identical_Candidates()
		{
			// Arrange — the same (package, manager) pair reported twice, e.g. one layer per culture row.
			List<SchemaLayerInfo> layers = new List<SchemaLayerInfo> {
				NewLayer("UsrProbePackage", "EntitySchemaManager"),
				NewLayer("UsrProbePackage", "EntitySchemaManager")
			};

			// Act
			string message = CreatioApiGateway.DescribeAmbiguity("UsrProbe", layers);

			// Assert
			message.Should().Contain("'UsrProbePackage' (EntitySchemaManager).",
				because: "a duplicated candidate list reads as two different targets the operator could choose between");
			message.IndexOf("UsrProbePackage").Should().Be(message.LastIndexOf("UsrProbePackage"),
				because: "the package must appear exactly once in the candidate list");
		}

		[Test]
		[Description("Falls back to the bare package name for a layer whose manager the environment did not report")]
		public void DescribeAmbiguity_Should_Omit_An_Empty_Manager()
		{
			// Arrange
			List<SchemaLayerInfo> layers = new List<SchemaLayerInfo> {
				NewLayer("CrtCoreBase", null),
				NewLayer("UsrProbePackage", "EntitySchemaManager")
			};

			// Act
			string message = CreatioApiGateway.DescribeAmbiguity("Contact", layers);

			// Assert
			message.Should().Contain("'CrtCoreBase',",
				because: "an empty manager must not render as an empty pair of brackets the operator cannot act on");
		}

		private static SchemaLayerInfo NewLayer(string packageName, string managerName) =>
			new SchemaLayerInfo {
				SchemaName = "Probe",
				PackageName = packageName,
				ManagerName = managerName
			};
	}
}

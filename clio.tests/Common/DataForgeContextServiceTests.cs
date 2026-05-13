using System;
using System.Collections.Generic;
using Clio.Common.DataForge;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
public sealed class DataForgeContextServiceTests {
	[Test]
	[Category("Unit")]
	[Description("Aggregates tables, lookups, relations, and columns from the proxy read client while deduplicating repeated matches.")]
	public void GetContext_Should_Aggregate_And_Dedupe_Results() {
		// Arrange
		IDataForgeReadClient readClient = Substitute.For<IDataForgeReadClient>();
		readClient.FindSimilarTables("customer request", null).Returns([
			new SimilarTableResult("Contact", "Contact", "Primary contact"),
			new SimilarTableResult("Contact", "Contact", "Duplicate contact"),
			new SimilarTableResult("Account", "Account", "Primary account")
		]);
		readClient.FindSimilarLookups("industry", null, null).Returns([
			new SimilarLookupResult("lookup-1", "Industry", "Manufacturing", 0.91m),
			new SimilarLookupResult("lookup-2", "Industry", "Manufacturing", 0.88m)
		]);
		readClient.GetTableRelationships("Contact", "Account", null).Returns(["(Contact)-[:Account]->(Account)"]);
		readClient.GetTableColumnsDetails("Contact").Returns([
			new DataForgeColumnResult("Name", "Full name", null, "Text", true, null)
		]);
		readClient.GetTableColumnsDetails("Account").Returns([
			new DataForgeColumnResult("Name", "Account name", null, "Text", true, null)
		]);

		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		maintenanceClient.GetFullStatus().Returns((
			new DataForgeHealthResult(true, true, true, true, "corr-health"),
			new DataForgeMaintenanceStatusResult(true, "Ready", null)));

		DataForgeContextService service = new(readClient, maintenanceClient);

		// Act
		DataForgeContextAggregationResult result = service.GetContext(
			new DataForgeContextRequest(
				"customer request",
				["customer request"],
				["industry"],
				[new DataForgeRelationPair("Contact", "Account")]));

		// Assert
		result.Health.CorrelationId.Should().Be("corr-health", because: "the aggregated result should preserve the health probe correlation id");
		result.Status.Status.Should().Be("Ready", because: "the aggregated result should preserve the maintenance status");
		result.SimilarTables.Should().HaveCount(2, because: "duplicate table hits should be deduplicated by schema name");
		result.SimilarLookups.Should().HaveCount(1, because: "duplicate lookup hits should be deduplicated by schema name and value");
		result.Relations.Should().ContainKey("Contact->Account", because: "requested relation pairs should be returned under a deterministic key");
		result.Columns.Should().ContainKey("Contact", because: "successful column reads should populate the columns dictionary");
		result.Coverage.Tables.Should().BeTrue(because: "coverage should report tables=true when requested table terms resolve successfully");
		result.Coverage.Lookups.Should().BeTrue(because: "coverage should report lookups=true when requested lookup hints resolve successfully");
		result.Coverage.Relations.Should().BeTrue(because: "coverage should report relations=true when requested relation pairs resolve successfully");
		result.Coverage.Columns.Should().BeTrue(because: "coverage should report columns=true when every distinct table resolved successfully");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns partial results and warnings when one column enrichment fails.")]
	public void GetContext_Should_Return_Partial_Results_When_Column_Enrichment_Fails() {
		// Arrange
		IDataForgeReadClient readClient = Substitute.For<IDataForgeReadClient>();
		readClient.FindSimilarTables("customer request", null).Returns([
			new SimilarTableResult("Contact", "Contact", "Primary contact"),
			new SimilarTableResult("Account", "Account", "Primary account")
		]);
		readClient.FindSimilarLookups(Arg.Any<string>(), null, null).Returns(new List<SimilarLookupResult>());
		readClient.GetTableColumnsDetails("Contact").Returns([
			new DataForgeColumnResult("Name", "Full name", null, "Text", true, null)
		]);
		readClient.GetTableColumnsDetails("Account").Returns(_ => throw new InvalidOperationException("column read failed"));

		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		maintenanceClient.GetFullStatus().Returns((
			new DataForgeHealthResult(true, true, true, true, "corr-health"),
			new DataForgeMaintenanceStatusResult(true, "Ready", null)));

		DataForgeContextService service = new(readClient, maintenanceClient);

		// Act
		DataForgeContextAggregationResult result = service.GetContext(
			new DataForgeContextRequest(
				"customer request",
				["customer request"],
				null,
				null));

		// Assert
		result.Columns.Should().ContainKey("Contact", because: "successful column reads should still be preserved when another table fails");
		result.Columns.Should().NotContainKey("Account", because: "failed column reads should not populate the columns dictionary");
		result.Warnings.Should().Contain(warning => warning.Contains("Account"),
			because: "failed column enrichments should be recorded as warnings");
		result.Coverage.Columns.Should().BeFalse(
			because: "coverage should report columns=false when not every distinct table resolved successfully");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports true coverage when the caller omits all optional discovery inputs.")]
	public void GetContext_Should_Report_True_Coverage_When_Optional_Inputs_Are_Omitted() {
		// Arrange
		IDataForgeReadClient readClient = Substitute.For<IDataForgeReadClient>();
		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		maintenanceClient.GetFullStatus().Returns((
			new DataForgeHealthResult(true, true, true, true, "corr-health"),
			new DataForgeMaintenanceStatusResult(true, "Ready", null)));

		DataForgeContextService service = new(readClient, maintenanceClient);

		// Act
		DataForgeContextAggregationResult result = service.GetContext(
			new DataForgeContextRequest(null, null, null, null));

		// Assert
		result.SimilarTables.Should().BeEmpty(because: "no candidate terms were provided");
		result.Coverage.Tables.Should().BeTrue(because: "coverage should stay true when table terms were omitted");
		result.Coverage.Lookups.Should().BeTrue(because: "coverage should stay true when lookup hints were omitted");
		result.Coverage.Relations.Should().BeTrue(because: "coverage should stay true when relation pairs were omitted");
		result.Coverage.Columns.Should().BeTrue(because: "coverage should stay true when no tables were resolved");
	}
}

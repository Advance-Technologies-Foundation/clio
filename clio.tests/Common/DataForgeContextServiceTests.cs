using System.Collections.Generic;
using System.Threading.Tasks;
using Clio.Common.DataForge;
using Clio.Common.EntitySchema;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
public sealed class DataForgeContextServiceTests {
	[Test]
	[Category("Unit")]
	[Description("Aggregates tables, lookups, relations, and columns from the underlying Data Forge clients while deduplicating repeated table and lookup matches.")]
	public async Task GetContextAsync_Should_Aggregate_And_Dedupe_Results() {
		// Arrange
		IDataForgeClient dataForgeClient = Substitute.For<IDataForgeClient>();
		dataForgeClient.CheckHealthAsync(Arg.Any<DataForgeConfigRequest>(), default)
			.Returns(new DataForgeHealthResult(true, true, true, true, "corr-health"));
		dataForgeClient.FindSimilarTablesAsync("customer request", null, Arg.Any<DataForgeConfigRequest>(), default)
			.Returns([
				new SimilarTableResult("Contact", "Contact", "Primary contact"),
				new SimilarTableResult("Contact", "Contact", "Duplicate contact"),
				new SimilarTableResult("Account", "Account", "Primary account")
			]);
		dataForgeClient.FindSimilarLookupsAsync("industry", null, null, Arg.Any<DataForgeConfigRequest>(), default)
			.Returns([
				new SimilarLookupResult("lookup-1", "Industry", "Manufacturing", 0.91m),
				new SimilarLookupResult("lookup-2", "Industry", "Manufacturing", 0.88m)
			]);
		dataForgeClient.GetTableRelationshipsAsync("Contact", "Account", null, Arg.Any<DataForgeConfigRequest>(), default)
			.Returns(["(Contact)-[:Account]->(Account)"]);

		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		maintenanceClient.GetStatus().Returns(new DataForgeMaintenanceStatusResult(true, "Ready", null));

		IRuntimeEntitySchemaReader runtimeEntitySchemaReader = Substitute.For<IRuntimeEntitySchemaReader>();
		runtimeEntitySchemaReader.GetByName("Contact").Returns(
			new RuntimeEntitySchemaResult(
				System.Guid.NewGuid(),
				"Contact",
				System.Guid.NewGuid(),
				"Name",
				null,
				[
					new RuntimeEntitySchemaColumnResult(System.Guid.NewGuid(), "Name", "Full name", null, 1, true, false, null),
					new RuntimeEntitySchemaColumnResult(System.Guid.NewGuid(), "CreatedOn", null, null, 7, false, true, null)
				]));
		runtimeEntitySchemaReader.GetByName("Account").Returns(
			new RuntimeEntitySchemaResult(
				System.Guid.NewGuid(),
				"Account",
				System.Guid.NewGuid(),
				"Name",
				null,
				[
					new RuntimeEntitySchemaColumnResult(System.Guid.NewGuid(), "Name", "Account name", null, 1, true, false, null)
				]));

		DataForgeContextService service = new(dataForgeClient, maintenanceClient, runtimeEntitySchemaReader);

		// Act
		DataForgeContextAggregationResult result = await service.GetContextAsync(
			new DataForgeContextRequest(
				"customer request",
				["customer request"],
				["industry"],
				[new DataForgeRelationPair("Contact", "Account")]),
			new DataForgeConfigRequest());

		// Assert
		result.Health.CorrelationId.Should().Be("corr-health", because: "the aggregated result should preserve the health probe correlation id");
		result.Status.Status.Should().Be("Ready", because: "the aggregated result should preserve the maintenance status");
		result.SimilarTables.Should().HaveCount(2, because: "duplicate table hits should be deduplicated by schema name");
		result.SimilarLookups.Should().HaveCount(1, because: "duplicate lookup hits should be deduplicated by schema name and value");
		result.Relations.Should().ContainKey("Contact->Account", because: "requested relation pairs should be returned under a deterministic key");
		result.Columns.Should().ContainKey("Contact", because: "successful runtime schema reads should populate the columns dictionary");
		result.Columns["Contact"].Should().HaveCount(1, because: "Data Forge column projection should filter inherited columns after reading the rich runtime schema");
		result.Coverage.Tables.Should().BeTrue(because: "coverage should report tables=true when requested table terms resolve successfully");
		result.Coverage.Lookups.Should().BeTrue(because: "coverage should report lookups=true when requested lookup hints resolve successfully");
		result.Coverage.Relations.Should().BeTrue(because: "coverage should report relations=true when requested relation pairs resolve successfully");
		result.Coverage.Columns.Should().BeTrue(because: "coverage should report columns=true when every distinct table resolved successfully");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns partial results and warnings when one of the table column enrichments fails, while still preserving successful tables and the coverage flags.")]
	public async Task GetContextAsync_Should_Return_Partial_Results_When_Column_Enrichment_Fails() {
		// Arrange
		IDataForgeClient dataForgeClient = Substitute.For<IDataForgeClient>();
		dataForgeClient.CheckHealthAsync(Arg.Any<DataForgeConfigRequest>(), default)
			.Returns(new DataForgeHealthResult(true, true, true, true, "corr-health"));
		dataForgeClient.FindSimilarTablesAsync("customer request", null, Arg.Any<DataForgeConfigRequest>(), default)
			.Returns([
				new SimilarTableResult("Contact", "Contact", "Primary contact"),
				new SimilarTableResult("Account", "Account", "Primary account")
			]);
		dataForgeClient.FindSimilarLookupsAsync(Arg.Any<string>(), null, null, Arg.Any<DataForgeConfigRequest>(), default)
			.Returns(new List<SimilarLookupResult>());

		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		maintenanceClient.GetStatus().Returns(new DataForgeMaintenanceStatusResult(true, "Ready", null));

		IRuntimeEntitySchemaReader runtimeEntitySchemaReader = Substitute.For<IRuntimeEntitySchemaReader>();
		runtimeEntitySchemaReader.GetByName("Contact").Returns(
			new RuntimeEntitySchemaResult(
				System.Guid.NewGuid(),
				"Contact",
				System.Guid.NewGuid(),
				"Name",
				null,
				[new RuntimeEntitySchemaColumnResult(System.Guid.NewGuid(), "Name", "Full name", null, 1, true, false, null)]));
		runtimeEntitySchemaReader.GetByName("Account").Returns(_ => throw new System.InvalidOperationException("runtime schema failed"));

		DataForgeContextService service = new(dataForgeClient, maintenanceClient, runtimeEntitySchemaReader);

		// Act
		DataForgeContextAggregationResult result = await service.GetContextAsync(
			new DataForgeContextRequest(
				"customer request",
				["customer request"],
				null,
				null),
			new DataForgeConfigRequest());

		// Assert
		result.Columns.Should().ContainKey("Contact", because: "successful runtime schema reads should still be preserved when another table fails");
		result.Columns.Should().NotContainKey("Account", because: "failed runtime schema reads should not populate a partial column projection for that table");
		result.Warnings.Should().Contain(warning => warning.Contains("columns:Account:runtime schema failed"),
			because: "failed runtime schema enrichments should be recorded as warnings");
		result.Coverage.Tables.Should().BeTrue(because: "coverage should still report tables=true when requested table terms resolved to distinct tables");
		result.Coverage.Lookups.Should().BeTrue(because: "coverage should report lookups=true when lookup hints were omitted entirely");
		result.Coverage.Relations.Should().BeTrue(because: "coverage should report relations=true when relation pairs were omitted entirely");
		result.Coverage.Columns.Should().BeFalse(
			because: "coverage should report columns=false when not every distinct table resolved successfully");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports false table lookup and relation coverage flags when the caller requested them but no matches or relations were resolved.")]
	public async Task GetContextAsync_Should_Report_False_Coverage_When_Requested_Inputs_Do_Not_Resolve() {
		// Arrange
		IDataForgeClient dataForgeClient = Substitute.For<IDataForgeClient>();
		dataForgeClient.CheckHealthAsync(Arg.Any<DataForgeConfigRequest>(), default)
			.Returns(new DataForgeHealthResult(true, true, true, true, "corr-health"));
		dataForgeClient.FindSimilarTablesAsync("missing table", null, Arg.Any<DataForgeConfigRequest>(), default)
			.Returns(new List<SimilarTableResult>());
		dataForgeClient.FindSimilarLookupsAsync("missing lookup", null, null, Arg.Any<DataForgeConfigRequest>(), default)
			.Returns(new List<SimilarLookupResult>());
		dataForgeClient.GetTableRelationshipsAsync("Contact", "Account", null, Arg.Any<DataForgeConfigRequest>(), default)
			.Returns(Task.FromException<IReadOnlyList<string>>(new System.InvalidOperationException("relations failed")));

		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		maintenanceClient.GetStatus().Returns(new DataForgeMaintenanceStatusResult(true, "Ready", null));

		IRuntimeEntitySchemaReader runtimeEntitySchemaReader = Substitute.For<IRuntimeEntitySchemaReader>();

		DataForgeContextService service = new(dataForgeClient, maintenanceClient, runtimeEntitySchemaReader);

		// Act
		DataForgeContextAggregationResult result = await service.GetContextAsync(
			new DataForgeContextRequest(
				null,
				["missing table"],
				["missing lookup"],
				[new DataForgeRelationPair("Contact", "Account")]),
			new DataForgeConfigRequest());

		// Assert
		result.Coverage.Tables.Should().BeFalse(because: "coverage should report tables=false when explicit table terms returned no matches");
		result.Coverage.Lookups.Should().BeFalse(because: "coverage should report lookups=false when explicit lookup hints returned no matches");
		result.Coverage.Relations.Should().BeFalse(because: "coverage should report relations=false when explicit relation pairs could not be resolved");
		result.Coverage.Columns.Should().BeTrue(because: "coverage should report columns=true when there were no resolved tables to enrich");
		result.Warnings.Should().Contain(warning => warning.Contains("relations:Contact->Account:relations failed"),
			because: "relation resolution failures should be preserved as warnings when coverage falls back to false");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps table lookup relation and column coverage true when the caller omitted all optional discovery inputs.")]
	public async Task GetContextAsync_Should_Report_True_Coverage_When_Optional_Inputs_Are_Omitted() {
		// Arrange
		IDataForgeClient dataForgeClient = Substitute.For<IDataForgeClient>();
		dataForgeClient.CheckHealthAsync(Arg.Any<DataForgeConfigRequest>(), default)
			.Returns(new DataForgeHealthResult(true, true, true, true, "corr-health"));

		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		maintenanceClient.GetStatus().Returns(new DataForgeMaintenanceStatusResult(true, "Ready", null));

		IRuntimeEntitySchemaReader runtimeEntitySchemaReader = Substitute.For<IRuntimeEntitySchemaReader>();

		DataForgeContextService service = new(dataForgeClient, maintenanceClient, runtimeEntitySchemaReader);

		// Act
		DataForgeContextAggregationResult result = await service.GetContextAsync(
			new DataForgeContextRequest(
				null,
				null,
				null,
				null),
			new DataForgeConfigRequest());

		// Assert
		result.SimilarTables.Should().BeEmpty(because: "no candidate terms were provided for table discovery");
		result.SimilarLookups.Should().BeEmpty(because: "no lookup hints were provided for lookup discovery");
		result.Relations.Should().BeEmpty(because: "no relation pairs were provided for relation discovery");
		result.Columns.Should().BeEmpty(because: "no tables were resolved so no runtime column enrichment should run");
		result.Coverage.Tables.Should().BeTrue(because: "coverage should stay true when table terms were omitted entirely");
		result.Coverage.Lookups.Should().BeTrue(because: "coverage should stay true when lookup hints were omitted entirely");
		result.Coverage.Relations.Should().BeTrue(because: "coverage should stay true when relation pairs were omitted entirely");
		result.Coverage.Columns.Should().BeTrue(because: "coverage should stay true when there were no resolved tables to enrich");
	}
}

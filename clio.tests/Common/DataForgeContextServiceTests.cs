using System;
using System.Collections.Generic;
using System.Threading;
using Clio.Common.DataForge;
using Clio.Common.EntitySchema;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
public sealed class DataForgeContextServiceTests {
	[Test]
	[Category("Unit")]
	[Description("Aggregates tables, lookups, relations, and columns from the proxy clients while deduplicating repeated table and lookup matches.")]
	public void GetContext_Should_Aggregate_And_Dedupe_Results() {
		// Arrange
		IDataForgeReadClient readClient = Substitute.For<IDataForgeReadClient>();
		readClient.FindSimilarTables("customer request").Returns([
			new SimilarTableResult("Contact", "Contact", "Primary contact"),
			new SimilarTableResult("Contact", "Contact", "Duplicate contact"),
			new SimilarTableResult("Account", "Account", "Primary account")
		]);
		readClient.FindSimilarLookups("industry").Returns([
			new SimilarLookupResult("lookup-1", "Industry", "Manufacturing", 0.91m),
			new SimilarLookupResult("lookup-2", "Industry", "Manufacturing", 0.88m)
		]);
		readClient.GetTableRelationships("Contact", "Account").Returns(["(Contact)-[:Account]->(Account)"]);
		IRuntimeEntitySchemaReader runtimeReader = Substitute.For<IRuntimeEntitySchemaReader>();
		runtimeReader.GetByName("Contact").Returns(new RuntimeEntitySchemaResult(
			Guid.NewGuid(), "Contact", Guid.NewGuid(), null, null,
			[new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "Name", "Full name", null, 1, true, false, null)]));
		runtimeReader.GetByName("Account").Returns(new RuntimeEntitySchemaResult(
			Guid.NewGuid(), "Account", Guid.NewGuid(), null, null,
			[new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "Name", "Account name", null, 1, true, false, null)]));
		IDataForgeMaintenanceClient maintenanceClient = CreateReadyMaintenanceClient();
		DataForgeContextService service = new(readClient, maintenanceClient, runtimeReader);

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
		result.Columns.Should().ContainKey("Contact", because: "successful proxy column reads should populate the columns dictionary");
		result.Columns["Contact"].Should().HaveCount(1, because: "all proxy column projections for a resolved table should be preserved");
		result.Coverage.Tables.Should().BeTrue(because: "coverage should report tables=true when requested table terms resolve successfully");
		result.Coverage.Lookups.Should().BeTrue(because: "coverage should report lookups=true when requested lookup hints resolve successfully");
		result.Coverage.Relations.Should().BeTrue(because: "coverage should report relations=true when requested relation pairs resolve successfully");
		result.Coverage.Columns.Should().BeTrue(because: "coverage should report columns=true when every distinct table resolved successfully");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns partial results and warnings when one of the proxy column reads fails, while still preserving successful tables and coverage flags.")]
	public void GetContext_Should_Return_Partial_Results_When_Column_Read_Fails() {
		// Arrange
		IDataForgeReadClient readClient = Substitute.For<IDataForgeReadClient>();
		readClient.FindSimilarTables("customer request").Returns([
			new SimilarTableResult("Contact", "Contact", "Primary contact"),
			new SimilarTableResult("Account", "Account", "Primary account")
		]);
		IRuntimeEntitySchemaReader runtimeReader = Substitute.For<IRuntimeEntitySchemaReader>();
		runtimeReader.GetByName("Contact").Returns(new RuntimeEntitySchemaResult(
			Guid.NewGuid(), "Contact", Guid.NewGuid(), null, null,
			[new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "Name", "Full name", null, 1, true, false, null)]));
		runtimeReader.GetByName("Account").Returns(_ => throw new System.InvalidOperationException("proxy column read failed"));
		IDataForgeMaintenanceClient maintenanceClient = CreateReadyMaintenanceClient();
		DataForgeContextService service = new(readClient, maintenanceClient, runtimeReader);

		// Act
		DataForgeContextAggregationResult result = service.GetContext(
			new DataForgeContextRequest(
				"customer request",
				["customer request"],
				null,
				null));

		// Assert
		result.Columns.Should().ContainKey("Contact", because: "successful proxy column reads should still be preserved when another table fails");
		result.Columns.Should().NotContainKey("Account", because: "failed proxy column reads should not populate a partial column projection for that table");
		result.Warnings.Should().Contain(warning => warning.Contains("columns:Account:proxy column read failed"),
			because: "failed proxy column reads should be recorded as warnings");
		result.Coverage.Tables.Should().BeTrue(because: "coverage should still report tables=true when requested table terms resolved to distinct tables");
		result.Coverage.Lookups.Should().BeTrue(because: "coverage should report lookups=true when lookup hints were omitted entirely");
		result.Coverage.Relations.Should().BeTrue(because: "coverage should report relations=true when relation pairs were omitted entirely");
		result.Coverage.Columns.Should().BeFalse(
			because: "coverage should report columns=false when not every distinct table resolved successfully");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports false table lookup and relation coverage flags when the caller requested them but no matches or relations were resolved.")]
	public void GetContext_Should_Report_False_Coverage_When_Requested_Inputs_Do_Not_Resolve() {
		// Arrange
		IDataForgeReadClient readClient = Substitute.For<IDataForgeReadClient>();
		readClient.FindSimilarTables("missing table").Returns(new List<SimilarTableResult>());
		readClient.FindSimilarLookups("missing lookup").Returns(new List<SimilarLookupResult>());
		readClient.GetTableRelationships("Contact", "Account")
			.Returns(_ => throw new System.InvalidOperationException("relations failed"));
		IDataForgeMaintenanceClient maintenanceClient = CreateReadyMaintenanceClient();
		DataForgeContextService service = new(readClient, maintenanceClient, Substitute.For<IRuntimeEntitySchemaReader>());

		// Act
		DataForgeContextAggregationResult result = service.GetContext(
			new DataForgeContextRequest(
				null,
				["missing table"],
				["missing lookup"],
				[new DataForgeRelationPair("Contact", "Account")]));

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
	[Description("Honors cancellation before making DataForge proxy or maintenance calls.")]
	public void GetContext_Should_Respect_Cancellation_Before_Requests() {
		// Arrange
		IDataForgeReadClient readClient = Substitute.For<IDataForgeReadClient>();
		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		DataForgeContextService service = new(readClient, maintenanceClient, Substitute.For<IRuntimeEntitySchemaReader>());
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		// Act
		Action action = () => service.GetContext(new DataForgeContextRequest(null, null, null, null), cancellation.Token);

		// Assert
		action.Should().Throw<OperationCanceledException>(because: "a canceled request should not start DataForge proxy work");
		maintenanceClient.DidNotReceive().GetFullStatus();
		readClient.DidNotReceiveWithAnyArgs().FindSimilarTables(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps table lookup relation and column coverage true when the caller omitted all optional discovery inputs.")]
	public void GetContext_Should_Report_True_Coverage_When_Optional_Inputs_Are_Omitted() {
		// Arrange
		IDataForgeReadClient readClient = Substitute.For<IDataForgeReadClient>();
		IDataForgeMaintenanceClient maintenanceClient = CreateReadyMaintenanceClient();
		DataForgeContextService service = new(readClient, maintenanceClient, Substitute.For<IRuntimeEntitySchemaReader>());

		// Act
		DataForgeContextAggregationResult result = service.GetContext(
			new DataForgeContextRequest(
				null,
				null,
				null,
				null));

		// Assert
		result.SimilarTables.Should().BeEmpty(because: "no candidate terms were provided for table discovery");
		result.SimilarLookups.Should().BeEmpty(because: "no lookup hints were provided for lookup discovery");
		result.Relations.Should().BeEmpty(because: "no relation pairs were provided for relation discovery");
		result.Columns.Should().BeEmpty(because: "no tables were resolved so no column discovery should run");
		result.Coverage.Tables.Should().BeTrue(because: "coverage should stay true when table terms were omitted entirely");
		result.Coverage.Lookups.Should().BeTrue(because: "coverage should stay true when lookup hints were omitted entirely");
		result.Coverage.Relations.Should().BeTrue(because: "coverage should stay true when relation pairs were omitted entirely");
		result.Coverage.Columns.Should().BeTrue(because: "coverage should stay true when there were no resolved tables to enrich");
	}

	private static IDataForgeMaintenanceClient CreateReadyMaintenanceClient() {
		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		maintenanceClient.GetFullStatus().Returns((
			new DataForgeHealthResult(true, true, true, true, "corr-health"),
			new DataForgeMaintenanceStatusResult(true, "Ready", null)));
		return maintenanceClient;
	}
}

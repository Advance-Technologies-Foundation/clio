using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clio.Common.DataForge;
using Clio.Common.EntitySchema;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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

	[Test]
	[Category("Unit")]
	[Description("Collapses one recurring read failure into a single warning naming every affected term, instead of emitting one identical warning per term and burying the diagnosis (issue #948).")]
	public void GetContext_Should_CollapseRepeatedIdenticalReadFailures_IntoOneWarning() {
		// Arrange — the same underlying failure for every term, mirroring an environment where the optional
		// Creatio-side Data Forge service is unconfigured and every search fails identically.
		IDataForgeReadClient readClient = Substitute.For<IDataForgeReadClient>();
		readClient.FindSimilarTables(Arg.Any<string>(), Arg.Any<int?>())
			.Throws(new InvalidOperationException("Value cannot be null. (Parameter 'baseUri')"));
		IRuntimeEntitySchemaReader runtimeReader = Substitute.For<IRuntimeEntitySchemaReader>();
		DataForgeContextService service = new(readClient, CreateReadyMaintenanceClient(), runtimeReader);

		// Act
		DataForgeContextAggregationResult result = service.GetContext(
			new DataForgeContextRequest(null, ["orders", "vendors", "invoices"], [], []),
			CancellationToken.None);

		// Assert
		result.Warnings.Should().HaveCount(1,
			because: "one recurring failure must produce one warning, not one per search term");
		result.Warnings[0].Should().Contain("Value cannot be null. (Parameter 'baseUri')",
			because: "the platform's own diagnosis must be preserved so the cause stays identifiable");
		result.Warnings[0].Should().Contain("orders").And.Contain("vendors").And.Contain("invoices",
			because: "collapsing must stay specific about every term the failure affected");
		result.Warnings[0].Should().StartWith("tables:orders:",
			because: "the first occurrence keeps the original category:item:message shape, so a single-failure "
				+ "payload is unchanged for existing consumers and only repeats are folded in");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps the platform's own message byte-for-byte when it happens to look like clio's collapse marker: collapsed state is tracked structurally, so a cause that both ends in ')' and contains ' (also: ' is never parsed as an already-collapsed line and never has the next term spliced into its own parenthetical.")]
	public void GetContext_Should_PreserveCauseText_WhenMessageItselfLooksLikeTheCollapseMarker() {
		// Arrange — a message crafted to satisfy the old `EndsWith(")") && Contains(" (also: ")` test on the
		// FIRST repeat, which is exactly when the emitted line still carries no collapse marker of its own.
		const string pathologicalMessage = "Load failed (also: check config)";
		IDataForgeReadClient readClient = Substitute.For<IDataForgeReadClient>();
		readClient.FindSimilarTables(Arg.Any<string>(), Arg.Any<int?>())
			.Throws(new InvalidOperationException(pathologicalMessage));
		IRuntimeEntitySchemaReader runtimeReader = Substitute.For<IRuntimeEntitySchemaReader>();
		DataForgeContextService service = new(readClient, CreateReadyMaintenanceClient(), runtimeReader);

		// Act
		DataForgeContextAggregationResult result = service.GetContext(
			new DataForgeContextRequest(null, ["orders", "vendors"], [], []),
			CancellationToken.None);

		// Assert
		result.Warnings.Should().HaveCount(1,
			because: "one recurring failure is still one warning regardless of what its message text looks like");
		result.Warnings[0].Should().Be($"tables:orders:{pathologicalMessage} (also: vendors)",
			because: "the cause text must survive verbatim and the collapse marker must be appended after it — "
				+ "parsing the emitted line instead would have produced "
				+ "'Load failed (also: check config, vendors)', silently rewriting the platform's own message");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps genuinely different read failures as separate warnings, so collapsing repeats never hides a second, distinct cause.")]
	public void GetContext_Should_KeepDistinctReadFailures_AsSeparateWarnings() {
		// Arrange
		IDataForgeReadClient readClient = Substitute.For<IDataForgeReadClient>();
		readClient.FindSimilarTables("orders", Arg.Any<int?>())
			.Throws(new InvalidOperationException("first cause"));
		readClient.FindSimilarTables("vendors", Arg.Any<int?>())
			.Throws(new InvalidOperationException("second cause"));
		IRuntimeEntitySchemaReader runtimeReader = Substitute.For<IRuntimeEntitySchemaReader>();
		DataForgeContextService service = new(readClient, CreateReadyMaintenanceClient(), runtimeReader);

		// Act
		DataForgeContextAggregationResult result = service.GetContext(
			new DataForgeContextRequest(null, ["orders", "vendors"], [], []),
			CancellationToken.None);

		// Assert
		result.Warnings.Should().HaveCount(2,
			because: "two distinct causes are two facts and must both survive the collapse");
	}

	[Test]
	[Category("Unit")]
	[Description("Still performs the Data Forge reads when the maintenance probe reports the subsystem offline: liveness does not predict whether the reads work, so skipping them would discard real results (the sandbox runs with liveness false while table-column reads succeed).")]
	public void GetContext_Should_StillAttemptReads_WhenMaintenanceProbeReportsOffline() {
		// Arrange
		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		maintenanceClient.GetFullStatus().Returns((
			new DataForgeHealthResult(false, false, false, false, "corr-offline"),
			new DataForgeMaintenanceStatusResult(false, "Unavailable", "Empty maintenance status response.")));
		IDataForgeReadClient readClient = Substitute.For<IDataForgeReadClient>();
		readClient.FindSimilarTables("contact", Arg.Any<int?>())
			.Returns([new SimilarTableResult("Contact", "Contact", "Primary contact")]);
		IRuntimeEntitySchemaReader runtimeReader = Substitute.For<IRuntimeEntitySchemaReader>();
		runtimeReader.GetByName("Contact").Returns(new RuntimeEntitySchemaResult(
			Guid.NewGuid(), "Contact", Guid.NewGuid(), null, null,
			[new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "Name", "Full name", null, 1, true, false, null)]));
		DataForgeContextService service = new(readClient, maintenanceClient, runtimeReader);

		// Act
		DataForgeContextAggregationResult result = service.GetContext(
			new DataForgeContextRequest(null, ["contact"], [], []),
			CancellationToken.None);

		// Assert
		result.SimilarTables.Should().ContainSingle(table => table.Name == "Contact",
			because: "a working read must not be discarded because the maintenance probe reported offline");
		result.Columns.Should().ContainKey("Contact",
			because: "column enrichment goes through the runtime schema reader, which is independent of the " +
				"Data Forge subsystem the probe describes");
		result.Health.CorrelationId.Should().Be("corr-offline",
			because: "the health probe result is still reported alongside the successful reads");
	}

	[Test]
	[Category("Unit")]
	[Description("A Decimal(0.01) column reports a numeric data type instead of Text, the defect reported in ENG-93202.")]
	public void MapColumns_Should_Report_Decimal_As_Numeric_Type() {
		// Arrange — dataValueType 32 is Float2, the "Decimal (0.01)" the EntitySchema designer shows. It was
		// absent from the mapper's private table, so it fell through to the "Text" fallback and consumers
		// filtered UsrAmount > 1000 as a lexicographic string comparison.
		RuntimeEntitySchemaResult schema = new(
			Guid.NewGuid(), "UsrClioFilterTest", Guid.NewGuid(), null, null,
			[new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "UsrAmount", "Amount", null, 32, false, false, null)]);

		// Act
		IReadOnlyList<DataForgeColumnResult> columns = DataForgeRuntimeSchemaMapper.MapColumns(schema);

		// Assert
		columns.Should().ContainSingle(because: "the schema declares exactly one non-inherited column");
		columns[0].DataType.Should().NotBe("Text",
			because: "reporting a Decimal column as Text is the defect ENG-93202 reported");
		columns[0].DataType.Should().Be("Float2",
			because: "the canonical registry names dataValueType 32 Float2, which the kind classifier "
				+ "resolves as numeric and the write vocabulary accepts back");
	}

	[TestCase(31, "Float1")]
	[TestCase(32, "Float2")]
	[TestCase(33, "Float3")]
	[TestCase(34, "Float4")]
	[TestCase(40, "Float8")]
	[TestCase(47, "Float0")]
	[TestCase(48, "Money0")]
	[TestCase(49, "Money1")]
	[TestCase(50, "Money3")]
	[Category("Unit")]
	[Description("Every decimal and money scale reports its own type rather than the shared Text fallback.")]
	public void MapColumns_Should_Report_Every_Numeric_Scale(int dataValueType, string expectedType) {
		// Arrange — all nine scales were missing from the private table, so all nine read back as Text.
		RuntimeEntitySchemaResult schema = new(
			Guid.NewGuid(), "UsrClioFilterTest", Guid.NewGuid(), null, null,
			[new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "UsrValue", "Value", null, dataValueType, false, false, null)]);

		// Act
		IReadOnlyList<DataForgeColumnResult> columns = DataForgeRuntimeSchemaMapper.MapColumns(schema);

		// Assert
		columns.Should().ContainSingle(
			because: "the schema declares exactly one non-inherited column, so an empty result must fail with a message rather than throw on indexing");
		columns[0].DataType.Should().Be(expectedType,
			because: $"dataValueType {dataValueType} is a distinct numeric scale and must not collapse into Text");
	}

	[Test]
	[Description("Columns whose types already resolved keep resolving, including the five that a designer-vocabulary fix would have degraded to raw ordinals.")]
	[Category("Unit")]
	public void MapColumns_Should_Preserve_Previously_Working_Types() {
		// Arrange — Float(5), Date(8), Time(9), Enum(11) and HashText(23) are readable but NOT writable by
		// clio, so mapping through the designer's write-scoped friendly vocabulary would have reported them
		// as "5"/"8"/"9"/"11"/"23". The canonical registry covers all 49 codes, so they keep their names.
		RuntimeEntitySchemaResult schema = new(
			Guid.NewGuid(), "UsrClioFilterTest", Guid.NewGuid(), null, null,
			[
				new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "UsrSubject", "Subject", null, 1, true, false, null),
				new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "UsrRate", "Rate", null, 5, false, false, null),
				new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "UsrDueDate", "Due date", null, 8, false, false, null),
				new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "UsrStartTime", "Start time", null, 9, false, false, null),
				new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "UsrKind", "Kind", null, 11, false, false, null),
				new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "UsrHash", "Hash", null, 23, false, false, null),
				new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "UsrStage", "Stage", null, 10, false, false, "UsrStageLookup"),
				new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "UsrIsActive", "Is active", null, 12, false, false, null)
			]);

		// Act
		IReadOnlyList<DataForgeColumnResult> columns = DataForgeRuntimeSchemaMapper.MapColumns(schema);

		// Assert
		columns.Select(column => (column.Name, column.DataType)).Should().Equal([
			("UsrDueDate", "Date"),
			("UsrHash", "HashText"),
			("UsrIsActive", "Boolean"),
			("UsrKind", "Enum"),
			("UsrRate", "Float"),
			("UsrStage", "Lookup"),
			("UsrStartTime", "Time"),
			("UsrSubject", "Text")
		], because: "the fix must widen coverage without regressing any type that already resolved, and the "
			+ "projection stays ordered by column name (Equal, not BeEquivalentTo, so the ordering is asserted)");
		DataForgeColumnResult stage = columns.Single(column => column.Name == "UsrStage");
		stage.Caption.Should().Be("Stage",
			because: "rewriting the projection must not drop the caption");
		stage.ReferenceSchemaName.Should().Be("UsrStageLookup",
			because: "the lookup target travels beside the data type and is the field an agent needs to follow "
				+ "the relation; dropping this argument from the projection would otherwise go unnoticed");
		columns.Single(column => column.Name == "UsrSubject").Required.Should().BeTrue(
			because: "the required flag must survive the projection rewrite");
		columns.Single(column => column.Name == "UsrRate").Required.Should().BeFalse(
			because: "a non-required column must not acquire the flag");
	}

	[Test]
	[Category("Unit")]
	[Description("An unmodelled data value type reports its ordinal rather than the plausible Text fallback that hid the original defect.")]
	public void MapColumns_Should_Report_Ordinal_For_Unmodelled_Type() {
		// Arrange
		RuntimeEntitySchemaResult schema = new(
			Guid.NewGuid(), "UsrClioFilterTest", Guid.NewGuid(), null, null,
			[new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "UsrFuture", "Future", null, 999, false, false, null)]);

		// Act
		IReadOnlyList<DataForgeColumnResult> columns = DataForgeRuntimeSchemaMapper.MapColumns(schema);

		// Assert
		columns.Should().ContainSingle(
			because: "the schema declares exactly one non-inherited column, so an empty result must fail with a message rather than throw on indexing");
		columns[0].DataType.Should().NotBe("Text",
			because: "a plausible fallback made the original wrong answer indistinguishable from a right one");
		columns[0].DataType.Should().Be("999",
			because: "a type clio does not model yet must name the ordinal so the gap is self-diagnosing");
	}

	private static IDataForgeMaintenanceClient CreateReadyMaintenanceClient() {
		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		maintenanceClient.GetFullStatus().Returns((
			new DataForgeHealthResult(true, true, true, true, "corr-health"),
			new DataForgeMaintenanceStatusResult(true, "Ready", null)));
		return maintenanceClient;
	}
}

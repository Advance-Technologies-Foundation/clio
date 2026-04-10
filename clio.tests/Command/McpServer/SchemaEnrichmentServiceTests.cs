using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common.DataForge;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class SchemaEnrichmentServiceTests {

	[Test]
	[Category("Unit")]
	[Description("Builds Data Forge candidate terms from the supplied schema names and returns a compact normalized summary for schema tools.")]
	public async Task EnrichAsync_Should_Build_DataForge_Context_From_Schema_Candidate_Terms() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		IDataForgeContextService contextService = Substitute.For<IDataForgeContextService>();
		DataForgeContextRequest? capturedRequest = null;
		DataForgeConfigRequest? capturedConfig = null;
		commandResolver.Resolve<IDataForgeContextService>(Arg.Any<EnvironmentOptions>())
			.Returns(contextService);
		contextService.GetContextAsync(
				Arg.Any<DataForgeContextRequest>(),
				Arg.Any<DataForgeConfigRequest>(),
				default)
			.Returns(callInfo => {
				capturedRequest = callInfo.Arg<DataForgeContextRequest>();
				capturedConfig = callInfo.ArgAt<DataForgeConfigRequest>(1);
				return new DataForgeContextAggregationResult(
					"corr-id",
					[],
					new DataForgeHealthResult(true, true, true, true, "corr-id"),
					new DataForgeMaintenanceStatusResult(true, "Ready", null),
					[new SimilarTableResult("UsrVehicle", "Vehicle", "Vehicle registry table")],
					[new SimilarLookupResult("lookup-id", "UsrVehicleType", "Sedan", 0.92m)],
					new Dictionary<string, IReadOnlyList<string>> {
						["UsrVehicle->Contact"] = new List<string> { "MATCH (v:UsrVehicle)-[:Owner]->(c:Contact)" }
					},
					new Dictionary<string, IReadOnlyList<DataForgeColumnResult>> {
						["UsrVehicle"] = new List<DataForgeColumnResult> {
							new DataForgeColumnResult("Name", "Name", null, "ShortText", true, null),
							new DataForgeColumnResult("OwnerId", "Owner", null, "Lookup", false, "Contact")
						}
					},
					new DataForgeCoverage(true, true, true, true, true));
			});
		SchemaEnrichmentService sut = new(commandResolver);
		IReadOnlyList<string> candidateTerms = ["UsrVehicle", "Vehicle"];
		IReadOnlyList<string> lookupHints = ["UsrVehicleType"];

		// Act
		ApplicationDataForgeResult result = await sut.EnrichAsync("sandbox", candidateTerms, lookupHints);

		// Assert
		commandResolver.Received(1).Resolve<IDataForgeContextService>(Arg.Is<DataForgeTargetOptions>(options =>
			options.Environment == "sandbox" &&
			options.AllowSysSettingsAuthFallback &&
			options.Scope == "use_enrichment"));
		capturedRequest.Should().NotBeNull(
			because: "the enrichment service should build an aggregated Data Forge context request from the supplied terms");
		capturedRequest!.CandidateTerms.Should().BeEquivalentTo(
			candidateTerms,
			because: "candidate terms should be forwarded verbatim to the Data Forge context service");
		capturedRequest.LookupHints.Should().BeEquivalentTo(
			lookupHints,
			because: "lookup hints should be forwarded verbatim to the Data Forge context service");
		capturedRequest.RequirementSummary.Should().Be("UsrVehicle",
			because: "the first candidate term is used as the requirement summary");
		capturedConfig.Should().NotBeNull(
			because: "the enrichment service should forward Data Forge auth defaults to the context layer");
		capturedConfig!.AllowSysSettingsAuthFallback.Should().BeTrue(
			because: "schema tools should keep the Data Forge syssettings auth fallback enabled by default");
		capturedConfig.Scope.Should().Be("use_enrichment",
			because: "schema tools should use the standard Data Forge OAuth scope");
		result.Used.Should().BeTrue(
			because: "schema tools should always report that the Data Forge enrichment stage ran");
		result.Coverage!.Columns.Should().BeTrue(
			because: "the returned coverage should preserve the aggregated Data Forge coverage flags");
		result.ContextSummary!.SimilarTables.Should().ContainSingle(t => t.Name == "UsrVehicle",
			because: "the compact summary should expose similar table results");
		result.ContextSummary.RelationPairs.Should().Equal(["UsrVehicle->Contact"],
			because: "the compact summary should expose sorted relation keys");
		result.ContextSummary.ColumnHints.Should().ContainSingle(hint =>
			hint.TableName == "UsrVehicle" &&
			hint.ColumnCount == 2 &&
			hint.RequiredColumnCount == 1 &&
			hint.LookupColumnCount == 1,
			because: "the compact summary should normalize per-table column counts");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns degraded Data Forge diagnostics instead of throwing when the context service is unavailable.")]
	public async Task EnrichAsync_Should_Return_Degraded_Result_When_DataForge_Context_Fails() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IDataForgeContextService>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("DataForge unavailable"));
		SchemaEnrichmentService sut = new(commandResolver);

		// Act
		ApplicationDataForgeResult result = await sut.EnrichAsync(
			"sandbox",
			candidateTerms: ["UsrVehicle"],
			lookupHints: null);

		// Assert
		result.Used.Should().BeTrue(
			because: "schema tools should still report that the enrichment stage was attempted");
		result.Health.Should().BeNull(
			because: "the degraded fallback should not invent health diagnostics when the Data Forge call never returned");
		result.Coverage.Should().BeEquivalentTo(
			new DataForgeCoverage(false, false, false, false, false),
			because: "the degraded fallback should mark all Data Forge coverage dimensions as unavailable");
		result.Warnings.Should().ContainSingle(warning => warning.Contains("DataForge unavailable", StringComparison.Ordinal),
			because: "the degraded fallback should preserve the failure reason as a warning instead of throwing");
		result.ContextSummary!.SimilarTables.Should().BeEmpty(
			because: "the degraded fallback should return an empty compact summary when no Data Forge context is available");
	}

	[Test]
	[Category("Unit")]
	[Description("When no lookup hints are provided, forwards an empty list to the Data Forge context service without error.")]
	public async Task EnrichAsync_Should_Use_Empty_LookupHints_When_None_Provided() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		IDataForgeContextService contextService = Substitute.For<IDataForgeContextService>();
		DataForgeContextRequest? capturedRequest = null;
		commandResolver.Resolve<IDataForgeContextService>(Arg.Any<EnvironmentOptions>())
			.Returns(contextService);
		contextService.GetContextAsync(
				Arg.Any<DataForgeContextRequest>(),
				Arg.Any<DataForgeConfigRequest>(),
				default)
			.Returns(callInfo => {
				capturedRequest = callInfo.Arg<DataForgeContextRequest>();
				return new DataForgeContextAggregationResult(
					"corr-id", [], null, null, [], [],
					new Dictionary<string, IReadOnlyList<string>>(),
					new Dictionary<string, IReadOnlyList<DataForgeColumnResult>>(),
					new DataForgeCoverage(false, false, false, false, false));
			});
		SchemaEnrichmentService sut = new(commandResolver);

		// Act
		ApplicationDataForgeResult result = await sut.EnrichAsync("sandbox", ["UsrFoo"], lookupHints: null);

		// Assert
		capturedRequest.Should().NotBeNull(
			because: "the enrichment service should always call the context service even when lookup hints are absent");
		capturedRequest!.LookupHints.Should().BeEmpty(
			because: "null lookupHints should be forwarded as an empty collection, not as null");
		result.Used.Should().BeTrue(
			because: "no lookup hints should not prevent the enrichment from running");
	}
}

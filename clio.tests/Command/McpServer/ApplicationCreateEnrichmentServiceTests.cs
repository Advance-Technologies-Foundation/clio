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
public sealed class ApplicationCreateEnrichmentServiceTests {
	[Test]
	[Category("Unit")]
	[Description("Builds Data Forge candidate terms from the application shell input and returns a compact normalized summary for application-create.")]
	public async Task EnrichAsync_Should_Build_DataForge_Context_From_Application_Create_Input() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		IDataForgeContextService contextService = Substitute.For<IDataForgeContextService>();
		DataForgeContextRequest? capturedRequest = null;
		DataForgeConfigRequest? capturedConfig = null;
		commandResolver.Resolve<IDataForgeContextService>(Arg.Any<EnvironmentOptions>())
			.Returns(contextService);
		contextService.GetContextAsync(Arg.Any<DataForgeContextRequest>(), Arg.Any<DataForgeConfigRequest>(), default)
			.Returns(callInfo => {
				capturedRequest = callInfo.Arg<DataForgeContextRequest>();
				capturedConfig = callInfo.ArgAt<DataForgeConfigRequest>(1);
				return new DataForgeContextAggregationResult(
					"corr-id",
					[],
					new DataForgeHealthResult(true, true, true, true, "corr-id"),
					new DataForgeMaintenanceStatusResult(true, "Ready", null),
					[new SimilarTableResult("Contact", "Contact", "Base contact table")],
					[new SimilarLookupResult("lookup-id", "ContactType", "Customer", 0.87m)],
					new Dictionary<string, IReadOnlyList<string>> {
						["Contact->Account"] = ["MATCH (c:Contact)-[:Account]->(a:Account)"]
					},
					new Dictionary<string, IReadOnlyList<DataForgeColumnResult>> {
						["Contact"] = [
							new DataForgeColumnResult("Name", "Full name", null, "Text", true, null),
							new DataForgeColumnResult("Type", "Type", null, "Lookup", false, "ContactType")
						]
					},
					new DataForgeCoverage(true, true, true, true, true));
			});
		ApplicationCreateEnrichmentService sut = new(commandResolver);

		// Act
		ApplicationDataForgeResult result = await sut.EnrichAsync(
			new ApplicationCreateArgs(
				EnvironmentName: "sandbox",
				Name: "Task App",
				Code: "UsrTaskApp",
				TemplateCode: "AppFreedomUI",
				IconBackground: "#112233",
				Description: "Track customer tasks",
				IconId: null,
				ClientTypeId: null,
				OptionalTemplateDataJson: null),
			new ApplicationOptionalTemplateData(
				EntitySchemaName: "UsrTask",
				UseExistingEntitySchema: false,
				UseAiContentGeneration: false,
				AppSectionDescription: "Task registry"));

		// Assert
		commandResolver.Received(1).Resolve<IDataForgeContextService>(Arg.Is<DataForgeTargetOptions>(options =>
			options.Environment == "sandbox" &&
			options.AllowSysSettingsAuthFallback &&
			options.Scope == "use_enrichment"));
		capturedRequest.Should().NotBeNull(
			because: "the enrichment service should build an aggregated Data Forge context request from the application shell input");
		capturedRequest!.RequirementSummary.Should().Be("Track customer tasks",
			because: "the application description should become the requirement summary when it is available");
		capturedRequest.CandidateTerms.Should().BeEquivalentTo(
			new[] { "Task App", "Track customer tasks", "Task registry", "UsrTask" },
			because: "application-create should search Data Forge using the app name, description, section description, and canonical entity hint");
		capturedRequest.LookupHints.Should().BeEquivalentTo(
			new[] { "UsrTask", "Task registry", "Task App" },
			because: "lookup hints should prefer the entity/schema and section naming context");
		capturedConfig.Should().NotBeNull(
			because: "the enrichment service should forward explicit Data Forge auth defaults to the context layer");
		capturedConfig!.AllowSysSettingsAuthFallback.Should().BeTrue(
			because: "application-create should keep the Data Forge syssettings auth fallback enabled by default");
		capturedConfig.Scope.Should().Be("use_enrichment",
			because: "application-create should use the standard Data Forge OAuth scope");
		result.Used.Should().BeTrue(
			because: "application-create should always report that the Data Forge enrichment stage ran");
		result.Coverage!.Columns.Should().BeTrue(
			because: "the returned coverage should preserve the aggregated Data Forge coverage flags");
		result.ContextSummary!.RelationPairs.Should().Equal(new[] { "Contact->Account" },
			because: "the compact summary should expose relation keys instead of the full relation payload");
		result.ContextSummary.ColumnHints.Should().ContainSingle(hint =>
			hint.TableName == "Contact" &&
			hint.ColumnCount == 2 &&
			hint.RequiredColumnCount == 1 &&
			hint.LookupColumnCount == 1,
			because: "the compact summary should normalize per-table column counts for application-create callers");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns degraded Data Forge diagnostics instead of throwing when the environment-scoped enrichment flow cannot resolve Data Forge context.")]
	public async Task EnrichAsync_Should_Return_Degraded_Result_When_DataForge_Context_Fails() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IDataForgeContextService>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("DataForge unavailable"));
		ApplicationCreateEnrichmentService sut = new(commandResolver);

		// Act
		ApplicationDataForgeResult result = await sut.EnrichAsync(
			new ApplicationCreateArgs(
				EnvironmentName: "sandbox",
				Name: "Task App",
				Code: "UsrTaskApp",
				TemplateCode: "AppFreedomUI",
				IconBackground: "#112233"),
			optionalTemplateData: null);

		// Assert
		result.Used.Should().BeTrue(
			because: "application-create should still report that it attempted the Data Forge enrichment stage");
		result.Health.Should().BeNull(
			because: "the degraded fallback should not invent health diagnostics when the Data Forge call never returned");
		result.Coverage.Should().BeEquivalentTo(new DataForgeCoverage(false, false, false, false, false),
			because: "the degraded fallback should mark all Data Forge coverage dimensions as unavailable");
		result.Warnings.Should().ContainSingle(warning => warning.Contains("DataForge unavailable", StringComparison.Ordinal),
			because: "the degraded fallback should preserve the failure reason as a warning instead of throwing");
		result.ContextSummary!.SimilarTables.Should().BeEmpty(
			because: "the degraded fallback should return an empty compact summary when no Data Forge context is available");
	}
}

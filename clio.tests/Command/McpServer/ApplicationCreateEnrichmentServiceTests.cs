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
	[Description("Builds a normalized Data Forge enrichment request from the application shell input and forwards it to the shared builder.")]
	public async Task EnrichAsync_Should_Build_Request_From_Application_Create_Input() {
		// Arrange
		IDataForgeEnrichmentBuilder enrichmentBuilder = Substitute.For<IDataForgeEnrichmentBuilder>();
		DataForgeEnrichmentRequest? capturedRequest = null;
		ApplicationDataForgeResult expected = new(
			Used: true,
			Health: new DataForgeHealthResult(true, true, true, true, "corr-id"),
			Status: new DataForgeMaintenanceStatusResult(true, "Ready", null),
			Coverage: new DataForgeCoverage(true, true, true, true, true),
			Warnings: [],
			ContextSummary: new ApplicationDataForgeContextSummary([], [], [], []));
		enrichmentBuilder.BuildAsync(Arg.Any<DataForgeEnrichmentRequest>(), default)
			.Returns(callInfo => {
				capturedRequest = callInfo.Arg<DataForgeEnrichmentRequest>();
				return expected;
			});
		ApplicationCreateEnrichmentService sut = new(enrichmentBuilder);

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
		capturedRequest.Should().NotBeNull(
			because: "the application enrichment service should normalize app-create input into one shared enrichment request");
		capturedRequest.Should().NotBeNull(
			because: "the application shell input should be forwarded to the shared builder");
		capturedRequest!.RequirementSummary.Should().Be("Track customer tasks",
			because: "the application description should become the requirement summary when it is available");
		capturedRequest.CandidateTerms.Should().BeEquivalentTo(
			new[] { "Task App", "Track customer tasks", "Task registry", "UsrTask" },
			because: "application-create should search Data Forge using the app name, description, section description, and canonical entity hint");
		capturedRequest.LookupHints.Should().BeEquivalentTo(
			new[] { "UsrTask", "Task registry", "Task App" },
			because: "lookup hints should prefer the entity/schema and section naming context");
		result.Should().BeSameAs(expected,
			because: "the service should return the exact result produced by the shared builder");
	}

	[Test]
	[Category("Unit")]
	[Description("When the description is absent, uses the section description as the requirement summary for the shared builder request.")]
	public async Task EnrichAsync_Should_Fall_Back_To_Section_Description_For_RequirementSummary() {
		// Arrange
		IDataForgeEnrichmentBuilder enrichmentBuilder = Substitute.For<IDataForgeEnrichmentBuilder>();
		DataForgeEnrichmentRequest? capturedRequest = null;
		enrichmentBuilder.BuildAsync(Arg.Any<DataForgeEnrichmentRequest>(), default)
			.Returns(callInfo => {
				capturedRequest = callInfo.Arg<DataForgeEnrichmentRequest>();
				return new ApplicationDataForgeResult(
					Used: true,
					Health: null,
					Status: null,
					Coverage: new DataForgeCoverage(false, false, false, false, false),
					Warnings: [],
					ContextSummary: new ApplicationDataForgeContextSummary([], [], [], []));
			});
		ApplicationCreateEnrichmentService sut = new(enrichmentBuilder);

		// Act
		_ = await sut.EnrichAsync(
			new ApplicationCreateArgs(
				EnvironmentName: "sandbox",
				Name: "Task App",
				Code: "UsrTaskApp",
				TemplateCode: "AppFreedomUI",
				IconBackground: "#112233",
				Description: null),
			new ApplicationOptionalTemplateData(
				EntitySchemaName: "UsrTask",
				UseExistingEntitySchema: false,
				UseAiContentGeneration: false,
				AppSectionDescription: "Task registry"));

		// Assert
		capturedRequest.Should().NotBeNull(
			because: "the service should still build one normalized enrichment request when no app description is provided");
		capturedRequest!.RequirementSummary.Should().Be("Task registry",
			because: "the section description should become the fallback requirement summary when the app description is absent");
	}
}

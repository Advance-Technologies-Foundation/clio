using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
	[Description("Builds one normalized Data Forge enrichment request from the supplied schema candidate terms and forwards it to the shared builder.")]
	public async Task EnrichAsync_Should_Build_Request_From_Schema_Candidate_Terms() {
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
		enrichmentBuilder.BuildAsync(
				Arg.Any<DataForgeEnrichmentRequest>(),
				default)
			.Returns(callInfo => {
				capturedRequest = callInfo.Arg<DataForgeEnrichmentRequest>();
				return expected;
			});
		SchemaEnrichmentService sut = new(enrichmentBuilder);
		IReadOnlyList<string> candidateTerms = ["UsrVehicle", "Vehicle"];
		IReadOnlyList<string> lookupHints = ["UsrVehicleType"];

		// Act
		ApplicationDataForgeResult result = await sut.EnrichAsync("sandbox", candidateTerms, lookupHints);

		// Assert
		capturedRequest.Should().NotBeNull(
			because: "schema candidate terms should be normalized into one shared enrichment request");
		capturedRequest!.CandidateTerms.Should().BeEquivalentTo(
			candidateTerms,
			because: "candidate terms should be forwarded verbatim to the shared builder");
		capturedRequest.LookupHints.Should().BeEquivalentTo(
			lookupHints,
			because: "lookup hints should be forwarded verbatim to the shared builder");
		capturedRequest.RequirementSummary.Should().Be("UsrVehicle",
			because: "the first candidate term is used as the requirement summary");
		result.Should().BeSameAs(expected,
			because: "schema tools should return the exact result produced by the shared builder");
	}

	[Test]
	[Category("Unit")]
	[Description("When no lookup hints are supplied, forwards an empty lookup-hint list to the shared builder.")]
	public async Task EnrichAsync_Should_Forward_Empty_LookupHints_When_None_Provided() {
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
		SchemaEnrichmentService sut = new(enrichmentBuilder);

		// Act
		_ = await sut.EnrichAsync(
			"sandbox",
			candidateTerms: ["UsrVehicle"],
			lookupHints: null);

		// Assert
		capturedRequest.Should().NotBeNull(
			because: "schema tools should still build a normalized request when lookup hints are absent");
		capturedRequest!.LookupHints.Should().BeEmpty(
			because: "null lookup hints should be normalized to an empty collection before calling the builder");
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards the target environment name unchanged to the shared builder request.")]
	public async Task EnrichAsync_Should_Forward_EnvironmentName_To_Shared_Builder() {
		// Arrange
		IDataForgeEnrichmentBuilder enrichmentBuilder = Substitute.For<IDataForgeEnrichmentBuilder>();
		DataForgeEnrichmentRequest? capturedRequest = null;
		enrichmentBuilder.BuildAsync(
				Arg.Any<DataForgeEnrichmentRequest>(),
				default)
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
		SchemaEnrichmentService sut = new(enrichmentBuilder);

		// Act
		_ = await sut.EnrichAsync("sandbox", ["UsrFoo"], lookupHints: null);

		// Assert
		capturedRequest.Should().NotBeNull(
			because: "the service should build one normalized builder request for every schema enrichment call");
		capturedRequest!.EnvironmentName.Should().Be("sandbox",
			because: "environment-scoped schema enrichment must preserve the target environment when delegating to the shared builder");
	}
}

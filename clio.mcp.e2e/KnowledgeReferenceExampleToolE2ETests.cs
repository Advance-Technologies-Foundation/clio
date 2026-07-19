using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Knowledge;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end contract coverage for reference-example discovery from a signed local catalog.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(KnowledgeManagementTools.ListKnowledgeExamplesToolName)]
[NonParallelizable]
public sealed class KnowledgeReferenceExampleToolE2ETests : McpContractFixtureBase {
	private readonly SyntheticKnowledgeNuGetFixture _fixture;
	private readonly SyntheticPackageEvidence _package;

	public KnowledgeReferenceExampleToolE2ETests() {
		_fixture = SyntheticKnowledgeNuGetFixture.Create();
		_package = _fixture.PublishValid("1.0.0", sequence: 10, revision: "reference-example");
	}

	[OneTimeTearDown]
	public void OneTimeTearDown() {
		_fixture.Dispose();
	}

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome(
			"{}",
			"knowledge-reference-examples");
	}

	[Test]
	[AllureTag(KnowledgeManagementTools.ListKnowledgeExamplesToolName)]
	[AllureName("list-knowledge-examples returns registered repository coordinates from local cache")]
	[AllureDescription("Installs a generated signed catalog, then verifies the real MCP tool returns its reference example without another transport request or cloning the referenced repository.")]
	[Description("Discovers registered example metadata from installed knowledge without fetching the leaf repository.")]
	public async Task ListKnowledgeExamples_ShouldReturnRegisteredMetadata_WithoutRemoteExampleFetch() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(2));
		CallToolResult addResult = await context.Session.CallToolAsync(
			KnowledgeManagementTools.AddKnowledgeSourceToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["alias"] = "synthetic",
					["libraryId"] = SyntheticKnowledgeNuGetFixture.LibraryId,
					["type"] = "nuget",
					["location"] = _fixture.Feed.ServiceIndexUri.AbsoluteUri,
					["packageId"] = SyntheticKnowledgeNuGetFixture.PackageId,
					["trustedKeyId"] = _fixture.KeyId,
					["trustedPublicKeyPath"] = _fixture.PublicKeyPath,
					["enabled"] = true,
					["priority"] = 100,
					["participation"] = "authoritative",
					["confirmed"] = true
				}
			},
			context.CancellationTokenSource.Token);
		CallToolResult installResult = await context.Session.CallToolAsync(
			KnowledgeManagementTools.InstallKnowledgeToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["source"] = "synthetic" }
			},
			context.CancellationTokenSource.Token);
		int completedTransportRequests = _fixture.Feed.CompletedRequests.Count;

		// Act
		CallToolResult listResult = await context.Session.CallToolAsync(
			KnowledgeManagementTools.ListKnowledgeExamplesToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["capability"] = "native-library-lifecycle" }
			},
			context.CancellationTokenSource.Token);
		string serialized = JsonSerializer.Serialize(listResult);

		// Assert
		AssertToolSucceeded(addResult,
			"the generated publisher trust root should be accepted by the real MCP process");
		AssertToolSucceeded(installResult,
			"the generated signed catalog should install through the real NuGet transport");
		AssertToolSucceeded(listResult,
			$"locally cached catalog discovery should succeed: {serialized}");
		AssertResultContains(serialized, SyntheticKnowledgeNuGetFixture.ReferenceExampleId,
			"the registered example identity must cross the full MCP delivery path");
		AssertResultContains(serialized, SyntheticKnowledgeNuGetFixture.ReferenceExampleRepository,
			"agents need the repository URL before deciding whether to clone the example");
		AssertResultContains(serialized, _package.ReferenceExampleRevision,
			"agents need the immutable revision before deciding whether to clone the example");
		AssertTransportWasNotContacted(completedTransportRequests, _fixture.Feed.CompletedRequests.Count);
	}

	[AllureStep("Assert MCP tool call completed without a protocol error")]
	private static void AssertToolSucceeded(CallToolResult result, string reason) {
		result.IsError.Should().NotBeTrue(because: reason);
	}

	[AllureStep("Assert structured example result contains '{expected}'")]
	private static void AssertResultContains(string serialized, string expected, string reason) {
		serialized.Should().Contain(expected, because: reason);
	}

	[AllureStep("Assert example discovery did not contact a transport")]
	private static void AssertTransportWasNotContacted(int expectedRequests, int actualRequests) {
		actualRequests.Should().Be(expectedRequests,
			because: "listing cached example metadata must not contact the knowledge transport or leaf repository");
	}
}

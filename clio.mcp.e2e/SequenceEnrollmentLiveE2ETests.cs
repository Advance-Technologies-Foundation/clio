using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>Opt-in native enrollment against explicitly selected disposable lab records.</summary>
[TestFixture, Category("McpE2E.Manual"), AllureNUnit, NonParallelizable]
[AllureFeature(SequenceEnrollmentTool.ToolName)]
public sealed class SequenceEnrollmentLiveE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Submits one explicitly selected lab contact and verifies native counts plus persisted status readback over MCP.")]
	public async Task EnrollSelectedLabContact_ReadsNativeResult() {
		// Arrange
		var settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		if (!Guid.TryParse(Environment.GetEnvironmentVariable("CLIO_SEQUENCE_TEST_SEQUENCE_ID"), out Guid sequenceId)
			|| !Guid.TryParse(Environment.GetEnvironmentVariable("CLIO_SEQUENCE_TEST_CONTACT_ID"), out Guid contactId)) {
			Assert.Ignore("Set CLIO_SEQUENCE_TEST_SEQUENCE_ID and CLIO_SEQUENCE_TEST_CONTACT_ID to disposable lab records. This test submits enrollment.");
			return;
		}
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
		// Act
		var call = await Session.CallToolAsync(SequenceEnrollmentTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> {
				["environment-name"] = settings.Sandbox.EnvironmentName, ["sequence-id"] = sequenceId,
				["contact-ids"] = new[] { contactId }
			}}, timeout.Token);
		var result = EntitySchemaStructuredResultParser.Extract<SequenceEnrollmentResult>(call);
		// Assert
		call.IsError.Should().NotBeTrue(because: "the configured lab request must execute through the real server");
		result.Diagnostic.Should().NotBeNull(because: "native enrollment includes safe submission context");
		result.Diagnostic.TransportOutcome.Should().Be("response-received", because: "the native service returned its counts");
		result.Completion.Should().Be("completed", because: "the native service must return a compatible response");
		result.AddedCount.Should().NotBeNull(because: "native applied counts must be available even on rejection");
		result.FailedCount.Should().NotBeNull(because: "partial failures must remain observable");
		result.Readback.State.Should().Be("complete", because: "the lab caller must be able to inspect current participant statuses");
		result.Readback.Participants.Should().NotBeEmpty(because: "an accepted or previously enrolled lab contact must have persisted readback");
		result.Readback.Participants.Should().OnlyContain(row => row.GetProperty("contact-id").GetGuid() == contactId,
			because: "readback must never include contacts outside the explicit selection");
	}
}

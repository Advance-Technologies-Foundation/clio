using FluentAssertions;

namespace Clio.Mcp.E2E.Support.Configuration;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
public sealed class ClioCliCommandRunnerEnvelopeTests {
	[TestCase("{\"success\":true}")]
	[TestCase("{\"schemaVersion\":\"1.0\",\"ok\":true,\"data\":[]}")]
	[Description("Recognizes both legacy success and canonical ok flags in structured CLI envelopes.")]
	public void TryReadSuccessFlag_Should_Accept_Supported_Envelope_Fields(string json) {
		// Arrange

		// Act
		bool parsed = ClioCliCommandRunner.TryReadSuccessFlag(json, out bool success);

		// Assert
		parsed.Should().BeTrue(
			because: "the E2E readiness harness must understand both supported CLI envelope generations");
		success.Should().BeTrue(
			because: "the supplied structured envelope reports a successful command");
	}
}

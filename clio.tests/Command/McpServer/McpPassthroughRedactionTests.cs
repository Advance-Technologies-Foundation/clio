using System.Collections.Generic;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// FR-11 (ENG-93208, review): the self-managed-capture tools (compile-creatio, add-item-model,
/// sync-schemas, create-lookup) build their own result from the log buffer and must run it through the
/// shared <see cref="McpPassthroughRedaction"/> so a passthrough log line cannot leak the target host/URI
/// or a token across the MCP boundary. Off passthrough the snapshot is untouched.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class McpPassthroughRedactionTests {

	private const string Secret = "SUPER-SECRET-XYZ";
	private const string SecretLogLine =
		"POST https://tenant.creatio.com/0/ServiceModel/EntitySchemaService.svc — Authorization: Bearer SUPER-SECRET-XYZ";

	[Test]
	[Description("On a passthrough tenant key, SanitizeAndRedact scrubs the Bearer token (and target URI) from a self-captured log line before it crosses the MCP boundary (FR-11).")]
	public void SanitizeAndRedact_ShouldRedactSecret_WhenTenantKeyIsPassthrough() {
		// Arrange
		string passthroughKey = $"{ToolCommandResolver.PassthroughKeyPrefix}https://tenant.creatio.com:HASH";
		List<LogMessage> messages = [new InfoMessage(SecretLogLine)];

		// Act
		McpPassthroughRedaction.SanitizeAndRedact(messages, passthroughKey);

		// Assert
		(messages[0].Value?.ToString() ?? string.Empty).Should().NotContain(Secret,
			because: "a passthrough request must not leak the Bearer token across the MCP boundary (FR-11)");
	}

	[Test]
	[Description("Off passthrough (a registry/URI or fallback key), SanitizeAndRedact leaves the log line intact so the trusted stdio / -e path keeps full-fidelity diagnostics.")]
	public void SanitizeAndRedact_ShouldPreserveSecret_WhenTenantKeyIsNotPassthrough() {
		// Arrange
		List<LogMessage> messages = [new InfoMessage(SecretLogLine)];

		// Act
		McpPassthroughRedaction.SanitizeAndRedact(messages, "registry:acme-prod");

		// Assert
		(messages[0].Value?.ToString() ?? string.Empty).Should().Contain(Secret,
			because: "off a passthrough request the trusted path keeps full-fidelity logs (no redaction)");
	}

	[Test]
	[Description("SanitizeForSerialization projects a non-string log value to its string form so the MCP envelope serializes without throwing, independent of passthrough.")]
	public void SanitizeAndRedact_ShouldStringifyNonStringValue_Always() {
		// Arrange
		LogMessage nonString = new InfoMessage("placeholder") { Value = 42 };
		List<LogMessage> messages = [nonString];

		// Act
		McpPassthroughRedaction.SanitizeAndRedact(messages, "registry:acme-prod");

		// Assert
		messages[0].Value.Should().BeOfType<string>(
			because: "a non-string value must be projected to its rendered string form before serialization");
		messages[0].Value.Should().Be("42",
			because: "the stringified value preserves the rendered content");
	}
}

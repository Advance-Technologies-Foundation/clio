using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Unit tests for the pure <see cref="TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition"/>
/// and <see cref="TransientPlatformConditionRetryGate.IsLoginRejection"/> classification predicates.
/// They construct <see cref="CallToolResult"/> payloads in-memory (no MCP server, no stand, no network
/// I/O), so they validate the retry-vs-real-failure contract locally and are categorized <c>Unit</c>
/// rather than <c>McpE2E.Sandbox</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class TransientPlatformConditionRetryGateTests {
	private static CallToolResult TextResult(string text) =>
		new() { IsError = true, Content = [new TextContentBlock { Text = text }] };

	[Test]
	[Description("Recognizes the OData-rebuild-window message as a known transient platform condition.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnTrue_ForODataRebuildMessage() {
		// Arrange
		CallToolResult result = TextResult("Creatio is currently rebuilding the OData library, try again later.");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeTrue(
			because: "the OData rebuild window is one of the three documented known transient platform conditions");
	}

	[Test]
	[Description("Recognizes the LoginDiagnostics login-rejection prefix as a known transient platform condition.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnTrue_ForLoginRejectionMessage() {
		// Arrange
		CallToolResult result = TextResult("Unauthorized someuser for https://example.creatio.com [clio-login kind=implicit]");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeTrue(
			because: "a message starting with the LoginDiagnostics rejection prefix is a known transient platform condition");
	}

	[Test]
	[Description("Recognizes the ServiceResponseJsonGuard HTML-page and login-page-redirect wording as a known transient platform condition.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnTrue_ForHtmlPageMessage() {
		// Arrange
		CallToolResult result = TextResult(
			"create-app returned an HTML page instead of JSON (URL: https://example.creatio.com/0/rest). "
			+ "The request was most likely redirected to a login page, or the server raised an unhandled error.");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeTrue(
			because: "the HTML-page-instead-of-JSON / redirected-to-a-login-page wording is a known transient platform condition");
	}

	[Test]
	[Description("Does not treat a failed data assertion / business-rule message as a known transient platform condition.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnFalse_ForBusinessRuleFailure() {
		// Arrange
		CallToolResult result = TextResult("success:false, error: an application with this code already exists.");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeFalse(
			because: "a business-rule failure is a real, repeatable outcome and must not be retried as if it were a platform hiccup");
	}

	[Test]
	[Description("Does not treat a contention error-class message as a known transient platform condition.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnFalse_ForContentionErrorClass() {
		// Arrange
		CallToolResult result = TextResult("success:false, error-class=contention, error: the record is locked by another process.");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeFalse(
			because: "contention has its own dedicated handling elsewhere in the harness and is not one of the three known transient platform conditions this gate matches");
	}

	[Test]
	[Description("Reports null as not a known transient platform condition.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnFalse_ForNullResult() {
		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(null);

		// Assert
		isTransient.Should().BeFalse(
			because: "there is no payload to classify, so it cannot be a known transient platform condition");
	}

	[Test]
	[Description("Identifies the login-rejection signature specifically, distinct from the other known transient conditions.")]
	public void IsLoginRejection_ShouldReturnTrue_OnlyForLoginRejectionMessage() {
		// Arrange
		CallToolResult loginRejection = TextResult("Unauthorized someuser for https://example.creatio.com");
		CallToolResult odataRebuild = TextResult("Creatio is currently rebuilding the OData library.");

		// Act
		bool loginRejectionIsLoginRejection = TransientPlatformConditionRetryGate.IsLoginRejection(loginRejection);
		bool odataRebuildIsLoginRejection = TransientPlatformConditionRetryGate.IsLoginRejection(odataRebuild);

		// Assert
		loginRejectionIsLoginRejection.Should().BeTrue(
			because: "the message carries the LoginDiagnostics login-rejection prefix");
		odataRebuildIsLoginRejection.Should().BeFalse(
			because: "the OData rebuild window is a known transient condition but not specifically a login rejection, so it must not trigger re-authentication");
	}
}

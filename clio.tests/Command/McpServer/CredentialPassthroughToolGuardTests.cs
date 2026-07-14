using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit tests for <see cref="ICredentialPassthroughToolGuard"/> and the
/// <c>BaseTool.RejectIfPassthroughUnsupported</c> helper in isolation (Story 1, ENG-93347):
/// active vs inactive passthrough, uniform message shape, and no secret leakage.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public class CredentialPassthroughToolGuardTests {

	private const string ToolName = "link-from-repository-by-environment";
	private const string AlternativeGuidance =
		"Register the target environment and use the stdio path, or a non-passthrough mcp-http request.";

	private static CredentialContext CreatePassthroughContext() =>
		new(
			"https://tenant.example.com",
			CredentialMaterial.FromAccessToken("super-secret-token", "Bearer"),
			false,
			McpTransport.Http,
			PassthroughModeEnabled: true);

	[Test]
	[Category("Unit")]
	[Description("The guard reports an active passthrough when the credential-context accessor carries a per-request context (authorized X-Integration-Credentials header).")]
	public void IsPassthroughActive_ShouldBeTrue_WhenCredentialContextIsPresent() {
		// Arrange
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		accessor.Current.Returns(CreatePassthroughContext());
		CredentialPassthroughToolGuard guard = new(accessor);

		// Act
		bool isActive = guard.IsPassthroughActive;

		// Assert
		isActive.Should().BeTrue(
			because: "a non-null CredentialContext is the ENG-93208 signal that credential passthrough is active");
	}

	[Test]
	[Category("Unit")]
	[Description("The guard reports no active passthrough when the credential-context accessor returns null (stdio transport, or an HTTP request without a credential header).")]
	public void IsPassthroughActive_ShouldBeFalse_WhenCredentialContextIsAbsent() {
		// Arrange
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		accessor.Current.Returns((CredentialContext)null);
		CredentialPassthroughToolGuard guard = new(accessor);

		// Act
		bool isActive = guard.IsPassthroughActive;

		// Assert
		isActive.Should().BeFalse(
			because: "a null CredentialContext means stdio or a registered-environment HTTP call, where the guard must never fire");
	}

	[Test]
	[Category("Unit")]
	[Description("The uniform rejection message names the rejected tool, states 'not supported under credential passthrough', and carries the supported-alternative guidance (FR-04).")]
	public void BuildUnsupportedMessage_ShouldNameToolAndAlternative_WhenCalled() {
		// Arrange
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		CredentialPassthroughToolGuard guard = new(accessor);

		// Act
		string message = guard.BuildUnsupportedMessage(ToolName, AlternativeGuidance);

		// Assert
		message.Should().Contain(ToolName,
			because: "the uniform FR-04 message must name the rejected tool so the caller knows which call was refused");
		message.Should().Contain("not supported under credential passthrough",
			because: "every tool-level fail-fast path must reuse the single uniform message shape");
		message.Should().Contain(AlternativeGuidance,
			because: "the message must point the caller at the supported alternative (register the environment / stdio path)");
	}

	[Test]
	[Category("Unit")]
	[Description("The uniform rejection message never echoes credential material from the active passthrough context (FR-04 secret hygiene, AC-ERR).")]
	public void BuildUnsupportedMessage_ShouldNotLeakCredentialMaterial_WhenPassthroughContextCarriesSecrets() {
		// Arrange
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		accessor.Current.Returns(CreatePassthroughContext());
		CredentialPassthroughToolGuard guard = new(accessor);

		// Act
		string message = guard.BuildUnsupportedMessage(ToolName, AlternativeGuidance);

		// Assert
		message.Should().NotContain("super-secret-token",
			because: "the rejection message must never echo the access token from the credential header");
		message.Should().NotContain("tenant.example.com",
			because: "the rejection message must not disclose the passthrough target URL either");
	}

	[Test]
	[Category("Unit")]
	[Description("BaseTool.RejectIfPassthroughUnsupported returns null when no passthrough context is active, so the calling tool proceeds with its normal execution path.")]
	public void RejectIfPassthroughUnsupported_ShouldReturnNull_WhenPassthroughIsInactive() {
		// Arrange
		ICredentialPassthroughToolGuard guard = Substitute.For<ICredentialPassthroughToolGuard>();
		guard.IsPassthroughActive.Returns(false);
		ProbeTool probe = new(guard);

		// Act
		CommandExecutionResult result = probe.Probe();

		// Assert
		result.Should().BeNull(
			because: "an inactive passthrough context must not fire the guard, letting the tool proceed normally");
		// No rejection message may be built when the guard does not fire.
		guard.DidNotReceive().BuildUnsupportedMessage(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("BaseTool.RejectIfPassthroughUnsupported returns null when no guard is wired (stdio direct construction), preserving pre-change behavior for hosts without the passthrough seam.")]
	public void RejectIfPassthroughUnsupported_ShouldReturnNull_WhenGuardIsNotWired() {
		// Arrange
		ProbeTool probe = new(passthroughGuard: null);

		// Act
		CommandExecutionResult result = probe.Probe();

		// Assert
		result.Should().BeNull(
			because: "a tool constructed without the guard (stdio path) must behave exactly as before the guard existed");
	}

	[Test]
	[Category("Unit")]
	[Description("BaseTool.RejectIfPassthroughUnsupported returns a typed exit-code-1 rejection carrying the guard's uniform message when passthrough is active (FR-04, AC-ERR envelope).")]
	public void RejectIfPassthroughUnsupported_ShouldReturnTypedRejection_WhenPassthroughIsActive() {
		// Arrange
		ICredentialPassthroughToolGuard guard = Substitute.For<ICredentialPassthroughToolGuard>();
		guard.IsPassthroughActive.Returns(true);
		guard.BuildUnsupportedMessage(ToolName, AlternativeGuidance)
			.Returns($"Tool '{ToolName}' is not supported under credential passthrough. {AlternativeGuidance}");
		ProbeTool probe = new(guard);

		// Act
		CommandExecutionResult result = probe.Probe();

		// Assert
		result.Should().NotBeNull(
			because: "an active passthrough context must produce the typed rejection envelope");
		result.ExitCode.Should().Be(1,
			because: "a guard rejection is an EXPECTED, caller-actionable refusal per the MCP exit-code contract (ENG-91825)");
		result.Output.Should().ContainSingle(logMessage =>
				logMessage.Value.ToString().Contains("not supported under credential passthrough"),
			because: "the rejection envelope must carry the single uniform FR-04 message");
	}

	/// <summary>
	/// Minimal <see cref="BaseTool{T}"/> subclass exposing <c>RejectIfPassthroughUnsupported</c> so the
	/// helper is testable in isolation from any concrete tool.
	/// </summary>
	private sealed class ProbeTool(ICredentialPassthroughToolGuard passthroughGuard)
		: BaseTool<Link4RepoOptions>(null, ConsoleLogger.Instance, passthroughGuard: passthroughGuard) {

		public CommandExecutionResult Probe() =>
			RejectIfPassthroughUnsupported(ToolName, AlternativeGuidance);
	}
}

using System;
using Clio;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// FR-19 (ENG-93208): on the multi-tenant HTTP edge with credential passthrough enabled, explicit
/// plaintext credential/environment tool-args must be REJECTED (they are a smuggling vector — the
/// credential belongs in the <c>X-Integration-Credentials</c> header, and the passthrough branch would
/// otherwise drop them silently). The check is mode-scoped: it fires only when the per-request
/// <see cref="ICredentialContextAccessor.Current"/> reports <see cref="McpTransport.Http"/> with
/// passthrough enabled, so stdio and default HTTP (no per-request context) keep honoring args exactly as
/// 8.1.0.72 (AC-02/AC-03). Rejection messages are secret-free (AC-ERR/FR-11).
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class TransportArgPolicyTests {

	private static ToolCommandResolver CreateResolver(ICredentialContextAccessor credentialContextAccessor) =>
		new(
			Substitute.For<ISettingsRepository>(),
			Substitute.For<ISettingsBootstrapService>(),
			new NonInteractiveConsole(),
			credentialContextAccessor,
			Substitute.For<ITargetUrlValidator>(),
			new SessionContainerCache(SessionContainerCacheDefaults.IdleTtl, SessionContainerCacheDefaults.MaxSessions));

	private static ICredentialContextAccessor PassthroughAccessor(
		McpTransport transport = McpTransport.Http,
		bool passthroughModeEnabled = true) {
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		accessor.Current.Returns(new CredentialContext(
			"https://acme.creatio.com",
			CredentialMaterial.FromAccessToken("header-supplied-token", "Bearer"),
			transport,
			passthroughModeEnabled));
		return accessor;
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects explicit plaintext uri/login/password tool-args on the HTTP passthrough edge with a header-pointing, secret-free error rather than silently dropping them (AC-01/AC-ERR).")]
	public void Resolve_ShouldRejectPlaintextCredentialArgs_WhenPassthroughEnabledOverHttp() {
		// Arrange
		ToolCommandResolver resolver = CreateResolver(PassthroughAccessor());
		EnvironmentOptions options = new() {
			Uri = "https://attacker.creatio.com",
			Login = "smuggled-login",
			Password = "smuggled-password-value"
		};

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(options);

		// Assert
		EnvironmentResolutionException exception = act.Should().Throw<EnvironmentResolutionException>(
				because: "on the passthrough HTTP edge the credential must come from the X-Integration-Credentials header, so plaintext args are a smuggling vector that must be rejected, not silently dropped (AC-01)")
			.Which;
		exception.Message.Should().Contain("X-Integration-Credentials",
			because: "the error must point the caller at the correct credential channel");
		exception.Message.Should().NotContainAny(
			["smuggled-login", "smuggled-password-value", "https://attacker.creatio.com"],
			"because the rejection message must never echo any supplied credential value (AC-ERR/FR-11)");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an explicit environment NAME on the HTTP passthrough edge, since the passthrough branch ignores it — leaving it unflagged would let a caller believe a named environment took effect (AC-01).")]
	public void Resolve_ShouldRejectExplicitEnvironmentName_WhenPassthroughEnabledOverHttp() {
		// Arrange
		ToolCommandResolver resolver = CreateResolver(PassthroughAccessor());
		EnvironmentOptions options = new() {
			Environment = "production-tenant"
		};

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(options);

		// Assert
		EnvironmentResolutionException exception = act.Should().Throw<EnvironmentResolutionException>(
				because: "on the passthrough path the environment name is ignored (the header context wins), so it must be rejected rather than silently dropped (AC-01)")
			.Which;
		exception.Message.Should().Contain("X-Integration-Credentials",
			because: "the error must direct the caller to supply the target via the header, not tool arguments");
		exception.Message.Should().NotContain("production-tenant",
			because: "the rejection message must never echo the supplied environment name (AC-ERR/FR-11)");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects explicit OAuth client-id/client-secret tool-args on the HTTP passthrough edge (AC-01).")]
	public void Resolve_ShouldRejectExplicitOAuthArgs_WhenPassthroughEnabledOverHttp() {
		// Arrange
		ToolCommandResolver resolver = CreateResolver(PassthroughAccessor());
		EnvironmentOptions options = new() {
			ClientId = "smuggled-client-id",
			ClientSecret = "smuggled-client-secret-value"
		};

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(options);

		// Assert
		EnvironmentResolutionException exception = act.Should().Throw<EnvironmentResolutionException>(
				because: "OAuth client credentials are credential material on the same axis passthrough replaces, so they must be rejected on the passthrough edge (AC-01)")
			.Which;
		exception.Message.Should().NotContainAny(
			["smuggled-client-id", "smuggled-client-secret-value"],
			"because the rejection message must never echo supplied OAuth credential values (AC-ERR/FR-11)");
	}

	[Test]
	[Category("Unit")]
	[Description("A passthrough request carrying NO explicit credential args resolves normally on the HTTP edge — the guard only fires on smuggled args, not on every passthrough call (regression guard for AC-01 scope).")]
	public void Resolve_ShouldResolveNormally_WhenPassthroughEnabledOverHttpWithoutExplicitArgs() {
		// Arrange
		ToolCommandResolver resolver = CreateResolver(PassthroughAccessor());

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(new EnvironmentOptions());

		// Assert
		act.Should().NotThrow(
			because: "a clean passthrough request with no plaintext args must resolve against the header-built ephemeral environment, so the FR-19 guard must not block the normal passthrough flow");
	}

	[Test]
	[Category("Unit")]
	[Description("With no per-request credential context (stdio / default HTTP), the SAME explicit uri/login/password args are honored exactly as 8.1.0.72 — the mode-scoped guard never fires and the shared MCP primitives are unmodified (AC-02/AC-03).")]
	public void Resolve_ShouldHonorExplicitArgs_WhenNoCredentialContextPresent() {
		// Arrange
		System.IO.Abstractions.IFileSystem originalFileSystem = SettingsRepository.FileSystem;
		SettingsRepository.FileSystem = TestFileSystem.MockFileSystem();
		// A default (null-object) accessor returns null Current — the stdio host / default-HTTP behavior.
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		ToolCommandResolver resolver = CreateResolver(accessor);
		EnvironmentOptions options = new() {
			Uri = "http://localhost",
			Login = "Supervisor",
			Password = "Supervisor"
		};

		try {
			// Act
			Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(options);

			// Assert
			act.Should().NotThrow(
				because: "without a passthrough context the explicit-URI path must keep working exactly as 8.1.0.72 — the FR-19 guard is scoped to authorized HTTP passthrough only (AC-02), and it never strips args from the shared primitives (AC-03)");
		}
		finally {
			SettingsRepository.FileSystem = originalFileSystem;
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Passthrough mode OFF (even over HTTP) does not reject explicit args — the guard reads PassthroughModeEnabled and stays inert when the mode is off (AC-02).")]
	public void Resolve_ShouldNotReject_WhenPassthroughModeDisabled() {
		// Arrange
		ToolCommandResolver resolver = CreateResolver(
			PassthroughAccessor(McpTransport.Http, passthroughModeEnabled: false));
		EnvironmentOptions options = new() {
			Uri = "https://attacker.creatio.com",
			Login = "some-login",
			Password = "some-password"
		};

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(options);

		// Assert — with the mode off the FR-19 guard is skipped; the passthrough branch still runs (the
		// context is present), so no FR-19 rejection is raised for the explicit args.
		act.Should().NotThrow<EnvironmentResolutionException>(
			because: "the FR-19 rejection is gated on PassthroughModeEnabled; with the mode off the smuggling guard must not fire (AC-02)");
	}
}

using System;
using System.IO.Abstractions.TestingHelpers;
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

[TestFixture]
[Property("Module", "McpServer")]
public class ToolCommandResolverTests {

	private static ToolCommandResolver CreateResolver(
		ISettingsRepository settingsRepository,
		ISettingsBootstrapService settingsBootstrapService,
		ICredentialContextAccessor credentialContextAccessor = null,
		ITargetUrlValidator targetUrlValidator = null) =>
		new(
			settingsRepository,
			settingsBootstrapService,
			new NonInteractiveConsole(),
			// A substitute accessor returns null Current by default → the non-passthrough (existing)
			// path, matching the stdio host's null-object behavior.
			credentialContextAccessor ?? Substitute.For<ICredentialContextAccessor>(),
			targetUrlValidator ?? Substitute.For<ITargetUrlValidator>());

	[Test]
	[Description("Rejects unknown environment names instead of resolving MCP commands against default localhost settings.")]
	[Category("Unit")]
	public void Resolve_Should_Reject_Unknown_Environment_Name() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		settingsBootstrapService.GetReport().Returns(new SettingsBootstrapReport(
			"healthy",
			SettingsRepository.AppSettingsFile,
			"dev",
			"dev",
			1,
			[],
			[],
			true,
			true));
		settingsRepository.IsEnvironmentExists("missing-env").Returns(false);
		ToolCommandResolver resolver = CreateResolver(settingsRepository, settingsBootstrapService);
		EnvironmentOptions options = new() {
			Environment = "missing-env"
		};

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(options);

		// Assert
		act.Should().Throw<EnvironmentResolutionException>()
			.WithMessage("*missing-env*",
				"because an unknown environment is an expected, caller-actionable resolution failure (mapped to exit code 1), not an unexpected runtime error, and must not fall back to default localhost credentials");
	}

	[Test]
	[Description("Allows explicit URI-based command resolution even when the persisted bootstrap report is broken.")]
	[Category("Unit")]
	public void Resolve_Should_Accept_Explicit_Uri_When_Bootstrap_Report_Is_Broken() {
		// Arrange
		System.IO.Abstractions.IFileSystem originalFileSystem = SettingsRepository.FileSystem;
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository.FileSystem = fileSystem;
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		settingsBootstrapService.GetReport().Returns(new SettingsBootstrapReport(
			"broken",
			SettingsRepository.AppSettingsFile,
			null,
			null,
			0,
			[new SettingsIssue("settings-file-unreadable", "appsettings.json is unreadable.")],
			[],
			true,
			false));
		ToolCommandResolver resolver = CreateResolver(settingsRepository, settingsBootstrapService);
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
				because: "explicit URI-based MCP execution should stay available even when named-environment bootstrap is broken");
		}
		finally {
			SettingsRepository.FileSystem = originalFileSystem;
		}
	}

	[Test]
	[Description("Keeps environmentless MCP resolution independent from the active configured environment.")]
	[Category("Unit")]
	public void ResolveWithoutEnvironment_Should_Not_Read_Default_Environment_When_Name_Is_Missing() {
		// Arrange
		System.IO.Abstractions.IFileSystem originalFileSystem = SettingsRepository.FileSystem;
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository.FileSystem = fileSystem;
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		ToolCommandResolver resolver = CreateResolver(settingsRepository, settingsBootstrapService);
		EnvironmentOptions options = new();

		try {
			// Act
			Action act = () => resolver.ResolveWithoutEnvironment<CreateEntitySchemaCommand>(options);

			// Assert
			act.Should().NotThrow(
				because: "environmentless MCP tools should not depend on whatever active environment is configured locally");
			settingsRepository.DidNotReceive().FindEnvironment(Arg.Any<string>());
		}
		finally {
			SettingsRepository.FileSystem = originalFileSystem;
		}
	}

	[Test]
	[Description("Maps an access-token credential context to an ephemeral EnvironmentSettings carrying the target URI, token, and token type.")]
	[Category("Unit")]
	public void BuildEphemeralSettings_Should_Map_Uri_And_AccessToken_When_Kind_Is_AccessToken() {
		// Arrange
		CredentialContext context = new(
			"https://acme.creatio.com",
			CredentialMaterial.FromAccessToken("opaque-token", "Bearer"),
			McpTransport.Http,
			true);

		// Act
		EnvironmentSettings settings = ToolCommandResolver.BuildEphemeralSettings(context);

		// Assert
		settings.Uri.Should().Be("https://acme.creatio.com",
			because: "the ephemeral environment must target the URL supplied on the credential context, not any registered environment");
		settings.AccessToken.Should().Be("opaque-token",
			because: "the bearer token from the header must flow into the ephemeral settings so ApplicationClientFactory builds a bearer client");
		settings.AccessTokenType.Should().Be("Bearer",
			because: "the token type from the context must be preserved for the bearer client build");
		settings.Login.Should().BeNullOrEmpty(
			because: "an access-token context carries no login/password material");
		settings.Password.Should().BeNullOrEmpty(
			because: "an access-token context carries no login/password material");
	}

	[Test]
	[Description("Maps a login/password credential context to an ephemeral EnvironmentSettings carrying the URI, login, and password.")]
	[Category("Unit")]
	public void BuildEphemeralSettings_Should_Map_Login_And_Password_When_Kind_Is_LoginPassword() {
		// Arrange
		CredentialContext context = new(
			"https://acme.creatio.com",
			CredentialMaterial.FromLoginPassword("Supervisor", "s3cret"),
			McpTransport.Http,
			true);

		// Act
		EnvironmentSettings settings = ToolCommandResolver.BuildEphemeralSettings(context);

		// Assert
		settings.Uri.Should().Be("https://acme.creatio.com",
			because: "the ephemeral environment must target the credential-context URL");
		settings.Login.Should().Be("Supervisor",
			because: "the login from the header must flow into the ephemeral settings for the login/password client build");
		settings.Password.Should().Be("s3cret",
			because: "the password from the header must flow into the ephemeral settings for the login/password client build");
		settings.AccessToken.Should().BeNullOrEmpty(
			because: "a login/password context carries no bearer token");
	}

	[Test]
	[Description("Validates the target URL before any settings/container are built, surfaces a blocked URL as an EnvironmentResolutionException (exit code 1), and does not consult the settings repository on the passthrough path.")]
	[Category("Unit")]
	public void Resolve_Should_Validate_Target_Url_Before_Resolution_When_Passthrough_Context_Present() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		accessor.Current.Returns(new CredentialContext(
			"https://blocked.internal",
			CredentialMaterial.FromAccessToken("opaque-token", "Bearer"),
			McpTransport.Http,
			true));
		ITargetUrlValidator validator = Substitute.For<ITargetUrlValidator>();
		validator.When(v => v.EnsureAllowed(Arg.Any<string>()))
			.Do(_ => throw new TargetUrlNotAllowedException("Error: target url is blocked: loopback address"));
		ToolCommandResolver resolver = CreateResolver(settingsRepository, settingsBootstrapService, accessor, validator);

		// Act
		// A rejecting validator that stops the whole resolution proves EnsureAllowed runs before the
		// container/client are built: the method is linear, so no command is produced and no settings
		// repository lookup happens once the guard throws.
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(new EnvironmentOptions());

		// Assert
		act.Should().Throw<EnvironmentResolutionException>(
				because: "a blocked passthrough URL is caller-actionable and must map to exit code 1, not a -1 'clio bug' (SSRF egress guard, AC-04)")
			.Which.Message.Should().Contain("loopback",
				because: "the validator's secret-free reason must be preserved verbatim on the wrapped exception");
		validator.Received(1).EnsureAllowed("https://blocked.internal");
		settingsRepository.DidNotReceive().IsEnvironmentExists(Arg.Any<string>());
		settingsRepository.DidNotReceive().FindEnvironment(Arg.Any<string>());
	}

	[Test]
	[Description("Two different access tokens on the SAME target URL produce different passthrough cache keys, so a bearer session is never crossed between tenants (does not depend on hash truncation).")]
	[Category("Unit")]
	public void BuildPassthroughCacheKey_Should_Differ_When_Tokens_Differ_On_Same_Url() {
		// Arrange
		CredentialContext contextA = new(
			"https://acme.creatio.com",
			CredentialMaterial.FromAccessToken("tenant-a-token", "Bearer"),
			McpTransport.Http,
			true);
		CredentialContext contextB = new(
			"https://acme.creatio.com",
			CredentialMaterial.FromAccessToken("tenant-b-token", "Bearer"),
			McpTransport.Http,
			true);

		// Act
		string keyA = ToolCommandResolver.BuildPassthroughCacheKey(contextA);
		string keyB = ToolCommandResolver.BuildPassthroughCacheKey(contextB);

		// Assert
		keyA.Should().NotBe(keyB,
			because: "same URL, different token is the norm for passthrough, so the credential discriminator must produce distinct keys — a collision would reuse another tenant's session");
	}

	[Test]
	[Description("The passthrough cache key embeds the FULL SHA-256 hex hash (64 chars), not a 64-bit truncation, to minimize the credential-crossover collision surface.")]
	[Category("Unit")]
	public void BuildPassthroughCacheKey_Should_Use_Full_Sha256_Hash() {
		// Arrange
		CredentialContext context = new(
			"https://acme.creatio.com",
			CredentialMaterial.FromAccessToken("tenant-a-token", "Bearer"),
			McpTransport.Http,
			true);

		// Act
		string key = ToolCommandResolver.BuildPassthroughCacheKey(context);

		// Assert
		string hashSegment = key[(key.LastIndexOf(':') + 1)..];
		hashSegment.Length.Should().Be(64,
			because: "a full SHA-256 render is 64 hex chars; a truncated 16-char key would widen the credential-crossover collision surface");
	}

	[Test]
	[Description("Resolves a passthrough command from the credential context without consulting or matching any registered environment.")]
	[Category("Unit")]
	public void Resolve_Should_Not_Consult_Settings_Repository_When_Passthrough_Context_Present() {
		// Arrange
		System.IO.Abstractions.IFileSystem originalFileSystem = SettingsRepository.FileSystem;
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository.FileSystem = fileSystem;
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		// A same-named registered environment exists; the passthrough path must NOT use it (AC-02).
		settingsRepository.IsEnvironmentExists(Arg.Any<string>()).Returns(true);
		settingsRepository.FindEnvironment(Arg.Any<string>()).Returns(new EnvironmentSettings {
			Uri = "https://registered.creatio.com",
			Login = "Registered",
			Password = "Registered"
		});
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		accessor.Current.Returns(new CredentialContext(
			"https://acme.creatio.com",
			CredentialMaterial.FromAccessToken("opaque-token", "Bearer"),
			McpTransport.Http,
			true));
		ToolCommandResolver resolver = CreateResolver(settingsRepository, settingsBootstrapService, accessor);

		try {
			// Act
			Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(new EnvironmentOptions());

			// Assert
			act.Should().NotThrow(
				because: "an access-token passthrough context resolves against an ephemeral bearer environment without dialing");
			settingsRepository.DidNotReceive().IsEnvironmentExists(Arg.Any<string>());
			settingsRepository.DidNotReceive().FindEnvironment(Arg.Any<string>());
			settingsBootstrapService.DidNotReceive().GetReport();
		}
		finally {
			SettingsRepository.FileSystem = originalFileSystem;
		}
	}

	[Test]
	[Description("Rejects a cookie credential context with a caller-actionable, secret-free error rather than a deep NotSupported wiring failure.")]
	[Category("Unit")]
	public void Resolve_Should_Reject_Cookie_Passthrough_With_Caller_Actionable_Error() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		accessor.Current.Returns(new CredentialContext(
			"https://acme.creatio.com",
			CredentialMaterial.FromCookie("BPMCSRF=super-secret-cookie-value"),
			McpTransport.Http,
			true));
		ToolCommandResolver resolver = CreateResolver(settingsRepository, settingsBootstrapService, accessor);

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(new EnvironmentOptions());

		// Assert
		act.Should().Throw<EnvironmentResolutionException>(
				because: "cookie auth is a caller-actionable limitation (exit code 1), not an unexpected wiring bug")
			.Which.Message.Should().NotContain("super-secret-cookie-value",
				"because credential material must never leak into error text (FR-11)");
	}

	[Test]
	[Description("Reports the real missing piece on the passthrough path and never the misleading environment-not-found wording (FR-12).")]
	[Category("Unit")]
	public void Resolve_Should_Not_Emit_Environment_Not_Found_Wording_When_Passthrough_Auth_Missing() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		accessor.Current.Returns(new CredentialContext(
			"https://acme.creatio.com",
			CredentialMaterial.FromAccessToken(string.Empty, "Bearer"),
			McpTransport.Http,
			true));
		ToolCommandResolver resolver = CreateResolver(settingsRepository, settingsBootstrapService, accessor);

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(new EnvironmentOptions());

		// Assert
		EnvironmentResolutionException exception = act.Should().Throw<EnvironmentResolutionException>(
				because: "supplied url + missing auth is a caller-actionable failure that must name the real missing piece")
			.Which;
		exception.Message.Should().NotContainAny(
			["environment not found", "not found", "name is required", "environment name"],
			"because FR-12 forbids the misleading environment-not-found wording when a credential context was supplied");
		exception.Message.Should().Contain("Authentication material",
			because: "the error must name the real missing piece (auth material)");
	}

	[Test]
	[Description("Rejects a non-Bearer access-token type with a caller-actionable error instead of a deep NotSupported wiring failure.")]
	[Category("Unit")]
	public void Resolve_Should_Reject_Non_Bearer_Token_Type_With_Caller_Actionable_Error() {
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		accessor.Current.Returns(new CredentialContext(
			"https://acme.creatio.com",
			CredentialMaterial.FromAccessToken("opaque-token", "MAC"),
			McpTransport.Http,
			true));
		ToolCommandResolver resolver = CreateResolver(settingsRepository, settingsBootstrapService, accessor);

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(new EnvironmentOptions());

		// Assert
		EnvironmentResolutionException exception = act.Should().Throw<EnvironmentResolutionException>(
				because: "a non-Bearer token type is a caller-actionable limitation (exit code 1), not an unexpected wiring bug")
			.Which;
		exception.Message.Should().NotContain("opaque-token",
			because: "the access token must never leak into error text (FR-11)");
		exception.Message.Should().Contain("Bearer",
			because: "the error must name the real constraint (only Bearer is supported)");
	}
}

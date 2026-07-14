using System.Threading;
using System.Threading.Tasks;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 10 (ENG-93347, PRD Security mode ii): dedicated passthrough regression coverage for
/// <c>get-user-culture</c> (<see cref="GetUserCultureTool"/>). The tool now resolves its
/// <see cref="EnvironmentSettings"/> through <see cref="IToolCommandResolver.Resolve{TCommand}"/>
/// instead of calling <c>ISettingsRepository.GetEnvironment(EnvironmentOptions)</c> directly — the
/// direct call was the real, silent active-tenant leak (<c>FindEnvironment(null)</c> returning the
/// configured active environment with stored credentials).
/// </summary>
/// <remarks>
/// AC-01/AC-02 use a REAL <see cref="ToolCommandResolver"/> (not a mocked one) wired with a
/// substituted <see cref="ISettingsRepository"/> that IS configured with a registered/active
/// environment, so the assertions prove the actual resolution branch never reaches that
/// repository under passthrough — not merely that a test double was told what to return.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class GetUserCultureToolPassthroughTests {

	private const string HeaderTenantUrl = "https://header-tenant.example.com";
	private const string HeaderTenantToken = "header-tenant-secret-token";
	private const string ActiveEnvironmentUrl = "https://active-tenant.example.com";
	private const string RegisteredEnvironmentName = "registered-env";
	private const string RegisteredEnvironmentUrl = "https://registered.example.com";

	private static ToolCommandResolver CreateRealResolver(
		ICredentialContextAccessor credentialContextAccessor,
		out ISettingsRepository settingsRepository,
		bool configureActiveEnvironment) {
		settingsRepository = Substitute.For<ISettingsRepository>();
		if (configureActiveEnvironment) {
			// Simulates the exact leak condition (ConfigurationOptions.cs:638-652 -> :621-629): an
			// active environment IS configured on the edge and a bare `FindEnvironment(null)` /
			// `GetEnvironment(EnvironmentOptions)` call would return it with stored credentials.
			settingsRepository.FindEnvironment(null).Returns(new EnvironmentSettings {
				Uri = ActiveEnvironmentUrl,
				Login = "ActiveUser",
				Password = "ActivePwd"
			});
			settingsRepository.GetEnvironment(Arg.Any<EnvironmentOptions>()).Returns(new EnvironmentSettings {
				Uri = ActiveEnvironmentUrl,
				Login = "ActiveUser",
				Password = "ActivePwd"
			});
		}
		settingsRepository.IsEnvironmentExists(RegisteredEnvironmentName).Returns(true);
		settingsRepository.FindEnvironment(RegisteredEnvironmentName).Returns(new EnvironmentSettings {
			Uri = RegisteredEnvironmentUrl,
			Login = "RegisteredUser",
			Password = "RegisteredPwd"
		});
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		settingsBootstrapService.GetReport().Returns(new SettingsBootstrapReport(
			"healthy", SettingsRepository.AppSettingsFile, RegisteredEnvironmentName, RegisteredEnvironmentName,
			1, [], [], true, true));
		return new ToolCommandResolver(
			settingsRepository,
			settingsBootstrapService,
			new NonInteractiveConsole(),
			credentialContextAccessor,
			Substitute.For<ITargetUrlValidator>(),
			new SessionContainerCache(SessionContainerCacheDefaults.IdleTtl, SessionContainerCacheDefaults.MaxSessions));
	}

	private static ICredentialContextAccessor CreateHeaderAccessor() {
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		accessor.Current.Returns(new CredentialContext(
			HeaderTenantUrl,
			CredentialMaterial.FromAccessToken(HeaderTenantToken, "Bearer"),
			false,
			McpTransport.Http,
			PassthroughModeEnabled: true));
		return accessor;
	}

	private static GetUserCultureTool CreateTool(
		IToolCommandResolver commandResolver, CultureResolution resolution = null) {
		ICurrentUserCultureResolver resolver = Substitute.For<ICurrentUserCultureResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(resolution ?? CultureResolution.Resolved("uk-UA")));
		ICurrentUserCultureResolverFactory factory = Substitute.For<ICurrentUserCultureResolverFactory>();
		factory.Create(Arg.Any<EnvironmentSettings>()).Returns(resolver);
		return new GetUserCultureTool(factory, commandResolver);
	}

	[TestCase(false, TestName = "GetUserCulture_ShouldResolveHeaderTenant_WhenHeaderOnlyAndNoActiveEnvironmentConfigured")]
	[TestCase(true, TestName = "GetUserCulture_ShouldResolveHeaderTenant_WhenHeaderOnlyAndActiveEnvironmentConfigured")]
	[Description("AC-01/AC-02: a header-only passthrough call (no environment-name) resolves the HEADER " +
		"tenant's settings and NEVER falls back to the configured active environment, tested both with " +
		"and without an active environment configured in the repository fixture — the exact condition " +
		"(PRD 'mode ii') under which the pre-fix leak was real.")]
	public async Task GetUserCulture_ShouldResolveHeaderTenant_WhenHeaderOnly(bool configureActiveEnvironment) {
		// Arrange
		ICredentialContextAccessor accessor = CreateHeaderAccessor();
		ToolCommandResolver resolver = CreateRealResolver(accessor, out ISettingsRepository settingsRepository,
			configureActiveEnvironment);
		GetUserCultureTool tool = CreateTool(resolver);

		// Act
		GetUserCultureResponse response = await tool.GetUserCulture(new GetUserCultureArgs());

		// Assert
		response.Success.Should().BeTrue(
			because: "a header-only passthrough call must resolve and read the header tenant's culture");
		response.Culture.Should().Be("uk-UA",
			because: "the resolved culture must come from the header-tenant settings the resolver built");
		settingsRepository.DidNotReceive().FindEnvironment(Arg.Any<string>());
		settingsRepository.DidNotReceiveWithAnyArgs().GetEnvironment(default(EnvironmentOptions));
		settingsRepository.DidNotReceiveWithAnyArgs().GetEnvironment(default(string));
	}

	[Test]
	[Description("AC-03 (mixed input, PRD AC-06): header present AND an explicit environment-name naming a " +
		"different registered environment is rejected by the resolver's HasExplicitCredentialArgs check " +
		"before any Creatio-reaching call — the tool surfaces the rejection as a failure signal and never " +
		"resolves or uses the named environment's stored credentials.")]
	public async Task GetUserCulture_ShouldRejectMixedInput_WhenHeaderAndEnvironmentNameBothPresent() {
		// Arrange
		ICredentialContextAccessor accessor = CreateHeaderAccessor();
		ToolCommandResolver resolver = CreateRealResolver(accessor, out ISettingsRepository settingsRepository,
			configureActiveEnvironment: false);
		ICurrentUserCultureResolverFactory factory = Substitute.For<ICurrentUserCultureResolverFactory>();
		GetUserCultureTool tool = new(factory, resolver);

		// Act
		GetUserCultureResponse response = await tool.GetUserCulture(
			new GetUserCultureArgs(EnvironmentName: RegisteredEnvironmentName));

		// Assert
		response.Success.Should().BeFalse(
			because: "mixed header + environment-name input must be rejected before any culture is resolved");
		response.Reason.Should().Contain("X-Integration-Credentials",
			because: "the rejection must teach the caller the correct credential channel (the resolver's uniform message)");
		settingsRepository.DidNotReceive().FindEnvironment(RegisteredEnvironmentName);
		factory.DidNotReceiveWithAnyArgs().Create(Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("AC-04: on stdio / registered-environment transports (no credential context) an explicit " +
		"environment-name resolves through the unchanged registry branch and the tool behaves exactly as " +
		"the pre-change baseline.")]
	public async Task GetUserCulture_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio() {
		// Arrange — no credential context: Current is null, selecting the non-passthrough registry branch.
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		ToolCommandResolver resolver = CreateRealResolver(accessor, out ISettingsRepository settingsRepository,
			configureActiveEnvironment: false);
		GetUserCultureTool tool = CreateTool(resolver, CultureResolution.Resolved("en-US"));

		// Act
		GetUserCultureResponse response = await tool.GetUserCulture(
			new GetUserCultureArgs(EnvironmentName: RegisteredEnvironmentName));

		// Assert
		response.Success.Should().BeTrue(
			because: "a registered environment name must keep resolving through the unchanged registry path");
		response.Culture.Should().Be("en-US",
			because: "the resolved culture must reflect the registered environment's settings");
		settingsRepository.Received(1).FindEnvironment(RegisteredEnvironmentName);
	}

	[Test]
	[Description("AC-ERR(b): a valid passthrough header whose target operation fails (culture resolution " +
		"throws) yields a redacted failure signal — no accessToken/login/password material leaks into the " +
		"'reason' field (SensitiveErrorTextRedactor scrubs any key=value pair whose key is a recognized " +
		"credential token, e.g. 'token=...').")]
	public async Task GetUserCulture_ShouldReturnRedactedFailure_WhenHeaderTenantOperationFails() {
		// Arrange
		ICredentialContextAccessor accessor = CreateHeaderAccessor();
		ToolCommandResolver resolver = CreateRealResolver(accessor, out _, configureActiveEnvironment: false);
		ICurrentUserCultureResolver cultureResolver = Substitute.For<ICurrentUserCultureResolver>();
		cultureResolver.ResolveAsync(Arg.Any<CancellationToken>())
			.ThrowsAsync(new System.InvalidOperationException(
				$"Unreachable tenant. token={HeaderTenantToken}"));
		ICurrentUserCultureResolverFactory factory = Substitute.For<ICurrentUserCultureResolverFactory>();
		factory.Create(Arg.Any<EnvironmentSettings>()).Returns(cultureResolver);
		GetUserCultureTool tool = new(factory, resolver);

		// Act
		GetUserCultureResponse response = await tool.GetUserCulture(new GetUserCultureArgs());

		// Assert
		response.Success.Should().BeFalse(
			because: "a failed operation against a valid header tenant must surface as a typed failure signal");
		response.Reason.Should().Contain("Unreachable tenant",
			because: "the caller-actionable failure description must be preserved");
		response.Reason.Should().NotContain(HeaderTenantToken,
			because: "SensitiveErrorTextRedactor must scrub credential material before it crosses the MCP boundary");
	}
}

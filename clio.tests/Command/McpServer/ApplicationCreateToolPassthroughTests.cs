using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 5 (ENG-93347) passthrough behavior of the <c>create-app</c> MCP tool: header-only calls
/// execute against the header tenant resolved by <see cref="IToolCommandResolver"/> across the
/// ENTIRE nested call graph — the tool drives a REAL <see cref="ApplicationCreateService"/> so the
/// nested caption-culture resolution (AC-03) and the timeout/polling readback (AC-04) are proven to
/// use the Story-2 settings-based overloads and never a name-based lookup or
/// <see cref="ISettingsRepository"/> read. Mixed input is rejected by the resolver's transport policy
/// before any Creatio-reaching call, and registered-environment / stdio behavior is unchanged.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ApplicationCreateToolPassthroughTests {

	private const string LegacyRequirednessMessage = "Environment name is required. (Parameter 'environmentName')";

	private const string MixedInputRejectionMessage =
		"Explicit credential or environment arguments (uri/login/password/client-id/client-secret/environment) "
		+ "are not accepted when credential passthrough is enabled over HTTP. Supply the target environment "
		+ "and credentials via the X-Integration-Credentials header, not tool arguments.";

	private const string ResolverRequirednessMessage =
		"Either a configured environment name or an explicit URI is required for MCP command execution. "
		+ "Prefer a registered environment name; use explicit URI credentials only as a bootstrap or emergency fallback.";

	private const string CreatedAppId = "33333333-3333-3333-3333-333333333333";

	private ISettingsRepository _settingsRepository;
	private IApplicationClientFactory _applicationClientFactory;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private IApplicationInfoService _applicationInfoService;
	private ISysSettingsManager _sysSettingsManager;
	private ICaptionCultureResolver _captionCultureResolver;
	private IApplicationCreateEnrichmentService _enrichmentService;
	private IToolCommandResolver _commandResolver;
	private ApplicationCreateTool _tool;

	[SetUp]
	public void SetUp() {
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_applicationInfoService = Substitute.For<IApplicationInfoService>();
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns("Usr");
		_captionCultureResolver = Substitute.For<ICaptionCultureResolver>();
		_captionCultureResolver.Resolve(Arg.Any<EnvironmentSettings>(), Arg.Any<string?>()).Returns("en-US");
		_enrichmentService = Substitute.For<IApplicationCreateEnrichmentService>();
		_enrichmentService.Enrich(
				Arg.Any<ApplicationCreateArgs>(),
				Arg.Any<ApplicationOptionalTemplateData?>(),
				Arg.Any<System.Threading.CancellationToken>())
			.Returns(new ApplicationDataForgeResult(
				Used: false, Health: null, Status: null, Coverage: null,
				Warnings: [], ContextSummary: null));
		_commandResolver = Substitute.For<IToolCommandResolver>();
		_serviceUrlBuilder.Build(Arg.Any<string>(), Arg.Any<EnvironmentSettings>())
			.Returns(callInfo => $"https://tenant.example.com/{callInfo.ArgAt<string>(0)}");
		// The tool drives the REAL create service so the nested caption-culture and polling/readback
		// calls are exercised for real instead of being hidden behind a service substitute.
		IApplicationCreateService createService = new ApplicationCreateService(
			_settingsRepository,
			_applicationClientFactory,
			_serviceUrlBuilder,
			_applicationInfoService,
			_ => _sysSettingsManager,
			new NullLogger(),
			_captionCultureResolver);
		_tool = new ApplicationCreateTool(
			Substitute.For<ILogger>(), _commandResolver, createService, _enrichmentService);
	}

	private static EnvironmentSettings CreateTenantSettings() =>
		new() { Uri = "https://tenant.example.com" };

	private static ApplicationCreateArgs CreateArgs(string? environmentName) =>
		new(
			EnvironmentName: environmentName,
			Name: "Tenant App",
			Code: "UsrTenantApp",
			TemplateCode: "AppFreedomUI",
			IconId: "11111111-1111-1111-1111-111111111111",
			IconBackground: "#0058EF");

	private static ApplicationInfoResult CreateInfoResult() =>
		new(
			"pkg-uid",
			"UsrTenantApp",
			[],
			ApplicationId: CreatedAppId,
			ApplicationName: "Tenant App",
			ApplicationCode: "UsrTenantApp",
			ApplicationVersion: "1.0.0");

	private void ConfigureHeaderTenant(EnvironmentSettings settings) {
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => string.IsNullOrWhiteSpace(options.Environment)))
			.Returns(settings);
		ConfigureCreateSuccess(settings);
	}

	private void ConfigureCreateSuccess(EnvironmentSettings settings) {
		_applicationClientFactory.CreateEnvironmentClient(settings).Returns(_applicationClient);
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns($$"""{"success":true,"value":"{{CreatedAppId}}"}""");
		_applicationInfoService.GetApplicationInfo(settings, CreatedAppId, "UsrTenantApp")
			.Returns(CreateInfoResult());
	}

	[Test]
	[Category("Unit")]
	[Description("AC-01/AC-02: a header-only passthrough call (no environment-name) creates the application against the header tenant resolved by IToolCommandResolver — the settings-based service overload runs end-to-end and the legacy 'Environment name is required' failure never surfaces.")]
	public async Task ApplicationCreate_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		ConfigureHeaderTenant(headerTenantSettings);

		// Act
		ApplicationContextResponse result = await _tool.ApplicationCreate(CreateArgs(environmentName: null));

		// Assert
		result.Success.Should().BeTrue(
			because: "a header-only passthrough call must route to the header tenant instead of failing on a blank environment name");
		result.Error.Should().BeNull(
			because: "the routed call succeeded, so no error payload may be present — in particular not the legacy requiredness error");
		result.ApplicationCode.Should().Be("UsrTenantApp",
			because: "the tool must return the application info the settings-based readback loaded from the header tenant");
		_applicationClientFactory.Received(1).CreateEnvironmentClient(headerTenantSettings);
		_settingsRepository.DidNotReceiveWithAnyArgs().FindEnvironment(default);
		_applicationInfoService.Received(1).GetApplicationInfo(headerTenantSettings, CreatedAppId, "UsrTenantApp");
		_applicationInfoService.DidNotReceiveWithAnyArgs().GetApplicationInfo(default(string)!, default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-03 (nested caption-culture path): under header-only passthrough the create flow resolves the caption culture through the settings-based ICaptionCultureResolver overload against the header tenant — never through the name-based overload or an ISettingsRepository read of the active registered environment.")]
	public async Task ApplicationCreate_ShouldResolveCaptionCultureAgainstHeaderTenant_WhenHeaderOnly() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		ConfigureHeaderTenant(headerTenantSettings);

		// Act
		ApplicationContextResponse result = await _tool.ApplicationCreate(CreateArgs(environmentName: null));

		// Assert
		result.Success.Should().BeTrue(
			because: "the nested culture resolution must not derail a header-only create call");
		_captionCultureResolver.Received(1).Resolve(headerTenantSettings, null);
		_captionCultureResolver.DidNotReceiveWithAnyArgs().Resolve(default(EnvironmentOptions)!, default);
		_settingsRepository.DidNotReceiveWithAnyArgs().GetEnvironment(default(EnvironmentOptions)!);
		_settingsRepository.DidNotReceiveWithAnyArgs().GetEnvironment(default(string));
	}

	[Test]
	[Category("Unit")]
	[Description("AC-04 (nested polling/readback path): when CreateApp times out under header-only passthrough, the polling loop reads the application back through the settings-based GetApplicationInfo overload against the header tenant — never through a name-based lookup against any other environment.")]
	public async Task ApplicationCreate_ShouldPollHeaderTenantForReadback_WhenCreateAppTimesOut() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => string.IsNullOrWhiteSpace(options.Environment)))
			.Returns(headerTenantSettings);
		_applicationClientFactory.CreateEnvironmentClient(headerTenantSettings).Returns(_applicationClient);
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns(_ => throw new InvalidOperationException("timeout of 100000ms exceeded"));
		_applicationInfoService.GetApplicationInfo(headerTenantSettings, null, "UsrTenantApp")
			.Returns(CreateInfoResult());

		// Act
		ApplicationContextResponse result = await _tool.ApplicationCreate(CreateArgs(environmentName: null));

		// Assert
		result.Success.Should().BeTrue(
			because: "a CreateApp timeout must fall back to polling the header tenant and still return the created application");
		_applicationInfoService.Received(1).GetApplicationInfo(headerTenantSettings, null, "UsrTenantApp");
		_applicationInfoService.DidNotReceiveWithAnyArgs().GetApplicationInfo(default(string)!, default, default);
		_settingsRepository.DidNotReceiveWithAnyArgs().FindEnvironment(default);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-06: under passthrough with BOTH the header and an explicit environment-name, the resolver's transport-policy rejection fires before ANY Creatio-reaching call in the entire graph — no client construction, no nested culture resolution, no application-info lookup by either overload.")]
	public async Task ApplicationCreate_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent() {
		// Arrange
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => options.Environment == "other-registered-env"))
			.Returns(_ => throw new EnvironmentResolutionException(MixedInputRejectionMessage));

		// Act
		ApplicationContextResponse result = await _tool.ApplicationCreate(CreateArgs("other-registered-env"));

		// Assert
		result.Success.Should().BeFalse(
			because: "mixed header + environment-name input must be rejected by the existing transport policy");
		result.Error.Should().Contain("X-Integration-Credentials",
			because: "the rejection must teach the caller the correct credential channel");
		_applicationClientFactory.DidNotReceiveWithAnyArgs().CreateEnvironmentClient(default!);
		_captionCultureResolver.DidNotReceiveWithAnyArgs().Resolve(default(EnvironmentSettings)!, default);
		_captionCultureResolver.DidNotReceiveWithAnyArgs().Resolve(default(EnvironmentOptions)!, default);
		_applicationInfoService.DidNotReceiveWithAnyArgs()
			.GetApplicationInfo(default(EnvironmentSettings)!, default, default);
		_applicationInfoService.DidNotReceiveWithAnyArgs().GetApplicationInfo(default(string)!, default, default);
		_settingsRepository.DidNotReceiveWithAnyArgs().FindEnvironment(default);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-07: on stdio / registered-environment transports an explicit environment-name resolves through the unchanged registry branch and the whole create flow — including the nested culture and readback calls — executes against those settings exactly as before.")]
	public async Task ApplicationCreate_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio() {
		// Arrange
		EnvironmentSettings registeredSettings = new() { Uri = "https://registered.example.com" };
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"))
			.Returns(registeredSettings);
		ConfigureCreateSuccess(registeredSettings);

		// Act
		ApplicationContextResponse result = await _tool.ApplicationCreate(CreateArgs("sandbox"));

		// Assert
		result.Success.Should().BeTrue(
			because: "registered-environment resolution must keep working exactly as the pre-change baseline");
		result.ApplicationCode.Should().Be("UsrTenantApp",
			because: "the tool must return the application created against the registered environment's settings");
		_commandResolver.Received(1).Resolve<EnvironmentSettings>(
			Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"));
		_captionCultureResolver.Received(1).Resolve(registeredSettings, null);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-02 (runtime requiredness): outside passthrough, a call with no environment-name surfaces the resolver's EnvironmentResolutionException as a typed error envelope — requiredness is enforced at runtime, not by the MCP schema.")]
	public async Task ApplicationCreate_ShouldReturnResolverRequirednessError_WhenNoEnvironmentNameAndNoPassthrough() {
		// Arrange
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => string.IsNullOrWhiteSpace(options.Environment)))
			.Returns(_ => throw new EnvironmentResolutionException(ResolverRequirednessMessage));

		// Act
		ApplicationContextResponse result = await _tool.ApplicationCreate(CreateArgs(environmentName: null));

		// Assert
		result.Success.Should().BeFalse(
			because: "without a passthrough context a blank environment-name has no resolvable target");
		result.Error.Should().Contain("configured environment name",
			because: "the resolver's actionable requiredness message must reach the caller instead of an opaque failure");
		result.Error.Should().NotBe(LegacyRequirednessMessage,
			because: "the opaque ArgumentException from the name-based service path must no longer be the failure surface");
		_applicationClientFactory.DidNotReceiveWithAnyArgs().CreateEnvironmentClient(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-ERR(b): a valid passthrough header whose target CreateApp operation fails yields the typed error envelope with redacted text — no accessToken/login/password material leaks.")]
	public async Task ApplicationCreate_ShouldReturnRedactedErrorEnvelope_WhenHeaderTenantOperationFails() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		_commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>())
			.Returns(headerTenantSettings);
		_applicationClientFactory.CreateEnvironmentClient(headerTenantSettings).Returns(_applicationClient);
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns(_ => throw new InvalidOperationException(
				"App Installer CreateApp request failed. token=super-secret-token-value"));

		// Act
		ApplicationContextResponse result = await _tool.ApplicationCreate(CreateArgs(environmentName: null));

		// Assert
		result.Success.Should().BeFalse(
			because: "an operation failure against a valid header tenant must surface as a typed error envelope, not a protocol error");
		result.Error.Should().Contain("CreateApp request failed",
			because: "the caller-actionable failure description must be preserved");
		result.Error.Should().NotContain("super-secret-token-value",
			because: "SensitiveErrorTextRedactor must scrub credential material before it crosses the MCP boundary");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-02 (schema relax, FR-05a): environment-name on ApplicationCreateArgs carries no [Required] attribute, so a header-only call reaches the tool method instead of being rejected at MCP binding.")]
	public void ApplicationCreateArgs_ShouldBeSchemaOptional_WhenEnvironmentNamePropertyIsInspected() {
		// Arrange
		PropertyInfo environmentNameProperty = typeof(ApplicationCreateArgs)
			.GetProperty(nameof(ApplicationCreateArgs.EnvironmentName))!;

		// Act
		bool hasRequiredAttribute = environmentNameProperty
			.GetCustomAttributes(typeof(RequiredAttribute), inherit: false)
			.Any();

		// Assert
		hasRequiredAttribute.Should().BeFalse(
			because: "FR-05a makes environment-name schema-optional on create-app so a header-only passthrough call is not rejected before the tool runs");
	}

	[Test]
	[Category("Unit")]
	[Description("FR-05: the tool executes its body through the options-aware tenant-lock path, deriving the lock key from THIS call's target via IToolCommandResolver.GetTenantKey rather than the shared fallback key.")]
	public async Task ApplicationCreate_ShouldDeriveTenantLockKeyFromCallOptions_WhenExecuted() {
		// Arrange
		EnvironmentSettings registeredSettings = new() { Uri = "https://registered.example.com" };
		_commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>())
			.Returns(registeredSettings);
		_commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("tenant-key");
		ConfigureCreateSuccess(registeredSettings);

		// Act
		await _tool.ApplicationCreate(CreateArgs("sandbox"));

		// Assert
		_commandResolver.Received(1).GetTenantKey(
			Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"));
	}
}

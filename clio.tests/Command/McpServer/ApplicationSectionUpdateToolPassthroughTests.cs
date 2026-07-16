using System;
using System.ComponentModel.DataAnnotations;
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
/// Story 7 (ENG-93347) passthrough behavior of the <c>update-app-section</c> MCP tool: header-only
/// calls execute against the header tenant resolved by <see cref="IToolCommandResolver"/> across the
/// WHOLE nested call graph — the tool drives a REAL <see cref="ApplicationSectionUpdateService"/> so
/// the nested caption-culture resolution (today <c>:87</c>) and the nested <c>GetApplicationInfo</c>
/// read (today <c>:93</c>) are proven to use the Story-2 settings-based overloads and never a
/// name-based lookup or <see cref="ISettingsRepository"/> read. Mixed input is rejected by the
/// resolver's transport policy before ANY Creatio-reaching call anywhere in the graph, and
/// registered-environment / stdio behavior is unchanged.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ApplicationSectionUpdateToolPassthroughTests {

	private const string MixedInputRejectionMessage =
		"Explicit credential or environment arguments (uri/login/password/client-id/client-secret/environment) "
		+ "are not accepted when credential passthrough is enabled over HTTP. Supply the target environment "
		+ "and credentials via the X-Integration-Credentials header, not tool arguments.";

	private const string ResolverRequirednessMessage =
		"Either a configured environment name or an explicit URI is required for MCP command execution. "
		+ "Prefer a registered environment name; use explicit URI credentials only as a bootstrap or emergency fallback.";

	private ISettingsRepository _settingsRepository;
	private IApplicationClientFactory _applicationClientFactory;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private IApplicationInfoService _applicationInfoService;
	private ICaptionCultureResolver _captionCultureResolver;
	private IToolCommandResolver _commandResolver;
	private ApplicationSectionUpdateTool _tool;

	[SetUp]
	public void SetUp() {
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_applicationInfoService = Substitute.For<IApplicationInfoService>();
		_captionCultureResolver = Substitute.For<ICaptionCultureResolver>();
		_captionCultureResolver.Resolve(Arg.Any<EnvironmentSettings>(), Arg.Any<string?>()).Returns("en-US");
		_commandResolver = Substitute.For<IToolCommandResolver>();
		_serviceUrlBuilder.Build(Arg.Any<string>(), Arg.Any<EnvironmentSettings>())
			.Returns(callInfo => $"https://tenant.example.com/{callInfo.ArgAt<string>(0)}");
		// The tool drives the REAL section-update service so both nested calls (the profile-culture
		// resolution and the application-info read) are exercised for real instead of being hidden
		// behind a service substitute.
		IApplicationSectionUpdateService sectionUpdateService = new ApplicationSectionUpdateService(
			_settingsRepository,
			_applicationClientFactory,
			_serviceUrlBuilder,
			_applicationInfoService,
			_captionCultureResolver);
		_tool = new ApplicationSectionUpdateTool(Substitute.For<ILogger>(), _commandResolver, sectionUpdateService);
	}

	private static EnvironmentSettings CreateTenantSettings() => new() { Uri = "https://tenant.example.com" };

	private static ApplicationInfoResult CreateApplicationInfo() =>
		new("pkg-uid", "UsrOrdersApp", [], [], "app-id", "Orders App", "UsrOrdersApp", "8.3.0");

	private static ApplicationSectionUpdateArgs CreateArgs(string? environmentName) =>
		new(
			ApplicationCode: "UsrOrdersApp",
			SectionCode: "UsrOrders",
			EnvironmentName: environmentName,
			Caption: "Orders");

	private void ConfigureHeaderTenant(EnvironmentSettings settings) {
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => string.IsNullOrWhiteSpace(options.Environment)))
			.Returns(settings);
	}

	/// <summary>
	/// Stubs the full happy-path collaborator chain (application-info read, before/after section
	/// readback, UpdateQuery persist) so <see cref="ApplicationSectionUpdateService.UpdateSection(EnvironmentSettings, ApplicationSectionUpdateRequest)"/>
	/// completes end-to-end against <paramref name="settings"/>.
	/// </summary>
	private void ConfigureSuccessfulUpdate(EnvironmentSettings settings, ApplicationInfoResult applicationInfo) {
		_applicationClientFactory.CreateEnvironmentClient(settings).Returns(_applicationClient);
		_applicationInfoService.GetApplicationInfo(settings, null, "UsrOrdersApp").Returns(applicationInfo);
		// Section select (before-and-after readback share this stub via consecutive returns).
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"Code\"", StringComparison.Ordinal)))
			.Returns(
				"""{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"{\"en-US\":\"Orders\"}","Code":"UsrOrders","Description":"Old description","EntitySchemaName":"UsrOrder","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-old","IconBackground":"#111111","ClientTypeId":null}]}""",
				"""{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"Orders","Code":"UsrOrders","Description":"Old description","EntitySchemaName":"UsrOrder","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-old","IconBackground":"#111111","ClientTypeId":null}]}""");
		// UpdateQuery persist.
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
					body.Contains("\"Caption\"", StringComparison.Ordinal)))
			.Returns("""{"success":true}""");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-01/AC-02: a header-only passthrough call (no environment-name) updates the section against the header tenant resolved by IToolCommandResolver — the settings-based service overload runs end-to-end and the legacy 'Environment name is required' failure never surfaces.")]
	public async Task ApplicationSectionUpdate_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		ConfigureHeaderTenant(headerTenantSettings);
		ApplicationInfoResult applicationInfo = CreateApplicationInfo();
		ConfigureSuccessfulUpdate(headerTenantSettings, applicationInfo);

		// Act
		ApplicationSectionUpdateContextResponse result = await _tool.ApplicationSectionUpdate(
			CreateArgs(environmentName: null), server: null);

		// Assert
		result.Success.Should().BeTrue(
			because: "a header-only passthrough call must route to the header tenant instead of failing on a blank environment name");
		result.Error.Should().BeNull(
			because: "the routed call succeeded, so no error payload may be present — in particular not the legacy requiredness error");
		_applicationClientFactory.Received(1).CreateEnvironmentClient(headerTenantSettings);
		_settingsRepository.DidNotReceiveWithAnyArgs().FindEnvironment(default);
		_settingsRepository.DidNotReceiveWithAnyArgs().GetEnvironment(default(string));
	}

	[Test]
	[Category("Unit")]
	[Description("AC-03: nested caption-culture path (today ':87'). Under header-only passthrough the profile-validation culture is resolved through the settings-based ICaptionCultureResolver overload against the header tenant — never through the name-based overload.")]
	public async Task ApplicationSectionUpdate_ShouldResolveCaptionCultureAgainstHeaderTenant_WhenHeaderOnly() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		ConfigureHeaderTenant(headerTenantSettings);
		ApplicationInfoResult applicationInfo = CreateApplicationInfo();
		ConfigureSuccessfulUpdate(headerTenantSettings, applicationInfo);

		// Act
		ApplicationSectionUpdateContextResponse result = await _tool.ApplicationSectionUpdate(
			CreateArgs(environmentName: null), server: null);

		// Assert
		result.Success.Should().BeTrue(
			because: "the profile-culture resolution must not derail a header-only update call");
		_captionCultureResolver.Received(1).Resolve(headerTenantSettings, null);
		_captionCultureResolver.DidNotReceiveWithAnyArgs().Resolve(default(EnvironmentOptions)!, default);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-04: nested GetApplicationInfo path (today ':93'). Under header-only passthrough the application-info read uses the settings-based GetApplicationInfo overload against the header tenant — never through the name-based overload.")]
	public async Task ApplicationSectionUpdate_ShouldLoadApplicationInfoAgainstHeaderTenant_WhenHeaderOnly() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		ConfigureHeaderTenant(headerTenantSettings);
		ApplicationInfoResult applicationInfo = CreateApplicationInfo();
		ConfigureSuccessfulUpdate(headerTenantSettings, applicationInfo);

		// Act
		ApplicationSectionUpdateContextResponse result = await _tool.ApplicationSectionUpdate(
			CreateArgs(environmentName: null), server: null);

		// Assert
		result.Success.Should().BeTrue(
			because: "the application-info read must not derail a header-only update call");
		_applicationInfoService.Received(1).GetApplicationInfo(headerTenantSettings, null, "UsrOrdersApp");
		_applicationInfoService.DidNotReceiveWithAnyArgs().GetApplicationInfo(default(string)!, default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-05: under passthrough with BOTH the header and an explicit environment-name, the resolver's transport-policy rejection fires before ANY Creatio-reaching call in the entire graph — no client construction, no caption-culture resolution, no application-info lookup by either overload.")]
	public async Task ApplicationSectionUpdate_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent() {
		// Arrange
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => options.Environment == "other-registered-env"))
			.Returns(_ => throw new EnvironmentResolutionException(MixedInputRejectionMessage));

		// Act
		ApplicationSectionUpdateContextResponse result = await _tool.ApplicationSectionUpdate(
			CreateArgs("other-registered-env"), server: null);

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
	[Description("AC-06: on stdio / registered-environment transports an explicit environment-name resolves through the unchanged registry branch and the whole update-section flow — including the nested caption-culture call and the nested application-info read — executes against those settings exactly as before.")]
	public async Task ApplicationSectionUpdate_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio() {
		// Arrange
		EnvironmentSettings registeredSettings = new() { Uri = "https://registered.example.com" };
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"))
			.Returns(registeredSettings);
		ApplicationInfoResult applicationInfo = CreateApplicationInfo();
		ConfigureSuccessfulUpdate(registeredSettings, applicationInfo);

		// Act
		ApplicationSectionUpdateContextResponse result = await _tool.ApplicationSectionUpdate(
			CreateArgs("sandbox"), server: null);

		// Assert
		result.Success.Should().BeTrue(
			because: "registered-environment resolution must keep working exactly as the pre-change baseline");
		_commandResolver.Received(1).Resolve<EnvironmentSettings>(
			Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"));
		_captionCultureResolver.Received(1).Resolve(registeredSettings, null);
		_applicationInfoService.Received(1).GetApplicationInfo(registeredSettings, null, "UsrOrdersApp");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-02 (runtime requiredness): outside passthrough, a call with no environment-name surfaces the resolver's EnvironmentResolutionException as a typed error envelope — requiredness is enforced at runtime, not by the MCP schema.")]
	public async Task ApplicationSectionUpdate_ShouldReturnResolverRequirednessError_WhenNoEnvironmentNameAndNoPassthrough() {
		// Arrange
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => string.IsNullOrWhiteSpace(options.Environment)))
			.Returns(_ => throw new EnvironmentResolutionException(ResolverRequirednessMessage));

		// Act
		ApplicationSectionUpdateContextResponse result = await _tool.ApplicationSectionUpdate(
			CreateArgs(environmentName: null), server: null);

		// Assert
		result.Success.Should().BeFalse(
			because: "without a passthrough context a blank environment-name has no resolvable target");
		result.Error.Should().Contain("configured environment name",
			because: "the resolver's actionable requiredness message must reach the caller instead of an opaque failure");
		_applicationClientFactory.DidNotReceiveWithAnyArgs().CreateEnvironmentClient(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-ERR(b): a valid passthrough header whose target update operation fails yields the typed error envelope with redacted text — no accessToken/login/password material leaks.")]
	public async Task ApplicationSectionUpdate_ShouldReturnRedactedErrorEnvelope_WhenHeaderTenantOperationFails() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		ConfigureHeaderTenant(headerTenantSettings);
		_applicationClientFactory.CreateEnvironmentClient(headerTenantSettings).Returns(_applicationClient);
		_applicationInfoService.GetApplicationInfo(headerTenantSettings, null, "UsrOrdersApp")
			.Returns(CreateApplicationInfo());
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"Code\"", StringComparison.Ordinal)))
			.Returns(_ => throw new InvalidOperationException(
				"Section select failed. token=super-secret-token-value"));

		// Act
		ApplicationSectionUpdateContextResponse result = await _tool.ApplicationSectionUpdate(
			CreateArgs(environmentName: null), server: null);

		// Assert
		result.Success.Should().BeFalse(
			because: "an operation failure against a valid header tenant must surface as a typed error envelope, not a protocol error");
		result.Error.Should().Contain("Section select failed",
			because: "the caller-actionable failure description must be preserved");
		result.Error.Should().NotContain("super-secret-token-value",
			because: "SensitiveErrorTextRedactor must scrub credential material before it crosses the MCP boundary");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-02 (schema relax, FR-05a): environment-name on ApplicationSectionUpdateArgs carries no [Required] attribute, so a header-only call reaches the tool method instead of being rejected at MCP binding.")]
	public void ApplicationSectionUpdateArgs_ShouldBeSchemaOptional_WhenEnvironmentNamePropertyIsInspected() {
		// Arrange
		PropertyInfo environmentNameProperty = typeof(ApplicationSectionUpdateArgs)
			.GetProperty(nameof(ApplicationSectionUpdateArgs.EnvironmentName))!;

		// Act
		bool hasRequiredAttribute = environmentNameProperty
			.GetCustomAttributes(typeof(RequiredAttribute), inherit: false)
			.Length > 0;

		// Assert
		hasRequiredAttribute.Should().BeFalse(
			because: "FR-05a makes environment-name schema-optional on update-app-section so a header-only passthrough call is not rejected before the tool runs");
	}

	[Test]
	[Category("Unit")]
	[Description("FR-05: the tool executes its body through the options-aware tenant-lock path, deriving the lock key from THIS call's target via IToolCommandResolver.GetTenantKey rather than the shared fallback key.")]
	public async Task ApplicationSectionUpdate_ShouldDeriveTenantLockKeyFromCallOptions_WhenExecuted() {
		// Arrange
		EnvironmentSettings registeredSettings = new() { Uri = "https://registered.example.com" };
		_commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>())
			.Returns(registeredSettings);
		_commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("tenant-key");
		ApplicationInfoResult applicationInfo = CreateApplicationInfo();
		ConfigureSuccessfulUpdate(registeredSettings, applicationInfo);

		// Act
		await _tool.ApplicationSectionUpdate(CreateArgs("sandbox"), server: null);

		// Assert
		_commandResolver.Received(1).GetTenantKey(
			Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"));
	}
}

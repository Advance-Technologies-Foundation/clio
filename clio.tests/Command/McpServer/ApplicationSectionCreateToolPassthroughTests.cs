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
/// Story 6 (ENG-93347) passthrough behavior of the <c>create-app-section</c> MCP tool: header-only
/// calls execute against the header tenant resolved by <see cref="IToolCommandResolver"/> across the
/// ENTIRE nested call graph — the tool drives a REAL <see cref="ApplicationSectionCreateService"/> so
/// the FOUR nested call sites (readback caption-culture AC-03, profile-validation caption-culture
/// AC-04, validation application-info AC-05, polling application-info AC-06) are proven to use the
/// Story-2 settings-based overloads and never a name-based lookup or <see cref="ISettingsRepository"/>
/// read. Mixed input is rejected by the resolver's transport policy before ANY Creatio-reaching call
/// anywhere in the graph, and registered-environment / stdio behavior is unchanged.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ApplicationSectionCreateToolPassthroughTests {

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
	private IServiceUrlBuilderFactory _serviceUrlBuilderFactory;
	private IApplicationInfoService _applicationInfoService;
	private ISysSettingsManager _sysSettingsManager;
	private ICaptionCultureResolver _captionCultureResolver;
	private IToolCommandResolver _commandResolver;
	private ApplicationSectionCreateTool _tool;

	[SetUp]
	public void SetUp() {
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilderFactory = Substitute.For<IServiceUrlBuilderFactory>();
		_serviceUrlBuilderFactory.Create(Arg.Any<EnvironmentSettings>()).Returns(_serviceUrlBuilder);
		_applicationInfoService = Substitute.For<IApplicationInfoService>();
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns("Usr");
		_captionCultureResolver = Substitute.For<ICaptionCultureResolver>();
		_captionCultureResolver.Resolve(Arg.Any<EnvironmentSettings>(), Arg.Any<string?>()).Returns("en-US");
		_commandResolver = Substitute.For<IToolCommandResolver>();
		_serviceUrlBuilder.Build(Arg.Any<string>(), Arg.Any<EnvironmentSettings>())
			.Returns(callInfo => $"https://tenant.example.com/{callInfo.ArgAt<string>(0)}");
		// The tool drives the REAL section-create service so all four nested calls (both
		// caption-culture resolutions and both application-info reads) are exercised for real
		// instead of being hidden behind a service substitute.
		IApplicationSectionCreateService sectionCreateService = new ApplicationSectionCreateService(
			_settingsRepository,
			_applicationClientFactory,
			_serviceUrlBuilder,
			_serviceUrlBuilderFactory,
			_applicationInfoService,
			_ => _sysSettingsManager,
			new NullLogger(),
			_captionCultureResolver);
		_tool = new ApplicationSectionCreateTool(Substitute.For<ILogger>(), _commandResolver, sectionCreateService);
	}

	private static EnvironmentSettings CreateTenantSettings() => new() { Uri = "https://tenant.example.com" };

	private static ApplicationInfoResult CreateBeforeInfo() =>
		new("pkg-uid", "UsrOrdersApp", [], [], "app-id", "Orders App", "UsrOrdersApp", "8.3.0");

	private static ApplicationEntityInfoResult CreateEntity() =>
		new("entity-uid", "UsrOrders", "Orders", []);

	private static ApplicationInfoResult CreateAfterInfo(ApplicationEntityInfoResult entity) =>
		new(
			"pkg-uid",
			"UsrOrdersApp",
			[entity],
			[
				new PageListItem {
					SchemaName = "UsrOrders_FormPage",
					UId = "page-new",
					PackageName = "UsrOrdersApp",
					ParentSchemaName = "BasePage"
				}
			],
			"app-id",
			"Orders App",
			"UsrOrdersApp",
			"8.4.0");

	private static ApplicationSectionCreateArgs CreateArgs(string? environmentName, string? captionCulture = null) =>
		new(
			ApplicationCode: "UsrOrdersApp",
			Caption: "Orders",
			EnvironmentName: environmentName,
			CaptionCulture: captionCulture);

	private void ConfigureHeaderTenant(EnvironmentSettings settings) {
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => string.IsNullOrWhiteSpace(options.Environment)))
			.Returns(settings);
	}

	/// <summary>
	/// Stubs the full happy-path collaborator chain (icon resolution, entity-existence probe, insert,
	/// section readback, icon-background update) so <see cref="ApplicationSectionCreateService.CreateSection(EnvironmentSettings, ApplicationSectionCreateRequest, int?, int?)"/>
	/// completes end-to-end against <paramref name="settings"/>.
	/// </summary>
	private void ConfigureSuccessfulCreate(
		EnvironmentSettings settings, ApplicationInfoResult beforeInfo, ApplicationInfoResult afterInfo) {
		_applicationClientFactory.CreateEnvironmentClient(settings).Returns(_applicationClient);
		_applicationInfoService.GetApplicationInfo(settings, null, "UsrOrdersApp").Returns(beforeInfo, afterInfo);
		// Random icon resolution (SysAppIcons select).
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysAppIcons\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"11111111-1111-1111-1111-111111111111"}]}""");
		// Entity-schema-does-not-exist probe (new-object section path — no entity-schema-name supplied).
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysSchema\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[]}""");
		// Section insert.
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
					!body.Contains("\"filters\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns("""{"success":true}""");
		// Section readback select. The tool ALWAYS passes an explicit BackgroundReadbackTimeoutMs
		// override, so this call runs with an explicit (non-default) requestTimeout — the 3-arg
		// ExecutePostRequest overload must be stubbed with Arg.Any<int>(), not left to the 2-arg
		// (implicit Timeout.Infinite) overload the CLI-only path uses.
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"SectionSchemaUId\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns("""{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"Orders","Code":"UsrOrders","Description":null,"EntitySchemaName":"UsrOrders","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-id","IconBackground":"#A6DE00","ClientTypeId":null}]}""");
		// Icon-background update — same explicit-timeout reasoning as the section readback above.
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
					body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
					body.Contains("\"filters\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns("""{"success":true}""");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-01/AC-02: a header-only passthrough call (no environment-name) creates the section against the header tenant resolved by IToolCommandResolver — the settings-based service overload runs end-to-end and the legacy 'Environment name is required' failure never surfaces.")]
	public async Task ApplicationSectionCreate_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		ConfigureHeaderTenant(headerTenantSettings);
		ApplicationInfoResult beforeInfo = CreateBeforeInfo();
		ApplicationEntityInfoResult entity = CreateEntity();
		ApplicationInfoResult afterInfo = CreateAfterInfo(entity);
		ConfigureSuccessfulCreate(headerTenantSettings, beforeInfo, afterInfo);

		// Act
		ApplicationSectionContextResponse result = await _tool.ApplicationSectionCreate(
			CreateArgs(environmentName: null), server: null);

		// Assert
		result.Success.Should().BeTrue(
			because: "a header-only passthrough call must route to the header tenant instead of failing on a blank environment name");
		result.Error.Should().BeNull(
			because: "the routed call succeeded, so no error payload may be present — in particular not the legacy requiredness error");
		result.ApplicationVersion.Should().Be("8.4.0",
			because: "the returned application context must come from the post-insert readback (afterInfo), proving the polling site resolved against the header tenant");
		_applicationClientFactory.Received(1).CreateEnvironmentClient(headerTenantSettings);
		_settingsRepository.DidNotReceiveWithAnyArgs().FindEnvironment(default);
		_settingsRepository.DidNotReceiveWithAnyArgs().GetEnvironment(default(string));
	}

	[Test]
	[Category("Unit")]
	[Description("AC-03 (nested caption-culture path, readback site — today ':202'): under header-only passthrough the readback culture is resolved through the settings-based ICaptionCultureResolver overload against the header tenant with the caller's caption-culture override — never through the name-based overload.")]
	public async Task ApplicationSectionCreate_ShouldResolveReadbackCaptionCultureAgainstHeaderTenant_WhenHeaderOnly() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		ConfigureHeaderTenant(headerTenantSettings);
		ApplicationInfoResult beforeInfo = CreateBeforeInfo();
		ApplicationEntityInfoResult entity = CreateEntity();
		ApplicationInfoResult afterInfo = CreateAfterInfo(entity);
		ConfigureSuccessfulCreate(headerTenantSettings, beforeInfo, afterInfo);

		// Act
		ApplicationSectionContextResponse result = await _tool.ApplicationSectionCreate(
			CreateArgs(environmentName: null, captionCulture: "uk-UA"), server: null);

		// Assert
		result.Success.Should().BeTrue(
			because: "the readback-culture resolution must not derail a header-only create call");
		_captionCultureResolver.Received(1).Resolve(headerTenantSettings, "uk-UA");
		_captionCultureResolver.DidNotReceiveWithAnyArgs().Resolve(default(EnvironmentOptions)!, default);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-04 (nested caption-culture path, profile-validation site — today ':219'): under header-only passthrough the profile-validation culture is resolved through the settings-based ICaptionCultureResolver overload against the header tenant with a null override — independent of AC-03's readback-culture call, which is a duplicated call site rather than one call reused twice.")]
	public async Task ApplicationSectionCreate_ShouldResolveProfileValidationCaptionCultureAgainstHeaderTenant_WhenHeaderOnly() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		ConfigureHeaderTenant(headerTenantSettings);
		ApplicationInfoResult beforeInfo = CreateBeforeInfo();
		ApplicationEntityInfoResult entity = CreateEntity();
		ApplicationInfoResult afterInfo = CreateAfterInfo(entity);
		ConfigureSuccessfulCreate(headerTenantSettings, beforeInfo, afterInfo);

		// Act
		ApplicationSectionContextResponse result = await _tool.ApplicationSectionCreate(
			CreateArgs(environmentName: null, captionCulture: "uk-UA"), server: null);

		// Assert
		result.Success.Should().BeTrue(
			because: "the profile-validation culture resolution must not derail a header-only create call");
		_captionCultureResolver.Received(1).Resolve(headerTenantSettings, null);
		_captionCultureResolver.DidNotReceiveWithAnyArgs().Resolve(default(EnvironmentOptions)!, default);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-05 (nested GetApplicationInfo path, validation site — today ':219' region): under header-only passthrough the pre-insert application-info read uses the settings-based GetApplicationInfo overload against the header tenant. Isolated from AC-06 by forcing the icon-resolution step (a later preparation step) to fail with an unclassified error, so the polling site is never reached and the validation call is proven independently, received exactly once.")]
	public async Task ApplicationSectionCreate_ShouldLoadValidationApplicationInfoAgainstHeaderTenant_WhenHeaderOnly() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		ConfigureHeaderTenant(headerTenantSettings);
		_applicationClientFactory.CreateEnvironmentClient(headerTenantSettings).Returns(_applicationClient);
		_applicationInfoService.GetApplicationInfo(headerTenantSettings, null, "UsrOrdersApp")
			.Returns(CreateBeforeInfo());
		// Icon resolution (SysAppIcons) is deliberately left unstubbed: ExecutePostRequest returns null,
		// which fails JSON deserialization with an unclassified exception — a preparation-step failure
		// that happens strictly AFTER the validation application-info read and strictly BEFORE the
		// insert/polling phase, isolating this call site.

		// Act
		ApplicationSectionContextResponse result = await _tool.ApplicationSectionCreate(
			CreateArgs(environmentName: null), server: null);

		// Assert
		result.Success.Should().BeFalse(
			because: "the forced icon-resolution failure must surface as a preparation-step error, proving the earlier validation application-info read already completed");
		_applicationInfoService.Received(1).GetApplicationInfo(headerTenantSettings, null, "UsrOrdersApp");
		_applicationInfoService.DidNotReceiveWithAnyArgs().GetApplicationInfo(default(string)!, default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-06 (nested GetApplicationInfo path, polling site — today ':737'): under header-only passthrough the post-insert polling readback ALSO uses the settings-based GetApplicationInfo overload against the header tenant — the returned application context reflects the SECOND (afterInfo) call, proving the polling site (not just the validation site) resolved against the header tenant. Both settings-based reads (validation + polling) are received, never the name-based overload.")]
	public async Task ApplicationSectionCreate_ShouldPollApplicationInfoAgainstHeaderTenant_WhenHeaderOnly() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		ConfigureHeaderTenant(headerTenantSettings);
		ApplicationInfoResult beforeInfo = CreateBeforeInfo();
		ApplicationEntityInfoResult entity = CreateEntity();
		ApplicationInfoResult afterInfo = CreateAfterInfo(entity);
		ConfigureSuccessfulCreate(headerTenantSettings, beforeInfo, afterInfo);

		// Act
		ApplicationSectionContextResponse result = await _tool.ApplicationSectionCreate(
			CreateArgs(environmentName: null), server: null);

		// Assert
		result.Success.Should().BeTrue(
			because: "the polling readback must complete against the header tenant for the call to succeed");
		result.ApplicationVersion.Should().Be(afterInfo.ApplicationVersion,
			because: "the returned application-version must come from the polling site's (second) settings-based read, not the validation site's (first) read");
		_applicationInfoService.Received(2).GetApplicationInfo(headerTenantSettings, null, "UsrOrdersApp");
		_applicationInfoService.DidNotReceiveWithAnyArgs().GetApplicationInfo(default(string)!, default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-07: under passthrough with BOTH the header and an explicit environment-name, the resolver's transport-policy rejection fires before ANY Creatio-reaching call in the entire graph — no client construction, no caption-culture resolution at either nested site, no application-info lookup at either nested site by either overload.")]
	public async Task ApplicationSectionCreate_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent() {
		// Arrange
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => options.Environment == "other-registered-env"))
			.Returns(_ => throw new EnvironmentResolutionException(MixedInputRejectionMessage));

		// Act
		ApplicationSectionContextResponse result = await _tool.ApplicationSectionCreate(
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
	[Description("AC-08: on stdio / registered-environment transports an explicit environment-name resolves through the unchanged registry branch and the whole create-section flow — including both nested caption-culture calls and both nested application-info reads — executes against those settings exactly as before.")]
	public async Task ApplicationSectionCreate_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio() {
		// Arrange
		EnvironmentSettings registeredSettings = new() { Uri = "https://registered.example.com" };
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"))
			.Returns(registeredSettings);
		ApplicationInfoResult beforeInfo = CreateBeforeInfo();
		ApplicationEntityInfoResult entity = CreateEntity();
		ApplicationInfoResult afterInfo = CreateAfterInfo(entity);
		ConfigureSuccessfulCreate(registeredSettings, beforeInfo, afterInfo);

		// Act
		ApplicationSectionContextResponse result = await _tool.ApplicationSectionCreate(
			CreateArgs("sandbox"), server: null);

		// Assert
		result.Success.Should().BeTrue(
			because: "registered-environment resolution must keep working exactly as the pre-change baseline");
		_commandResolver.Received(1).Resolve<EnvironmentSettings>(
			Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"));
		// No caption-culture override was supplied, so BOTH nested culture calls (readback + profile
		// validation) resolve with a null override — hence exactly 2 matching calls, not 1.
		_captionCultureResolver.Received(2).Resolve(registeredSettings, null);
		_applicationInfoService.Received(2).GetApplicationInfo(registeredSettings, null, "UsrOrdersApp");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-02 (runtime requiredness): outside passthrough, a call with no environment-name surfaces the resolver's EnvironmentResolutionException as a typed error envelope — requiredness is enforced at runtime, not by the MCP schema.")]
	public async Task ApplicationSectionCreate_ShouldReturnResolverRequirednessError_WhenNoEnvironmentNameAndNoPassthrough() {
		// Arrange
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => string.IsNullOrWhiteSpace(options.Environment)))
			.Returns(_ => throw new EnvironmentResolutionException(ResolverRequirednessMessage));

		// Act
		ApplicationSectionContextResponse result = await _tool.ApplicationSectionCreate(
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
	[Description("AC-ERR(b): a valid passthrough header whose target section-insert operation fails yields the typed error envelope with redacted text — no accessToken/login/password material leaks.")]
	public async Task ApplicationSectionCreate_ShouldReturnRedactedErrorEnvelope_WhenHeaderTenantOperationFails() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		ConfigureHeaderTenant(headerTenantSettings);
		_applicationClientFactory.CreateEnvironmentClient(headerTenantSettings).Returns(_applicationClient);
		_applicationInfoService.GetApplicationInfo(headerTenantSettings, null, "UsrOrdersApp")
			.Returns(CreateBeforeInfo());
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysAppIcons\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"11111111-1111-1111-1111-111111111111"}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysSchema\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
					!body.Contains("\"filters\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns(_ => throw new InvalidOperationException(
				"Insert request failed. token=super-secret-token-value"));

		// Act
		ApplicationSectionContextResponse result = await _tool.ApplicationSectionCreate(
			CreateArgs(environmentName: null), server: null);

		// Assert
		result.Success.Should().BeFalse(
			because: "an operation failure against a valid header tenant must surface as a typed error envelope, not a protocol error");
		result.Error.Should().Contain("Insert request failed",
			because: "the caller-actionable failure description must be preserved");
		result.Error.Should().NotContain("super-secret-token-value",
			because: "SensitiveErrorTextRedactor must scrub credential material before it crosses the MCP boundary");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-02 (schema relax, FR-05a): environment-name on ApplicationSectionCreateArgs carries no [Required] attribute, so a header-only call reaches the tool method instead of being rejected at MCP binding.")]
	public void ApplicationSectionCreateArgs_ShouldBeSchemaOptional_WhenEnvironmentNamePropertyIsInspected() {
		// Arrange
		PropertyInfo environmentNameProperty = typeof(ApplicationSectionCreateArgs)
			.GetProperty(nameof(ApplicationSectionCreateArgs.EnvironmentName))!;

		// Act
		bool hasRequiredAttribute = environmentNameProperty
			.GetCustomAttributes(typeof(RequiredAttribute), inherit: false)
			.Any();

		// Assert
		hasRequiredAttribute.Should().BeFalse(
			because: "FR-05a makes environment-name schema-optional on create-app-section so a header-only passthrough call is not rejected before the tool runs");
	}

	[Test]
	[Category("Unit")]
	[Description("FR-05: the tool executes its body through the options-aware tenant-lock path, deriving the lock key from THIS call's target via IToolCommandResolver.GetTenantKey rather than the shared fallback key.")]
	public async Task ApplicationSectionCreate_ShouldDeriveTenantLockKeyFromCallOptions_WhenExecuted() {
		// Arrange
		EnvironmentSettings registeredSettings = new() { Uri = "https://registered.example.com" };
		_commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>())
			.Returns(registeredSettings);
		_commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("tenant-key");
		ApplicationInfoResult beforeInfo = CreateBeforeInfo();
		ApplicationEntityInfoResult entity = CreateEntity();
		ApplicationInfoResult afterInfo = CreateAfterInfo(entity);
		ConfigureSuccessfulCreate(registeredSettings, beforeInfo, afterInfo);

		// Act
		await _tool.ApplicationSectionCreate(CreateArgs("sandbox"), server: null);

		// Assert
		_commandResolver.Received(1).GetTenantKey(
			Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"));
	}
}

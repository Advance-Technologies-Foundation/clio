using System;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 9 (ENG-93347) passthrough behavior of the <c>list-app-sections</c> MCP tool: header-only
/// calls execute against the header tenant resolved by <see cref="IToolCommandResolver"/> across the
/// WHOLE nested call graph — the tool drives a REAL <see cref="ApplicationSectionGetListService"/> so
/// the single nested dependency (today <c>ApplicationSectionGetListCommand.cs:76</c>,
/// <c>FindApplicationId</c>) is proven to use the Story-2 settings-based overload and never a
/// name-based lookup or <see cref="ISettingsRepository"/> read. Mixed input is rejected by the
/// resolver's transport policy before ANY Creatio-reaching call anywhere in the graph, and
/// registered-environment / stdio behavior is unchanged.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ApplicationSectionGetListToolPassthroughTests {

	private const string MixedInputRejectionMessage =
		"Explicit credential or environment arguments (uri/login/password/client-id/client-secret/environment) "
		+ "are not accepted when credential passthrough is enabled over HTTP. Supply the target environment "
		+ "and credentials via the X-Integration-Credentials header, not tool arguments.";

	private const string ResolverRequirednessMessage =
		"Either a configured environment name or an explicit URI is required for MCP command execution. "
		+ "Prefer a registered environment name; use explicit URI credentials only as a bootstrap or emergency fallback.";

	private const string SectionId = "61f65fdb-3b63-4fcf-9110-9863457b3a0b";

	private ISettingsRepository _settingsRepository;
	private IApplicationClientFactory _applicationClientFactory;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private IApplicationInfoService _applicationInfoService;
	private IToolCommandResolver _commandResolver;
	private ApplicationSectionGetListTool _tool;

	[SetUp]
	public void SetUp() {
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_applicationInfoService = Substitute.For<IApplicationInfoService>();
		_commandResolver = Substitute.For<IToolCommandResolver>();
		_serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>(), Arg.Any<EnvironmentSettings>())
			.Returns(callInfo => $"https://tenant.example.com/{callInfo.ArgAt<ServiceUrlBuilder.KnownRoute>(0)}");
		// The tool drives the REAL section-list service so the nested FindApplicationId call is
		// exercised for real instead of being hidden behind a service substitute.
		IApplicationSectionGetListService sectionGetListService = new ApplicationSectionGetListService(
			_settingsRepository,
			_applicationClientFactory,
			_serviceUrlBuilder,
			_applicationInfoService,
			Substitute.For<ILogger>());
		_tool = new ApplicationSectionGetListTool(Substitute.For<ILogger>(), _commandResolver, sectionGetListService);
	}

	private static EnvironmentSettings CreateTenantSettings() => new() { Uri = "https://tenant.example.com" };

	private static InstalledAppSummary CreateApplicationSummary() =>
		new("app-id", "UsrOrdersApp", "Orders App", "8.3.0");

	private static ApplicationSectionGetListArgs CreateArgs(string? environmentName) =>
		new(
			ApplicationCode: "UsrOrdersApp",
			EnvironmentName: environmentName);

	private void ConfigureHeaderTenant(EnvironmentSettings settings) {
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => string.IsNullOrWhiteSpace(options.Environment)))
			.Returns(settings);
	}

	/// <summary>
	/// Stubs the full happy-path collaborator chain (application lookup, section select) so
	/// <see cref="ApplicationSectionGetListService.GetSections(EnvironmentSettings, ApplicationSectionGetListRequest)"/>
	/// completes end-to-end against <paramref name="settings"/>.
	/// </summary>
	private void ConfigureSuccessfulList(EnvironmentSettings settings, InstalledAppSummary applicationSummary) {
		_applicationClientFactory.CreateEnvironmentClient(settings).Returns(_applicationClient);
		_applicationInfoService.FindApplicationId(settings, "UsrOrdersApp").Returns(applicationSummary);
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(BuildSectionSelectResponse());
	}

	private static string BuildSectionSelectResponse() =>
		$$"""
		{
		  "success": true,
		  "rows": [{
		    "Id": "{{SectionId}}",
		    "ApplicationId": "app-id",
		    "Caption": "Orders",
		    "Code": "UsrOrders",
		    "Description": "Order workspace",
		    "EntitySchemaName": "UsrOrder",
		    "PackageId": "00000000-0000-0000-0000-000000000000",
		    "SectionSchemaUId": "731ef26f-5a01-4e9d-8586-2e83b5ae6998",
		    "LogoId": "icon-id",
		    "IconBackground": "#A49839",
		    "ClientTypeId": null
		  }]
		}
		""";

	[Test]
	[Category("Unit")]
	[Description("AC-01/AC-02: a header-only passthrough call (no environment-name) lists the sections against the header tenant resolved by IToolCommandResolver — the settings-based service overload runs end-to-end and the legacy 'Environment name is required' failure never surfaces.")]
	public async Task ApplicationSectionGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		ConfigureHeaderTenant(headerTenantSettings);
		InstalledAppSummary applicationSummary = CreateApplicationSummary();
		ConfigureSuccessfulList(headerTenantSettings, applicationSummary);

		// Act
		ApplicationSectionListContextResponse result = await _tool.ApplicationSectionGetList(
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
	[Description("AC-03: nested FindApplicationId path (today ':76'). Under header-only passthrough the application lookup is resolved through the settings-based IApplicationInfoService overload against the header tenant — never through the name-based overload.")]
	public async Task ApplicationSectionGetList_ShouldResolveApplicationIdAgainstHeaderTenant_WhenHeaderOnly() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		ConfigureHeaderTenant(headerTenantSettings);
		InstalledAppSummary applicationSummary = CreateApplicationSummary();
		ConfigureSuccessfulList(headerTenantSettings, applicationSummary);

		// Act
		ApplicationSectionListContextResponse result = await _tool.ApplicationSectionGetList(
			CreateArgs(environmentName: null), server: null);

		// Assert
		result.Success.Should().BeTrue(
			because: "the nested application lookup must not derail a header-only list call");
		_applicationInfoService.Received(1).FindApplicationId(headerTenantSettings, "UsrOrdersApp");
		_applicationInfoService.DidNotReceiveWithAnyArgs().FindApplicationId(default(string)!, default!);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-04: nested FindApplicationId path. Under passthrough with BOTH the header and an explicit environment-name, the resolver's transport-policy rejection fires before ANY Creatio-reaching call — no client construction and no application lookup by either overload.")]
	public async Task ApplicationSectionGetList_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent() {
		// Arrange
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => options.Environment == "other-registered-env"))
			.Returns(_ => throw new EnvironmentResolutionException(MixedInputRejectionMessage));

		// Act
		ApplicationSectionListContextResponse result = await _tool.ApplicationSectionGetList(
			CreateArgs("other-registered-env"), server: null);

		// Assert
		result.Success.Should().BeFalse(
			because: "mixed header + environment-name input must be rejected by the existing transport policy");
		result.Error.Should().Contain("X-Integration-Credentials",
			because: "the rejection must teach the caller the correct credential channel");
		_applicationClientFactory.DidNotReceiveWithAnyArgs().CreateEnvironmentClient(default!);
		_applicationInfoService.DidNotReceiveWithAnyArgs().FindApplicationId(default(EnvironmentSettings)!, default!);
		_applicationInfoService.DidNotReceiveWithAnyArgs().FindApplicationId(default(string)!, default!);
		_settingsRepository.DidNotReceiveWithAnyArgs().FindEnvironment(default);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-05: on stdio / registered-environment transports an explicit environment-name resolves through the unchanged registry branch and the whole section-list flow — including the nested FindApplicationId call — executes against those settings exactly as before.")]
	public async Task ApplicationSectionGetList_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio() {
		// Arrange
		EnvironmentSettings registeredSettings = new() { Uri = "https://registered.example.com" };
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"))
			.Returns(registeredSettings);
		InstalledAppSummary applicationSummary = CreateApplicationSummary();
		ConfigureSuccessfulList(registeredSettings, applicationSummary);

		// Act
		ApplicationSectionListContextResponse result = await _tool.ApplicationSectionGetList(
			CreateArgs("sandbox"), server: null);

		// Assert
		result.Success.Should().BeTrue(
			because: "registered-environment resolution must keep working exactly as the pre-change baseline");
		_commandResolver.Received(1).Resolve<EnvironmentSettings>(
			Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"));
		_applicationInfoService.Received(1).FindApplicationId(registeredSettings, "UsrOrdersApp");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-02 (runtime requiredness): outside passthrough, a call with no environment-name surfaces the resolver's EnvironmentResolutionException as a typed error envelope — requiredness is enforced at runtime, not by the MCP schema.")]
	public async Task ApplicationSectionGetList_ShouldReturnResolverRequirednessError_WhenNoEnvironmentNameAndNoPassthrough() {
		// Arrange
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => string.IsNullOrWhiteSpace(options.Environment)))
			.Returns(_ => throw new EnvironmentResolutionException(ResolverRequirednessMessage));

		// Act
		ApplicationSectionListContextResponse result = await _tool.ApplicationSectionGetList(
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
	[Description("AC-ERR(b): a valid passthrough header whose target list operation fails yields the typed error envelope with redacted text — no accessToken/login/password material leaks.")]
	public async Task ApplicationSectionGetList_ShouldReturnRedactedErrorEnvelope_WhenHeaderTenantOperationFails() {
		// Arrange
		EnvironmentSettings headerTenantSettings = CreateTenantSettings();
		ConfigureHeaderTenant(headerTenantSettings);
		_applicationClientFactory.CreateEnvironmentClient(headerTenantSettings).Returns(_applicationClient);
		_applicationInfoService.FindApplicationId(headerTenantSettings, "UsrOrdersApp")
			.Returns(CreateApplicationSummary());
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal)))
			.Returns(_ => throw new InvalidOperationException(
				"Section select failed. token=super-secret-token-value"));

		// Act
		ApplicationSectionListContextResponse result = await _tool.ApplicationSectionGetList(
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
	[Description("AC-02 (schema relax, FR-05a): environment-name on ApplicationSectionGetListArgs carries no [Required] attribute, so a header-only call reaches the tool method instead of being rejected at MCP binding.")]
	public void ApplicationSectionGetListArgs_ShouldBeSchemaOptional_WhenEnvironmentNamePropertyIsInspected() {
		// Arrange
		PropertyInfo environmentNameProperty = typeof(ApplicationSectionGetListArgs)
			.GetProperty(nameof(ApplicationSectionGetListArgs.EnvironmentName))!;

		// Act
		bool hasRequiredAttribute = environmentNameProperty
			.GetCustomAttributes(typeof(RequiredAttribute), inherit: false)
			.Length > 0;

		// Assert
		hasRequiredAttribute.Should().BeFalse(
			because: "FR-05a makes environment-name schema-optional on list-app-sections so a header-only passthrough call is not rejected before the tool runs");
	}

	[Test]
	[Category("Unit")]
	[Description("FR-05: the tool executes its body through the options-aware tenant-lock path, deriving the lock key from THIS call's target via IToolCommandResolver.GetTenantKey rather than the shared fallback key.")]
	public async Task ApplicationSectionGetList_ShouldDeriveTenantLockKeyFromCallOptions_WhenExecuted() {
		// Arrange
		EnvironmentSettings registeredSettings = new() { Uri = "https://registered.example.com" };
		_commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>())
			.Returns(registeredSettings);
		_commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("tenant-key");
		InstalledAppSummary applicationSummary = CreateApplicationSummary();
		ConfigureSuccessfulList(registeredSettings, applicationSummary);

		// Act
		await _tool.ApplicationSectionGetList(CreateArgs("sandbox"), server: null);

		// Assert
		_commandResolver.Received(1).GetTenantKey(
			Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"));
	}
}

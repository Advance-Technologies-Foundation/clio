using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 3 (ENG-93347) passthrough behavior of the <c>list-apps</c> MCP tool: header-only calls
/// execute against the header tenant resolved by <see cref="IToolCommandResolver"/> (the schema no
/// longer rejects a blank <c>environment-name</c>), mixed input is rejected by the resolver's
/// transport policy before any Creatio-reaching call, and registered-environment / stdio behavior is
/// unchanged — including the resolver's runtime requiredness error on non-passthrough transports.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ApplicationGetListToolPassthroughTests {

	private const string LegacyRequirednessMessage = "Environment name is required. (Parameter 'environmentName')";

	private const string MixedInputRejectionMessage =
		"Explicit credential or environment arguments (uri/login/password/client-id/client-secret/environment) "
		+ "are not accepted when credential passthrough is enabled over HTTP. Supply the target environment "
		+ "and credentials via the X-Integration-Credentials header, not tool arguments.";

	private const string ResolverRequirednessMessage =
		"Either a configured environment name or an explicit URI is required for MCP command execution. "
		+ "Prefer a registered environment name; use explicit URI credentials only as a bootstrap or emergency fallback.";

	private IApplicationListService _applicationListService;
	private IToolCommandResolver _commandResolver;
	private ApplicationGetListTool _tool;

	[SetUp]
	public void SetUp() {
		_applicationListService = Substitute.For<IApplicationListService>();
		_commandResolver = Substitute.For<IToolCommandResolver>();
		_tool = new ApplicationGetListTool(Substitute.For<ILogger>(), _commandResolver, _applicationListService);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-01/AC-02: a header-only passthrough call (no environment-name) executes against the header tenant resolved by IToolCommandResolver and never surfaces the legacy 'Environment name is required' failure.")]
	public void ApplicationGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly() {
		// Arrange
		EnvironmentSettings headerTenantSettings = new() { Uri = "https://tenant.example.com" };
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => string.IsNullOrWhiteSpace(options.Environment)))
			.Returns(headerTenantSettings);
		Guid appId = Guid.NewGuid();
		_applicationListService.GetApplications(headerTenantSettings, null, null)
			.Returns([new InstalledApplicationListItem(appId, "Tenant App", "TENANTAPP", "1.0.0", "desc")]);

		// Act
		ApplicationListResponse result = _tool.ApplicationGetList(new ApplicationGetListArgs(EnvironmentName: null));

		// Assert
		result.Success.Should().BeTrue(
			because: "a header-only passthrough call must route to the header tenant instead of failing on a blank environment name");
		result.Error.Should().BeNull(
			because: "the routed call succeeded, so no error payload may be present — in particular not the legacy requiredness error");
		result.Applications.Should().ContainSingle(application => application.Code == "TENANTAPP",
			because: "the tool must return the applications the settings-based service overload loaded from the header tenant");
		_applicationListService.Received(1).GetApplications(headerTenantSettings, null, null);
		_applicationListService.DidNotReceiveWithAnyArgs().GetApplications(default(string)!, null, null);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-03: under passthrough with BOTH the header and an explicit environment-name, the resolver's existing transport-policy rejection surfaces as a typed error envelope and no Creatio-reaching service call is made.")]
	public void ApplicationGetList_ShouldRejectCall_WhenHeaderAndEnvironmentNameBothPresent() {
		// Arrange
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => options.Environment == "other-registered-env"))
			.Returns(_ => throw new EnvironmentResolutionException(MixedInputRejectionMessage));

		// Act
		ApplicationListResponse result = _tool.ApplicationGetList(
			new ApplicationGetListArgs(EnvironmentName: "other-registered-env"));

		// Assert
		result.Success.Should().BeFalse(
			because: "mixed header + environment-name input must be rejected by the existing transport policy");
		result.Error.Should().Contain("X-Integration-Credentials",
			because: "the rejection must teach the caller the correct credential channel");
		_applicationListService.DidNotReceiveWithAnyArgs().GetApplications(default(EnvironmentSettings)!, null, null);
		_applicationListService.DidNotReceiveWithAnyArgs().GetApplications(default(string)!, null, null);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-04: on stdio / registered-environment transports an explicit environment-name resolves through the unchanged registry branch and the tool executes against those settings exactly as before.")]
	public void ApplicationGetList_ShouldBehaveUnchanged_WhenRegisteredEnvironmentStdio() {
		// Arrange
		EnvironmentSettings registeredSettings = new() { Uri = "https://registered.example.com" };
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"))
			.Returns(registeredSettings);
		Guid appId = Guid.NewGuid();
		_applicationListService.GetApplications(registeredSettings, null, null)
			.Returns([new InstalledApplicationListItem(appId, "Alpha", "ALPHA", "1.0.0", "Alpha description")]);

		// Act
		ApplicationListResponse result = _tool.ApplicationGetList(new ApplicationGetListArgs(EnvironmentName: "sandbox"));

		// Assert
		result.Success.Should().BeTrue(
			because: "registered-environment resolution must keep working exactly as the pre-change baseline");
		result.Applications.Should().ContainSingle(application => application.Code == "ALPHA",
			because: "the tool must return the applications loaded from the registered environment's settings");
		_commandResolver.Received(1).Resolve<EnvironmentSettings>(
			Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"));
	}

	[Test]
	[Category("Unit")]
	[Description("AC-02 (runtime requiredness): outside passthrough, a call with no environment-name surfaces the resolver's EnvironmentResolutionException as a typed error envelope — requiredness is enforced at runtime, not by the MCP schema.")]
	public void ApplicationGetList_ShouldReturnResolverRequirednessError_WhenNoEnvironmentNameAndNoPassthrough() {
		// Arrange
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => string.IsNullOrWhiteSpace(options.Environment)))
			.Returns(_ => throw new EnvironmentResolutionException(ResolverRequirednessMessage));

		// Act
		ApplicationListResponse result = _tool.ApplicationGetList(new ApplicationGetListArgs(EnvironmentName: null));

		// Assert
		result.Success.Should().BeFalse(
			because: "without a passthrough context a blank environment-name has no resolvable target");
		result.Error.Should().Contain("configured environment name",
			because: "the resolver's actionable requiredness message must reach the caller instead of an opaque failure");
		result.Error.Should().NotBe(LegacyRequirednessMessage,
			because: "the opaque ArgumentException from the name-based service path must no longer be the failure surface");
		_applicationListService.DidNotReceiveWithAnyArgs().GetApplications(default(EnvironmentSettings)!, null, null);
	}

	[Test]
	[Category("Unit")]
	[Description("AC-ERR(b): a valid passthrough header whose target operation fails yields the typed error envelope with redacted text — no accessToken/login/password material leaks.")]
	public void ApplicationGetList_ShouldReturnRedactedErrorEnvelope_WhenHeaderTenantOperationFails() {
		// Arrange
		EnvironmentSettings headerTenantSettings = new() { Uri = "https://tenant.example.com" };
		_commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>())
			.Returns(headerTenantSettings);
		_applicationListService.GetApplications(headerTenantSettings, null, null)
			.Returns(_ => throw new InvalidOperationException(
				"Installed application query failed. token=super-secret-token-value"));

		// Act
		ApplicationListResponse result = _tool.ApplicationGetList(new ApplicationGetListArgs(EnvironmentName: null));

		// Assert
		result.Success.Should().BeFalse(
			because: "an operation failure against a valid header tenant must surface as a typed error envelope, not a protocol error");
		result.Error.Should().Contain("Installed application query failed",
			because: "the caller-actionable failure description must be preserved");
		result.Error.Should().NotContain("super-secret-token-value",
			because: "SensitiveErrorTextRedactor must scrub credential material before it crosses the MCP boundary");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-02 (schema relax, FR-05a): environment-name on ApplicationGetListArgs carries no [Required] attribute, so a header-only call reaches the tool method instead of being rejected at MCP binding.")]
	public void ApplicationGetListArgs_ShouldBeSchemaOptional_WhenEnvironmentNamePropertyIsInspected() {
		// Arrange
		PropertyInfo environmentNameProperty = typeof(ApplicationGetListArgs)
			.GetProperty(nameof(ApplicationGetListArgs.EnvironmentName))!;

		// Act
		bool hasRequiredAttribute = environmentNameProperty
			.GetCustomAttributes(typeof(RequiredAttribute), inherit: false)
			.Any();

		// Assert
		hasRequiredAttribute.Should().BeFalse(
			because: "FR-05a makes environment-name schema-optional on list-apps so a header-only passthrough call is not rejected before the tool runs");
	}

	[Test]
	[Category("Unit")]
	[Description("FR-05: the tool executes its body through the options-aware tenant-lock path, deriving the lock key from THIS call's target via IToolCommandResolver.GetTenantKey rather than the shared fallback key.")]
	public void ApplicationGetList_ShouldDeriveTenantLockKeyFromCallOptions_WhenExecuted() {
		// Arrange
		EnvironmentSettings registeredSettings = new() { Uri = "https://registered.example.com" };
		_commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>())
			.Returns(registeredSettings);
		_commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("tenant-key");
		_applicationListService.GetApplications(registeredSettings, null, null).Returns([]);

		// Act
		_tool.ApplicationGetList(new ApplicationGetListArgs(EnvironmentName: "sandbox"));

		// Assert
		_commandResolver.Received(1).GetTenantKey(
			Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"));
	}
}

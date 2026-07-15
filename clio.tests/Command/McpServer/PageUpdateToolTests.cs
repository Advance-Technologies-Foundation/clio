using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NSubstitute.Core;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 11 (ENG-93347) header-aware platform-version probe for the <c>update-page</c> MCP tool.
/// <see cref="PageUpdateTool"/>'s write path (<c>UpdatePage</c> body, already resolving
/// <see cref="PageUpdateCommand"/> through <see cref="IToolCommandResolver"/>) and its
/// <c>ResolvePlatformVersionAsync</c> probe must now route through the SAME resolver, so a header-only
/// passthrough call scopes chart-widget validation to the header tenant's platform version instead of
/// silently falling back to the active/registered environment (ADR "Matrix tools", <c>update-page</c> row).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageUpdateToolTests {

	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
	private const string SchemaUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string SchemaName = "Test_FormPage";

	private const string MixedInputRejectionMessage =
		"Explicit credential or environment arguments (uri/login/password/client-id/client-secret/environment) "
		+ "are not accepted when credential passthrough is enabled over HTTP. Supply the target environment "
		+ "and credentials via the X-Integration-Credentials header, not tool arguments.";

	private const string ValidBody =
		"define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private IComponentInfoCatalog _webComponentCatalog;
	private IToolCommandResolver _commandResolver;
	private IPlatformVersionResolverFactory _resolverFactory;
	private PageUpdateTool _tool;

	[SetUp]
	public void SetUp() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema").Returns(SaveSchemaUrl);
		applicationClient.ExecutePostRequest(
				SelectQueryUrl,
				Arg.Is<string>(body => !body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}"}]}""");
		applicationClient.ExecutePostRequest(
				GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "schema": {"body": "old body", "name": "{{SchemaName}}" } }""");
		applicationClient.ExecutePostRequest(
				SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true}""");
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns("test-pkg-uid");
		hierarchyClient.GetParentSchemas(SchemaUId, "test-pkg-uid").Returns([
			new PageDesignerHierarchySchema { UId = SchemaUId, Name = SchemaName, PackageUId = "test-pkg-uid" }
		]);
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>(), hierarchyClient);
		_commandResolver = Substitute.For<IToolCommandResolver>();
		_commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		_webComponentCatalog = Substitute.For<IComponentInfoCatalog>();
		// Retained only as ResolvePlatformVersionAsync's dependency-presence gate (Story 11, ENG-93347) —
		// the actual settings lookup now goes exclusively through IToolCommandResolver.
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		_resolverFactory = Substitute.For<IPlatformVersionResolverFactory>();
		_tool = new PageUpdateTool(
			command, logger, _commandResolver,
			Substitute.For<IMobileComponentInfoCatalog>(),
			_webComponentCatalog,
			Substitute.For<IPageBodySamplingService>(),
			Substitute.For<IPageBaselineGuard>(),
			_resolverFactory, settingsRepository);
	}

	private static PageUpdateArgs CreateArgs(string environmentName) =>
		new(SchemaName, ValidBody, null, null, environmentName, null, null, null, SkipSampling: true);

	private string ReceivedChartValidationVersion() =>
		(string)_webComponentCatalog.ReceivedCalls()
			.Single(c => c.GetMethodInfo().Name == nameof(IComponentInfoCatalog.LoadAsync))
			.GetArguments()[0];

	[Test]
	[Description("AC-01/AC-02: a header-only passthrough call (no environment-name) reaches commandResolver.Resolve<EnvironmentSettings> inside ResolvePlatformVersionAsync and scopes chart-widget validation to the header tenant's resolved platform version, instead of silently falling back to an active/registered environment.")]
	public async Task UpdatePage_ShouldResolveVersionAgainstHeaderTenant_WhenHeaderOnly() {
		// Arrange
		EnvironmentSettings headerTenantSettings = new() { Uri = "https://tenant.example.com" };
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => string.IsNullOrWhiteSpace(options.Environment)))
			.Returns(headerTenantSettings);
		IPlatformVersionResolver resolver = Substitute.For<IPlatformVersionResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(new PlatformVersionResolution("9.9.9", VersionResolutionSource.Environment));
		_resolverFactory.Create(headerTenantSettings).Returns(resolver);
		PageUpdateArgs args = CreateArgs(environmentName: null);

		// Act
		await _tool.UpdatePage(args, null);

		// Assert
		_commandResolver.Received(1).Resolve<EnvironmentSettings>(
			Arg.Is<EnvironmentOptions>(options => string.IsNullOrWhiteSpace(options.Environment)));
		ReceivedChartValidationVersion().Should().Be("9.9.9",
			because: "the version probe must resolve against the header tenant's settings, not a silent active-environment fallback");
	}

	[Test]
	[Description("AC-03: mixed header + explicit environment-name input is rejected by the resolver's HasExplicitCredentialArgs guard before any named-tenant lookup — the version probe never calls the resolver factory with a named registered environment's stored credentials, and fails soft to 'latest' instead of blocking the write.")]
	public async Task UpdatePage_ShouldRejectProbeBeforeNamedTenantLookup_WhenMixedHeaderAndEnvironmentName() {
		// Arrange
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => options.Environment == "other-registered-env"))
			.Returns(_ => throw new EnvironmentResolutionException(MixedInputRejectionMessage));
		PageUpdateArgs args = CreateArgs(environmentName: "other-registered-env");

		// Act
		PageUpdateResponse response = await _tool.UpdatePage(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "the version probe is fail-soft and must never block the write when the resolver rejects mixed input");
		_resolverFactory.DidNotReceiveWithAnyArgs().Create(default);
		ReceivedChartValidationVersion().Should().Be(ComponentRegistryClient.LatestVersion,
			because: "a rejected probe must degrade to the 'latest' superset instead of ever reaching a named-tenant lookup with stored credentials");
	}

	[Test]
	[Description("AC-04: on stdio/registered-environment input, the version probe resolves through commandResolver.Resolve<EnvironmentSettings> against the SAME registered environment as the already-compliant write path (:64), matching the pre-change baseline exactly.")]
	public async Task UpdatePage_ShouldResolveVersionAgainstRegisteredEnvironment_WhenEnvironmentNameSupplied() {
		// Arrange
		EnvironmentSettings registeredSettings = new() { Uri = "https://registered.example.com" };
		_commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"))
			.Returns(registeredSettings);
		IPlatformVersionResolver resolver = Substitute.For<IPlatformVersionResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(new PlatformVersionResolution("8.3.4", VersionResolutionSource.Environment));
		_resolverFactory.Create(registeredSettings).Returns(resolver);
		PageUpdateArgs args = CreateArgs(environmentName: "sandbox");

		// Act
		PageUpdateResponse response = await _tool.UpdatePage(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "a registered-environment call must save successfully exactly as the pre-change baseline");
		_commandResolver.Received(1).Resolve<PageUpdateCommand>(
			Arg.Is<PageUpdateOptions>(options => options.Environment == "sandbox"));
		_commandResolver.Received(1).Resolve<EnvironmentSettings>(
			Arg.Is<EnvironmentOptions>(options => options.Environment == "sandbox"));
		ReceivedChartValidationVersion().Should().Be("8.3.4",
			because: "the write path and the version probe must resolve the SAME registered environment identically to the pre-change baseline");
	}
}

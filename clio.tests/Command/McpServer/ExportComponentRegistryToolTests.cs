using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ExportComponentRegistryToolTests {

	private const string SampleRegistry = """
	{ "components": [ {"componentType":"crt.Button","description":"Button.","inputs":{"caption":{"type":"string"}}} ] }
	""";

	[Test]
	[Description("args map correctly (version/schema-type/output-file/connection fallback) onto the options passed to the shared export pipeline; an explicit version never touches IToolCommandResolver.")]
	public async Task ExportComponentRegistry_ShouldMapArgs_AndAvoidEnvironmentProbe_ForExplicitVersion() {
		// Arrange
		ExportComponentRegistryCommand command = CreateCommand(out IComponentRegistryClient webClient, out _);
		IPlatformVersionResolverFactory resolverFactory = Substitute.For<IPlatformVersionResolverFactory>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ExportComponentRegistryTool tool = new(command, resolverFactory, commandResolver);
		ExportComponentRegistryArgs args = new(Version: "8.3.4") { OutputFile = null };

		// Act
		ExportComponentRegistryResponse response = await tool.ExportComponentRegistry(args, CancellationToken.None);

		// Assert
		response.Success.Should().BeTrue(because: "an explicit, well-formed version must export without an environment");
		response.ResolvedTargetVersion.Should().Be("8.3.4",
			because: "the explicit version must be echoed back verbatim");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>());
		resolverFactory.DidNotReceiveWithAnyArgs().Create(Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("environment-name routes version resolution through IToolCommandResolver.Resolve<EnvironmentSettings> — the credential-passthrough-aware seam — not ISettingsRepository directly.")]
	public async Task ExportComponentRegistry_ShouldResolveVersion_ViaToolCommandResolver_ForEnvironmentName() {
		// Arrange
		ExportComponentRegistryCommand command = CreateCommand(out _, out _);
		IPlatformVersionResolverFactory resolverFactory = Substitute.For<IPlatformVersionResolverFactory>();
		IOwnedPlatformVersionResolver resolver = Substitute.For<IOwnedPlatformVersionResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new PlatformVersionResolution("8.3.4", VersionResolutionSource.Environment)));
		resolverFactory.Create(Arg.Any<EnvironmentSettings>()).Returns(resolver);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>())
			.Returns(new EnvironmentSettings { Uri = "https://prod-stand.example.com" });
		ExportComponentRegistryTool tool = new(command, resolverFactory, commandResolver);
		ExportComponentRegistryArgs args = new() { EnvironmentName = "prod-stand" };

		// Act
		ExportComponentRegistryResponse response = await tool.ExportComponentRegistry(args, CancellationToken.None);

		// Assert
		response.Success.Should().BeTrue(because: "the environment-backed probe and the fetch must both succeed");
		commandResolver.Received(1).Resolve<EnvironmentSettings>(Arg.Is<EnvironmentOptions>(o => o.Environment == "prod-stand"));
	}

	[Test]
	[Description("ENG-93347 AC-01 regression guard: a header-only call (neither environment-name nor uri) never calls IToolCommandResolver — it stays on the CreateNoActiveEnvironmentFallback path.")]
	public async Task ExportComponentRegistry_ShouldNeverCallCommandResolver_WhenHeaderOnly() {
		// Arrange
		ExportComponentRegistryCommand command = CreateCommand(out _, out _);
		IPlatformVersionResolverFactory resolverFactory = Substitute.For<IPlatformVersionResolverFactory>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ExportComponentRegistryTool tool = new(command, resolverFactory, commandResolver);
		ExportComponentRegistryArgs args = new();

		// Act
		ExportComponentRegistryResponse response = await tool.ExportComponentRegistry(args, CancellationToken.None);

		// Assert
		response.ResolvedFrom.Should().Be(ComponentInfoResolution.ResolvedFromLatestFallback,
			because: "the header-only, no-environment branch must keep degrading to the loud latest-fallback marker");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("ENG-93347: mixed header + environment-name input is rejected by IToolCommandResolver's existing transport policy before any named-tenant probe — the platform-version resolver factory is never invoked.")]
	public async Task ExportComponentRegistry_ShouldRejectMixedInput_BeforeNamedTenantProbe() {
		// Arrange
		const string rejectionMessage =
			"Explicit credential or environment arguments (uri/login/password/client-id/client-secret/environment) "
			+ "are not accepted when credential passthrough is enabled over HTTP. Supply the target environment "
			+ "and credentials via the X-Integration-Credentials header, not tool arguments.";
		ExportComponentRegistryCommand command = CreateCommand(out _, out _);
		IPlatformVersionResolverFactory resolverFactory = Substitute.For<IPlatformVersionResolverFactory>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(o => o.Environment == "other-registered-env"))
			.Returns(_ => throw new EnvironmentResolutionException(rejectionMessage));
		ExportComponentRegistryTool tool = new(command, resolverFactory, commandResolver);
		ExportComponentRegistryArgs args = new() { EnvironmentName = "other-registered-env" };

		// Act
		ExportComponentRegistryResponse response = await tool.ExportComponentRegistry(args, CancellationToken.None);

		// Assert
		response.Success.Should().BeFalse(
			because: "mixed header + environment-name input must be rejected instead of silently succeeding against the named tenant");
		response.Error.Should().Contain("X-Integration-Credentials",
			because: "the rejection must teach the caller the correct credential channel, matching get-component-info's fail-soft error shape");
		resolverFactory.DidNotReceiveWithAnyArgs().Create(Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("Combining version and environment-name is rejected before any registry fetch, mirroring the CLI verb.")]
	public async Task ExportComponentRegistry_ShouldFail_WhenVersionAndEnvironmentBothProvided() {
		// Arrange
		ExportComponentRegistryCommand command = CreateCommand(out IComponentRegistryClient webClient, out _);
		IPlatformVersionResolverFactory resolverFactory = Substitute.For<IPlatformVersionResolverFactory>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ExportComponentRegistryTool tool = new(command, resolverFactory, commandResolver);
		ExportComponentRegistryArgs args = new(Version: "8.3.4") { EnvironmentName = "dev" };

		// Act
		ExportComponentRegistryResponse response = await tool.ExportComponentRegistry(args, CancellationToken.None);

		// Assert
		response.Success.Should().BeFalse(because: "version and environment-name are mutually exclusive");
		response.Error.Should().Contain("mutually exclusive",
			because: "the caller must be told which two arguments conflicted");
		await webClient.DidNotReceiveWithAnyArgs().GetAsync(default, default);
	}

	[Test]
	[Description("A null args value is rejected with a typed error rather than throwing.")]
	public async Task ExportComponentRegistry_ShouldFail_WhenArgsIsNull() {
		// Arrange
		ExportComponentRegistryCommand command = CreateCommand(out _, out _);
		ExportComponentRegistryTool tool = new(
			command, Substitute.For<IPlatformVersionResolverFactory>(), Substitute.For<IToolCommandResolver>());

		// Act
		ExportComponentRegistryResponse response = await tool.ExportComponentRegistry(null, CancellationToken.None);

		// Assert
		response.Success.Should().BeFalse(because: "null args must never reach the export pipeline");
		response.Error.Should().Contain("args is required",
			because: "the caller must be told which argument was missing");
	}

	[Test]
	[Description("An error message is redacted at the MCP boundary before it reaches the caller.")]
	public async Task ExportComponentRegistry_ShouldRedact_ErrorMessages() {
		// Arrange
		IComponentRegistryClient webClient = Substitute.For<IComponentRegistryClient>();
		webClient.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns<Task<ComponentRegistryFetchResult>>(_ => throw new InvalidOperationException(
				"failed calling https://secret-tenant.example.com/ServiceModel/EntityDataService.svc"));
		ExportComponentRegistryCommand command = new(
			webClient,
			Substitute.For<IMobileComponentRegistryClient>(),
			Substitute.For<IPlatformVersionResolverFactory>(),
			Substitute.For<ISettingsRepository>(),
			new System.IO.Abstractions.TestingHelpers.MockFileSystem(),
			Substitute.For<ILogger>());
		ExportComponentRegistryTool tool = new(
			command, Substitute.For<IPlatformVersionResolverFactory>(), Substitute.For<IToolCommandResolver>());
		ExportComponentRegistryArgs args = new(Version: "8.3.4");

		// Act
		ExportComponentRegistryResponse response = await tool.ExportComponentRegistry(args, CancellationToken.None);

		// Assert
		response.Success.Should().BeFalse(because: "the underlying transport failure must surface as a failed export");
		response.Error.Should().NotContain("secret-tenant.example.com",
			because: "an underlying transport error must be redacted before it reaches the MCP transcript");
	}

	// The VersionResolutionBudget is a 30s wall clock, so these two tests drive the FALLBACK ARMS directly
	// (a resolver that throws what an elapsed budget throws) instead of actually elapsing it — the assertion
	// is that the tool degrades to the loud latest-fallback instead of hanging or failing the whole export.
	[Test]
	[Description("A version probe that abandons the wait with a TimeoutException degrades to the latest-fallback tier and still exports, rather than failing or hanging.")]
	public async Task ExportComponentRegistry_ShouldDegradeToLatestFallback_WhenProbeTimesOut() {
		// Arrange
		ExportComponentRegistryTool tool = CreateToolWithFailingProbe(new TimeoutException("The operation has timed out."));
		ExportComponentRegistryArgs args = new() { EnvironmentName = "unreachable-stand" };

		// Act
		ExportComponentRegistryResponse response = await tool.ExportComponentRegistry(args, CancellationToken.None);

		// Assert
		response.Success.Should().BeTrue(
			because: "an elapsed version budget is a soft degrade, not an export failure — the registry itself is still fetchable");
		response.ResolvedFrom.Should().Be(ComponentInfoResolution.ResolvedFromLatestFallback,
			because: "the probe never produced a version, so the export must advertise the fallback tier");
		response.RequiresVersionConfirmation.Should().BeTrue(
			because: "the caller must not silently assume the exported set matches the unreachable environment");
	}

	[Test]
	[Description("A version probe cancelled by the internal budget token (not by the caller) degrades to the latest-fallback tier instead of surfacing the cancellation as an error.")]
	public async Task ExportComponentRegistry_ShouldDegradeToLatestFallback_WhenBudgetTokenCancelsTheProbe() {
		// Arrange
		ExportComponentRegistryTool tool = CreateToolWithFailingProbe(new OperationCanceledException());
		ExportComponentRegistryArgs args = new() { EnvironmentName = "unreachable-stand" };

		// Act
		ExportComponentRegistryResponse response = await tool.ExportComponentRegistry(args, CancellationToken.None);

		// Assert
		response.Success.Should().BeTrue(
			because: "a budget-driven cancellation is the tool's own ceiling, not a caller cancellation, so the export continues");
		response.ResolvedFrom.Should().Be(ComponentInfoResolution.ResolvedFromLatestFallback,
			because: "the cooperative-exit arm must land on the same fallback as the abandoned-wait arm");
	}

	private static ExportComponentRegistryTool CreateToolWithFailingProbe(Exception failure) {
		ExportComponentRegistryCommand command = CreateCommand(out _, out _);
		IOwnedPlatformVersionResolver resolver = Substitute.For<IOwnedPlatformVersionResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromException<PlatformVersionResolution>(failure));
		IPlatformVersionResolverFactory resolverFactory = Substitute.For<IPlatformVersionResolverFactory>();
		resolverFactory.Create(Arg.Any<EnvironmentSettings>()).Returns(resolver);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>())
			.Returns(new EnvironmentSettings { Uri = "https://unreachable-stand.example.com" });
		return new ExportComponentRegistryTool(command, resolverFactory, commandResolver);
	}

	[Test]
	[Description("A camelCase 'schemaType' argument is REJECTED with a rename hint instead of being dropped — an unbound schema-type leaves the field null, which is the legitimate 'use web' value, so a mis-cased mobile request would otherwise return a successful web export with no warning.")]
	public async Task ExportComponentRegistry_ShouldRejectLegacyAlias_WhenCamelCaseSchemaTypeIsUsed() {
		// Arrange
		ExportComponentRegistryCommand command = CreateCommand(out IComponentRegistryClient webClient, out _);
		ExportComponentRegistryTool tool = new(
			command, Substitute.For<IPlatformVersionResolverFactory>(), Substitute.For<IToolCommandResolver>());
		ExportComponentRegistryArgs args = new(Version: "8.3.4") {
			ExtensionData = new Dictionary<string, JsonElement> {
				["schemaType"] = JsonDocument.Parse("\"mobile\"").RootElement.Clone()
			}
		};

		// Act
		ExportComponentRegistryResponse response = await tool.ExportComponentRegistry(args, CancellationToken.None);

		// Assert
		response.Success.Should().BeFalse(because: "an unbound camelCase argument must be rejected, not ignored");
		response.Error.Should().Contain("schema-type",
			because: "the rename hint must point at the canonical kebab-case parameter");
		await webClient.DidNotReceiveWithAnyArgs().GetAsync(default, default);
	}

	[Test]
	[Description("The alias check runs before any work: a snake_case 'output_file' is refused with a rename hint and nothing is fetched or written.")]
	public async Task ExportComponentRegistry_ShouldRejectLegacyAlias_WhenSnakeCaseOutputFileIsUsed() {
		// Arrange
		ExportComponentRegistryCommand command = CreateCommand(out IComponentRegistryClient webClient, out _);
		ExportComponentRegistryTool tool = new(
			command, Substitute.For<IPlatformVersionResolverFactory>(), Substitute.For<IToolCommandResolver>());
		ExportComponentRegistryArgs args = new(Version: "8.3.4") {
			ExtensionData = new Dictionary<string, JsonElement> {
				["output_file"] = JsonDocument.Parse("\"/tmp/registry.json\"").RootElement.Clone()
			}
		};

		// Act
		ExportComponentRegistryResponse response = await tool.ExportComponentRegistry(args, CancellationToken.None);

		// Assert
		response.Success.Should().BeFalse(
			because: "a dropped output-file would silently write to the default path instead of where the caller asked");
		response.Error.Should().Contain("output-file", because: "the rename hint must name the canonical parameter");
		await webClient.DidNotReceiveWithAnyArgs().GetAsync(default, default);
	}

	[Test]
	[Description("An argument that is not a known alias either is refused, naming the valid parameter set — a typo must not degrade into a default-shaped export.")]
	public async Task ExportComponentRegistry_ShouldRejectUnknownArgument_NamingTheValidSet() {
		// Arrange
		ExportComponentRegistryCommand command = CreateCommand(out _, out _);
		ExportComponentRegistryTool tool = new(
			command, Substitute.For<IPlatformVersionResolverFactory>(), Substitute.For<IToolCommandResolver>());
		ExportComponentRegistryArgs args = new(Version: "8.3.4") {
			ExtensionData = new Dictionary<string, JsonElement> {
				["schemaTyp"] = JsonDocument.Parse("\"mobile\"").RootElement.Clone()
			}
		};

		// Act
		ExportComponentRegistryResponse response = await tool.ExportComponentRegistry(args, CancellationToken.None);

		// Assert
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("schema-type",
			because: "the error lists the valid parameter set so the caller can correct the spelling");
	}

	private static ExportComponentRegistryCommand CreateCommand(
		out IComponentRegistryClient webClient, out IMobileComponentRegistryClient mobileClient) {
		webClient = Substitute.For<IComponentRegistryClient>();
		mobileClient = Substitute.For<IMobileComponentRegistryClient>();
		byte[] bytes = Encoding.UTF8.GetBytes(SampleRegistry);
		webClient.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(callInfo => Task.FromResult(new ComponentRegistryFetchResult(
				new MemoryStream(bytes), callInfo.ArgAt<string>(0), ComponentRegistrySource.Cdn)));
		return new ExportComponentRegistryCommand(
			webClient,
			mobileClient,
			Substitute.For<IPlatformVersionResolverFactory>(),
			Substitute.For<ISettingsRepository>(),
			new System.IO.Abstractions.TestingHelpers.MockFileSystem(),
			Substitute.For<ILogger>());
	}
}

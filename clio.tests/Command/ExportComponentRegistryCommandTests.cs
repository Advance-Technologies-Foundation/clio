using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
internal class ExportComponentRegistryCommandTests : BaseCommandTests<ExportComponentRegistryOptions> {

	private const string SampleRegistryWithDeprecation = """
	{
	  "components": [
	    {"componentType":"crt.ApprovalList","description":"Approval list.","inputs":{
	      "selectedRows":{"type":"array","deprecated":true,"deprecationReason":"Use `selectionState` input instead.","items":{"type":"unknown"}},
	      "selectionState":{"type":"string"}
	    },"outputs":{}},
	    {"componentType":"crt.Button","description":"Button.","inputs":{"caption":{"type":"string"}},"outputs":{}}
	  ],
	  "composites": [
	    {"caption":"Next steps","description":"Composite.","docs":["docs/next-steps.component.md"]}
	  ],
	  "references": { "baseInputs": {}, "typeDefinitions": {} }
	}
	""";

	private const string SecondRunRegistry = """
	{ "components": [ {"componentType":"crt.Label","description":"Label.","inputs":{"caption":{"type":"string"}}} ] }
	""";

	private const string MobileRegistry = """
	{ "components": [ {"componentType":"crt.Toggle","description":"Mobile toggle.","inputs":{"value":{"type":"boolean"}}} ] }
	""";

	private ExportComponentRegistryCommand _command;
	private IComponentRegistryClient _webRegistryClient;
	private IMobileComponentRegistryClient _mobileRegistryClient;
	private StubResolverFactory _resolverFactory;
	private ISettingsRepository _settingsRepository;
	private System.IO.Abstractions.TestingHelpers.MockFileSystem _ioFileSystem;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<ExportComponentRegistryCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_webRegistryClient = Substitute.For<IComponentRegistryClient>();
		_mobileRegistryClient = Substitute.For<IMobileComponentRegistryClient>();
		StubFetch(_webRegistryClient, SampleRegistryWithDeprecation);
		StubFetch(_mobileRegistryClient, MobileRegistry);
		_resolverFactory = new StubResolverFactory(new PlatformVersionResolution("8.3.4", VersionResolutionSource.Environment));
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_settingsRepository.GetEnvironment(Arg.Any<EnvironmentOptions>())
			.Returns(new EnvironmentSettings { Uri = "https://dev.example.com" });
		_ioFileSystem = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_webRegistryClient);
		containerBuilder.AddSingleton(_mobileRegistryClient);
		containerBuilder.AddSingleton<IPlatformVersionResolverFactory>(_resolverFactory);
		containerBuilder.AddSingleton(_settingsRepository);
		containerBuilder.AddSingleton<System.IO.Abstractions.IFileSystem>(_ioFileSystem);
		containerBuilder.AddSingleton(_logger);
	}

	private static void StubFetch(IComponentRegistryClient client, string content, string resolvedVersion = null) {
		byte[] bytes = Encoding.UTF8.GetBytes(content);
		client.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(callInfo => Task.FromResult(new ComponentRegistryFetchResult(
				new MemoryStream(bytes),
				resolvedVersion ?? callInfo.ArgAt<string>(0),
				ComponentRegistrySource.Cdn)));
	}

	[Test]
	[Description("An environment-name resolves the version through the resolver factory and reports resolvedFrom=environment when the fetched version matches the probed one.")]
	public async Task TryExportAsync_ShouldResolveVersion_ViaEnvironmentName() {
		// Arrange
		ExportComponentRegistryOptions options = new() { Environment = "dev" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeTrue(because: "the environment-backed probe and the fetch must both succeed");
		response.ResolvedTargetVersion.Should().Be("8.3.4",
			because: "the resolved version must be the one the cliogate probe reported");
		response.ResolvedFrom.Should().Be(ComponentInfoResolution.ResolvedFromEnvironment,
			because: "the probed version matches the version the registry actually served");
		response.RequiresVersionConfirmation.Should().BeFalse(
			because: "a known, probed version never requires the caller to confirm it");
		_resolverFactory.CreateCallCount.Should().Be(1, because: "environment-name must trigger exactly one cliogate probe");
	}

	[Test]
	[Description("An explicit --version is treated as authoritative and never probes an environment.")]
	public async Task TryExportAsync_ShouldResolveVersion_ViaExplicitVersion() {
		// Arrange
		ExportComponentRegistryOptions options = new() { Version = "8.2.1" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeTrue(because: "an explicit, well-formed version must resolve without an environment");
		response.ResolvedTargetVersion.Should().Be("8.2.1",
			because: "the explicit version is authoritative and must be echoed back verbatim");
		response.ResolvedFrom.Should().Be(ComponentInfoResolution.ResolvedFromEnvironment,
			because: "an explicit version that the registry actually served maps to the 'environment' tier");
		_resolverFactory.CreateCallCount.Should().Be(0, because: "an explicit version must never trigger an environment probe");
	}

	[Test]
	[Description("A known version whose per-version catalog is not published maps to resolvedFrom=environment-superset, needs no confirmation, and keys the default filename by the version the CDN actually served.")]
	public async Task TryExportAsync_ShouldReportEnvironmentSuperset_WhenServedVersionDiffersFromRequested() {
		// Arrange
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string workspace = _ioFileSystem.Path.Combine(tempRoot, "ecr-superset-ws");
		_ioFileSystem.Directory.CreateDirectory(workspace);
		_ioFileSystem.Directory.SetCurrentDirectory(workspace);
		StubFetch(_webRegistryClient, SampleRegistryWithDeprecation, resolvedVersion: "latest");
		ExportComponentRegistryOptions options = new() { Version = "8.2.1" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeTrue(because: "a served superset catalog is still a successful export");
		response.ResolvedFrom.Should().Be(ComponentInfoResolution.ResolvedFromEnvironmentSuperset,
			because: "the version was known but the CDN served 'latest' instead of the requested per-version catalog");
		response.ResolvedTargetVersion.Should().Be("latest",
			because: "the response must report the version actually served, not the one requested");
		response.RequiresVersionConfirmation.Should().BeFalse(
			because: "the target version is known on the superset tier, so no confirmation gate applies");
		response.OutputFile.Should().EndWith("latest.json",
			because: "the default filename is keyed by the served version — requesting 8.2.1 while 'latest' is served writes latest.json, not 8.2.1.json");
	}

	[Test]
	[Description("With neither --version nor --environment/--uri, the export falls back to 'latest' and requiresVersionConfirmation is true.")]
	public async Task TryExportAsync_ShouldFallBackToLatest_RequiringVersionConfirmation() {
		// Arrange
		ExportComponentRegistryOptions options = new();

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeTrue(because: "the latest-fallback tier still produces a written file");
		response.ResolvedFrom.Should().Be(ComponentInfoResolution.ResolvedFromLatestFallback,
			because: "with no version and no environment there is nothing to probe");
		response.RequiresVersionConfirmation.Should().BeTrue(
			because: "an unknown target version must not be silently assumed by the caller");
		response.ResolvedFromReason.Should().Be("no-active-environment",
			because: "the gap is a clear input omission, not a probe failure");
	}

	[Test]
	[Description("Combining --version and --environment is a hard error before any registry fetch is attempted.")]
	public async Task TryExportAsync_ShouldFail_WhenVersionAndEnvironmentBothProvided() {
		// Arrange
		ExportComponentRegistryOptions options = new() { Version = "8.3.4", Environment = "dev" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeFalse(because: "version and environment-name/uri are mutually exclusive");
		response.Error.Should().Contain("mutually exclusive",
			because: "the caller must be told which two arguments conflicted");
		await _webRegistryClient.DidNotReceiveWithAnyArgs().GetAsync(default, default);
	}

	[Test]
	[Description("An explicit output-file that escapes both the workspace and the OS temp dir is rejected before any write.")]
	public async Task TryExportAsync_ShouldReject_ExplicitOutputFile_OutsideAllowedZones() {
		// Arrange
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string workspace = _ioFileSystem.Path.Combine(tempRoot, "ecr-ws");
		_ioFileSystem.Directory.CreateDirectory(workspace);
		_ioFileSystem.Directory.SetCurrentDirectory(workspace);
		string escape = _ioFileSystem.Path.Combine(tempRoot, "..", "ecr-escape", "registry.json");
		ExportComponentRegistryOptions options = new() { Version = "8.3.4", OutputFile = escape };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeFalse(because: "an output-file escaping both allowed zones must not be written");
		response.Error.Should().Contain("output-file",
			because: "the failure must name the offending option so the caller can correct it");
		_ioFileSystem.File.Exists(_ioFileSystem.Path.GetFullPath(escape)).Should().BeFalse(
			because: "no file may be written when the path is rejected");
	}

	[Test]
	[Description("An explicit output-file that already exists is refused rather than overwritten (Destructive=false).")]
	public async Task TryExportAsync_ShouldRefuse_ExistingExplicitOutputFile() {
		// Arrange
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string workspace = _ioFileSystem.Path.Combine(tempRoot, "ecr-ws-exist");
		_ioFileSystem.Directory.CreateDirectory(workspace);
		_ioFileSystem.Directory.SetCurrentDirectory(workspace);
		string scratch = _ioFileSystem.Path.Combine(tempRoot, "ecr-exist", "registry.json");
		_ioFileSystem.Directory.CreateDirectory(_ioFileSystem.Path.GetDirectoryName(scratch));
		_ioFileSystem.File.WriteAllText(scratch, "pre-existing");
		ExportComponentRegistryOptions options = new() { Version = "8.3.4", OutputFile = scratch };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeFalse(because: "an existing explicit output-file must not be silently overwritten");
		response.Error.Should().Contain("already exists",
			because: "the caller is told why the write was refused");
		_ioFileSystem.File.ReadAllText(_ioFileSystem.Path.GetFullPath(scratch)).Should().Be("pre-existing",
			because: "the pre-existing file is left untouched when the atomic write is refused");
	}

	[Test]
	[Description("An explicit output-file inside the OS temp scratch dir is written verbatim (byte-faithful) to the confined path.")]
	public async Task TryExportAsync_ShouldWrite_ExplicitOutputFile_InsideTempRoot() {
		// Arrange
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string workspace = _ioFileSystem.Path.Combine(tempRoot, "ecr-ws-ok");
		_ioFileSystem.Directory.CreateDirectory(workspace);
		_ioFileSystem.Directory.SetCurrentDirectory(workspace);
		string scratch = _ioFileSystem.Path.Combine(tempRoot, "ecr-ok", "registry.json");
		ExportComponentRegistryOptions options = new() { Version = "8.3.4", OutputFile = scratch };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeTrue(because: "an output-file inside the OS temp scratch dir is an allowed destination");
		response.OutputFile.Should().Be(_ioFileSystem.Path.GetFullPath(scratch),
			because: "the confined explicit path is honored as the write location");
		string written = _ioFileSystem.File.ReadAllText(response.OutputFile);
		written.Should().Be(SampleRegistryWithDeprecation,
			because: "the file must be byte-faithful to the source registry — no re-serialization through a typed model");
		written.Should().Contain("deprecationReason",
			because: "deprecated/deprecationReason exist only as raw JSON on an inputs entry and must survive verbatim");
	}

	[Test]
	[Description("With no output-file, a second run at the default path overwrites the first (a different contract from an explicit output-file, which refuses an existing target).")]
	public async Task TryExportAsync_ShouldOverwrite_DefaultPath_OnRepeatRun() {
		// Arrange
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string workspace = _ioFileSystem.Path.Combine(tempRoot, "ecr-default-ws");
		_ioFileSystem.Directory.CreateDirectory(workspace);
		_ioFileSystem.Directory.SetCurrentDirectory(workspace);
		ExportComponentRegistryOptions options = new() { Version = "8.3.4" };

		// Act
		ExportComponentRegistryResponse first = await _command.TryExportAsync(options, CancellationToken.None);
		string afterFirst = _ioFileSystem.File.ReadAllText(first.OutputFile);
		StubFetch(_webRegistryClient, SecondRunRegistry);
		ExportComponentRegistryResponse second = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		first.Success.Should().BeTrue(because: "the first run at the default path must succeed");
		second.Success.Should().BeTrue(because: "the tool-owned default path must be re-runnable, unlike an explicit output-file");
		second.OutputFile.Should().Be(first.OutputFile,
			because: "both runs must resolve to the same tool-owned default path");
		second.OutputFile.Should().Contain("component-registry",
			because: "the default path is anchored under .clio-migration/component-registry");
		second.OutputFile.Should().Contain("8.3.4.json",
			because: "the default filename is keyed by the resolved version");
		afterFirst.Should().Be(SampleRegistryWithDeprecation,
			because: "the first run must have written its own payload before the second run replaced it");
		_ioFileSystem.File.ReadAllText(second.OutputFile).Should().Be(SecondRunRegistry,
			because: "the second run must actually rewrite the tool-owned default path, not skip or refuse the write");
	}

	[Test]
	[Description("The response never carries registry content — no componentType occurrence anywhere in the serialized envelope.")]
	public async Task TryExportAsync_ShouldNeverReturn_RegistryContent() {
		// Arrange
		ExportComponentRegistryOptions options = new() { Version = "8.3.4" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		string serialized = JsonSerializer.Serialize(response);
		serialized.Should().NotContain("componentType",
			because: "the registry content lives only in the written file, never in the response");
		serialized.Should().NotContain("crt.Button",
			because: "no concrete component identifier from the registry may leak into the response");
	}

	[Test]
	[Description("Counters (componentCount/inputCount) are computed from the same bytes written to disk, covering both the top-level 'inputs' shape used here.")]
	public async Task TryExportAsync_ShouldReport_AccurateCounters() {
		// Arrange
		ExportComponentRegistryOptions options = new() { Version = "8.3.4" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.ComponentCount.Should().Be(2, because: "the sample registry has two components");
		response.CompositeCount.Should().Be(1, because: "the sample registry has one composite");
		response.InputCount.Should().Be(3, because: "crt.ApprovalList has 2 inputs and crt.Button has 1");
	}

	[Test]
	[Description("schema-type=mobile sources from the mobile registry client, not the web one.")]
	public async Task TryExportAsync_ShouldUseMobileClient_WhenSchemaTypeIsMobile() {
		// Arrange
		ExportComponentRegistryOptions options = new() { Version = "8.3.4", SchemaType = "mobile" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeTrue(because: "the mobile registry must export successfully");
		string written = _ioFileSystem.File.ReadAllText(response.OutputFile);
		written.Should().Be(MobileRegistry, because: "the mobile client's payload, not the web one, must be written");
		await _webRegistryClient.DidNotReceiveWithAnyArgs().GetAsync(default, default);
		await _mobileRegistryClient.Received(1).GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Omitting schema-type (or passing 'web') sources from the web registry client.")]
	public async Task TryExportAsync_ShouldUseWebClient_ByDefault() {
		// Arrange
		ExportComponentRegistryOptions options = new() { Version = "8.3.4" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeTrue(because: "the default web registry must export successfully");
		await _webRegistryClient.Received(1).GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
		await _mobileRegistryClient.DidNotReceiveWithAnyArgs().GetAsync(default, default);
	}

	[Test]
	[Description("An unrecognized schema-type value degrades to the web catalog and surfaces a schemaTypeWarning instead of hard-failing.")]
	public async Task TryExportAsync_ShouldWarnAndFallBackToWeb_ForUnrecognizedSchemaType() {
		// Arrange
		ExportComponentRegistryOptions options = new() { Version = "8.3.4", SchemaType = "moblie" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeTrue(because: "a mistyped schema-type must degrade rather than hard-fail the call");
		response.SchemaTypeWarning.Should().Contain("moblie",
			because: "the warning must name the offending value so a typo is distinguishable from an intentional 'web' request");
		await _webRegistryClient.Received(1).GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A 200-status body that is not JSON at all fails the export and leaves no file behind — the count-before-write ordering that keeps a junk payload off disk.")]
	public async Task TryExportAsync_ShouldFailWithoutWriting_WhenPayloadIsNotJson() {
		// Arrange
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string workspace = _ioFileSystem.Path.Combine(tempRoot, "ecr-nonjson-ws");
		_ioFileSystem.Directory.CreateDirectory(workspace);
		_ioFileSystem.Directory.SetCurrentDirectory(workspace);
		string scratch = _ioFileSystem.Path.Combine(tempRoot, "ecr-nonjson", "registry.json");
		StubFetch(_webRegistryClient, "<html>502 Bad Gateway</html>");
		ExportComponentRegistryOptions options = new() { Version = "8.3.4", OutputFile = scratch };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeFalse(because: "a proxy or CDN error page served with status 200 is not a registry");
		_ioFileSystem.File.Exists(_ioFileSystem.Path.GetFullPath(scratch)).Should().BeFalse(
			because: "the payload must be parsed before it is written, so an unparseable body never reaches disk — "
				+ "an explicit output-file is refuse-if-exists, so a junk file would block every retry to the same path");
	}

	[Test]
	[Description("A 200-status body that is parseable JSON but carries no components array fails the export instead of reporting success with every counter at zero, and writes nothing to the default path.")]
	public async Task TryExportAsync_ShouldFailWithoutWriting_WhenPayloadIsJsonButNotARegistry() {
		// Arrange
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string workspace = _ioFileSystem.Path.Combine(tempRoot, "ecr-notregistry-ws");
		_ioFileSystem.Directory.CreateDirectory(workspace);
		_ioFileSystem.Directory.SetCurrentDirectory(workspace);
		StubFetch(_webRegistryClient, """{ "error": "gateway timeout" }""");
		ExportComponentRegistryOptions options = new() { Version = "8.3.4" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeFalse(
			because: "counters are the consumer's only verification signal, so 'no components array' must be an attributable failure, not an empty registry");
		response.Error.Should().Contain("not a component registry",
			because: "the caller must be told the payload shape was rejected rather than silently exported empty");
		response.ComponentCount.Should().Be(0, because: "a failed export reports no counters");
	}

	[Test]
	[Description("An explicit version is normalised to the 3-part catalog key before the fetch, so a 4-part CoreVersion string asks for the catalog that actually exists.")]
	public async Task TryExportAsync_ShouldNormaliseExplicitVersion_BeforeFetchingTheCatalog() {
		// Arrange
		ExportComponentRegistryOptions options = new() { Version = "8.3.4.5678" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeTrue(because: "a 4-part CoreVersion string is a valid platform version");
		await _webRegistryClient.Received(1).GetAsync("8.3.4", Arg.Any<CancellationToken>());
		response.ResolvedFrom.Should().Be(ComponentInfoResolution.ResolvedFromEnvironment,
			because: "the normalised catalog exists, so the tier must be the exact match — not a bogus superset caused by requesting '8.3.4.5678'");
	}

	[Test]
	[Description("A malformed version is rejected with a format error before any registry fetch.")]
	public async Task TryExportAsync_ShouldFail_WhenVersionIsNotAPlatformVersion() {
		// Arrange
		ExportComponentRegistryOptions options = new() { Version = "8.x" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeFalse(because: "a value that is not a platform version cannot select a catalog");
		response.Error.Should().Contain("8.x",
			because: "the error must name the offending value so the caller can correct it");
		await _webRegistryClient.DidNotReceiveWithAnyArgs().GetAsync(default, default);
	}

	[Test]
	[Description("The latest-fallback tier carries the prose version warning, not only the requiresVersionConfirmation boolean.")]
	public async Task TryExportAsync_ShouldCarryVersionWarning_OnLatestFallback() {
		// Arrange
		ExportComponentRegistryOptions options = new();

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.VersionWarning.Should().Be(ComponentInfoResolution.LatestFallbackWarning,
			because: "an unknown target version must reach the caller as the hard-stop prose, not only as a boolean flag");
	}

	[Test]
	[Description("An exact per-version match carries no version warning.")]
	public async Task TryExportAsync_ShouldCarryNoVersionWarning_OnExactMatch() {
		// Arrange
		ExportComponentRegistryOptions options = new() { Version = "8.3.4" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.VersionWarning.Should().BeNull(
			because: "the exact 'environment' tier is authoritative, so a caveat would be noise");
	}

	[Test]
	[Description("The CLI entry point returns exit code 0 on success and writes the response envelope to stdout exactly once.")]
	public async Task ExecuteAsync_ShouldReturnZero_AndLogOneEnvelope_OnSuccess() {
		// Arrange
		ExportComponentRegistryOptions options = new() { Version = "8.3.4" };

		// Act
		int exitCode = await _command.ExecuteAsync(options, CancellationToken.None);

		// Assert
		exitCode.Should().Be(0, because: "a successful export must not fail the shell pipeline");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("\"success\":true")));
	}

	[Test]
	[Description("The CLI entry point returns exit code 1 on failure and still writes the JSON envelope so a pipeline can read the error.")]
	public async Task ExecuteAsync_ShouldReturnOne_AndLogTheErrorEnvelope_OnFailure() {
		// Arrange
		ExportComponentRegistryOptions options = new() { Version = "8.3.4", Environment = "dev" };

		// Act
		int exitCode = await _command.ExecuteAsync(options, CancellationToken.None);

		// Assert
		exitCode.Should().Be(1, because: "a rejected invocation must be visible to the shell as a failure");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("\"success\":false") && message.Contains("mutually exclusive")));
	}

	// This command has NO IComponentRegistryDocsClient dependency at all (unlike ComponentInfoCommand/Tool,
	// which fetch per-component documentation) — so "docs are never fetched" holds by construction, not by a
	// runtime call count. This test pins that architectural guarantee: a future edit that adds a docs
	// dependency to satisfy some other requirement must not silently reopen the ~150-190 HTTP round-trip cost
	// this command is explicitly designed to avoid (ADR D3 / spec CAP-03).
	[Test]
	[Description("The command's constructor takes no IComponentRegistryDocsClient dependency, so documentation bodies can never be fetched by this code path.")]
	public void Constructor_ShouldNotDepend_OnDocsClient() {
		// Arrange
		System.Reflection.ConstructorInfo constructor = typeof(ExportComponentRegistryCommand)
			.GetConstructors()[0];

		// Act
		System.Reflection.ParameterInfo[] parameters = constructor.GetParameters();

		// Assert
		foreach (System.Reflection.ParameterInfo parameter in parameters) {
			parameter.ParameterType.Name.Should().NotContain("ComponentRegistryDocsClient",
				because: "fetching documentation bodies is explicitly out of scope for this export command");
		}
	}

	[Test]
	[Description("A platform-version probe that fails (network error / 401 / timeout) fails the export with the probe's message, returns exit code 1, and writes no file — the environment-name branch's catch-all is not a silent success.")]
	public async Task ExecuteAsync_ShouldFailWithoutWriting_WhenEnvironmentProbeThrows() {
		// Arrange
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string workspace = _ioFileSystem.Path.Combine(tempRoot, "ecr-probe-fail-ws");
		_ioFileSystem.Directory.CreateDirectory(workspace);
		_ioFileSystem.Directory.SetCurrentDirectory(workspace);
		ExportComponentRegistryCommand command = new(
			_webRegistryClient,
			_mobileRegistryClient,
			new ThrowingResolverFactory(new HttpRequestException("No such host is known (dev.example.com:443)")),
			_settingsRepository,
			_ioFileSystem,
			_logger);
		ExportComponentRegistryOptions options = new() { Environment = "dev" };

		// Act
		int exitCode = await command.ExecuteAsync(options, CancellationToken.None);

		// Assert
		exitCode.Should().Be(1, because: "an unreachable environment must be visible to the shell as a failure");
		// The probe's own message is the only clue the caller has about why the export failed.
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("\"success\":false") && message.Contains("No such host is known")));
		await _webRegistryClient.DidNotReceiveWithAnyArgs().GetAsync(default, default);
		_ioFileSystem.AllFiles.Should().NotContain(path => path.Contains(RegistrySubdirectoryNameForTest),
			because: "a failed probe must leave nothing behind at the default path");
	}

	[Test]
	[Description("A CDN-reported version carrying a path-traversal segment is refused before Path.Combine, so nothing is written anywhere — IsSafePathSegment is the only guard on that network-controlled path segment.")]
	public async Task TryExportAsync_ShouldRefuseDefaultPath_WhenCdnVersionEscapesTheAnchor() {
		// Arrange
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string workspace = _ioFileSystem.Path.Combine(tempRoot, "ecr-traversal-ws");
		_ioFileSystem.Directory.CreateDirectory(workspace);
		_ioFileSystem.Directory.SetCurrentDirectory(workspace);
		// The resolved version arrives from the network, not from the validated input: the request asks for a
		// well-formed 8.3.4 and the CDN answers with a traversal segment.
		StubFetch(_webRegistryClient, SampleRegistryWithDeprecation, resolvedVersion: "../../etc/passwd");
		ExportComponentRegistryOptions options = new() { Version = "8.3.4" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeFalse(
			because: "a version that is not a plain file name must never become a path segment of the default path");
		response.Error.Should().Contain("not usable as a file name",
			because: "the caller must be told to pass an explicit --output-file instead of getting a silent write");
		response.OutputFile.Should().BeNull(because: "a refused export reports no destination");
		_ioFileSystem.AllFiles.Should().NotContain(path => path.Contains("passwd"),
			because: "the guard must run before any directory is created or any byte is written");
		_ioFileSystem.AllFiles.Should().NotContain(path => path.Contains(RegistrySubdirectoryNameForTest),
			because: "no registry file may exist anywhere once the segment guard refuses the write");
	}

	[Test]
	[Description("A well-formed CDN-reported version passes the segment guard and lands at the expected default path under the workspace anchor.")]
	public async Task TryExportAsync_ShouldWriteToDefaultPath_WhenCdnVersionIsWellFormed() {
		// Arrange
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string workspace = _ioFileSystem.Path.Combine(tempRoot, "ecr-safe-segment-ws");
		_ioFileSystem.Directory.CreateDirectory(workspace);
		_ioFileSystem.Directory.SetCurrentDirectory(workspace);
		StubFetch(_webRegistryClient, SampleRegistryWithDeprecation, resolvedVersion: "8.2.1");
		ExportComponentRegistryOptions options = new() { Version = "8.2.1" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeTrue(because: "a plain 3-part version is a valid file-name segment");
		response.OutputFile.Should().Be(
			_ioFileSystem.Path.Combine(workspace, ".clio-migration", RegistrySubdirectoryNameForTest, "8.2.1.json"),
			because: "the web flavor's default path carries no flavor subdirectory — <workspace-root>/.clio-migration/component-registry/<version>.json");
		_ioFileSystem.File.ReadAllText(response.OutputFile).Should().Be(SampleRegistryWithDeprecation,
			because: "the registry must be written byte-faithfully to the guarded default path");
	}

	[Test]
	[Description("A CDN that answers non-2xx on every tier surfaces as a clean failure carrying the unavailable-registry guidance, with no file written — an HTTP-level miss must not read as an empty registry.")]
	public async Task TryExportAsync_ShouldFailWithoutWriting_WhenTheCdnIsUnavailable() {
		// Arrange
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string workspace = _ioFileSystem.Path.Combine(tempRoot, "ecr-cdn-404-ws");
		_ioFileSystem.Directory.CreateDirectory(workspace);
		_ioFileSystem.Directory.SetCurrentDirectory(workspace);
		// A 4xx is a permanent per-attempt failure in ComponentRegistryClient, and once cache and the latest
		// fallback also miss the chain ends in this exception — the shape a 403/404 actually reaches us as.
		_webRegistryClient.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns<Task<ComponentRegistryFetchResult>>(_ => throw new ComponentRegistryUnavailableException("8.3.4", "https://academy.creatio.com"));
		ExportComponentRegistryOptions options = new() { Version = "8.3.4" };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeFalse(because: "an HTTP-level miss is a failure, not a registry with zero components");
		response.Error.Should().Contain("8.3.4",
			because: "the caller has to know WHICH version could not be fetched to act on the failure");
		response.ComponentCount.Should().Be(0, because: "a failed export reports no counters");
		_ioFileSystem.AllFiles.Should().NotContain(path => path.Contains(RegistrySubdirectoryNameForTest),
			because: "nothing may be written when the fetch never produced a payload");
	}

	[Test]
	[Description("A caller-requested cancellation PROPAGATES instead of becoming a failure envelope — the MCP dispatcher and a Ctrl-C'd CLI both need the cooperative cancel, not a report that the export failed.")]
	public void TryExportAsync_ShouldPropagateCancellation_RatherThanReportingFailure() {
		// Arrange
		using CancellationTokenSource cts = new();
		cts.Cancel();
		_webRegistryClient.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns<Task<ComponentRegistryFetchResult>>(callInfo => throw new OperationCanceledException(callInfo.ArgAt<CancellationToken>(1)));
		ExportComponentRegistryOptions options = new() { Version = "8.3.4" };

		// Act
		Func<Task> export = () => _command.TryExportAsync(options, cts.Token);

		// Assert
		export.Should().ThrowAsync<OperationCanceledException>(
			because: "converting a withdrawn request into \"success=false, error=The operation was canceled.\" reports it as if the CDN or the filesystem had refused");
	}

	[Test]
	[Description("The written file is byte-identical to the fetched payload even when the CDN prefixes a UTF-8 BOM — 'byte-faithful' has to mean the wire bytes, and a decoder round-trip would silently drop it.")]
	public async Task TryExportAsync_ShouldWriteTheWireBytesVerbatim_IncludingAByteOrderMark() {
		// Arrange
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string scratch = _ioFileSystem.Path.Combine(tempRoot, "ecr-bom", "registry.json");
		byte[] wireBytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(SampleRegistryWithDeprecation)];
		_webRegistryClient.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(callInfo => Task.FromResult(new ComponentRegistryFetchResult(
				new MemoryStream(wireBytes), callInfo.ArgAt<string>(0), ComponentRegistrySource.Cdn)));
		ExportComponentRegistryOptions options = new() { Version = "8.3.4", OutputFile = scratch };

		// Act
		ExportComponentRegistryResponse response = await _command.TryExportAsync(options, CancellationToken.None);

		// Assert
		response.Success.Should().BeTrue(because: "a BOM-prefixed payload is still valid JSON and still a registry");
		_ioFileSystem.File.ReadAllBytes(response.OutputFile).Should().Equal(wireBytes,
			because: "the file must be a byte-for-byte copy of what the CDN served, BOM included");
		response.ComponentCount.Should().Be(2,
			because: "the counters are read off the same bytes, so a BOM must not break the parse either");
	}

	[Test]
	[Description("A web export and a mobile export of the SAME version land on DIFFERENT default paths, so exporting both leaves two files instead of one silently overwriting the other.")]
	public async Task TryExportAsync_ShouldSeparateDefaultPathsByFlavor_ForTheSameVersion() {
		// Arrange
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string workspace = _ioFileSystem.Path.Combine(tempRoot, "ecr-flavor-ws");
		_ioFileSystem.Directory.CreateDirectory(workspace);
		_ioFileSystem.Directory.SetCurrentDirectory(workspace);

		// Act
		ExportComponentRegistryResponse web =
			await _command.TryExportAsync(new ExportComponentRegistryOptions { Version = "8.3.4" }, CancellationToken.None);
		ExportComponentRegistryResponse mobile = await _command.TryExportAsync(
			new ExportComponentRegistryOptions { Version = "8.3.4", SchemaType = "mobile" }, CancellationToken.None);

		// Assert
		web.Success.Should().BeTrue();
		mobile.Success.Should().BeTrue();
		mobile.OutputFile.Should().NotBe(web.OutputFile,
			because: "the default path overwrites its own prior output, so a shared path would leave the second export silently replacing the first");
		mobile.OutputFile.Should().Be(
			_ioFileSystem.Path.Combine(workspace, ".clio-migration", RegistrySubdirectoryNameForTest, "mobile", "8.3.4.json"),
			because: "the flavor subdirectory mirrors RegistryFlavor.Mobile.CacheSubdirectoryName so the output layout stays in lockstep with the cache layout");
		_ioFileSystem.File.ReadAllText(web.OutputFile).Should().Be(SampleRegistryWithDeprecation,
			because: "the web export must survive the later mobile export unchanged");
		_ioFileSystem.File.ReadAllText(mobile.OutputFile).Should().Be(MobileRegistry,
			because: "each flavor's file must carry its own registry, not whichever ran last");
	}

	private const string RegistrySubdirectoryNameForTest = "component-registry";

	private sealed class ThrowingResolverFactory(Exception failure) : IPlatformVersionResolverFactory {
		public IPlatformVersionResolver Create(EnvironmentSettings settings) => new ThrowingResolver(failure);

		private sealed class ThrowingResolver(Exception failure) : IOwnedPlatformVersionResolver {
			public Task<PlatformVersionResolution> ResolveAsync(CancellationToken cancellationToken = default) =>
				Task.FromException<PlatformVersionResolution>(failure);
			public void Dispose() { }
		}
	}

	private sealed class StubResolverFactory(PlatformVersionResolution result) : IPlatformVersionResolverFactory {
		public int CreateCallCount { get; private set; }

		public IPlatformVersionResolver Create(EnvironmentSettings settings) {
			CreateCallCount++;
			return new StubResolver(result);
		}

		private sealed class StubResolver(PlatformVersionResolution result) : IOwnedPlatformVersionResolver {
			public Task<PlatformVersionResolution> ResolveAsync(CancellationToken cancellationToken = default) =>
				Task.FromResult(result);
			public void Dispose() { }
		}
	}
}

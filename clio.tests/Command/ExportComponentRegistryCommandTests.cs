using System;
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

	private sealed class StubResolverFactory(PlatformVersionResolution result) : IPlatformVersionResolverFactory {
		public int CreateCallCount { get; private set; }

		public IPlatformVersionResolver Create(EnvironmentSettings settings) {
			CreateCallCount++;
			return new StubResolver(result);
		}

		private sealed class StubResolver(PlatformVersionResolution result) : IPlatformVersionResolver {
			public Task<PlatformVersionResolution> ResolveAsync(CancellationToken cancellationToken = default) =>
				Task.FromResult(result);
		}
	}
}

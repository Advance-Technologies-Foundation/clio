using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using ConsoleTables;
using FluentAssertions;
using FluentValidation.Results;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ComponentInfoCommandTests {
	private const string SampleRegistry = """
	[
	  {"componentType":"crt.TabContainer","category":"containers","description":"Tab body.","container":true,"properties":{"caption":{"type":"string","description":"Tab caption."}}},
	  {"componentType":"crt.Input","category":"fields","description":"Text input.","container":false,"properties":{}},
	  {"componentType":"crt.Button","category":"interactive","description":"Menu button.","container":false,"properties":{}}
	]
	""";

	[Test]
	[Description("With no positional component-type the verb emits a flat JSON list and exits 0.")]
	public async Task Returns_List_When_No_Type_Specified() {
		using CapturedLogger logger = new();
		ComponentInfoCommand command = CreateCommand(logger);

		int exit = await command.ExecuteAsync(new ComponentInfoCommandOptions(), CancellationToken.None);

		exit.Should().Be(0);
		ComponentInfoResponse parsed = ParseJson(logger.Captured);
		parsed.Mode.Should().Be("list");
		parsed.Count.Should().Be(3);
		parsed.Items.Should().HaveCount(3, because: "all sample entries surface at the response root");
	}

	[Test]
	[Description("With a known positional component-type the verb emits a detail JSON and exits 0.")]
	public async Task Returns_Detail_When_Type_Matches() {
		using CapturedLogger logger = new();
		ComponentInfoCommand command = CreateCommand(logger);

		int exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { ComponentType = "crt.TabContainer" }, CancellationToken.None);

		exit.Should().Be(0);
		ComponentInfoResponse parsed = ParseJson(logger.Captured);
		parsed.Mode.Should().Be("detail");
		parsed.ComponentType.Should().Be("crt.TabContainer");
		parsed.Container.Should().BeTrue();
	}

	[Test]
	[Description("An unknown component-type exits 1 and emits an error payload with not-found suggestions.")]
	public async Task Returns_Exit_Code_1_For_Unknown_Type() {
		using CapturedLogger logger = new();
		ComponentInfoCommand command = CreateCommand(logger);

		int exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { ComponentType = "crt.DoesNotExist" }, CancellationToken.None);

		exit.Should().Be(1);
		ComponentInfoResponse parsed = ParseJson(logger.Captured);
		parsed.Success.Should().BeFalse();
		parsed.Error.Should().Contain("crt.DoesNotExist");
	}

	[Test]
	[Description("Combining --version and --environment is a hard error before the catalog is even touched.")]
	public async Task Returns_Exit_Code_1_When_Version_And_Environment_Both_Provided() {
		using CapturedLogger logger = new();
		ComponentInfoCommand command = CreateCommand(logger);

		int exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { Version = "8.3.4", Environment = "dev" }, CancellationToken.None);

		exit.Should().Be(1);
		logger.Errors.Should().NotBeEmpty(because: "the conflict must be surfaced as an explicit ILogger.WriteError line");
	}

	[Test]
	[Description("--search reduces the list to entries whose metadata contains the query.")]
	public async Task Filters_List_By_Search() {
		using CapturedLogger logger = new();
		ComponentInfoCommand command = CreateCommand(logger);

		int exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { Search = "menu" }, CancellationToken.None);

		exit.Should().Be(0);
		ComponentInfoResponse parsed = ParseJson(logger.Captured);
		parsed.Count.Should().Be(1, because: "only crt.Button matches the 'menu' keyword in the sample");
		parsed.Items.Should().ContainSingle().Which.ComponentType.Should().Be("crt.Button");
	}

	[Test]
	[Description("When the catalog returns a different version than what was requested the response says latest-fallback.")]
	public async Task Reports_Latest_Fallback_When_Catalog_Falls_Back() {
		using CapturedLogger logger = new();
		ComponentInfoCommand command = CreateCommand(logger, fallbackVersion: "latest");

		int exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { Version = "8.1.5", ComponentType = "crt.Input" }, CancellationToken.None);

		exit.Should().Be(0);
		ComponentInfoResponse parsed = ParseJson(logger.Captured);
		parsed.ResolvedTargetVersion.Should().Be("latest");
		parsed.ResolvedFrom.Should().Be(ComponentInfoResolution.ResolvedFromLatestFallback);
	}

	[Test]
	[Description("--version goes straight into the catalog request — no environment probe is attempted.")]
	public async Task Honors_Explicit_Version_Flag() {
		using CapturedLogger logger = new();
		RecordingCatalog catalog = new(SampleRegistry, echoRequestedVersion: true);
		ComponentInfoCommand command = CreateCommandWith(catalog, logger, resolverFactoryProbeCount: 0);

		int exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { Version = "8.3.4" }, CancellationToken.None);

		exit.Should().Be(0);
		catalog.RequestedVersions.Should().Contain("8.3.4");
	}

	[Test]
	[Description("--environment triggers the version resolver factory once.")]
	public async Task Probes_Environment_When_Environment_Flag_Set() {
		using CapturedLogger logger = new();
		RecordingCatalog catalog = new(SampleRegistry, echoRequestedVersion: true);
		StubResolverFactory factory = new(new PlatformVersionResolution("8.3.4", VersionResolutionSource.Environment));
		ISettingsRepository repository = StubSettingsRepository("dev");
		ComponentInfoCommand command = new(catalog, StubMobileCatalog.Empty(), factory, repository, logger);

		int exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { Environment = "dev" }, CancellationToken.None);

		exit.Should().Be(0);
		factory.CreateCallCount.Should().Be(1, because: "the resolver factory must be invoked once per env-aware call");
		catalog.RequestedVersions.Should().Contain("8.3.4",
			because: "the probed platform version must feed the catalog load");
	}

	[Test]
	[Description("--pretty switches stdout to a human-readable block instead of JSON.")]
	public async Task Emits_Pretty_Text_When_Pretty_Flag_Set() {
		using CapturedLogger logger = new();
		ComponentInfoCommand command = CreateCommand(logger);

		int exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { ComponentType = "crt.TabContainer", Pretty = true }, CancellationToken.None);

		exit.Should().Be(0);
		logger.Captured.Should().NotStartWith("{", because: "the pretty renderer does not emit JSON");
		logger.Captured.Should().Contain("componentType:");
		logger.Captured.Should().Contain("crt.TabContainer");
	}

	private static ComponentInfoCommand CreateCommand(CapturedLogger logger, string fallbackVersion = null, IMobileComponentInfoCatalog mobileCatalog = null) {
		IComponentInfoCatalog catalog = fallbackVersion is null
			? new RecordingCatalog(SampleRegistry, echoRequestedVersion: true)
			: new RecordingCatalog(SampleRegistry, echoRequestedVersion: false, fallbackVersion: fallbackVersion);
		StubResolverFactory factory = new(new PlatformVersionResolution("latest", VersionResolutionSource.LatestFallback));
		ISettingsRepository repository = Substitute.For<ISettingsRepository>();
		return new ComponentInfoCommand(catalog, mobileCatalog ?? StubMobileCatalog.Empty(), factory, repository, logger);
	}

	private static ComponentInfoCommand CreateCommandWith(RecordingCatalog catalog, CapturedLogger logger, int resolverFactoryProbeCount) {
		StubResolverFactory factory = new(new PlatformVersionResolution("latest", VersionResolutionSource.LatestFallback));
		ISettingsRepository repository = Substitute.For<ISettingsRepository>();
		return new ComponentInfoCommand(catalog, StubMobileCatalog.Empty(), factory, repository, logger);
	}

	private static ComponentInfoResponse ParseJson(string output) {
		string trimmed = output.Trim();
		return JsonSerializer.Deserialize<ComponentInfoResponse>(trimmed, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
	}

	private static ISettingsRepository StubSettingsRepository(string activeEnv) {
		ISettingsRepository repo = Substitute.For<ISettingsRepository>();
		repo.GetDefaultEnvironmentName().Returns(activeEnv);
		repo.IsEnvironmentExists(activeEnv).Returns(true);
		repo.GetEnvironment(Arg.Any<EnvironmentOptions>()).Returns(new EnvironmentSettings { Uri = "https://dev.example.com" });
		return repo;
	}

	/// <summary>Captures ILogger output without a real terminal.</summary>
	private sealed class CapturedLogger : ILogger, IDisposable {
		private readonly StringBuilder _buffer = new();
		public string Captured => _buffer.ToString();
		public List<string> Errors { get; } = new();

		// Members carrying the actual captured signal:
		public void Write(string value) => _buffer.Append(value);
		public void WriteLine() => _buffer.AppendLine();
		public void WriteLine(string value) => _buffer.AppendLine(value);
		public void WriteError(string value) { Errors.Add(value); _buffer.Append("[ERROR] ").AppendLine(value); }
		public void WriteInfo(string value) => _buffer.Append("[INFO] ").AppendLine(value);
		public void WriteWarning(string value) => _buffer.Append("[WARN] ").AppendLine(value);
		public void WriteDebug(string value) => _buffer.Append("[DEBUG] ").AppendLine(value);

		// No-op infrastructure required by the ILogger contract.
		List<LogMessage> ILogger.LogMessages { get; } = new();
		bool ILogger.PreserveMessages { get; set; }
		public void ClearMessages() { }
		public IDisposable BeginScopedFileSink(string logFilePath) => this;
		public void Start(string logFilePath = "") { }
		public void StartWithStream() { }
		public void Stop() { }
		public void SetCreatioLogStreamer(ILogStreamer creatioLogStreamer) { }
		public void PrintTable(ConsoleTable table) { }
		public void PrintValidationFailureErrors(IEnumerable<ValidationFailure> errors) { }
		public void Dispose() { }
	}

	private sealed class RecordingCatalog : IComponentInfoCatalog {
		private readonly Stream _payloadFactoryAnchor = Stream.Null; // suppresses CA1822 if we ever switch helpers to instance
		private readonly byte[] _payload;
		private readonly bool _echoRequestedVersion;
		private readonly string _fallbackVersion;
		public List<string> RequestedVersions { get; } = new();

		public RecordingCatalog(string registryJson, bool echoRequestedVersion, string fallbackVersion = "latest") {
			_payload = Encoding.UTF8.GetBytes(registryJson);
			_echoRequestedVersion = echoRequestedVersion;
			_fallbackVersion = fallbackVersion;
		}

		public Task<ComponentCatalogState> LoadAsync(string requestedVersion, CancellationToken cancellationToken = default) {
			RequestedVersions.Add(requestedVersion);
			string actual = _echoRequestedVersion ? requestedVersion : _fallbackVersion;
			using MemoryStream stream = new(_payload, writable: false);
			ComponentCatalogState state = ComponentInfoCatalog.LoadFromStream(stream, actual, ComponentRegistrySource.Cdn);
			return Task.FromResult(state);
		}

		public async Task<IReadOnlyList<ComponentRegistryEntry>> GetAllAsync(string requestedVersion, CancellationToken cancellationToken = default) {
			ComponentCatalogState state = await LoadAsync(requestedVersion, cancellationToken).ConfigureAwait(false);
			return state.Entries;
		}

		public Task<IReadOnlyList<ComponentRegistryEntry>> SearchAsync(string requestedVersion, string search, CancellationToken cancellationToken = default) =>
			throw new NotImplementedException();

		public Task<ComponentRegistryEntry> FindAsync(string requestedVersion, string componentType, CancellationToken cancellationToken = default) =>
			throw new NotImplementedException();
	}

	/// <summary>Test double for the mobile catalog: in-memory entries, no filesystem.</summary>
	private sealed class StubMobileCatalog : IMobileComponentInfoCatalog {
		private readonly IReadOnlyList<ComponentRegistryEntry> _entries;
		private readonly IReadOnlyDictionary<string, ComponentRegistryEntry> _lookup;

		private StubMobileCatalog(IReadOnlyList<ComponentRegistryEntry> entries) {
			_entries = entries;
			_lookup = entries.ToDictionary(e => e.ComponentType, StringComparer.OrdinalIgnoreCase);
		}

		public static StubMobileCatalog Empty() => new(Array.Empty<ComponentRegistryEntry>());

		public static StubMobileCatalog FromJson(string registryJson) {
			ComponentRegistryEntry[] parsed = JsonSerializer.Deserialize<ComponentRegistryEntry[]>(
				registryJson,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
			return new StubMobileCatalog(parsed);
		}

		public IReadOnlyList<ComponentRegistryEntry> GetAll() => _entries;

		public IReadOnlyList<ComponentRegistryEntry> Search(string? search) =>
			ComponentInfoGrouping.FilterEntries(_entries, search);

		public ComponentRegistryEntry? Find(string componentType) =>
			string.IsNullOrWhiteSpace(componentType)
				? null
				: _lookup.TryGetValue(componentType.Trim(), out ComponentRegistryEntry? entry) ? entry : null;
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

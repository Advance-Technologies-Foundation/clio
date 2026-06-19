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
	  {"componentType":"crt.Button","category":"interactive","description":"Menu button.","whenToUse":"Use to trigger an action or open a menu.","whenNotToUse":"Not for navigation links.","container":false,"properties":{}}
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
	[Description("When the requested version is known but its exact catalog is unavailable, the client serves 'latest' and the response reports 'environment-superset' with a soft caveat — the version is not uncertain but the catalog is approximate.")]
	public async Task Reports_EnvironmentSuperset_When_Version_Known_But_Catalog_Falls_Back() {
		using CapturedLogger logger = new();
		ComponentInfoCommand command = CreateCommand(logger, fallbackVersion: "latest");

		int exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { Version = "8.1.5", ComponentType = "crt.Input" }, CancellationToken.None);

		exit.Should().Be(0);
		ComponentInfoResponse parsed = ParseJson(logger.Captured);
		parsed.ResolvedTargetVersion.Should().Be("latest",
			because: "the response must echo the catalog version actually loaded by the client");
		parsed.ResolvedFrom.Should().Be(ComponentInfoResolution.ResolvedFromEnvironmentSuperset,
			because: "the version was known but the exact catalog was absent, so 'environment-superset' signals an approximate catalog");
		parsed.VersionWarning.Should().Be(ComponentInfoResolution.EnvironmentSupersetWarning,
			because: "a soft caveat must be surfaced so the agent checks critical components against the actual environment");
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
		ComponentInfoCommand command = new(catalog, StubMobileCatalog.Empty(), new FakeDocsClient(), factory, repository, logger);

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

	[Test]
	[Description("--pretty wires the Solution A selection-metadata through the CLI verb: the whenToUse line reaches the human detail view. Full per-line rendering parity (all six fields, both appliesToCustomEntities arms, the '; ' vs ', ' joins) is covered directly in ComponentInfoPrettyRenderer_Should_Render_All_Selection_Metadata_Lines.")]
	public async Task Emits_Selection_Metadata_In_Pretty_Detail() {
		using CapturedLogger logger = new();
		ComponentInfoCommand command = CreateCommand(logger);

		int exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { ComponentType = "crt.Button", Pretty = true }, CancellationToken.None);

		exit.Should().Be(0,
			because: "rendering an existing component in --pretty mode is a successful invocation");
		logger.Captured.Should().Contain("whenToUse:",
			because: "the --pretty detail view must label the selection guidance like the JSON response surfaces it");
		logger.Captured.Should().Contain("Use to trigger an action or open a menu.",
			because: "the published @whenToUse text must reach the human CLI surface, not just JSON");
	}

	[Test]
	[Description("Wrapped-shape detail responses surface references.typeDefinitions on JSON output and render it under references.typeDefinitions: in --pretty — AI needs the named-type schemas to resolve the type names referenced in inputs/outputs.")]
	public async Task Returns_Wrapped_Detail_With_TypeDefinitions() {
		const string wrappedRegistry = """
		{
		  "components": [
		    {
		      "componentType": "crt.WithTypes",
		      "inputs": {
		        "icon": { "type": "string | ButtonIcon" }
		      },
		      "references": {
		        "typeDefinitions": {
		          "ButtonIcon": { "type": "string", "values": ["close-icon", "edit-icon"] }
		        }
		      }
		    }
		  ]
		}
		""";
		using CapturedLogger logger = new();
		ComponentInfoCommand command = CreateCommandWith(
			new RecordingCatalog(wrappedRegistry, echoRequestedVersion: true),
			logger,
			resolverFactoryProbeCount: 0);

		// Act — JSON path
		int exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { ComponentType = "crt.WithTypes" }, CancellationToken.None);
		exit.Should().Be(0);

		ComponentInfoResponse parsed = ParseJson(logger.Captured);
		parsed.Mode.Should().Be("detail");
		parsed.References.Should().NotBeNull(
			because: "the CLI verb must surface references.typeDefinitions verbatim, not drop them");
		parsed.References!.TypeDefinitions.Should().NotBeNull();
		parsed.References.TypeDefinitions!.Should().ContainKey("ButtonIcon");

		// Act — --pretty path on the same input
		logger.Reset();
		exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { ComponentType = "crt.WithTypes", Pretty = true }, CancellationToken.None);
		exit.Should().Be(0);
		logger.Captured.Should().Contain("references.typeDefinitions:",
			because: "the --pretty renderer must label the typeDefinitions block so operators can read it");
		logger.Captured.Should().Contain("ButtonIcon",
			because: "each type-definition key must be listed by name");
		logger.Captured.Should().Contain("close-icon",
			because: "the compact JSON dump must surface the enum values from the producer schema");
	}

	[Test]
	[Description("Wrapped-shape detail responses emit inputs/outputs in JSON and on the --pretty stdout block — the CLI verb shares the same bug surface as the MCP tool.")]
	public async Task Returns_Wrapped_Detail_With_Inputs_And_Outputs() {
		const string wrappedRegistry = """
		{
		  "components": [
		    {
		      "componentType": "crt.WrappedButton",
		      "inputs": {
		        "caption": { "type": "string", "default": "", "description": "Component title." },
		        "color":   { "type": "string", "values": ["primary", "accent"] }
		      },
		      "outputs": {
		        "clicked": { "type": "RequestBindingConfig" }
		      }
		    }
		  ]
		}
		""";
		using CapturedLogger logger = new();
		ComponentInfoCommand command = CreateCommandWith(
			new RecordingCatalog(wrappedRegistry, echoRequestedVersion: true),
			logger,
			resolverFactoryProbeCount: 0);

		// Act — JSON path
		int exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { ComponentType = "crt.WrappedButton" }, CancellationToken.None);
		exit.Should().Be(0);

		ComponentInfoResponse parsed = ParseJson(logger.Captured);
		parsed.Mode.Should().Be("detail");
		parsed.ComponentType.Should().Be("crt.WrappedButton");
		parsed.Inputs.Should().NotBeNull(because: "the CLI verb must surface wrapped-shape inputs verbatim, not drop them");
		parsed.Inputs!.Should().ContainKeys("caption", "color");
		parsed.Outputs.Should().NotBeNull();
		parsed.Outputs!.Should().ContainKey("clicked");
		parsed.Properties.Should().BeNull(because: "the legacy properties block must be omitted, not emitted as an empty object");

		// Act — --pretty path on the same input
		logger.Reset();
		exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { ComponentType = "crt.WrappedButton", Pretty = true }, CancellationToken.None);
		exit.Should().Be(0);
		logger.Captured.Should().Contain("inputs:",
			because: "the --pretty renderer must label the inputs block so operators can read it");
		logger.Captured.Should().Contain("caption");
		logger.Captured.Should().Contain("color");
		logger.Captured.Should().Contain("values=[primary, accent]",
			because: "enum constraints from the inputs block must be rendered the same way as legacy properties");
		logger.Captured.Should().Contain("outputs:");
		logger.Captured.Should().Contain("clicked");
	}

	[Test]
	[Description("Detail responses fetch every content.docs[] file through IComponentRegistryDocsClient and surface the concatenated markdown on response.documentation — same payload the MCP tool produces.")]
	public async Task Returns_Detail_With_Documentation_When_Entry_Has_Docs() {
		const string registry = """
		{
		  "components": [
		    {
		      "componentType": "crt.Button",
		      "references": {
		        "docs": ["docs/button.component.md", "docs/button.recipes.md"]
		      }
		    }
		  ]
		}
		""";
		using CapturedLogger logger = new();
		FakeDocsClient docsClient = new FakeDocsClient()
			.Seed("latest", "docs/button.component.md", "# Button\nPrimary action element.")
			.Seed("latest", "docs/button.recipes.md", "## Recipes\nUse `crt.Button` inside a toolbar.");
		ComponentInfoCommand command = CreateCommandWith(
			new RecordingCatalog(registry, echoRequestedVersion: true),
			logger,
			resolverFactoryProbeCount: 0,
			docsClient: docsClient);

		int exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { ComponentType = "crt.Button" }, CancellationToken.None);

		exit.Should().Be(0);
		ComponentInfoResponse parsed = ParseJson(logger.Captured);
		parsed.Mode.Should().Be("detail");
		parsed.Documentation.Should().NotBeNullOrEmpty(
			because: "the CLI verb must surface long-form docs the same way the MCP tool does");
		parsed.Documentation!.Should().Contain("# Button");
		parsed.Documentation.Should().Contain("## Recipes");
		parsed.Documentation.Should().Contain("\n\n---\n\n",
			because: "every successfully-fetched doc block is concatenated with the canonical separator");
		docsClient.Requests.Should().HaveCount(2,
			because: "each path under content.docs[] must hit the docs client exactly once");
		docsClient.Requests.Should().Contain(("latest", "docs/button.component.md"));
		docsClient.Requests.Should().Contain(("latest", "docs/button.recipes.md"));
	}

	[Test]
	[Description("The --pretty renderer emits a 'documentation:' section when the entry has content.docs[] — operators reading stdout see the markdown the MCP tool would serve over JSON.")]
	public async Task Pretty_Output_Renders_Documentation_Block_When_Entry_Has_Docs() {
		const string registry = """
		{
		  "components": [
		    {
		      "componentType": "crt.Button",
		      "references": { "docs": ["docs/button.component.md"] }
		    }
		  ]
		}
		""";
		using CapturedLogger logger = new();
		FakeDocsClient docsClient = new FakeDocsClient()
			.Seed("latest", "docs/button.component.md", "# Button\nPrimary action element.");
		ComponentInfoCommand command = CreateCommandWith(
			new RecordingCatalog(registry, echoRequestedVersion: true),
			logger,
			resolverFactoryProbeCount: 0,
			docsClient: docsClient);

		int exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { ComponentType = "crt.Button", Pretty = true }, CancellationToken.None);

		exit.Should().Be(0);
		logger.Captured.Should().Contain("documentation:",
			because: "the pretty renderer labels the docs block so operators can spot it on stdout");
		logger.Captured.Should().Contain("# Button");
		logger.Captured.Should().Contain("Primary action element.");
	}

	[Test]
	[Description("When the matched entry has no content.docs[], the CLI does not touch the docs client and omits the documentation field — short-circuit identical to the MCP tool.")]
	public async Task Skips_Docs_Client_When_Entry_Has_No_Docs() {
		using CapturedLogger logger = new();
		FakeDocsClient docsClient = new();
		ComponentInfoCommand command = CreateCommand(logger, docsClient: docsClient);

		int exit = await command.ExecuteAsync(
			new ComponentInfoCommandOptions { ComponentType = "crt.TabContainer" }, CancellationToken.None);

		exit.Should().Be(0);
		ComponentInfoResponse parsed = ParseJson(logger.Captured);
		parsed.Mode.Should().Be("detail");
		parsed.Documentation.Should().BeNull(
			because: "the sample registry does not declare content.docs[] for crt.TabContainer");
		docsClient.Requests.Should().BeEmpty(
			because: "no content.docs[] means the docs pipeline must not run at all (no wasted HTTP calls)");
	}

	private static ComponentInfoCommand CreateCommand(
		CapturedLogger logger,
		string fallbackVersion = null,
		IMobileComponentInfoCatalog mobileCatalog = null,
		IComponentRegistryDocsClient docsClient = null) {
		IComponentInfoCatalog catalog = fallbackVersion is null
			? new RecordingCatalog(SampleRegistry, echoRequestedVersion: true)
			: new RecordingCatalog(SampleRegistry, echoRequestedVersion: false, fallbackVersion: fallbackVersion);
		StubResolverFactory factory = new(new PlatformVersionResolution("latest", VersionResolutionSource.LatestFallback));
		ISettingsRepository repository = Substitute.For<ISettingsRepository>();
		return new ComponentInfoCommand(
			catalog,
			mobileCatalog ?? StubMobileCatalog.Empty(),
			docsClient ?? new FakeDocsClient(),
			factory,
			repository,
			logger);
	}

	private static ComponentInfoCommand CreateCommandWith(
		RecordingCatalog catalog,
		CapturedLogger logger,
		int resolverFactoryProbeCount,
		IComponentRegistryDocsClient docsClient = null) {
		StubResolverFactory factory = new(new PlatformVersionResolution("latest", VersionResolutionSource.LatestFallback));
		ISettingsRepository repository = Substitute.For<ISettingsRepository>();
		return new ComponentInfoCommand(
			catalog,
			StubMobileCatalog.Empty(),
			docsClient ?? new FakeDocsClient(),
			factory,
			repository,
			logger);
	}

	private static ComponentInfoResponse ParseJson(string output) {
		string trimmed = output.Trim();
		return JsonSerializer.Deserialize<ComponentInfoResponse>(trimmed, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
	}

	/// <summary>
	/// Test double for the docs client. Returns a pre-seeded markdown blob for the
	/// matching (version, path) tuple or <see langword="null"/> otherwise — matching
	/// the contract that the real client uses to signal "skip this doc".
	/// </summary>
	private sealed class FakeDocsClient : IComponentRegistryDocsClient {
		private readonly Dictionary<(string Version, string DocPath), string> _docs = new();
		public List<(string Version, string DocPath)> Requests { get; } = new();

		public FakeDocsClient Seed(string version, string docPath, string content) {
			_docs[(version, docPath)] = content;
			return this;
		}

		public Task<string> GetDocAsync(string version, string docPath, CancellationToken cancellationToken = default) {
			Requests.Add((version, docPath));
			return Task.FromResult(_docs.TryGetValue((version, docPath), out string value) ? value : null);
		}
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

		/// <summary>
		/// Clears the captured buffer and error list. Used by multi-act tests that
		/// invoke the verb twice on the same logger (e.g. JSON output then --pretty
		/// output on the same registry) so the second assertion only sees the
		/// second invocation's output.
		/// </summary>
		public void Reset() {
			_buffer.Clear();
			Errors.Clear();
		}

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
		private readonly ComponentCatalogState _state;

		private StubMobileCatalog(IReadOnlyList<ComponentRegistryEntry> entries) {
			Dictionary<string, ComponentRegistryEntry> lookup = entries.ToDictionary(e => e.ComponentType, StringComparer.OrdinalIgnoreCase);
			_state = new ComponentCatalogState(entries, lookup, "latest", ComponentRegistrySource.Local);
		}

		public static StubMobileCatalog Empty() => new(Array.Empty<ComponentRegistryEntry>());

		public static StubMobileCatalog FromJson(string registryJson) {
			ComponentRegistryEntry[] parsed = JsonSerializer.Deserialize<ComponentRegistryEntry[]>(
				registryJson,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
			return new StubMobileCatalog(parsed);
		}

		public Task<ComponentCatalogState> LoadAsync(string requestedVersion, CancellationToken cancellationToken = default) =>
			Task.FromResult(_state);

		public Task<IReadOnlyList<ComponentRegistryEntry>> GetAllAsync(string requestedVersion, CancellationToken cancellationToken = default) =>
			Task.FromResult(_state.Entries);

		public Task<IReadOnlyList<ComponentRegistryEntry>> SearchAsync(string requestedVersion, string? search, CancellationToken cancellationToken = default) =>
			Task.FromResult(ComponentInfoGrouping.FilterEntries(_state.Entries, search));

		public Task<ComponentRegistryEntry?> FindAsync(string requestedVersion, string componentType, CancellationToken cancellationToken = default) =>
			Task.FromResult(string.IsNullOrWhiteSpace(componentType)
				? null
				: _state.Lookup.TryGetValue(componentType.Trim(), out ComponentRegistryEntry? entry) ? entry : null);
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

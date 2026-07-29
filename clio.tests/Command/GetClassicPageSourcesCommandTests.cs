using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
internal class GetClassicPageSourcesCommandTests : BaseCommandTests<GetClassicPageSourcesOptions> {

	private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";

	private GetClassicPageSourcesCommand _command;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private IPageDesignerHierarchyClient _hierarchyClient;
	private IClassicSectionSchemaResolver _sectionResolver;
	private System.IO.Abstractions.TestingHelpers.MockFileSystem _ioFileSystem;
	private ILogger _logger;

	// Name-aware fake Creatio: schema name -> SysSchema layer rows; layer UId -> loaded schema object;
	// layer UId -> merged localizable strings (returned only on a full-hierarchy load).
	private readonly Dictionary<string, JArray> _layersByName = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, JObject> _schemaByUid = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, JArray> _localizableByUid = new(StringComparer.OrdinalIgnoreCase);

	public override void Setup() {
		base.Setup();
		_layersByName.Clear();
		_schemaByUid.Clear();
		_localizableByUid.Clear();
		_command = Container.GetRequiredService<GetClassicPageSourcesCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		_sectionResolver = Substitute.For<IClassicSectionSchemaResolver>();
		_ioFileSystem = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build(Arg.Any<string>()).Returns("http://localhost/svc");
		_applicationClient.ExecutePostRequest(default, default).ReturnsForAnyArgs(ci => Route(ci.ArgAt<string>(1)));
		// Default: no hierarchy -> LoadChainAndSeed falls back to the legacy per-layer fan-out, so the existing
		// tests exercise (and keep asserting) that fallback path. The hierarchy-path tests configure this explicitly.
		_hierarchyClient.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>())
			.Returns(new List<PageDesignerHierarchySchema>());
		// Default: SysModule metadata binds no section, so section resolution falls through to the name
		// conventions and the pre-existing tests keep asserting that path unchanged.
		_sectionResolver.ResolveSectionSchemaNames(Arg.Any<string>())
			.Returns(new ClassicSectionLookup(Array.Empty<string>(), null));
		containerBuilder.AddSingleton(_applicationClient);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_columnManager);
		containerBuilder.AddSingleton(_hierarchyClient);
		containerBuilder.AddSingleton(_sectionResolver);
		containerBuilder.AddSingleton<System.IO.Abstractions.IFileSystem>(_ioFileSystem);
		containerBuilder.AddSingleton(_logger);
	}

	// The manifest content the command wrote, read back through the io file system — the single abstraction every
	// write now goes through (the tool-owned default path and an explicit --output-file alike).
	private string ReadManifest(GetClassicPageSourcesResponse response) =>
		_ioFileSystem.File.ReadAllText(response.ManifestPath);

	// True when nothing landed under the tool-owned default output directory, i.e. the run wrote no manifest at all.
	private bool NoDefaultManifestWritten =>
		!_ioFileSystem.AllFiles.Any(path => path.Contains(".clio-migration", StringComparison.OrdinalIgnoreCase));

	[Test]
	[Description("TryAssemblePageSources writes a manifest with base->top schemas, the parent seed, resources, and entity columns; the response carries only a summary.")]
	public void TryAssemblePageSources_ShouldWriteManifest_WithLayersSeedResourcesAndColumns() {
		// Arrange — a two-layer page with a parent template and merged resources, no details/section
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddLayer("UsrTestPage", "uid-base", "BaseApp", 100);
		AddSchema("uid-base", "define(\"UsrTestPage\", [], function() { return { entitySchemaName: \"UsrTest\" }; });", "uid-parent", "BaseApp");
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return {}; });", "uid-parent", "UsrApp");
		AddSchema("uid-parent", "define(\"BaseModulePageV2\", [], function() { return {}; });", EmptyGuid, "CrtBase");
		AddLocalizable("uid-top", "HeaderCaption", "Header");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert — summary
		ok.Should().BeTrue(because: "a resolvable multi-layer page assembles successfully");
		response.Entity.Should().Be("UsrTest", because: "the entity is inferred from the page body's entitySchemaName");
		response.LayerCount.Should().Be(2, because: "both replacing layers were enumerated");
		response.SeedCount.Should().Be(1, because: "one parent-template body was walked into the seed");
		response.ResourceCount.Should().Be(1, because: "one merged localizable string became a resource");
		response.ColumnCount.Should().Be(2, because: "both entity columns contributed a title");

		// Assert — manifest content (bodies live here, NOT in the response)
		JObject manifest = JObject.Parse(ReadManifest(response));
		var schemas = (JArray)manifest["schemas"];
		schemas.Should().HaveCount(2, because: "every replacing layer body belongs to the manifest chain");
		schemas[0]["pkg"]!.ToString().Should().Be("BaseApp", because: "the base layer sorts first (base->top)");
		schemas[1]["pkg"]!.ToString().Should().Be("UsrApp", because: "the most-derived layer sorts last");
		((JArray)manifest["seed"]).Should().ContainSingle(because: "one parent-template layer was seeded")
			.Which["pkg"]!.ToString().Should().Be("CrtBase", because: "the seed carries the parent template's package");
		manifest["entity"]!.ToString().Should().Be("UsrTest", because: "the inferred entity lands in the manifest");
		manifest["resources"]!["HeaderCaption"]!.ToString().Should().Be("Header",
			because: "the merged localizable string becomes a resource entry");
		manifest["columnTitles"]!["Account"]!.ToString().Should().Be("Customer",
			because: "the entity column title is gathered into columnTitles");
		manifest["entityColumns"]!["Account"]!["ref"]!.ToString().Should().Be("Account",
			because: "the lookup column's reference schema is gathered into entityColumns");
		manifest["detailSchemas"].Should().BeNull(because: "the page references no details");
		manifest["section"].Should().BeNull(because: "no section schema resolves for this entity");
	}

	[Test]
	[Description("TryAssemblePageSources anchors the default manifest path (absolute) and reports it in the response, instead of a cwd-relative string an MCP caller cannot resolve.")]
	public void TryAssemblePageSources_ShouldUseAnchoredAbsoluteDefaultPath_WhenOutputFileOmitted() {
		// Arrange
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return { entitySchemaName: \"UsrTest\" }; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert — no workspace marker above the mock cwd, so the anchor is the current directory itself
		string expected = Path.Combine(
			_ioFileSystem.Directory.GetCurrentDirectory(), ".clio-migration", "UsrTestPage", "manifest.json");
		response.ManifestPath.Should().Be(expected,
			because: "the default output anchors at the resolved directory (PRD OQ-04, get-page convention)");
		Path.IsPathRooted(response.ManifestPath).Should().BeTrue(
			because: "the reported path must be absolute — the MCP caller does not know the server's cwd");
		_ioFileSystem.File.Exists(expected).Should().BeTrue(
			because: "the manifest is written to the same resolved path it reports");
	}

	[Test]
	[Description("TryAssemblePageSources anchors the default manifest path at the enclosing workspace root when the cwd is inside a workspace.")]
	public void TryAssemblePageSources_ShouldAnchorDefaultPath_AtWorkspaceRoot() {
		// Arrange — a workspace marker above the current directory
		string root = _ioFileSystem.Directory.GetCurrentDirectory();
		string workspace = _ioFileSystem.Path.Combine(root, "ws");
		_ioFileSystem.Directory.CreateDirectory(_ioFileSystem.Path.Combine(workspace, ".clio"));
		_ioFileSystem.File.WriteAllText(
			_ioFileSystem.Path.Combine(workspace, ".clio", "workspaceSettings.json"), "{}");
		string nested = _ioFileSystem.Path.Combine(workspace, "packages", "MyPkg");
		_ioFileSystem.Directory.CreateDirectory(nested);
		_ioFileSystem.Directory.SetCurrentDirectory(nested);
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return { entitySchemaName: \"UsrTest\" }; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		string expected = Path.Combine(workspace, ".clio-migration", "UsrTestPage", "manifest.json");
		response.ManifestPath.Should().Be(expected,
			because: "a cwd inside a workspace anchors the manifest at the workspace root, not the nested directory");
	}

	[Test]
	[Description("TryAssemblePageSources absolutizes an explicit relative output-file so the response reports where the file actually lands.")]
	public void TryAssemblePageSources_ShouldAbsolutizeExplicitOutputFile() {
		// Arrange
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return { entitySchemaName: \"UsrTest\" }; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		// Run from a trusted directory under the OS temp root so a relative output-file resolves inside an
		// allowed (confined) zone — a filesystem-root cwd is no longer trusted as a write boundary (RC-25).
		string workingDir = _ioFileSystem.Path.Combine(_ioFileSystem.Path.GetTempPath(), "gcps-cwd");
		_ioFileSystem.Directory.CreateDirectory(workingDir);
		_ioFileSystem.Directory.SetCurrentDirectory(workingDir);
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrTestPage", OutputFile = "./sources.json" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.ManifestPath.Should().Be(_ioFileSystem.Path.GetFullPath("./sources.json"),
			because: "an explicit relative path is resolved to the absolute location it is written to");
		Path.IsPathRooted(response.ManifestPath).Should().BeTrue(
			because: "the reported path must be absolute regardless of how the caller expressed it");
	}

	[Test]
	[Description("IsPathConfined accepts a path inside the workspace anchor.")]
	public void IsPathConfined_ShouldAccept_PathInsideWorkspaceAnchor() {
		// Arrange — a real absolute base so the check is deterministic and cross-platform
		string workspace = Path.Combine(Path.GetTempPath(), "gcmb-ws");
		string tempRoot = Path.Combine(Path.GetTempPath(), "gcmb-other-temp");
		string candidate = Path.GetFullPath(Path.Combine(workspace, "sub", "manifest.json"));

		// Act
		bool confined = OutputPathConfinement.IsPathConfined(candidate, workspace, tempRoot);

		// Assert
		confined.Should().BeTrue(because: "a file under the workspace anchor is an allowed destination");
	}

	[Test]
	[Description("IsPathConfined accepts a path inside the OS temp root even when it is outside the workspace anchor.")]
	public void IsPathConfined_ShouldAccept_PathInsideTempRoot() {
		// Arrange
		string workspace = Path.Combine(Path.GetTempPath(), "gcmb-ws");
		string tempRoot = Path.Combine(Path.GetTempPath(), "gcmb-scratch-root");
		string candidate = Path.GetFullPath(Path.Combine(tempRoot, "run", "manifest.json"));

		// Act
		bool confined = OutputPathConfinement.IsPathConfined(candidate, workspace, tempRoot);

		// Assert
		confined.Should().BeTrue(because: "the OS temp scratch dir is the second allowed destination (skill temp policy)");
	}

	[Test]
	[Description("IsPathConfined rejects a parent-traversal path that escapes both the workspace anchor and the temp root.")]
	public void IsPathConfined_ShouldReject_ParentTraversalEscape() {
		// Arrange
		string workspace = Path.Combine(Path.GetTempPath(), "gcmb-ws");
		string tempRoot = Path.Combine(Path.GetTempPath(), "gcmb-temp");
		string candidate = Path.GetFullPath(Path.Combine(workspace, "..", "..", "escape", "hosts"));

		// Act
		bool confined = OutputPathConfinement.IsPathConfined(candidate, workspace, tempRoot);

		// Assert
		confined.Should().BeFalse(because: "a `..` escape out of both allowed zones must be rejected before any write");
	}

	[Test]
	[Description("IsPathConfined rejects an absolute path that lies under neither allowed zone.")]
	public void IsPathConfined_ShouldReject_UnrelatedAbsolutePath() {
		// Arrange — three siblings under temp: none contains another
		string workspace = Path.Combine(Path.GetTempPath(), "gcmb-ws");
		string tempRoot = Path.Combine(Path.GetTempPath(), "gcmb-temp");
		string candidate = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "gcmb-elsewhere", "manifest.json"));

		// Act
		bool confined = OutputPathConfinement.IsPathConfined(candidate, workspace, tempRoot);

		// Assert
		confined.Should().BeFalse(because: "a path outside both the workspace anchor and the temp root is out of bounds");
	}

	[Test]
	[Description("TryAssemblePageSources writes an explicit output-file that lands inside the OS temp scratch dir — the location the migration skill targets.")]
	public void TryAssemblePageSources_ShouldAccept_ExplicitOutputFile_UnderOsTemp() {
		// Arrange — anchor the cwd under temp, and point output-file at a sibling scratch dir under temp
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string workspace = _ioFileSystem.Path.Combine(tempRoot, "gcmb-ws");
		_ioFileSystem.Directory.CreateDirectory(workspace);
		_ioFileSystem.Directory.SetCurrentDirectory(workspace);
		string scratch = _ioFileSystem.Path.Combine(tempRoot, "gcmb-scratch", "manifest.json");
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return { entitySchemaName: \"UsrTest\" }; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrTestPage", OutputFile = scratch };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeTrue(because: "an output-file inside the OS temp scratch dir is an allowed destination");
		response.ManifestPath.Should().Be(_ioFileSystem.Path.GetFullPath(scratch),
			because: "the confined explicit path is honored as the manifest location");
		_ioFileSystem.File.Exists(_ioFileSystem.Path.GetFullPath(scratch)).Should().BeTrue(
			because: "an explicit output-file is written atomically (WriteAtomic) to the confined path");
	}

	[Test]
	[Description("TryAssemblePageSources refuses to overwrite an explicit output-file that already exists (WriteAtomic FileMode.CreateNew), keeping the Destructive=false contract honest against a target planted after Resolve.")]
	public void TryAssemblePageSources_ShouldRefuse_ExistingExplicitOutputFile() {
		// Arrange — an allowed (temp) explicit output-file that already exists on disk
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string workspace = _ioFileSystem.Path.Combine(tempRoot, "gcmb-ws-exist");
		_ioFileSystem.Directory.CreateDirectory(workspace);
		_ioFileSystem.Directory.SetCurrentDirectory(workspace);
		string scratch = _ioFileSystem.Path.Combine(tempRoot, "gcmb-exist", "manifest.json");
		_ioFileSystem.Directory.CreateDirectory(_ioFileSystem.Path.GetDirectoryName(scratch));
		_ioFileSystem.File.WriteAllText(scratch, "old manifest");
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return { entitySchemaName: \"UsrTest\" }; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrTestPage", OutputFile = scratch };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeFalse(because: "an existing explicit output-file must not be silently overwritten");
		response.Error.Should().Contain("already exists",
			because: "the caller is told why the write was refused");
		_ioFileSystem.File.ReadAllText(_ioFileSystem.Path.GetFullPath(scratch)).Should().Be("old manifest",
			because: "the pre-existing file is left untouched when the atomic write is refused");
	}

	[Test]
	[Description("TryAssemblePageSources rejects an explicit output-file that escapes both the workspace and the OS temp dir, failing before any write instead of overwriting an arbitrary file.")]
	public void TryAssemblePageSources_ShouldReject_ExplicitOutputFile_OutsideAllowedZones() {
		// Arrange — cwd under temp so the anchor is known; output-file traverses out of temp to a sibling
		string tempRoot = _ioFileSystem.Path.GetFullPath(_ioFileSystem.Path.GetTempPath());
		string workspace = _ioFileSystem.Path.Combine(tempRoot, "gcmb-ws");
		_ioFileSystem.Directory.CreateDirectory(workspace);
		_ioFileSystem.Directory.SetCurrentDirectory(workspace);
		string escape = _ioFileSystem.Path.Combine(tempRoot, "..", "gcmb-escape", "manifest.json");
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return { entitySchemaName: \"UsrTest\" }; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrTestPage", OutputFile = escape };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeFalse(because: "an output-file escaping both allowed zones must not be written");
		response.Error.Should().Contain("output-file",
			because: "the failure must name the offending option so the caller can correct it");
		_ioFileSystem.File.Exists(_ioFileSystem.Path.GetFullPath(escape)).Should().BeFalse(
			because: "no file may be written when the path is rejected");
	}

	[Test]
	[Description("TryAssemblePageSources rejects a schema name that is not a valid identifier before any network call, keeping the default path confined to the anchor.")]
	public void TryAssemblePageSources_ShouldRejectInvalidSchemaName_BeforeAnyRequest() {
		// Arrange — a traversal-shaped name that must never become a path segment
		GetClassicPageSourcesOptions options = new() { SchemaName = "..\\..\\evil" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeFalse(because: "an invalid schema name cannot have its sources collected");
		response.Error.Should().Be(PageSchemaMetadataHelper.SchemaNameFormatError,
			because: "the canonical format error tells the caller what a valid name looks like");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
		NoDefaultManifestWritten.Should().BeTrue(because: "nothing is written for a rejected name");
	}

	[Test]
	[Description("TryAssemblePageSources returns a not-found error and writes nothing when the schema has no layers.")]
	public void TryAssemblePageSources_ShouldReturnNotFound_WhenSchemaHasNoLayers() {
		// Arrange — no layers registered for the requested name
		GetClassicPageSourcesOptions options = new() { SchemaName = "MissingPage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeFalse(because: "an unresolvable schema cannot have its sources collected");
		response.Error.Should().Contain("not found", because: "the caller needs a clear not-found message");
		NoDefaultManifestWritten.Should().BeTrue(because: "no manifest is written when the schema is missing");
	}

	[Test]
	[Description("TryAssemblePageSources aborts with a layer-specific error (and writes nothing) when a mid-chain layer body fails to load.")]
	public void TryAssemblePageSources_ShouldFail_WhenChainLayerLoadFails() {
		// Arrange — two enumerated layers, but the top layer has no loadable schema
		AddLayer("UsrTestPage", "uid-base", "BaseApp", 100);
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-base", "define(\"UsrTestPage\", [], function() { return {}; });", EmptyGuid, "BaseApp");
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeFalse(because: "a manifest with a hole in the layer chain would misfold in the engine");
		response.Error.Should().Contain("Failed to load layer", because: "the error names the failing step");
		response.Error.Should().Contain("uid-top", because: "the error identifies the exact layer that failed");
		NoDefaultManifestWritten.Should().BeTrue(because: "no partial manifest may be written on a chain failure");
	}

	[Test]
	[Description("TryAssemblePageSources converts a malformed (non-JSON) server response into a failed response instead of an unhandled exception.")]
	public void TryAssemblePageSources_ShouldFail_WhenServerReturnsMalformedResponse() {
		// Arrange — the server answers every request with an HTML error page
		_applicationClient.ExecutePostRequest(default, default).ReturnsForAnyArgs("<html>login required</html>");
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeFalse(because: "a malformed transport response cannot produce a manifest");
		response.Error.Should().NotBeNullOrWhiteSpace(because: "the parse failure must surface as a readable error");
		NoDefaultManifestWritten.Should().BeTrue(because: "nothing is written when assembly fails");
	}

	[Test]
	[Description("TryAssemblePageSources honors an explicit --entity option over body inference and feeds it to the column manager.")]
	public void TryAssemblePageSources_ShouldHonorExplicitEntity_OverBodyInference() {
		// Arrange — the body names a DIFFERENT entity than the explicit option
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return { entitySchemaName: \"UsrOther\" }; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrTestPage", Entity = "UsrOverride" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.Entity.Should().Be("UsrOverride", because: "an explicit --entity wins over regex inference");
		_columnManager.Received(1).GetSchemaProperties(
			Arg.Is<GetEntitySchemaPropertiesOptions>(o => o.SchemaName == "UsrOverride"));
	}

	[Test]
	[Description("TryAssemblePageSources succeeds without an entity: entity, columns, and section are omitted, and the column manager is not called.")]
	public void TryAssemblePageSources_ShouldOmitEntityBlocks_WhenNoEntityResolvable() {
		// Arrange — a body with no entitySchemaName and no explicit --entity
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return {}; });", EmptyGuid, "UsrApp");
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeTrue(because: "a page without a resolvable entity still collects its layer chain");
		JObject manifest = JObject.Parse(ReadManifest(response));
		manifest["entity"].Should().BeNull(because: "an unknown entity is omitted, never fabricated");
		manifest["entityColumns"].Should().BeNull(because: "no entity means no columns block");
		manifest["section"].Should().BeNull(because: "no entity means no section naming convention to probe");
		_columnManager.DidNotReceiveWithAnyArgs().GetSchemaProperties(default);
	}

	[Test]
	[Description("TryAssemblePageSources keeps the collection successful (with empty columns) when the column manager throws, and logs the degradation.")]
	public void TryAssemblePageSources_ShouldDegradeGracefully_WhenColumnManagerThrows() {
		// Arrange
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return { entitySchemaName: \"UsrTest\" }; });", EmptyGuid, "UsrApp");
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(_ => throw new InvalidOperationException("designer unavailable"));
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeTrue(because: "entity columns are a best-effort enricher, not a bundling precondition");
		JObject manifest = JObject.Parse(ReadManifest(response));
		manifest["entityColumns"].Should().BeNull(because: "the failed enricher is omitted, never fabricated");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("UsrTest")));
		response.Warnings.Should().Contain(w => w.Contains("entity columns"),
			because: "the omitted entityColumns section must be visible to an MCP caller via response.Warnings");
	}

	[Test]
	[Description("TryAssemblePageSources gathers detailSchemas, the section chain, and the child edit page as a nested manifest when they resolve.")]
	public void TryAssemblePageSources_ShouldGatherEnrichers_WhenResolvable() {
		// Arrange — page references a detail; detail names a child entity + edit page; section + child page resolve
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\", details: { D: { schemaName: \"UsrNoteDetail\" } } }; });",
			EmptyGuid, "UsrApp");
		AddLayer("UsrNoteDetail", "uid-detail", "UsrApp", 200);
		AddSchema("uid-detail",
			"define(\"UsrNoteDetail\", [], function() { return { entitySchemaName: \"UsrNote\", getEditPageName: function() { return \"UsrNotePage\"; } }; });",
			EmptyGuid, "UsrApp", caption: "Notes");
		AddLayer("UsrCaseSectionV2", "uid-section", "UsrApp", 200);
		AddSchema("uid-section", "define(\"UsrCaseSectionV2\", [], function() { return {}; });", EmptyGuid, "UsrApp");
		AddLayer("UsrNotePage", "uid-child", "UsrApp", 200);
		AddSchema("uid-child", "define(\"UsrNotePage\", [], function() { return { entitySchemaName: \"UsrNote\" }; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.DetailCount.Should().Be(1, because: "the referenced detail schema resolves");
		response.SectionLayerCount.Should().Be(1, because: "the <Entity>SectionV2 convention resolves one layer");
		response.ChildPageCount.Should().Be(1, because: "the detail's edit page resolves to a nested manifest");
		JObject manifest = JObject.Parse(ReadManifest(response));
		manifest["detailSchemas"]!["UsrNoteDetail"]!["title"]!.ToString().Should().Be("Notes",
			because: "the detail's caption becomes its title");
		manifest["detailSchemas"]!["UsrNoteDetail"]!["body"]!.ToString().Should().Contain("UsrNote",
			because: "the detail body is fetched into the manifest");
		((JArray)manifest["section"]).Should().ContainSingle(because: "one section layer resolves")
			.Which["pkg"]!.ToString().Should().Be("UsrApp", because: "the section layer body is gathered");
		manifest["childPageSchemas"]!["UsrNotePage"]!["schemas"].Should().NotBeNull(
			because: "the child edit page is nested as its own manifest keyed by the edit-page name");
	}

	[Test]
	[Description("TryAssemblePageSources resolves every enricher name (details + section candidates) through ONE batched SelectQuery instead of one round-trip per name.")]
	public void TryAssemblePageSources_ShouldBatchEnricherEnumeration_InSingleSelectQuery() {
		// Arrange — same enricher topology as the gather test
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\", details: { D: { schemaName: \"UsrNoteDetail\" } } }; });",
			EmptyGuid, "UsrApp");
		AddLayer("UsrNoteDetail", "uid-detail", "UsrApp", 200);
		AddSchema("uid-detail", "define(\"UsrNoteDetail\", [], function() { return {}; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssemblePageSources(options, out _);

		// Assert — one request carries the detail name AND both section candidates together
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("UsrNoteDetail")
				&& body.Contains("UsrCaseSectionV2")
				&& body.Contains("UsrCaseSection")));
	}

	[Test]
	[Description("TryAssemblePageSources loads a schema body only once per run: a child page sharing the main page's parent template reuses the cached layer.")]
	public void TryAssemblePageSources_ShouldMemoizeSchemaLoads_AcrossMainAndChildSeeds() {
		// Arrange — page and its detail's child edit page inherit the SAME template layer uid-tpl
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\", details: { D: { schemaName: \"UsrNoteDetail\" } } }; });",
			"uid-tpl", "UsrApp");
		AddLayer("UsrNoteDetail", "uid-detail", "UsrApp", 200);
		AddSchema("uid-detail",
			"define(\"UsrNoteDetail\", [], function() { return { getEditPageName: function() { return \"UsrNotePage\"; } }; });",
			EmptyGuid, "UsrApp");
		AddLayer("UsrNotePage", "uid-child", "UsrApp", 200);
		AddSchema("uid-child", "define(\"UsrNotePage\", [], function() { return {}; });", "uid-tpl", "UsrApp");
		AddLayer("BaseTpl", "uid-tpl", "Core", 100);
		AddSchema("uid-tpl", "define(\"BaseTpl\", [], function() { return {}; });", EmptyGuid, "Core", name: "BaseTpl");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert — both seeds carry the template, but its body traveled the wire once
		response.ChildPageCount.Should().Be(1, because: "the child page assembles from the same run");
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"uid-tpl\"")));
	}

	[Test]
	[Description("TryAssemblePageSources omits an enricher it cannot resolve rather than fabricating it.")]
	public void TryAssemblePageSources_ShouldOmitUnresolvedDetail_WhenDetailSchemaMissing() {
		// Arrange — page references a detail that has no layers registered
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\", details: { D: { schemaName: \"UsrGhostDetail\" } } }; });",
			EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.DetailCount.Should().Be(0, because: "an unresolved detail is omitted, not fabricated");
		JObject manifest = JObject.Parse(ReadManifest(response));
		manifest["detailSchemas"].Should().BeNull(because: "no detail resolved, so the field is absent");
	}

	[Test]
	[Description("TryAssemblePageSources falls back to the <Entity>Section naming convention when no <Entity>SectionV2 schema exists.")]
	public void TryAssemblePageSources_ShouldGatherSection_ViaNonV2NamingFallback() {
		// Arrange — only the non-V2 section name resolves
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\" }; });", EmptyGuid, "UsrApp");
		AddLayer("UsrCaseSection", "uid-section", "UsrApp", 200);
		AddSchema("uid-section", "define(\"UsrCaseSection\", [], function() { return {}; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.SectionLayerCount.Should().Be(1, because: "the non-V2 naming fallback resolves the section");
	}

	[Test]
	[Description("TryAssemblePageSources resolves a section named after the page prefix (UsrApplicant1Page -> UsrApplicant1Section) when no <Entity>Section[V2] schema exists, so a section cloned/renamed off the page is not silently dropped.")]
	public void TryAssemblePageSources_ShouldGatherSection_ViaPagePrefixNaming_WhenEntitySectionDoesNotExist() {
		// Arrange — the page prefix (UsrApplicant1) differs from the bare entity (UsrApplicant); only the
		// page-prefixed section schema exists, so the <Entity>Section[V2] candidates cannot resolve it.
		AddLayer("UsrApplicant1Page", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrApplicant1Page\", [], function() { return { entitySchemaName: \"UsrApplicant\" }; });", EmptyGuid, "UsrApp");
		AddLayer("UsrApplicant1Section", "uid-section", "UsrApp", 200);
		AddSchema("uid-section", "define(\"UsrApplicant1Section\", [], function() { return {}; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrApplicant1Page" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.SectionLayerCount.Should().Be(1,
			because: "the section named off the page prefix resolves even though no <Entity>Section[V2] schema exists");
		JObject manifest = JObject.Parse(ReadManifest(response));
		((JArray)manifest["section"]).Should().ContainSingle(because: "the page-prefixed section layer is gathered")
			.Which["body"]!.ToString().Should().Contain("UsrApplicant1Section",
				because: "the resolved section is the page-prefixed schema, not a bare-entity section");
	}

	[Test]
	[Description("TryAssemblePageSources prefers the page-prefixed section (UsrApplicant1Section) over the bare-entity section (UsrApplicantSection) when both exist, so a cloned page maps to its own section rather than the base one.")]
	public void TryAssemblePageSources_ShouldPreferPagePrefixSection_OverEntitySection_WhenBothExist() {
		// Arrange — both the page-prefixed section and the bare-entity section exist; the page-prefixed one must win.
		AddLayer("UsrApplicant1Page", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrApplicant1Page\", [], function() { return { entitySchemaName: \"UsrApplicant\" }; });", EmptyGuid, "UsrApp");
		AddLayer("UsrApplicant1Section", "uid-page-section", "PagePkg", 200);
		AddSchema("uid-page-section", "define(\"UsrApplicant1Section\", [], function() { return {}; });", EmptyGuid, "PagePkg");
		AddLayer("UsrApplicantSection", "uid-entity-section", "EntityPkg", 200);
		AddSchema("uid-entity-section", "define(\"UsrApplicantSection\", [], function() { return {}; });", EmptyGuid, "EntityPkg");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrApplicant1Page" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.SectionLayerCount.Should().Be(1, because: "the first section candidate that resolves wins, and only one chain is emitted");
		JObject manifest = JObject.Parse(ReadManifest(response));
		((JArray)manifest["section"])[0]["pkg"]!.ToString().Should().Be("PagePkg",
			because: "the page-prefixed section (UsrApplicant1Section) takes precedence over the bare-entity section (UsrApplicantSection)");
	}

	[Test]
	[Description("TryAssemblePageSources resolves a section whose schema name carries a UId/app infix (ASPContractDatac145c7efSection) from SysModule metadata, which no name derivation off the entity or page can reach.")]
	public void TryAssemblePageSources_ShouldGatherSection_ViaMetadata_WhenNameCarriesUIdInfix() {
		// Arrange — the real section name (…c145c7efSection) is not derivable from the entity (ASPContractData)
		// nor from the page prefix (ASPContractData1); only the SysModule binding reaches it.
		AddLayer("ASPContractData1Page", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"ASPContractData1Page\", [], function() { return { entitySchemaName: \"ASPContractData\" }; });",
			EmptyGuid, "UsrApp");
		AddLayer("ASPContractDatac145c7efSection", "uid-section", "SectionPkg", 200);
		AddSchema("uid-section",
			"define(\"ASPContractDatac145c7efSection\", [], function() { return {}; });", EmptyGuid, "SectionPkg");
		_sectionResolver.ResolveSectionSchemaNames("ASPContractData")
			.Returns(new ClassicSectionLookup(new[] { "ASPContractDatac145c7efSection" }, null));
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "ASPContractData1Page" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.SectionLayerCount.Should().Be(1,
			because: "the SysModule binding resolves a section name that no naming convention can derive");
		response.Warnings.Should().BeNull(because: "a resolved section leaves nothing for the caller to weigh");
		JObject manifest = JObject.Parse(ReadManifest(response));
		((JArray)manifest["section"]).Should().ContainSingle(because: "the metadata-resolved section layer is gathered")
			.Which["pkg"]!.ToString().Should().Be("SectionPkg",
				because: "the gathered chain is the section the metadata pointed at");
	}

	[Test]
	[Description("TryAssemblePageSources prefers the metadata-bound section over a name-derived one when both resolve, so a renamed section wins over a same-named leftover.")]
	public void TryAssemblePageSources_ShouldPreferMetadataSection_OverNameDerivedSection_WhenBothExist() {
		// Arrange — both the metadata-bound section and the <Entity>Section convention resolve; metadata must win.
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\" }; });", EmptyGuid, "UsrApp");
		AddLayer("UsrCaseRenamedSection", "uid-meta-section", "MetaPkg", 200);
		AddSchema("uid-meta-section", "define(\"UsrCaseRenamedSection\", [], function() { return {}; });", EmptyGuid, "MetaPkg");
		AddLayer("UsrCaseSection", "uid-name-section", "NamePkg", 200);
		AddSchema("uid-name-section", "define(\"UsrCaseSection\", [], function() { return {}; });", EmptyGuid, "NamePkg");
		_sectionResolver.ResolveSectionSchemaNames("UsrCase")
			.Returns(new ClassicSectionLookup(new[] { "UsrCaseRenamedSection" }, null));
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.SectionLayerCount.Should().Be(1, because: "the first candidate that resolves wins and only one chain is emitted");
		JObject manifest = JObject.Parse(ReadManifest(response));
		((JArray)manifest["section"])[0]["pkg"]!.ToString().Should().Be("MetaPkg",
			because: "the SysModule binding is authoritative and outranks the name convention");
	}

	[Test]
	[Description("TryAssemblePageSources degrades to the name conventions and warns in the response when the SysModule metadata lookup fails, instead of losing the section silently.")]
	public void TryAssemblePageSources_ShouldFallBackToNaming_AndWarn_WhenMetadataLookupFails() {
		// Arrange — the metadata lookup errors out, but the <Entity>Section convention still resolves.
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\" }; });", EmptyGuid, "UsrApp");
		AddLayer("UsrCaseSection", "uid-section", "NamePkg", 200);
		AddSchema("uid-section", "define(\"UsrCaseSection\", [], function() { return {}; });", EmptyGuid, "NamePkg");
		_sectionResolver.ResolveSectionSchemaNames("UsrCase")
			.Returns(new ClassicSectionLookup(Array.Empty<string>(), "DataService call failed"));
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.SectionLayerCount.Should().Be(1, because: "the name convention still resolves the section after the metadata failure");
		response.Warnings.Should().ContainSingle(because: "the degraded lookup must be visible to the caller")
			.Which.Should().Contain("DataService call failed",
				because: "the warning carries the underlying reason the metadata path was skipped");
	}

	[Test]
	[Description("TryAssemblePageSources warns in the response when no section resolves at all, so sectionLayerCount:0 is not mistaken for 'this entity has no section'.")]
	public void TryAssemblePageSources_ShouldWarn_WhenNoSectionResolves() {
		// Arrange — neither metadata nor any naming convention resolves a section.
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\" }; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeTrue(because: "a missing section is an enricher gap, not a collection failure");
		response.SectionLayerCount.Should().Be(0, because: "no section candidate resolved");
		response.Warnings.Should().ContainSingle(because: "the empty section must be surfaced, not left silent")
			.Which.Should().Contain("UsrCase",
				because: "the warning names the entity whose section could not be found");
	}

	[Test]
	[Description("SafeMatch appends a caller-visible warning on a regex timeout, so a skipped body is not reported as an empty page.")]
	public void SafeMatch_ShouldAppendWarning_WhenRegexTimesOut() {
		// Arrange — catastrophic backtracking against a 1-tick budget makes the timeout deterministic.
		var warnings = new List<string>();
		System.Text.RegularExpressions.Regex slow = new("(a+)+$",
			System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromTicks(1));

		// Act
		System.Text.RegularExpressions.Match match =
			_command.SafeMatch(warnings, slow, new string('a', 40) + "b", "collecting detail-schema references");

		// Assert
		match.Success.Should().BeFalse(because: "a timed-out match degrades to no match rather than throwing");
		warnings.Should().ContainSingle(because: "the caller must learn the body was skipped")
			.Which.Should().Contain("collecting detail-schema references",
				because: "the warning names the operation that degraded");
		warnings[0].Should().Contain("may be incomplete",
			because: "a lower count must not be read as 'the page has nothing to migrate'");
	}

	[Test]
	[Description("SafeMatches appends the same warning on a regex timeout and dedupes it, so one pathological page does not flood the response with identical entries.")]
	public void SafeMatches_ShouldAppendWarningOnce_WhenSeveralBodiesTimeOut() {
		// Arrange
		var warnings = new List<string>();
		System.Text.RegularExpressions.Regex slow = new("(a+)+$",
			System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromTicks(1));
		string pathological = new string('a', 40) + "b";

		// Act — the same guard trips on two separate bodies
		IReadOnlyList<System.Text.RegularExpressions.Match> first =
			_command.SafeMatches(warnings, slow, pathological, "collecting detail-schema references");
		_command.SafeMatches(warnings, slow, pathological, "collecting detail-schema references");

		// Assert
		first.Should().BeEmpty(because: "a timed-out enumeration degrades to no matches");
		warnings.Should().ContainSingle(
			because: "repeated timeouts on the same operation must collapse into one caller-visible warning");
	}

	[Test]
	[Description("A real regex timeout during TryAssemblePageSources surfaces in the response's Warnings, proving the degradation is wired end-to-end through the public path, not only in SafeMatch called directly.")]
	public void TryAssemblePageSources_ShouldWarn_WhenEntityInferenceRegexTimesOut() {
		// Arrange — point entity inference at a 1-tick catastrophic-backtracking pattern, and give the page a
		// pathological body so the timeout fires inside the real InferEntity -> SafeMatch call. No explicit entity,
		// so inference actually runs over the body.
		_command.EntityInferenceRegex = new System.Text.RegularExpressions.Regex("(a+)+$",
			System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromTicks(1));
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-top", new string('a', 40) + "b", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeTrue(because: "a regex timeout degrades, never fails the whole assembly");
		response.Warnings.Should().NotBeNull(
			because: "the timeout must surface to an MCP caller who never sees the logger");
		response.Warnings.Should().Contain(w => w.Contains("inferring the bound entity"),
			because: "the surfaced warning names the operation that degraded, through the real TryAssemblePageSources path");
	}

	[Test]
	[Description("TryAssemblePageSources seeds EVERY layer of a multi-package parent template (base->top), not just the single parent.uId layer, so base containers in sibling layers are not dropped.")]
	public void TryAssemblePageSources_ShouldSeedAllParentTemplateLayers_WhenParentIsMultiLayer() {
		// Arrange — a single-layer page whose parent template "BaseTpl" is replaced across TWO packages;
		// the page's parent.uId links only the top template layer.
		AddLayer("UsrPage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page", "define(\"UsrPage\", [], function() { return { entitySchemaName: \"UsrX\" }; });",
			"uid-tpl-top", "UsrApp");
		AddLayer("BaseTpl", "uid-tpl-base", "Core", 100);
		AddLayer("BaseTpl", "uid-tpl-top", "CrtUI", 150);
		AddSchema("uid-tpl-base", "define(\"BaseTpl\", [], function() { return { baseContainer: true }; });",
			EmptyGuid, "Core", name: "BaseTpl");
		AddSchema("uid-tpl-top", "define(\"BaseTpl\", [], function() { return {}; });", EmptyGuid, "CrtUI", name: "BaseTpl");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrPage" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.SeedCount.Should().Be(2,
			because: "both layers of the multi-package parent template must be seeded, not only the parent.uId layer");
		JObject manifest = JObject.Parse(ReadManifest(response));
		var seed = (JArray)manifest["seed"];
		seed.Should().HaveCount(2, because: "the seed carries the whole template layer set for the level");
		seed[0]["pkg"]!.ToString().Should().Be("Core",
			because: "the lower-hierarchy parent-template layer sorts first (base->top)");
		seed[1]["pkg"]!.ToString().Should().Be("CrtUI",
			because: "the higher-hierarchy parent-template layer sorts last");
		seed[0]["body"]!.ToString().Should().Contain("baseContainer",
			because: "the base sibling layer body — dropped by a single-parent walk — is now seeded");
	}

	[Test]
	[Description("TryAssemblePageSources never seeds the same layer body twice when the parent walk revisits a template it already enumerated (parent link into a replaced sibling).")]
	public void TryAssemblePageSources_ShouldNotDuplicateSeedLayer_WhenParentWalkRevisitsTemplate() {
		// Arrange — the top template layer's own parent link points at its replaced base sibling
		AddLayer("UsrPage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page", "define(\"UsrPage\", [], function() { return { entitySchemaName: \"UsrX\" }; });",
			"uid-tpl-top", "UsrApp");
		AddLayer("BaseTpl", "uid-tpl-base", "Core", 100);
		AddLayer("BaseTpl", "uid-tpl-top", "CrtUI", 150);
		AddSchema("uid-tpl-base", "define(\"BaseTpl\", [], function() { return { baseContainer: true }; });",
			EmptyGuid, "Core", name: "BaseTpl");
		// The linked top layer's parent is the SAME template's base layer (replacing-schema link shape).
		AddSchema("uid-tpl-top", "define(\"BaseTpl\", [], function() { return {}; });", "uid-tpl-base", "CrtUI", name: "BaseTpl");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrPage" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert — the base layer appears once, not once per walk visit
		response.SeedCount.Should().Be(2,
			because: "revisiting the template through the parent link must not append the already-seeded base layer again");
	}

	[Test]
	[Description("TryAssemblePageSources omits the seed entry's pkg when the parent layer carries no package name, instead of fabricating one from the schema name.")]
	public void TryAssemblePageSources_ShouldOmitSeedPkg_WhenParentPackageUnknown() {
		// Arrange — a parent layer whose GetSchema response has no package block (and no name -> single-layer fallback)
		AddLayer("UsrPage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page", "define(\"UsrPage\", [], function() { return { entitySchemaName: \"UsrX\" }; });",
			"uid-parent", "UsrApp");
		AddSchema("uid-parent", "define(\"BaseTpl\", [], function() { return {}; });", EmptyGuid, package: null);
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrPage" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.SeedCount.Should().Be(1, because: "the parent body itself is still seeded");
		JObject manifest = JObject.Parse(ReadManifest(response));
		JToken entry = ((JArray)manifest["seed"])[0];
		entry["pkg"].Should().BeNull(
			because: "pkg is package provenance — when unknown it is omitted, never substituted with the schema name");
		entry["body"]!.ToString().Should().Contain("BaseTpl", because: "the body is still carried");
	}

	[Test]
	[Description("TryAssemblePageSources infers the page's own entitySchemaName and ignores a longer identifier like masterEntitySchemaName.")]
	public void TryAssemblePageSources_ShouldInferPageEntity_WhenBodyContainsMasterEntitySchemaNameSubstring() {
		// Arrange — a masterEntitySchemaName appears BEFORE the page's own entitySchemaName in the body
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { masterEntitySchemaName: \"UsrWrong\", entitySchemaName: \"UsrCase\" }; });",
			EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.Entity.Should().Be("UsrCase",
			because: "the word-boundary anchor skips 'masterEntitySchemaName' and binds the page's own entitySchemaName");
	}

	[Test]
	[Description("TryAssemblePageSources does not misclassify an entity reference whose name merely ends in 'Detail' (entitySchemaName: \"XDetail\") as a detail schema.")]
	public void TryAssemblePageSources_ShouldNotTreatDetailNamedEntityReference_AsDetailSchema() {
		// Arrange — the only 'Detail' substring sits inside entitySchemaName, not a schemaName reference;
		// a same-named client-unit schema exists, so a false positive WOULD resolve and pollute detailSchemas.
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCaseDetail\" }; });",
			EmptyGuid, "UsrApp");
		AddLayer("UsrCaseDetail", "uid-lookalike", "UsrApp", 200);
		AddSchema("uid-lookalike", "define(\"UsrCaseDetail\", [], function() { return {}; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.DetailCount.Should().Be(0,
			because: "an entity reference is not a detail declaration — the heuristic must not fabricate a detail from it");
	}

	[Test]
	[Description("TryAssemblePageSources surfaces a DataService errorInfo-only failure (no success:false) as the collection error instead of a misleading not-found.")]
	public void TryAssemblePageSources_ShouldSurfaceDataServiceFailure_WhenSelectQueryReturnsErrorInfoOnly() {
		// Arrange - the layer-enumeration SelectQuery answers with an errorInfo object and NO success:false
		// (the restricted-SysSchema shape). The shared detector must classify it as a failure so the collection
		// reports the real reason, not "not found" from a silently empty row set.
		_applicationClient.ExecutePostRequest(default, default).ReturnsForAnyArgs(
			"""{ "errorInfo": { "errorCode": "AccessDenied", "message": "Access to SysSchema is denied" } }""");
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeFalse(because: "a DataService failure envelope cannot produce a manifest");
		response.Error.Should().Contain("Access to SysSchema is denied",
			because: "the real DataService reason must surface, not a masked empty-result not-found");
		response.Error.Should().NotContain("not found",
			because: "an access failure must not be reported as a missing schema");
		NoDefaultManifestWritten.Should().BeTrue(because: "no manifest is written when enumeration fails");
	}

	[Test]
	[Description("TryAssemblePageSources caps detailSchemas at MaxDetails (50) and warns when the page body resolves more than fifty distinct details.")]
	public void TryAssemblePageSources_ShouldCapDetailSchemasAtMaxDetails_WhenMoreThanFiftyDetailsResolve() {
		// Arrange — 55 distinct, individually resolvable detail references on one page (55 < collectionCap 100)
		const int detailReferenceCount = 55;
		var detailRefs = new List<string>();
		for (int i = 0; i < detailReferenceCount; i++) {
			string detail = "UsrDetail" + i;
			detailRefs.Add("schemaName: \"" + detail + "\"");
			AddLayer(detail, "uid-" + detail, "UsrApp", 200);
			AddSchema("uid-" + detail, "define(\"" + detail + "\", [], function() { return {}; });", EmptyGuid, "UsrApp");
		}
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\", details: { " +
			string.Join(", ", detailRefs) + " } }; });",
			EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.DetailCount.Should().Be(50,
			because: "detail gathering is capped at MaxDetails (50) resolved schemas even when more resolve");
		JObject manifest = JObject.Parse(ReadManifest(response));
		((JObject)manifest["detailSchemas"]).Count.Should().Be(50,
			because: "only the first fifty resolvable details are folded into the manifest");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("Detail gathering stopped at 50")));
		response.Warnings.Should().Contain(w => w.Contains("Detail gathering stopped at 50"),
			because: "a logger warning does not reach an MCP caller, so the truncation must also surface in response.Warnings");
	}

	[Test]
	[Description("TryAssemblePageSources stops collecting detail-schema references at collectionCap (100) and warns when a body names more references than the cap.")]
	public void TryAssemblePageSources_ShouldStopDetailNameCollection_WhenMoreThanCollectionCapReferences() {
		// Arrange — 105 distinct references, over collectionCap (MaxDetails * 2 = 100); none need to resolve
		const int detailReferenceCount = 105;
		var detailRefs = new List<string>();
		for (int i = 0; i < detailReferenceCount; i++) {
			detailRefs.Add("schemaName: \"UsrDetail" + i + "\"");
		}
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\", details: { " +
			string.Join(", ", detailRefs) + " } }; });",
			EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeTrue(because: "an over-cap reference list truncates collection, it does not fail the collection");
		response.DetailCount.Should().Be(0,
			because: "none of the referenced details were registered, so none resolve into the manifest");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("More than 100 distinct detail-schema references")));
	}

	[Test]
	[Description("TryAssemblePageSources bounds childPageSchemas at fifty because child pages come only from the (MaxDetails-capped) detail set, and the detail cap warning is emitted.")]
	public void TryAssemblePageSources_ShouldCapChildPages_WhenManyDetailsEachReferenceAnEditPage() {
		// Arrange — 55 distinct details, each naming its own resolvable child edit page
		const int detailReferenceCount = 55;
		var detailRefs = new List<string>();
		for (int i = 0; i < detailReferenceCount; i++) {
			string detail = "UsrDetail" + i;
			string childPage = "UsrChildPage" + i;
			detailRefs.Add("schemaName: \"" + detail + "\"");
			AddLayer(detail, "uid-" + detail, "UsrApp", 200);
			AddSchema("uid-" + detail,
				"define(\"" + detail + "\", [], function() { return { getEditPageName: function() { return \"" + childPage + "\"; } }; });",
				EmptyGuid, "UsrApp");
			AddLayer(childPage, "uid-" + childPage, "UsrApp", 200);
			AddSchema("uid-" + childPage, "define(\"" + childPage + "\", [], function() { return {}; });", EmptyGuid, "UsrApp");
		}
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\", details: { " +
			string.Join(", ", detailRefs) + " } }; });",
			EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		response.ChildPageCount.Should().Be(50,
			because: "child pages come only from the fifty resolved details, so the set is bounded at MaxChildPages (50)");
		JObject manifest = JObject.Parse(ReadManifest(response));
		((JObject)manifest["childPageSchemas"]).Count.Should().Be(50,
			because: "exactly fifty child edit pages are nested into the manifest");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("Detail gathering stopped at 50")));
	}

	[Test]
	[Description("TryAssemblePageSources warns that the parent-template walk stopped at the depth cap when the chain is deeper than MaxParentDepth (20) with a parent still to follow.")]
	public void TryAssemblePageSources_ShouldWarnDepthCap_WhenParentWalkExceedsMaxParentDepth() {
		// Arrange — a page whose parent chain is 21 distinct levels deep (uid-p1 -> ... -> uid-p21)
		AddLayer("UsrPage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page", "define(\"UsrPage\", [], function() { return { entitySchemaName: \"UsrX\" }; });",
			"uid-p1", "UsrApp");
		for (int i = 1; i <= 20; i++) {
			AddSchema("uid-p" + i, "define(\"Tpl\", [], function() { return {}; });", "uid-p" + (i + 1), "Core");
		}
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrPage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeTrue(because: "a truncated seed still produces a usable manifest, it does not fail assembly");
		response.SeedCount.Should().Be(20,
			because: "exactly MaxParentDepth (20) parent levels are walked before the cap stops the walk");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("depth cap")));
		response.Warnings.Should().Contain(w => w.Contains("depth cap"),
			because: "the truncated seed must be visible to an MCP caller via response.Warnings, not only the logger");
	}

	[Test]
	[Description("TryAssemblePageSources warns that the parent-template walk stopped on a cycle when the parent chain revisits a UId (page -> A -> B -> A).")]
	public void TryAssemblePageSources_ShouldWarnCycle_WhenParentWalkRevisitsUid() {
		// Arrange — a parent chain that loops back on itself: uid-a -> uid-b -> uid-a
		AddLayer("UsrPage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page", "define(\"UsrPage\", [], function() { return { entitySchemaName: \"UsrX\" }; });",
			"uid-a", "UsrApp");
		AddSchema("uid-a", "define(\"Tpl\", [], function() { return {}; });", "uid-b", "Core");
		AddSchema("uid-b", "define(\"Tpl\", [], function() { return {}; });", "uid-a", "Core");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrPage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeTrue(because: "a cycle truncates the seed but still yields a usable manifest");
		response.SeedCount.Should().Be(2,
			because: "only the two distinct parent layers are seeded before the cycle stops the walk");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("cycle")));
	}

	[Test]
	[Description("TryAssemblePageSources resolves schemas[] (page layers) and seed[] (parent templates) from a single GetParentSchemas hierarchy call, split by name and ordered base->top, instead of the per-layer fan-out.")]
	public void TryAssemblePageSources_ShouldResolveChainAndSeed_ViaGetParentSchemas() {
		// Arrange — only the name->UId metadata row is registered on the fake DataService; the layer bodies come
		// from GetParentSchemas (leaf-first: most-derived page layer, base page layer, parent template). The base
		// page layer's UId equals the metadata UId so no root re-anchor re-fetch is needed.
		AddLayer("UsrPage", "uid-page", "UsrApp", 200);   // metadata resolve target (UId + PackageUId)
		AddSchema("uid-top", "define(\"UsrPage\", [], function() { return {}; });", EmptyGuid, "pkgB"); // BuildResources reads topLayerUId
		// The top page layer's UId (topLayerUId) is the one resolved FROM the hierarchy call, not the legacy
		// enumeration; a localizable string on it proves BuildResources still merges resources off that UId.
		AddLocalizable("uid-top", "HeaderCaption", "Header");
		_hierarchyClient.GetDesignPackageUId(Arg.Any<string>()).Returns("dp-uid");
		_hierarchyClient.GetParentSchemas("uid-page", Arg.Any<string>()).Returns(new List<PageDesignerHierarchySchema> {
			Hier("UsrPage", "pkgB", "uid-top", "define(\"UsrPage\", [], function() { return {}; });"),
			Hier("UsrPage", "pkgA", "uid-page", "define(\"UsrPage\", [], function() { return { entitySchemaName: \"UsrX\" }; });"),
			Hier("BaseTpl", "Core", "uid-tpl", "define(\"BaseTpl\", [], function() { return { baseContainer: true }; });")
		});
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrPage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeTrue(because: "the hierarchy resolves the page chain and seed");
		_hierarchyClient.Received(1).GetParentSchemas("uid-page", Arg.Any<string>());
		response.LayerCount.Should().Be(2, because: "both UsrPage-named layers become the schemas[] chain");
		response.SeedCount.Should().Be(1, because: "the one non-page (parent-template) layer becomes the seed");
		response.Entity.Should().Be("UsrX", because: "the entity is inferred from the split page bodies");
		JObject manifest = JObject.Parse(ReadManifest(response));
		var schemas = (JArray)manifest["schemas"];
		schemas[0]["pkg"]!.ToString().Should().Be("pkgA", because: "schemas[] is ordered base->top (lower hierarchy first)");
		schemas[1]["pkg"]!.ToString().Should().Be("pkgB", because: "the most-derived page layer sorts last");
		var seed = (JArray)manifest["seed"];
		seed.Should().HaveCount(1, because: "only the parent template is seed content");
		seed[0]["pkg"]!.ToString().Should().Be("Core",
			because: "the parent-template layer (never registered as a DataService layer) came from the hierarchy call");
		response.ResourceCount.Should().Be(1,
			because: "BuildResources merges localizable strings off the hierarchy-resolved topLayerUId");
		manifest["resources"]!["HeaderCaption"]!.ToString().Should().Be("Header",
			because: "the top page layer's merged localizable string becomes a resource in the hierarchy path too");
	}

	[Test]
	[Description("TryAssemblePageSources re-anchors on the root schema UId and re-fetches the full hierarchy when the name->UId metadata resolves to a mid-chain layer, using the full re-fetched chain rather than the partial initial fetch.")]
	public void TryAssemblePageSources_ShouldReanchorOnRootAndRefetch_WhenMetadataResolvesToMidChainLayer() {
		// Arrange — the name->UId metadata resolves to a MID-CHAIN layer (uid-mid), not the most-base same-named
		// layer. The initial GetParentSchemas(uid-mid) is a partial leaf-first list whose last UsrPage entry
		// carries a DIFFERENT (root) UId, so FindRootSchemaUId returns uid-root != uid-mid and the else branch
		// re-anchors: GetParentSchemas(uid-root) returns the FULL base->top chain, which must supersede initial.
		AddLayer("UsrPage", "uid-mid", "UsrApp", 300); // only metadata row -> schemaUId resolves to uid-mid
		_hierarchyClient.GetDesignPackageUId(Arg.Any<string>()).Returns("dp-uid");
		_hierarchyClient.GetParentSchemas("uid-mid", Arg.Any<string>()).Returns(new List<PageDesignerHierarchySchema> {
			Hier("UsrPage", "pkgMid", "uid-mid", "define(\"UsrPage\", [], function() { return { entitySchemaName: \"UsrX\" }; });"),
			Hier("UsrPage", "pkgRoot", "uid-root", "define(\"UsrPage\", [], function() { return {}; });")
		}); // leaf-first partial fetch; last UsrPage entry (uid-root) != metadata UId -> else branch
		_hierarchyClient.GetParentSchemas("uid-root", Arg.Any<string>()).Returns(new List<PageDesignerHierarchySchema> {
			Hier("UsrPage", "pkgTop", "uid-top", "define(\"UsrPage\", [], function() { return {}; });"),
			Hier("UsrPage", "pkgMid", "uid-mid", "define(\"UsrPage\", [], function() { return { entitySchemaName: \"UsrX\" }; });"),
			Hier("UsrPage", "pkgRoot", "uid-root", "define(\"UsrPage\", [], function() { return {}; });"),
			Hier("BaseTpl", "Core", "uid-tpl", "define(\"BaseTpl\", [], function() { return { baseContainer: true }; });")
		}); // full leaf-first chain re-fetched from the root anchor
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrPage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeTrue(because: "the re-anchored full hierarchy resolves the page chain and seed");
		_hierarchyClient.Received(1).GetParentSchemas("uid-mid", Arg.Any<string>());
		_hierarchyClient.Received(1).GetParentSchemas("uid-root", Arg.Any<string>());
		response.LayerCount.Should().Be(3,
			because: "the full re-fetched chain (three UsrPage layers) is used, not the two-layer partial initial fetch");
		response.SeedCount.Should().Be(1, because: "the parent template from the full re-fetched chain becomes the seed");
		JObject manifest = JObject.Parse(ReadManifest(response));
		var schemas = (JArray)manifest["schemas"];
		schemas[0]["pkg"]!.ToString().Should().Be("pkgRoot", because: "the full chain is ordered base->top (most-base root layer first)");
		schemas[2]["pkg"]!.ToString().Should().Be("pkgTop", because: "the most-derived layer of the full chain sorts last");
	}

	[Test]
	[Description("TryAssemblePageSources keeps the initial hierarchy fetch when the root re-anchor re-fetch returns empty, so re-anchoring never collapses an already-resolved chain (the full.Count > 0 ? full : initial guard).")]
	public void TryAssemblePageSources_ShouldKeepInitialHierarchy_WhenRootReanchorRefetchIsEmpty() {
		// Arrange — metadata resolves to uid-mid; the initial fetch's last same-named entry is uid-root
		// (!= uid-mid), so the else re-anchor is entered, but GetParentSchemas(uid-root) returns EMPTY, so the
		// guard must retain 'initial' rather than dropping the already-resolved chain.
		AddLayer("UsrPage", "uid-mid", "UsrApp", 300);
		_hierarchyClient.GetDesignPackageUId(Arg.Any<string>()).Returns("dp-uid");
		_hierarchyClient.GetParentSchemas("uid-mid", Arg.Any<string>()).Returns(new List<PageDesignerHierarchySchema> {
			Hier("UsrPage", "pkgMid", "uid-mid", "define(\"UsrPage\", [], function() { return { entitySchemaName: \"UsrX\" }; });"),
			Hier("UsrPage", "pkgRoot", "uid-root", "define(\"UsrPage\", [], function() { return {}; });")
		});
		_hierarchyClient.GetParentSchemas("uid-root", Arg.Any<string>()).Returns(new List<PageDesignerHierarchySchema>());
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrPage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeTrue(because: "an empty re-fetch must fall back to the initial hierarchy, not fail assembly");
		_hierarchyClient.Received(1).GetParentSchemas("uid-root", Arg.Any<string>());
		response.LayerCount.Should().Be(2,
			because: "the initial two-layer fetch is retained when the re-anchor re-fetch yields nothing (full.Count > 0 ? full : initial)");
	}

	[Test]
	[Description("TryAssemblePageSources falls back to the legacy per-layer enumeration (and logs it) when the GetParentSchemas hierarchy call fails, still producing a manifest.")]
	public void TryAssemblePageSources_ShouldFallBackToLegacy_WhenGetParentSchemasThrows() {
		// Arrange — a full legacy fake (layers + bodies + parent template), but the hierarchy call throws.
		AddLayer("UsrPage2", "uid-p", "UsrApp", 200);
		AddSchema("uid-p", "define(\"UsrPage2\", [], function() { return { entitySchemaName: \"UsrX\" }; });", "uid-par", "UsrApp");
		AddLayer("BaseTpl", "uid-par", "Core", 100);
		AddSchema("uid-par", "define(\"BaseTpl\", [], function() { return {}; });", EmptyGuid, "Core", name: "BaseTpl");
		_hierarchyClient.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>())
			.Returns<IReadOnlyList<PageDesignerHierarchySchema>>(_ => throw new InvalidOperationException("designer down"));
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrPage2" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeTrue(because: "the legacy per-layer path assembles the collection when the hierarchy call fails");
		response.LayerCount.Should().Be(1, because: "the legacy enumeration loaded the page's single layer");
		response.SeedCount.Should().Be(1, because: "the legacy parent walk seeded the base template");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("falling back")));
	}

	[Test]
	[Description("TryAssemblePageSources resolves a child edit-page manifest through ONE GetParentSchemas hierarchy call (chain + seed) rather than the per-layer LoadLayerChain + BuildSeed fan-out it used before.")]
	public void TryAssemblePageSources_ShouldResolveChildManifest_ViaGetParentSchemas() {
		// Arrange — a page whose detail names an edit page (the child). The child page's LAYER BODIES are
		// registered ONLY on the hierarchy call, not on the fake DataService (no AddSchema for uid-child*), so
		// the child resolves iff the hierarchy path is taken: the legacy fan-out would fail to load uid-child's
		// body and omit the child. Only the child's name->UId metadata row is registered (via AddLayer) so
		// QuerySysSchemaRow can anchor the hierarchy; its base layer UId equals the metadata UId so no re-anchor
		// re-fetch is needed (one GetParentSchemas call).
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\", details: { D: { schemaName: \"UsrNoteDetail\" } } }; });",
			EmptyGuid, "UsrApp");
		AddLayer("UsrNoteDetail", "uid-detail", "UsrApp", 200);
		AddSchema("uid-detail",
			"define(\"UsrNoteDetail\", [], function() { return { getEditPageName: function() { return \"UsrNotePage\"; } }; });",
			EmptyGuid, "UsrApp");
		AddLayer("UsrNotePage", "uid-child", "UsrApp", 200); // metadata resolve target only — NO AddSchema body
		_hierarchyClient.GetDesignPackageUId(Arg.Any<string>()).Returns("dp-uid");
		_hierarchyClient.GetParentSchemas("uid-child", Arg.Any<string>()).Returns(new List<PageDesignerHierarchySchema> {
			Hier("UsrNotePage", "pkgB", "uid-child-top", "define(\"UsrNotePage\", [], function() { return {}; });"),
			Hier("UsrNotePage", "pkgA", "uid-child", "define(\"UsrNotePage\", [], function() { return { entitySchemaName: \"UsrNote\" }; });"),
			Hier("BaseTpl", "Core", "uid-ctpl", "define(\"BaseTpl\", [], function() { return { baseContainer: true }; });")
		});
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeTrue(because: "the page assembles and the child edit page nests from the hierarchy call");
		_hierarchyClient.Received(1).GetParentSchemas("uid-child", Arg.Any<string>());
		response.ChildPageCount.Should().Be(1,
			because: "the child edit page resolves — proving the hierarchy path ran, since its body was never registered on the DataService fan-out");
		JObject manifest = JObject.Parse(ReadManifest(response));
		JToken childManifest = manifest["childPageSchemas"]!["UsrNotePage"]!;
		var childSchemas = (JArray)childManifest["schemas"]!;
		childSchemas.Should().HaveCount(2,
			because: "both UsrNotePage-named hierarchy layers become the child chain, split from the parent template");
		childSchemas[0]["pkg"]!.ToString().Should().Be("pkgA", because: "the child chain is ordered base->top");
		childSchemas[1]["pkg"]!.ToString().Should().Be("pkgB", because: "the most-derived child layer sorts last");
		((JArray)childManifest["seed"]!).Should().ContainSingle(
				because: "the child's non-page hierarchy layer becomes its seed")
			.Which["pkg"]!.ToString().Should().Be("Core", because: "the parent template seeds the child manifest");
		childManifest["entity"]!.ToString().Should().Be("UsrNote",
			because: "the child entity is inferred from its hierarchy-resolved body");
	}

	[Test]
	[Description("TryAssemblePageSources still nests a child edit-page manifest via the legacy per-layer fan-out when the child's GetParentSchemas hierarchy call yields nothing, so the optimization never regresses child resolution.")]
	public void TryAssemblePageSources_ShouldResolveChildManifest_ViaLegacyFanout_WhenChildHierarchyEmpty() {
		// Arrange — child page fully registered on the fake DataService (layer + body), and the hierarchy call
		// returns empty for every schema (the Setup default), so the child must resolve through the legacy path.
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\", details: { D: { schemaName: \"UsrNoteDetail\" } } }; });",
			EmptyGuid, "UsrApp");
		AddLayer("UsrNoteDetail", "uid-detail", "UsrApp", 200);
		AddSchema("uid-detail",
			"define(\"UsrNoteDetail\", [], function() { return { getEditPageName: function() { return \"UsrNotePage\"; } }; });",
			EmptyGuid, "UsrApp");
		AddLayer("UsrNotePage", "uid-child", "UsrApp", 200);
		AddSchema("uid-child", "define(\"UsrNotePage\", [], function() { return { entitySchemaName: \"UsrNote\" }; });",
			EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicPageSourcesOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		bool ok = _command.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);

		// Assert
		ok.Should().BeTrue(because: "the legacy per-layer fan-out still assembles the child manifest");
		response.ChildPageCount.Should().Be(1,
			because: "with an empty hierarchy the child resolves through the identical legacy LoadLayerChain + BuildSeed path");
		JObject manifest = JObject.Parse(ReadManifest(response));
		manifest["childPageSchemas"]!["UsrNotePage"]!["schemas"].Should().NotBeNull(
			because: "the child edit page is still nested as its own manifest via the fallback");
	}

	// --- fake-environment helpers ------------------------------------------------------------------

	private static PageDesignerHierarchySchema Hier(string name, string pkg, string uid, string body) =>
		new() { Name = name, PackageName = pkg, PackageUId = "pkguid-" + pkg, UId = uid, Body = body };

	private string Route(string requestBody) {
		if (string.IsNullOrEmpty(requestBody)) {
			return new JObject().ToString();
		}
		if (requestBody.Contains("rootSchemaName")) {
			JObject query = JObject.Parse(requestBody);
			var names = new List<string>();
			// Scan every filter item keyed on the Name column, so this fake answers BOTH the SchemaDesignerHelper
			// layer queries (filter item "byName") AND QuerySysSchemaRow's metadata query (filter item "filter0",
			// with a sibling "filter1" on ManagerName that this skips).
			if (query["filters"]?["items"] is JObject items) {
				foreach (JProperty item in items.Properties()) {
					JToken filter = item.Value;
					if (filter?["leftExpression"]?["columnPath"]?.ToString() != "Name") {
						continue;
					}
					string single = filter["rightExpression"]?["parameter"]?["value"]?.ToString();
					if (!string.IsNullOrEmpty(single)) {
						names.Add(single);
					}
					if (filter["rightExpressions"] is JArray many) {
						names.AddRange(many
							.Select(expression => expression["parameter"]?["value"]?.ToString())
							.Where(value => !string.IsNullOrEmpty(value)));
					}
				}
			}
			var rows = new JArray();
			foreach (string name in names) {
				if (_layersByName.TryGetValue(name, out JArray layers)) {
					foreach (JToken row in layers) {
						rows.Add(row.DeepClone());
					}
				}
			}
			// success:true so QuerySysSchemaRow's ExecuteSelectQuery (strict success check) resolves the
			// name->UId+package metadata; the layer-enumeration path keys failure off TryGetFailure, for which
			// a success:true envelope is a non-failure too.
			return new JObject { ["success"] = true, ["rows"] = rows }.ToString();
		}
		JObject request = JObject.Parse(requestBody);
		string uid = request["schemaUId"]?.ToString();
		bool fullHierarchy = request["useFullHierarchy"]?.Value<bool>() ?? false;
		if (uid == null || !_schemaByUid.TryGetValue(uid, out JObject schema)) {
			return new JObject().ToString(); // no "schema" node -> LoadSchema reports a load error
		}
		var clone = (JObject)schema.DeepClone();
		if (fullHierarchy && _localizableByUid.TryGetValue(uid, out JArray localizable)) {
			clone["localizableStrings"] = localizable.DeepClone();
		}
		return new JObject { ["schema"] = clone }.ToString();
	}

	private void AddLayer(string name, string uid, string package, int hierarchyLevel) {
		if (!_layersByName.TryGetValue(name, out JArray rows)) {
			rows = new JArray();
			_layersByName[name] = rows;
		}
		rows.Add(new JObject {
			["UId"] = uid, ["Name"] = name, ["PackageName"] = package, ["HierarchyLevel"] = hierarchyLevel,
			// PackageUId lets QuerySysSchemaRow (the hierarchy path's name->UId+package resolve) return a full
			// metadata row; without a configured GetParentSchemas the hierarchy path still falls back to legacy.
			["PackageUId"] = "pkguid-" + package
		});
	}

	private void AddSchema(string uid, string body, string parentUid, string package, string caption = null, string name = null) {
		var schema = new JObject {
			["body"] = body,
			["parent"] = new JObject { ["uId"] = parentUid }
		};
		if (package != null) {
			schema["package"] = new JObject { ["name"] = package };
		}
		if (name != null) {
			// The real GetSchema response carries the schema name; the parent-template seed reads it to
			// enumerate every layer of that template by name.
			schema["name"] = name;
		}
		if (caption != null) {
			schema["caption"] = new JArray { new JObject { ["cultureName"] = "en-US", ["value"] = caption } };
		}
		_schemaByUid[uid] = schema;
	}

	private void AddLocalizable(string uid, string name, string value) {
		if (!_localizableByUid.TryGetValue(uid, out JArray strings)) {
			strings = new JArray();
			_localizableByUid[uid] = strings;
		}
		strings.Add(new JObject {
			["name"] = name,
			["parentSchemaUId"] = "p",
			["uId"] = "ls-" + name,
			["values"] = new JArray { new JObject { ["cultureName"] = "en-US", ["value"] = value } }
		});
	}

	private void StubEntityColumns() {
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(
			new EntitySchemaPropertiesInfo(
				"UsrTest", "UsrTest object", null, "UsrApp", null, false, "Id", "Subject", 2, 0, null,
				false, false, null, false, null, false, false, false, false, null, null,
				new List<EntitySchemaPropertyColumnInfo> {
					new("Subject", Guid.Empty, "own", "Subject", null, "ShortText", false, false, null),
					new("Account", Guid.Empty, "own", "Customer", null, "Lookup", false, false, "Account")
				}));
	}
}

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
internal class GetClassicMigrationBundleCommandTests : BaseCommandTests<GetClassicMigrationBundleOptions> {

	private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";

	private GetClassicMigrationBundleCommand _command;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private IFileSystem _fileSystem;
	private System.IO.Abstractions.TestingHelpers.MockFileSystem _ioFileSystem;
	private ILogger _logger;

	// Name-aware fake Creatio: schema name -> SysSchema layer rows; layer UId -> loaded schema object;
	// layer UId -> merged localizable strings (returned only on a full-hierarchy load).
	private readonly Dictionary<string, JArray> _layersByName = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, JObject> _schemaByUid = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, JArray> _localizableByUid = new(StringComparer.OrdinalIgnoreCase);
	private string _writtenPath;
	private string _writtenContent;

	public override void Setup() {
		base.Setup();
		_layersByName.Clear();
		_schemaByUid.Clear();
		_localizableByUid.Clear();
		_writtenPath = null;
		_writtenContent = null;
		_command = Container.GetRequiredService<GetClassicMigrationBundleCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_fileSystem = Substitute.For<IFileSystem>();
		_ioFileSystem = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build(Arg.Any<string>()).Returns("http://localhost/svc");
		_applicationClient.ExecutePostRequest(default, default).ReturnsForAnyArgs(ci => Route(ci.ArgAt<string>(1)));
		_fileSystem.When(fs => fs.WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>()))
			.Do(ci => { _writtenPath = ci.ArgAt<string>(0); _writtenContent = ci.ArgAt<string>(1); });
		containerBuilder.AddSingleton(_applicationClient);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_columnManager);
		containerBuilder.AddSingleton(_fileSystem);
		containerBuilder.AddSingleton<System.IO.Abstractions.IFileSystem>(_ioFileSystem);
		containerBuilder.AddSingleton(_logger);
	}

	[Test]
	[Description("TryAssembleBundle writes a manifest with base->top schemas, the parent seed, resources, and entity columns; the response carries only a summary.")]
	public void TryAssembleBundle_ShouldWriteManifest_WithLayersSeedResourcesAndColumns() {
		// Arrange — a two-layer page with a parent template and merged resources, no details/section
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddLayer("UsrTestPage", "uid-base", "BaseApp", 100);
		AddSchema("uid-base", "define(\"UsrTestPage\", [], function() { return { entitySchemaName: \"UsrTest\" }; });", "uid-parent", "BaseApp");
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return {}; });", "uid-parent", "UsrApp");
		AddSchema("uid-parent", "define(\"BaseModulePageV2\", [], function() { return {}; });", EmptyGuid, "CrtBase");
		AddLocalizable("uid-top", "HeaderCaption", "Header");
		StubEntityColumns();
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		bool ok = _command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert — summary
		ok.Should().BeTrue(because: "a resolvable multi-layer page assembles successfully");
		response.Entity.Should().Be("UsrTest", because: "the entity is inferred from the page body's entitySchemaName");
		response.LayerCount.Should().Be(2, because: "both replacing layers were enumerated");
		response.SeedCount.Should().Be(1, because: "one parent-template body was walked into the seed");
		response.ResourceCount.Should().Be(1, because: "one merged localizable string became a resource");
		response.ColumnCount.Should().Be(2, because: "both entity columns contributed a title");

		// Assert — manifest content (bodies live here, NOT in the response)
		JObject manifest = JObject.Parse(_writtenContent);
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
	[Description("TryAssembleBundle anchors the default manifest path (absolute) and reports it in the response, instead of a cwd-relative string an MCP caller cannot resolve.")]
	public void TryAssembleBundle_ShouldUseAnchoredAbsoluteDefaultPath_WhenOutputFileOmitted() {
		// Arrange
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return { entitySchemaName: \"UsrTest\" }; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		_command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert — no workspace marker above the mock cwd, so the anchor is the current directory itself
		string expected = Path.Combine(
			_ioFileSystem.Directory.GetCurrentDirectory(), ".clio-migration", "UsrTestPage", "manifest.json");
		response.ManifestPath.Should().Be(expected,
			because: "the default output anchors at the resolved directory (PRD OQ-04, get-page convention)");
		Path.IsPathRooted(response.ManifestPath).Should().BeTrue(
			because: "the reported path must be absolute — the MCP caller does not know the server's cwd");
		_writtenPath.Should().Be(expected, because: "the manifest is written to the same resolved path");
	}

	[Test]
	[Description("TryAssembleBundle anchors the default manifest path at the enclosing workspace root when the cwd is inside a workspace.")]
	public void TryAssembleBundle_ShouldAnchorDefaultPath_AtWorkspaceRoot() {
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
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		_command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		string expected = Path.Combine(workspace, ".clio-migration", "UsrTestPage", "manifest.json");
		response.ManifestPath.Should().Be(expected,
			because: "a cwd inside a workspace anchors the manifest at the workspace root, not the nested directory");
	}

	[Test]
	[Description("TryAssembleBundle absolutizes an explicit relative output-file so the response reports where the file actually lands.")]
	public void TryAssembleBundle_ShouldAbsolutizeExplicitOutputFile() {
		// Arrange
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return { entitySchemaName: \"UsrTest\" }; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrTestPage", OutputFile = "./bundle.json" };

		// Act
		_command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		response.ManifestPath.Should().Be(_ioFileSystem.Path.GetFullPath("./bundle.json"),
			because: "an explicit relative path is resolved to the absolute location it is written to");
		Path.IsPathRooted(response.ManifestPath).Should().BeTrue(
			because: "the reported path must be absolute regardless of how the caller expressed it");
	}

	[Test]
	[Description("TryAssembleBundle rejects a schema name that is not a valid identifier before any network call, keeping the default path confined to the anchor.")]
	public void TryAssembleBundle_ShouldRejectInvalidSchemaName_BeforeAnyRequest() {
		// Arrange — a traversal-shaped name that must never become a path segment
		GetClassicMigrationBundleOptions options = new() { SchemaName = "..\\..\\evil" };

		// Act
		bool ok = _command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		ok.Should().BeFalse(because: "an invalid schema name cannot be bundled");
		response.Error.Should().Be(PageSchemaMetadataHelper.SchemaNameFormatError,
			because: "the canonical format error tells the caller what a valid name looks like");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
		_writtenContent.Should().BeNull(because: "nothing is written for a rejected name");
	}

	[Test]
	[Description("TryAssembleBundle returns a not-found error and writes nothing when the schema has no layers.")]
	public void TryAssembleBundle_ShouldReturnNotFound_WhenSchemaHasNoLayers() {
		// Arrange — no layers registered for the requested name
		GetClassicMigrationBundleOptions options = new() { SchemaName = "MissingPage" };

		// Act
		bool ok = _command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		ok.Should().BeFalse(because: "an unresolvable schema cannot be bundled");
		response.Error.Should().Contain("not found", because: "the caller needs a clear not-found message");
		_writtenContent.Should().BeNull(because: "no manifest is written when the schema is missing");
	}

	[Test]
	[Description("TryAssembleBundle aborts with a layer-specific error (and writes nothing) when a mid-chain layer body fails to load.")]
	public void TryAssembleBundle_ShouldFail_WhenChainLayerLoadFails() {
		// Arrange — two enumerated layers, but the top layer has no loadable schema
		AddLayer("UsrTestPage", "uid-base", "BaseApp", 100);
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-base", "define(\"UsrTestPage\", [], function() { return {}; });", EmptyGuid, "BaseApp");
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		bool ok = _command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		ok.Should().BeFalse(because: "a manifest with a hole in the layer chain would misfold in the engine");
		response.Error.Should().Contain("Failed to load layer", because: "the error names the failing step");
		response.Error.Should().Contain("uid-top", because: "the error identifies the exact layer that failed");
		_writtenContent.Should().BeNull(because: "no partial manifest may be written on a chain failure");
	}

	[Test]
	[Description("TryAssembleBundle converts a malformed (non-JSON) server response into a failed response instead of an unhandled exception.")]
	public void TryAssembleBundle_ShouldFail_WhenServerReturnsMalformedResponse() {
		// Arrange — the server answers every request with an HTML error page
		_applicationClient.ExecutePostRequest(default, default).ReturnsForAnyArgs("<html>login required</html>");
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		bool ok = _command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		ok.Should().BeFalse(because: "a malformed transport response cannot produce a bundle");
		response.Error.Should().NotBeNullOrWhiteSpace(because: "the parse failure must surface as a readable error");
		_writtenContent.Should().BeNull(because: "nothing is written when assembly fails");
	}

	[Test]
	[Description("TryAssembleBundle honors an explicit --entity option over body inference and feeds it to the column manager.")]
	public void TryAssembleBundle_ShouldHonorExplicitEntity_OverBodyInference() {
		// Arrange — the body names a DIFFERENT entity than the explicit option
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return { entitySchemaName: \"UsrOther\" }; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrTestPage", Entity = "UsrOverride" };

		// Act
		_command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		response.Entity.Should().Be("UsrOverride", because: "an explicit --entity wins over regex inference");
		_columnManager.Received(1).GetSchemaProperties(
			Arg.Is<GetEntitySchemaPropertiesOptions>(o => o.SchemaName == "UsrOverride"));
	}

	[Test]
	[Description("TryAssembleBundle succeeds without an entity: entity, columns, and section are omitted, and the column manager is not called.")]
	public void TryAssembleBundle_ShouldOmitEntityBlocks_WhenNoEntityResolvable() {
		// Arrange — a body with no entitySchemaName and no explicit --entity
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return {}; });", EmptyGuid, "UsrApp");
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		bool ok = _command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		ok.Should().BeTrue(because: "a page without a resolvable entity still bundles its layer chain");
		JObject manifest = JObject.Parse(_writtenContent);
		manifest["entity"].Should().BeNull(because: "an unknown entity is omitted, never fabricated");
		manifest["entityColumns"].Should().BeNull(because: "no entity means no columns block");
		manifest["section"].Should().BeNull(because: "no entity means no section naming convention to probe");
		_columnManager.DidNotReceiveWithAnyArgs().GetSchemaProperties(default);
	}

	[Test]
	[Description("TryAssembleBundle keeps the bundle successful (with empty columns) when the column manager throws, and logs the degradation.")]
	public void TryAssembleBundle_ShouldDegradeGracefully_WhenColumnManagerThrows() {
		// Arrange
		AddLayer("UsrTestPage", "uid-top", "UsrApp", 200);
		AddSchema("uid-top", "define(\"UsrTestPage\", [], function() { return { entitySchemaName: \"UsrTest\" }; });", EmptyGuid, "UsrApp");
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(_ => throw new InvalidOperationException("designer unavailable"));
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		bool ok = _command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		ok.Should().BeTrue(because: "entity columns are a best-effort enricher, not a bundling precondition");
		JObject manifest = JObject.Parse(_writtenContent);
		manifest["entityColumns"].Should().BeNull(because: "the failed enricher is omitted, never fabricated");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("UsrTest")));
	}

	[Test]
	[Description("TryAssembleBundle gathers detailSchemas, the section chain, and the child edit page as a nested manifest when they resolve.")]
	public void TryAssembleBundle_ShouldGatherEnrichers_WhenResolvable() {
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
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		response.DetailCount.Should().Be(1, because: "the referenced detail schema resolves");
		response.SectionLayerCount.Should().Be(1, because: "the <Entity>SectionV2 convention resolves one layer");
		response.ChildPageCount.Should().Be(1, because: "the detail's edit page resolves to a nested manifest");
		JObject manifest = JObject.Parse(_writtenContent);
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
	[Description("TryAssembleBundle resolves every enricher name (details + section candidates) through ONE batched SelectQuery instead of one round-trip per name.")]
	public void TryAssembleBundle_ShouldBatchEnricherEnumeration_InSingleSelectQuery() {
		// Arrange — same enricher topology as the gather test
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\", details: { D: { schemaName: \"UsrNoteDetail\" } } }; });",
			EmptyGuid, "UsrApp");
		AddLayer("UsrNoteDetail", "uid-detail", "UsrApp", 200);
		AddSchema("uid-detail", "define(\"UsrNoteDetail\", [], function() { return {}; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssembleBundle(options, out _);

		// Assert — one request carries the detail name AND both section candidates together
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("UsrNoteDetail")
				&& body.Contains("UsrCaseSectionV2")
				&& body.Contains("UsrCaseSection")));
	}

	[Test]
	[Description("TryAssembleBundle loads a schema body only once per run: a child page sharing the main page's parent template reuses the cached layer.")]
	public void TryAssembleBundle_ShouldMemoizeSchemaLoads_AcrossMainAndChildSeeds() {
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
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert — both seeds carry the template, but its body traveled the wire once
		response.ChildPageCount.Should().Be(1, because: "the child page assembles from the same run");
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"uid-tpl\"")));
	}

	[Test]
	[Description("TryAssembleBundle omits an enricher it cannot resolve rather than fabricating it.")]
	public void TryAssembleBundle_ShouldOmitUnresolvedDetail_WhenDetailSchemaMissing() {
		// Arrange — page references a detail that has no layers registered
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\", details: { D: { schemaName: \"UsrGhostDetail\" } } }; });",
			EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		response.DetailCount.Should().Be(0, because: "an unresolved detail is omitted, not fabricated");
		JObject manifest = JObject.Parse(_writtenContent);
		manifest["detailSchemas"].Should().BeNull(because: "no detail resolved, so the field is absent");
	}

	[Test]
	[Description("TryAssembleBundle falls back to the <Entity>Section naming convention when no <Entity>SectionV2 schema exists.")]
	public void TryAssembleBundle_ShouldGatherSection_ViaNonV2NamingFallback() {
		// Arrange — only the non-V2 section name resolves
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCase\" }; });", EmptyGuid, "UsrApp");
		AddLayer("UsrCaseSection", "uid-section", "UsrApp", 200);
		AddSchema("uid-section", "define(\"UsrCaseSection\", [], function() { return {}; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		response.SectionLayerCount.Should().Be(1, because: "the non-V2 naming fallback resolves the section");
	}

	[Test]
	[Description("TryAssembleBundle seeds EVERY layer of a multi-package parent template (base->top), not just the single parent.uId layer, so base containers in sibling layers are not dropped.")]
	public void TryAssembleBundle_ShouldSeedAllParentTemplateLayers_WhenParentIsMultiLayer() {
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
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrPage" };

		// Act
		_command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		response.SeedCount.Should().Be(2,
			because: "both layers of the multi-package parent template must be seeded, not only the parent.uId layer");
		JObject manifest = JObject.Parse(_writtenContent);
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
	[Description("TryAssembleBundle never seeds the same layer body twice when the parent walk revisits a template it already enumerated (parent link into a replaced sibling).")]
	public void TryAssembleBundle_ShouldNotDuplicateSeedLayer_WhenParentWalkRevisitsTemplate() {
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
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrPage" };

		// Act
		_command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert — the base layer appears once, not once per walk visit
		response.SeedCount.Should().Be(2,
			because: "revisiting the template through the parent link must not append the already-seeded base layer again");
	}

	[Test]
	[Description("TryAssembleBundle omits the seed entry's pkg when the parent layer carries no package name, instead of fabricating one from the schema name.")]
	public void TryAssembleBundle_ShouldOmitSeedPkg_WhenParentPackageUnknown() {
		// Arrange — a parent layer whose GetSchema response has no package block (and no name -> single-layer fallback)
		AddLayer("UsrPage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page", "define(\"UsrPage\", [], function() { return { entitySchemaName: \"UsrX\" }; });",
			"uid-parent", "UsrApp");
		AddSchema("uid-parent", "define(\"BaseTpl\", [], function() { return {}; });", EmptyGuid, package: null);
		StubEntityColumns();
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrPage" };

		// Act
		_command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		response.SeedCount.Should().Be(1, because: "the parent body itself is still seeded");
		JObject manifest = JObject.Parse(_writtenContent);
		JToken entry = ((JArray)manifest["seed"])[0];
		entry["pkg"].Should().BeNull(
			because: "pkg is package provenance — when unknown it is omitted, never substituted with the schema name");
		entry["body"]!.ToString().Should().Contain("BaseTpl", because: "the body is still carried");
	}

	[Test]
	[Description("TryAssembleBundle infers the page's own entitySchemaName and ignores a longer identifier like masterEntitySchemaName.")]
	public void TryAssembleBundle_ShouldInferPageEntity_WhenBodyContainsMasterEntitySchemaNameSubstring() {
		// Arrange — a masterEntitySchemaName appears BEFORE the page's own entitySchemaName in the body
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { masterEntitySchemaName: \"UsrWrong\", entitySchemaName: \"UsrCase\" }; });",
			EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		response.Entity.Should().Be("UsrCase",
			because: "the word-boundary anchor skips 'masterEntitySchemaName' and binds the page's own entitySchemaName");
	}

	[Test]
	[Description("TryAssembleBundle does not misclassify an entity reference whose name merely ends in 'Detail' (entitySchemaName: \"XDetail\") as a detail schema.")]
	public void TryAssembleBundle_ShouldNotTreatDetailNamedEntityReference_AsDetailSchema() {
		// Arrange — the only 'Detail' substring sits inside entitySchemaName, not a schemaName reference;
		// a same-named client-unit schema exists, so a false positive WOULD resolve and pollute detailSchemas.
		AddLayer("UsrCasePage", "uid-page", "UsrApp", 200);
		AddSchema("uid-page",
			"define(\"UsrCasePage\", [], function() { return { entitySchemaName: \"UsrCaseDetail\" }; });",
			EmptyGuid, "UsrApp");
		AddLayer("UsrCaseDetail", "uid-lookalike", "UsrApp", 200);
		AddSchema("uid-lookalike", "define(\"UsrCaseDetail\", [], function() { return {}; });", EmptyGuid, "UsrApp");
		StubEntityColumns();
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrCasePage" };

		// Act
		_command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		response.DetailCount.Should().Be(0,
			because: "an entity reference is not a detail declaration — the heuristic must not fabricate a detail from it");
	}

	[Test]
	[Description("TryAssembleBundle surfaces a DataService errorInfo-only failure (no success:false) as the bundle error instead of a misleading not-found.")]
	public void TryAssembleBundle_ShouldSurfaceDataServiceFailure_WhenSelectQueryReturnsErrorInfoOnly() {
		// Arrange - the layer-enumeration SelectQuery answers with an errorInfo object and NO success:false
		// (the restricted-SysSchema shape). The shared detector must classify it as a failure so the bundle
		// reports the real reason, not "not found" from a silently empty row set.
		_applicationClient.ExecutePostRequest(default, default).ReturnsForAnyArgs(
			"""{ "errorInfo": { "errorCode": "AccessDenied", "message": "Access to SysSchema is denied" } }""");
		GetClassicMigrationBundleOptions options = new() { SchemaName = "UsrTestPage" };

		// Act
		bool ok = _command.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);

		// Assert
		ok.Should().BeFalse(because: "a DataService failure envelope cannot produce a bundle");
		response.Error.Should().Contain("Access to SysSchema is denied",
			because: "the real DataService reason must surface, not a masked empty-result not-found");
		response.Error.Should().NotContain("not found",
			because: "an access failure must not be reported as a missing schema");
		_writtenContent.Should().BeNull(because: "no manifest is written when enumeration fails");
	}

	// --- fake-environment helpers ------------------------------------------------------------------

	private string Route(string requestBody) {
		if (string.IsNullOrEmpty(requestBody)) {
			return new JObject().ToString();
		}
		if (requestBody.Contains("rootSchemaName")) {
			JObject query = JObject.Parse(requestBody);
			JToken byName = query["filters"]?["items"]?["byName"];
			var names = new List<string>();
			string single = byName?["rightExpression"]?["parameter"]?["value"]?.ToString();
			if (!string.IsNullOrEmpty(single)) {
				names.Add(single);
			}
			if (byName?["rightExpressions"] is JArray many) {
				names.AddRange(many
					.Select(expression => expression["parameter"]?["value"]?.ToString())
					.Where(value => !string.IsNullOrEmpty(value)));
			}
			var rows = new JArray();
			foreach (string name in names) {
				if (_layersByName.TryGetValue(name, out JArray layers)) {
					foreach (JToken row in layers) {
						rows.Add(row.DeepClone());
					}
				}
			}
			return new JObject { ["rows"] = rows }.ToString();
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
			["UId"] = uid, ["Name"] = name, ["PackageName"] = package, ["HierarchyLevel"] = hierarchyLevel
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

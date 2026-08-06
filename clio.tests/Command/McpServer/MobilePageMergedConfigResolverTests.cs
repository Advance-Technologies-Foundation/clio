using System;
using Clio;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Tests for <see cref="MobilePageMergedConfigResolver"/> — the mode-aware base resolver for the mobile
/// apply-oracle. A REPLACE-mode write overwrites the page's own body verbatim, so its validation base must
/// EXCLUDE that own body (the config the incoming body layers over at runtime); an APPEND-mode write keeps the
/// own body and merges into it, so the base must INCLUDE it. A read failure degrades to <c>(null, null)</c>
/// (the oracle then seeds its own base) after a diagnostic warning; a cancellation propagates.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class MobilePageMergedConfigResolverTests {

	private const string SchemaName = "Test_FormPage";
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";

	// Parent template body: establishes viewModelConfig.attributes.BaseAttr and modelConfig.baseKey.
	private static readonly string ParentBody = BuildBody(
		vmcDiff: """[ { "operation": "merge", "path": [], "values": { "attributes": { "BaseAttr": { "type": "string" } } } } ]""",
		mcDiff: """[ { "operation": "merge", "path": [], "values": { "baseKey": "b" } } ]""");

	// Editable (head/own) body: layers viewModelConfig.attributes.OwnAttr and modelConfig.ownKey on top.
	private static readonly string HeadBody = BuildBody(
		vmcDiff: """[ { "operation": "merge", "path": ["attributes"], "values": { "OwnAttr": { "type": "string" } } } ]""",
		mcDiff: """[ { "operation": "merge", "path": [], "values": { "ownKey": "o" } } ]""");

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
	}

	private static string BuildBody(string vmcDiff, string mcDiff) =>
		"define(\"" + SchemaName + "\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/" + vmcDiff + "/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/" + mcDiff + "/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	// A real PageGetCommand over a two-schema hierarchy: head (uid-1, pkg-1, owns HeadBody) + parent template
	// (uid-2, pkg-2, ParentBody). The design package is pkg-1, so the head IS the editable schema replace mode
	// overwrites — excluding it leaves the parent-only base.
	private PageGetCommand CreateCommandWithHierarchy() {
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success":true,"rows":[{"Name":"{{SchemaName}}","UId":"uid-1","PackageName":"UsrPkg","PackageUId":"pkg-1","ParentSchemaName":"BaseTemplate"}]}""");
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("uid-1").Returns("pkg-1");
		hierarchyClient.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>()).Returns([
			new PageDesignerHierarchySchema {
				UId = "uid-1", Name = SchemaName, PackageUId = "pkg-1", PackageName = "UsrPkg", SchemaVersion = 1, Body = HeadBody
			},
			new PageDesignerHierarchySchema {
				UId = "uid-2", Name = "BaseTemplate", PackageUId = "pkg-2", PackageName = "CrtPkg", SchemaVersion = 1, Body = ParentBody
			}
		]);
		return new PageGetCommand(_applicationClient, _serviceUrlBuilder, Substitute.For<ILogger>(),
			hierarchyClient, new PageSchemaBodyParser(),
			new PageBundleBuilder(new PageJsonDiffApplier(), new PageJsonPathDiffApplier()),
			Substitute.For<IPageFileWriter>());
	}

	private static IToolCommandResolver ResolverReturning(PageGetCommand command) {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		return resolver;
	}

	[Test]
	[Description("Replace mode (the default) validates against the base WITHOUT the editable schema's own body — the config the overwritten body layers over at runtime — so the own body's contributions are absent.")]
	public void ResolveMergedConfig_ReplaceMode_ExcludesOwnBody() {
		// Arrange
		IToolCommandResolver resolver = ResolverReturning(CreateCommandWithHierarchy());

		// Act
		(string viewModelConfigJson, string modelConfigJson) = MobilePageMergedConfigResolver.ResolveMergedConfig(
			new MobilePageMergedConfigContext(resolver, SchemaName, "dev", null, null, null, Mode: "replace"));

		// Assert
		viewModelConfigJson.Should().Contain("BaseAttr",
			because: "the parent template contribution is part of the replace-mode runtime base");
		viewModelConfigJson.Should().NotContain("OwnAttr",
			because: "the editable schema's own body is overwritten by a replace write, so it is NOT part of the base");
		modelConfigJson.Should().Contain("baseKey").And.NotContain("ownKey",
			because: "modelConfig follows the same rule — only the parent base survives a replace-mode write");
	}

	[Test]
	[Description("Append mode validates against the FULL merged config including the editable schema's own body, because an append write keeps that body and merges the incoming fragment into it.")]
	public void ResolveMergedConfig_AppendMode_IncludesOwnBody() {
		// Arrange
		IToolCommandResolver resolver = ResolverReturning(CreateCommandWithHierarchy());

		// Act
		(string viewModelConfigJson, string modelConfigJson) = MobilePageMergedConfigResolver.ResolveMergedConfig(
			new MobilePageMergedConfigContext(resolver, SchemaName, "dev", null, null, null, Mode: "append"));

		// Assert
		viewModelConfigJson.Should().Contain("BaseAttr").And.Contain("OwnAttr",
			because: "an append write keeps the own body, so its contributions remain part of the validation base");
		modelConfigJson.Should().Contain("baseKey").And.Contain("ownKey",
			because: "append mode validates against the full merged config, own body included");
	}

	[Test]
	[Description("A read failure (empty metadata → get-page fails) degrades to (null, null) so the oracle seeds its own base, and logs a warning naming the schema so the degraded resolution is not silent.")]
	public void ResolveMergedConfig_ReadFailure_ReturnsNullsAndWarns() {
		// Arrange — empty rows make the metadata query fail, so TryGetPage returns false.
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success":true,"rows":[]}""");
		PageGetCommand command = new(_applicationClient, _serviceUrlBuilder, Substitute.For<ILogger>(),
			Substitute.For<IPageDesignerHierarchyClient>(), new PageSchemaBodyParser(),
			new PageBundleBuilder(new PageJsonDiffApplier(), new PageJsonPathDiffApplier()),
			Substitute.For<IPageFileWriter>());
		IToolCommandResolver resolver = ResolverReturning(command);
		ILogger logger = Substitute.For<ILogger>();

		// Act
		(string viewModelConfigJson, string modelConfigJson) = MobilePageMergedConfigResolver.ResolveMergedConfig(
			new MobilePageMergedConfigContext(resolver, SchemaName, "dev", null, null, null, Mode: "replace", Logger: logger));

		// Assert
		viewModelConfigJson.Should().BeNull(because: "a failed base resolution must fall back to the oracle's seeded base");
		modelConfigJson.Should().BeNull(because: "both sections degrade together when the page could not be read");
		// A degraded resolution must leave a diagnostic trail (naming the schema) so it is not mistaken for a
		// genuine success during triage.
		logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains(SchemaName)));
	}

	[Test]
	[Description("Verifies the exception-passthrough CONTRACT (not token wiring): an OperationCanceledException surfacing during resolution propagates rather than being swallowed into the seeded-base fallback. The resolver threads no CancellationToken into the get-page read, so this pins the re-raise behavior for an ambient cancellation, not caller-token cancellation.")]
	public void ResolveMergedConfig_AmbientCancellation_Propagates() {
		// Arrange — an ambient cancellation surfaces during resolution (simulated at the command-resolution step).
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>()).Returns(_ => throw new OperationCanceledException());

		// Act
		Action act = () => MobilePageMergedConfigResolver.ResolveMergedConfig(
			new MobilePageMergedConfigContext(resolver, SchemaName, "dev", null, null, null, Mode: "replace"));

		// Assert
		act.Should().Throw<OperationCanceledException>(
			because: "a cancelled validation must not silently degrade to the seeded base — the re-raise contract holds regardless of token wiring");
	}

	[Test]
	[Description("A null context resolves to (null, null) without touching the command resolver — the oracle then seeds its own base.")]
	public void ResolveMergedConfig_NullContext_ReturnsNulls() {
		// Act
		(string viewModelConfigJson, string modelConfigJson) =
			MobilePageMergedConfigResolver.ResolveMergedConfig((MobilePageMergedConfigContext)null);

		// Assert
		viewModelConfigJson.Should().BeNull(because: "a null context carries no schema/environment to resolve a base from");
		modelConfigJson.Should().BeNull(because: "a null context yields no base for either section");
	}

	[Test]
	[Description("An ACCESS-CONTROL read failure (401/403) is swallowed to (null, null) — never rethrown, so it cannot crash the caller's foreach — and the diagnostic names it ACCESS DENIED, distinct from a benign miss.")]
	public void ResolveMergedConfig_AccessDeniedFailure_ReturnsNullsAndWarnsAccessDenied() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("401 Unauthorized"));
		ILogger logger = Substitute.For<ILogger>();
		string warning = null;
		logger.When(l => l.WriteWarning(Arg.Any<string>())).Do(ci => warning = ci.Arg<string>());

		// Act
		(string viewModelConfigJson, string modelConfigJson) = MobilePageMergedConfigResolver.ResolveMergedConfig(
			new MobilePageMergedConfigContext(resolver, SchemaName, "dev", null, null, null, Mode: "replace", Logger: logger));

		// Assert
		viewModelConfigJson.Should().BeNull(because: "an access-denied failure degrades to the seeded base, not a throw");
		modelConfigJson.Should().BeNull(because: "both sections degrade together");
		warning.Should().NotBeNull().And.Contain("ACCESS DENIED",
			because: "a 401/403 must be classified as access-denied so it is not mistaken for a benign template miss");
	}

	[Test]
	[Description("A non-auth read failure is swallowed to (null, null) with a GENERIC warning that does not claim ACCESS DENIED.")]
	public void ResolveMergedConfig_GenericFailure_ReturnsNullsAndWarnsGeneric() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("boom"));
		ILogger logger = Substitute.For<ILogger>();
		string warning = null;
		logger.When(l => l.WriteWarning(Arg.Any<string>())).Do(ci => warning = ci.Arg<string>());

		// Act
		(string viewModelConfigJson, string modelConfigJson) = MobilePageMergedConfigResolver.ResolveMergedConfig(
			new MobilePageMergedConfigContext(resolver, SchemaName, "dev", null, null, null, Mode: "replace", Logger: logger));

		// Assert
		viewModelConfigJson.Should().BeNull(because: "a generic read failure also degrades to the seeded base without throwing");
		modelConfigJson.Should().BeNull();
		warning.Should().NotBeNull().And.NotContain("ACCESS DENIED",
			because: "a non-auth failure must NOT be misclassified as access-denied");
	}
}

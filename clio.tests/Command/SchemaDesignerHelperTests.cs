using System;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit tests for <see cref="SchemaDesignerHelper"/>:
/// (1) <see cref="SchemaDesignerHelper.ApplySchemaMetadata"/> — the shared caption/description write chokepoint
/// used by create-sql-schema and create-source-code-schema, verifying the ENG-91044 script/culture guard;
/// (2) <see cref="SchemaDesignerHelper.EnumerateSchemaLayers"/> / <see cref="SchemaDesignerHelper.ResolveSchemaUId"/>
/// — deterministic base-&gt;top layer ordering by package hierarchy level (ENG-90577 classic-migration-bundle),
/// so a multi-layer classic schema name resolves to the top layer instead of a DB-order-dependent one.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SchemaDesignerHelperTests {

	[Test]
	[Description("ApplySchemaMetadata rejects a Cyrillic caption under the Latin-script en-US culture.")]
	public void ApplySchemaMetadata_ShouldThrow_WhenCaptionScriptMismatchesLatinCulture() {
		// Arrange
		JObject schema = new();

		// Act
		Action act = () => SchemaDesignerHelper.ApplySchemaMetadata(schema, "UsrSchema", "Заявка", null, "en-US");

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				because: "a Cyrillic caption must not be stored under the Latin-script en-US culture (ENG-91044)")
			.Which.Message.Should().Contain("en-US",
				because: "the error must name the culture whose value is in the wrong script");
	}

	[Test]
	[Description("ApplySchemaMetadata allows a Cyrillic caption under the Cyrillic-script uk-UA culture.")]
	public void ApplySchemaMetadata_ShouldApply_WhenCaptionMatchesCyrillicCulture() {
		// Arrange
		JObject schema = new();

		// Act
		Action act = () => SchemaDesignerHelper.ApplySchemaMetadata(schema, "UsrSchema", "Заявка", null, "uk-UA");

		// Assert
		act.Should().NotThrow(
			"because a Cyrillic caption is correct under the uk-UA culture and must not be rejected");
	}

	[Test]
	[Description("ApplySchemaMetadata applies an English caption under en-US and writes the localized caption array.")]
	public void ApplySchemaMetadata_ShouldApply_WhenEnglishCaptionUnderEnUs() {
		// Arrange
		JObject schema = new();

		// Act
		SchemaDesignerHelper.ApplySchemaMetadata(schema, "UsrSchema", "Orders", "Order workspace", "en-US");

		// Assert
		schema["caption"].Should().NotBeNull(
			"because a valid English caption under en-US must be applied to the schema payload");
		schema["name"]!.ToString().Should().Be("UsrSchema",
			"because the schema name is applied verbatim alongside the localized caption");
	}

	[Test]
	[Description("EnumerateSchemaLayers orders same-named layers base->top by package hierarchy level, even when the DataService returns them out of order.")]
	public void EnumerateSchemaLayers_ShouldOrderLayersBaseToTop_WhenMultiplePackages() {
		// Arrange — rows returned deliberately out of hierarchy order (top, base, mid)
		(IApplicationClient client, IServiceUrlBuilder urlBuilder) = MakeSelectQueryClient(LayersResponse(
			("uid-top", "SalesEnterprise", 438),
			("uid-base", "CrtUIv2", 115),
			("uid-mid", "Case", 365)));

		// Act
		(var layers, string error) = SchemaDesignerHelper.EnumerateSchemaLayers(
			client, urlBuilder, "ContactPageV2", SchemaDesignerKind.ClientUnit);

		// Assert
		error.Should().BeNull(because: "a well-formed SelectQuery response is not an error");
		layers.Should().HaveCount(3, because: "every same-named layer must be enumerated, one per package");
		layers[0].PackageName.Should().Be("CrtUIv2",
			because: "the lowest hierarchy level (115) is the base layer and must sort first");
		layers[^1].PackageName.Should().Be("SalesEnterprise",
			because: "the highest hierarchy level (438) is the most-derived top layer and must sort last");
		layers[^1].UId.Should().Be("uid-top",
			because: "the top layer's UId must be preserved through the ordering");
	}

	[Test]
	[Description("ResolveSchemaUId returns the top (most-derived) layer's UId for a multi-layer classic schema name, deterministically.")]
	public void ResolveSchemaUId_ShouldReturnTopLayerUId_WhenMultiplePackages() {
		// Arrange
		(IApplicationClient client, IServiceUrlBuilder urlBuilder) = MakeSelectQueryClient(LayersResponse(
			("uid-top", "SalesEnterprise", 438),
			("uid-base", "CrtUIv2", 115),
			("uid-mid", "Case", 365)));

		// Act
		(string uId, string error) = SchemaDesignerHelper.ResolveSchemaUId(
			client, urlBuilder, "ContactPageV2", SchemaDesignerKind.ClientUnit);

		// Assert
		error.Should().BeNull(because: "a resolvable schema must not report an error");
		uId.Should().Be("uid-top",
			because: "resolution must return the highest-hierarchy-level layer, not a DB-order-dependent one");
	}

	[Test]
	[Description("ResolveSchemaUId reports a not-found error naming the manager when no layer exists.")]
	public void ResolveSchemaUId_ShouldReturnNotFoundError_WhenNoLayers() {
		// Arrange
		(IApplicationClient client, IServiceUrlBuilder urlBuilder) = MakeSelectQueryClient(LayersResponse());

		// Act
		(string uId, string error) = SchemaDesignerHelper.ResolveSchemaUId(
			client, urlBuilder, "MissingPage", SchemaDesignerKind.ClientUnit);

		// Assert
		uId.Should().BeNull(because: "an unresolvable schema must not yield a UId");
		error.Should().Contain("not found",
			because: "the caller needs a clear not-found message");
		error.Should().Contain(SchemaDesignerKind.ClientUnit.ManagerName,
			because: "the error must name the manager it searched so the caller can diagnose the wrong kind");
	}

	[Test]
	[Description("EnumerateSchemaLayers returns exactly one layer for a single-package schema.")]
	public void EnumerateSchemaLayers_ShouldReturnSingleLayer_WhenOnlyOnePackage() {
		// Arrange
		(IApplicationClient client, IServiceUrlBuilder urlBuilder) = MakeSelectQueryClient(LayersResponse(
			("uid-only", "UsrCustom", 500)));

		// Act
		(var layers, string error) = SchemaDesignerHelper.EnumerateSchemaLayers(
			client, urlBuilder, "UsrPage", SchemaDesignerKind.ClientUnit);

		// Assert
		error.Should().BeNull(because: "a single-layer schema is a valid result");
		layers.Should().ContainSingle(because: "a schema owned by one package has exactly one layer")
			.Which.UId.Should().Be("uid-only", because: "the single layer's UId must be returned");
	}

	[Test]
	[Description("EnumerateSchemaLayers breaks hierarchy-level ties by package name so the order is deterministic.")]
	public void EnumerateSchemaLayers_ShouldTiebreakByPackageName_WhenHierarchyLevelsEqual() {
		// Arrange — two sibling packages at the SAME level, returned in reverse-alphabetical order
		(IApplicationClient client, IServiceUrlBuilder urlBuilder) = MakeSelectQueryClient(LayersResponse(
			("uid-z", "ZPackage", 200),
			("uid-a", "APackage", 200)));

		// Act
		(var layers, string error) = SchemaDesignerHelper.EnumerateSchemaLayers(
			client, urlBuilder, "TiePage", SchemaDesignerKind.ClientUnit);

		// Assert
		error.Should().BeNull(because: "equal hierarchy levels are valid, not an error");
		layers[0].PackageName.Should().Be("APackage",
			because: "on a hierarchy-level tie the package name must break it deterministically (ascending)");
		layers[^1].PackageName.Should().Be("ZPackage",
			because: "the alphabetically-last sibling is the deterministic top on a tie");
	}

	[Test]
	[Description("EnumerateSchemaLayers queries SysSchema joined to SysPackage.HierarchyLevel, filtered by the schema name and manager.")]
	public void EnumerateSchemaLayers_ShouldQuerySysPackageHierarchyLevel_WhenComposingTheSelectQuery() {
		// Arrange — capture the outgoing SelectQuery body (arg index 1 of ExecutePostRequest)
		string capturedBody = null;
		var client = Substitute.For<IApplicationClient>();
		var urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns("http://host/0/DataService/json/SyncReply/SelectQuery");
		client.ExecutePostRequest(default, default).ReturnsForAnyArgs(ci => {
			capturedBody = ci.ArgAt<string>(1);
			return LayersResponse(("uid-only", "UsrCustom", 500));
		});

		// Act
		SchemaDesignerHelper.EnumerateSchemaLayers(client, urlBuilder, "ContactPageV2", SchemaDesignerKind.ClientUnit);

		// Assert
		capturedBody.Should().NotBeNull(because: "the helper must POST a SelectQuery body");
		capturedBody.Should().Contain("SysPackage.HierarchyLevel",
			because: "layer ordering is driven by the package hierarchy-level join column");
		capturedBody.Should().Contain("ContactPageV2",
			because: "the query must filter by the requested schema name");
		capturedBody.Should().Contain(SchemaDesignerKind.ClientUnit.ManagerName,
			because: "the query must filter by the schema-kind manager so it targets client-unit schemas");
	}

	[Test]
	[Description("ExtractMergedLocalizableStrings parses name, parentSchemaUId provenance, uId, and per-culture values from a full-hierarchy schema.")]
	public void ExtractMergedLocalizableStrings_ShouldReturnStructuredStrings_WhenSchemaHasLocalizableStrings() {
		// Arrange
		JObject schema = new() {
			["localizableStrings"] = new JArray {
				new JObject {
					["name"] = "GeneralInfoTabCaption",
					["parentSchemaUId"] = "parent-1",
					["uId"] = "uid-1",
					["values"] = new JArray {
						new JObject { ["cultureName"] = "en-US", ["value"] = "General information" },
						new JObject { ["cultureName"] = "uk-UA", ["value"] = "Загальна інформація" }
					}
				},
				new JObject {
					["name"] = "HistoryTabCaption",
					["parentSchemaUId"] = "parent-2",
					["uId"] = "uid-2",
					["values"] = new JArray { new JObject { ["cultureName"] = "en-US", ["value"] = "History" } }
				}
			}
		};

		// Act
		var strings = SchemaDesignerHelper.ExtractMergedLocalizableStrings(schema);

		// Assert
		strings.Should().HaveCount(2, because: "every merged localizable string must be surfaced");
		strings[0].Name.Should().Be("GeneralInfoTabCaption", because: "the string key must be read from 'name'");
		strings[0].ParentSchemaUId.Should().Be("parent-1",
			because: "parentSchemaUId provenance identifies the schema in the hierarchy that contributed the string");
		strings[0].UId.Should().Be("uid-1", because: "the string's own uId must be preserved");
		strings[0].Values.Should().HaveCount(2, because: "all culture values must be captured");
		strings[0].Values[0].CultureName.Should().Be("en-US", because: "the culture name must be read from each value");
		strings[0].Values[0].Value.Should().Be("General information", because: "the localized text must be read from each value");
	}

	[Test]
	[Description("ExtractMergedLocalizableStrings returns an empty list when the schema carries no localizable strings.")]
	public void ExtractMergedLocalizableStrings_ShouldReturnEmpty_WhenSchemaHasNoLocalizableStrings() {
		// Arrange
		JObject schema = new();

		// Act
		var strings = SchemaDesignerHelper.ExtractMergedLocalizableStrings(schema);

		// Assert
		strings.Should().BeEmpty(because: "a schema without localizable strings must yield an empty list, not throw");
	}

	[Test]
	[Description("EnumerateSchemaLayers surfaces a DataService failure (success:false with errorInfo) as an error instead of masking it as an empty not-found result.")]
	public void EnumerateSchemaLayers_ShouldSurfaceError_WhenDataServiceReportsFailure() {
		// Arrange — the DataService returns a permission/error envelope, not rows
		(IApplicationClient client, IServiceUrlBuilder urlBuilder) = MakeSelectQueryClient(
			"""{"success": false, "errorInfo": {"message": "Access denied"}}""");

		// Act
		(var layers, string error) = SchemaDesignerHelper.EnumerateSchemaLayers(
			client, urlBuilder, "ContactPageV2", SchemaDesignerKind.ClientUnit);

		// Assert
		error.Should().NotBeNull(because: "a DataService failure must be surfaced, not masked as not-found");
		error.Should().Contain("Access denied", because: "the operator needs the underlying failure reason");
		layers.Should().BeEmpty(because: "no layers are returned when the query fails");
	}

	[Test]
	[Description("EnumerateSchemaLayersBatch returns every requested name, mapping a found name to its ordered layers and a missing name to an empty list.")]
	public void EnumerateSchemaLayersBatch_ShouldReturnEmptyListForMissingName_WhenSomeNamesResolve() {
		// Arrange — the response carries layers only for "Found"; "Missing" has no rows at all
		(IApplicationClient client, IServiceUrlBuilder urlBuilder) = MakeSelectQueryClient(NamedLayersResponse(
			("uid-found-top", "Found", "SalesEnterprise", 438),
			("uid-found-base", "Found", "CrtUIv2", 115)));

		// Act
		(var layersByName, string error) = SchemaDesignerHelper.EnumerateSchemaLayersBatch(
			client, urlBuilder, new[] { "Found", "Missing" }, SchemaDesignerKind.ClientUnit);

		// Assert
		error.Should().BeNull(because: "a well-formed batch response is not an error");
		layersByName.Should().ContainKey("Found",
			because: "a requested name that resolves must be present in the result");
		layersByName.Should().ContainKey("Missing",
			because: "every requested name is pre-seeded so callers can memoize not-found without re-querying");
		layersByName["Found"].Should().HaveCount(2,
			because: "both layers of the found schema must be enumerated, one per package");
		layersByName["Found"][0].UId.Should().Be("uid-found-base",
			because: "the found name's layers must be ordered base->top by hierarchy level");
		layersByName["Found"][^1].UId.Should().Be("uid-found-top",
			because: "the most-derived layer of the found name must sort last");
		layersByName["Missing"].Should().BeEmpty(
			because: "a requested name with no rows must map to an empty list, not be dropped");
	}

	[Test]
	[Description("EnumerateSchemaLayersBatch ignores rows whose Name was not requested, without throwing.")]
	public void EnumerateSchemaLayersBatch_ShouldIgnoreUnrequestedNames_WhenResponseHasExtraRows() {
		// Arrange — the response includes a row for "Unwanted", which was never requested
		(IApplicationClient client, IServiceUrlBuilder urlBuilder) = MakeSelectQueryClient(NamedLayersResponse(
			("uid-wanted", "Wanted", "UsrCustom", 500),
			("uid-unwanted", "Unwanted", "UsrOther", 500)));

		// Act
		(var layersByName, string error) = SchemaDesignerHelper.EnumerateSchemaLayersBatch(
			client, urlBuilder, new[] { "Wanted" }, SchemaDesignerKind.ClientUnit);

		// Assert
		error.Should().BeNull(because: "a response with extra rows is still a valid batch result");
		layersByName.Should().ContainKey("Wanted",
			because: "the requested name must be present in the result");
		layersByName.Should().NotContainKey("Unwanted",
			because: "a row whose Name was not requested must be filtered out client-side");
		layersByName["Wanted"].Should().ContainSingle(
			because: "only the requested name's single layer must survive the filter")
			.Which.UId.Should().Be("uid-wanted", because: "the requested name's layer UId must be preserved");
	}

	[Test]
	[Description("EnumerateSchemaLayersBatch surfaces a DataService failure while still returning every requested name mapped to an empty list, so a failure is never memoized as a real empty result.")]
	public void EnumerateSchemaLayersBatch_ShouldSurfaceErrorButKeepSeededNames_WhenDataServiceReportsFailure() {
		// Arrange — the DataService returns a failure envelope instead of rows
		(IApplicationClient client, IServiceUrlBuilder urlBuilder) = MakeSelectQueryClient(
			"""{"success": false, "errorInfo": {"message": "Access denied"}}""");

		// Act
		(var layersByName, string error) = SchemaDesignerHelper.EnumerateSchemaLayersBatch(
			client, urlBuilder, new[] { "Alpha", "Beta" }, SchemaDesignerKind.ClientUnit);

		// Assert
		error.Should().NotBeNull(because: "a batch DataService failure must be surfaced, not masked as all-empty");
		error.Should().Contain("Access denied", because: "the operator needs the underlying failure reason");
		layersByName.Should().ContainKey("Alpha",
			because: "the pre-seeded name must remain so a failed run cannot memoize a bogus not-found");
		layersByName.Should().ContainKey("Beta",
			because: "every requested name must remain present even on failure");
		layersByName["Alpha"].Should().BeEmpty(
			because: "on failure a requested name keeps its pre-seeded empty list rather than a resolved one");
		layersByName["Beta"].Should().BeEmpty(
			because: "on failure a requested name keeps its pre-seeded empty list rather than a resolved one");
	}

	[Test]
	[Description("EnumerateSchemaLayersBatch short-circuits on empty input: no error, empty result, and no DataService round-trip.")]
	public void EnumerateSchemaLayersBatch_ShouldNotQuery_WhenNoNamesRequested() {
		// Arrange
		(IApplicationClient client, IServiceUrlBuilder urlBuilder) = MakeSelectQueryClient(NamedLayersResponse());

		// Act
		(var layersByName, string error) = SchemaDesignerHelper.EnumerateSchemaLayersBatch(
			client, urlBuilder, Array.Empty<string>(), SchemaDesignerKind.ClientUnit);

		// Assert
		error.Should().BeNull(because: "an empty request is not an error, just a no-op");
		layersByName.Should().BeEmpty(because: "no requested names means no result entries");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	[Test]
	[Description("EnumerateSchemaLayersBatch orders a single name's layers base->top by hierarchy level, breaking equal-level ties by package name, regardless of DB row order.")]
	public void EnumerateSchemaLayersBatch_ShouldOrderLayersBaseToTop_WhenNameHasMultipleLayers() {
		// Arrange — rows returned out of order, including two siblings at the same hierarchy level
		(IApplicationClient client, IServiceUrlBuilder urlBuilder) = MakeSelectQueryClient(NamedLayersResponse(
			("uid-top", "MultiPage", "SalesEnterprise", 438),
			("uid-mid-z", "MultiPage", "ZMid", 365),
			("uid-base", "MultiPage", "CrtUIv2", 115),
			("uid-mid-a", "MultiPage", "AMid", 365)));

		// Act
		(var layersByName, string error) = SchemaDesignerHelper.EnumerateSchemaLayersBatch(
			client, urlBuilder, new[] { "MultiPage" }, SchemaDesignerKind.ClientUnit);

		// Assert
		error.Should().BeNull(because: "a well-formed multi-layer response is not an error");
		var layers = layersByName["MultiPage"];
		layers.Should().HaveCount(4, because: "every layer of the name must be enumerated");
		layers[0].UId.Should().Be("uid-base",
			because: "the lowest hierarchy level (115) is the base layer and must sort first");
		layers[1].PackageName.Should().Be("AMid",
			because: "on a hierarchy-level tie (365) the package name must break it ascending");
		layers[2].PackageName.Should().Be("ZMid",
			because: "the alphabetically-later sibling at the tied level sorts after");
		layers[^1].UId.Should().Be("uid-top",
			because: "the highest hierarchy level (438) is the most-derived top layer and must sort last");
	}

	[Test]
	[Description("ResolveSchemaUId returns the highest-hierarchy-level layer's UId for a multi-layer ClientUnit schema, not a DB-order-dependent one.")]
	public void ResolveSchemaUId_ShouldReturnHighestHierarchyLayer_WhenClientUnitHasMultipleLayers() {
		// Arrange — the top layer (highest level) is deliberately not the first row returned
		(IApplicationClient client, IServiceUrlBuilder urlBuilder) = MakeSelectQueryClient(NamedLayersResponse(
			("uid-mid", "OrderPageV2", "Case", 365),
			("uid-top", "OrderPageV2", "SalesEnterprise", 438),
			("uid-base", "OrderPageV2", "CrtUIv2", 115)));

		// Act
		(string uId, string error) = SchemaDesignerHelper.ResolveSchemaUId(
			client, urlBuilder, "OrderPageV2", SchemaDesignerKind.ClientUnit);

		// Assert
		error.Should().BeNull(because: "a resolvable multi-layer ClientUnit schema must not report an error");
		uId.Should().Be("uid-top",
			because: "ClientUnit resolution must return the highest-hierarchy-level (top) layer deterministically");
	}

	[Test]
	[Description("ResolveSchemaUId for a non-ClientUnit kind (SqlScript) keeps the pre-PR single-row behavior and returns rows[0].UId, NOT the highest-hierarchy-level row.")]
	public void ResolveSchemaUId_ShouldReturnFirstRowUId_WhenKindIsSqlScript() {
		// Arrange — rows[0] is deliberately NOT the highest-hierarchy-level row, to distinguish the two behaviors
		(IApplicationClient client, IServiceUrlBuilder urlBuilder) = MakeSelectQueryClient(NamedLayersResponse(
			("uid-first", "UsrSqlScript", "CrtUIv2", 115),
			("uid-highest", "UsrSqlScript", "SalesEnterprise", 438)));

		// Act
		(string uId, string error) = SchemaDesignerHelper.ResolveSchemaUId(
			client, urlBuilder, "UsrSqlScript", SchemaDesignerKind.SqlScript);

		// Assert
		error.Should().BeNull(because: "a resolvable SqlScript schema must not report an error");
		uId.Should().Be("uid-first",
			because: "SqlScript/SourceCode kinds keep the pre-PR single-row pick (rows[0].UId), scoped away from top-layer resolution");
	}

	// Builds an IApplicationClient/IServiceUrlBuilder pair whose SelectQuery POST returns the given response JSON,
	// regardless of the URL, body, or optional timeout/retry arguments.
	private static (IApplicationClient client, IServiceUrlBuilder urlBuilder) MakeSelectQueryClient(string responseJson) {
		var client = Substitute.For<IApplicationClient>();
		var urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns("http://host/0/DataService/json/SyncReply/SelectQuery");
		client.ExecutePostRequest(default, default).ReturnsForAnyArgs(responseJson);
		return (client, urlBuilder);
	}

	// Builds a canned DataService SelectQuery response with one row per (UId, package, hierarchy level).
	private static string LayersResponse(params (string uid, string package, int level)[] rows) {
		var array = new JArray();
		foreach ((string uid, string package, int level) in rows) {
			array.Add(new JObject {
				["UId"] = uid,
				["Name"] = "ContactPageV2",
				["PackageName"] = package,
				["HierarchyLevel"] = level
			});
		}
		return new JObject { ["rows"] = array }.ToString();
	}

	// Builds a canned DataService SelectQuery response with one row per (UId, Name, package, hierarchy level),
	// letting a test cover many schema names in a single batch response.
	private static string NamedLayersResponse(params (string uid, string name, string package, int level)[] rows) {
		var array = new JArray();
		foreach ((string uid, string name, string package, int level) in rows) {
			array.Add(new JObject {
				["UId"] = uid,
				["Name"] = name,
				["PackageName"] = package,
				["HierarchyLevel"] = level
			});
		}
		return new JObject { ["rows"] = array }.ToString();
	}
}

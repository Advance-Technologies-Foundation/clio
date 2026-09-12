using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Snapshot guard against silent data loss in the request-registry deserialiser.
/// Every key the static-files-mcp producer publishes under
/// <c>https://academy.creatio.com/api/mcp/latest/RequestRegistry.json</c> must be
/// either mapped to a POCO field or land on an explicit
/// <see cref="System.Text.Json.Serialization.JsonExtensionDataAttribute"/> bucket
/// that this test inspects — mirroring the component-registry guard.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class RequestRegistrySnapshotTests {
	private const string SnapshotRelativePath = "Command/McpServer/Fixtures/RequestRegistry.live-snapshot.json";
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	/// <summary>
	/// Refreshing the snapshot: from the repo root, run
	/// <code>curl -s "https://academy.creatio.com/api/mcp/latest/RequestRegistry.json" \
	///   > clio.tests/Command/McpServer/Fixtures/RequestRegistry.live-snapshot.json</code>
	/// then re-run this test. Until the producer publishes the file to the academy CDN,
	/// the fixture pins the authored payload from the <c>static-files-mcp</c> repository
	/// (<c>latest/RequestRegistry.json</c>) — the same bytes the CDN will serve.
	/// </summary>
	[Test]
	[Description("The pinned request-registry payload must deserialise without leaving any field on an UnmappedExtensions bucket — that bucket is the canary for silent data loss when the producer schema evolves.")]
	public void Pinned_Request_Registry_Snapshot_Should_Have_No_Unmapped_Fields() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		File.Exists(snapshotPath).Should().BeTrue(
			because: $"the snapshot fixture must be present at '{snapshotPath}' for this guard to be meaningful");

		// Act
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);

		// Assert — root-level envelope.
		state.GlobalReferences.Should().NotBeNull(
			because: "the payload ships a top-level 'references' block (baseParameters + global typeDefinitions)");
		UnmappedKeys(state.GlobalReferences!.UnmappedExtensions).Should().BeEmpty(
			because: "any new key under root.references.* must be mapped or explicitly allowlisted");

		// Assert — per-request entries.
		state.Entries.Should().NotBeEmpty(
			because: "the pinned catalog must list at least one request");
		foreach (RequestRegistryEntry entry in state.Entries) {
			UnmappedKeys(entry.UnmappedExtensions).Should().BeEmpty(
				because: $"any new top-level key on entry '{entry.RequestType}' must be mapped");
			if (entry.References is not null) {
				UnmappedKeys(entry.References.UnmappedExtensions).Should().BeEmpty(
					because: $"any new key under '{entry.RequestType}'.references.* must be mapped");
			}
		}
	}

	[Test]
	[Description("A detail response against the pinned payload must keep the platform-injected baseParameters SEPARATE from the authorable parameters map (deliberate divergence from the component catalog's baseInputs merge) and inline the RequestBindingConfig wiring contract through the type-definition closure.")]
	public void Pinned_Snapshot_Detail_Should_Keep_BaseParameters_Separate_And_Resolve_Wiring_TypeDefinitions() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.ClosePageRequest", out RequestRegistryEntry? closePage).Should().BeTrue(
			because: "crt.ClosePageRequest is the pilot entry shipped in the pinned payload");

		// Act
		RequestInfoResponse detail = RequestInfoTool.CreateDetailResponse(
			closePage!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert — the authorable surface: explicitly empty, never inflated by base fields.
		detail.Parameters.Should().NotBeNull(
			because: "the producer publishes an explicit empty parameters map on crt.ClosePageRequest");
		detail.Parameters.Should().BeEmpty(
			because: "crt.ClosePageRequest accepts no authorable parameters");
		detail.BaseParameters.Should().NotBeNull(
			because: "root.references.baseParameters must surface as its own field");
		detail.BaseParameters!.Should().ContainKey("$context",
			because: "the platform-injected context is part of the published base surface");
		detail.BaseParameters.Should().ContainKey("$initialEvent",
			because: "the pinned producer snapshot publishes the deprecated initial event on BaseRequest");
		detail.BaseParameters["$initialEvent"].GetProperty("deprecated").GetBoolean().Should().BeTrue(
			because: "the detail response must preserve the producer's deprecation metadata");
		detail.BaseParameters["$initialEvent"].GetProperty("deprecationReason").GetString().Should()
			.Be("use event binding expression instead.",
				because: "the pinned producer snapshot directs consumers to event binding expressions");
		detail.Parameters.Should().NotContainKey("$context",
			because: "platform-injected fields must never leak into the authorable parameters map");
		detail.Parameters.Should().NotContainKey("$initialEvent",
			because: "deprecated BaseRequest fields remain platform-injected rather than authorable parameters");

		// Assert — wiring contract inlined via the closure seed.
		detail.References.Should().NotBeNull(
			because: "the payload publishes global typeDefinitions reachable from the wiring seed");
		detail.References!.TypeDefinitions.Should().ContainKey("RequestBindingConfig",
			because: "every request is wired through RequestBindingConfig, so the detail response inlines its schema");
	}

	[Test]
	[Description("A detail response against the pinned payload must surface the templateId parameter's environment valueSource verbatim at the DATA layer (parameters['templateId'].valueSource.tool == 'list-printables'), pinning the probe-routing contract as structured data rather than only as a guide-text substring.")]
	public void Pinned_Snapshot_Detail_Should_Surface_TemplateId_EnvironmentValueSource_Probe() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.PrintablesRequest", out RequestRegistryEntry? printables).Should().BeTrue(
			because: "crt.PrintablesRequest is the environment-valued request shipped in the pinned payload");

		// Act
		RequestInfoResponse detail = RequestInfoTool.CreateDetailResponse(
			printables!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert — the valueSource annotation survives as structured data on the parameter blob.
		detail.Parameters.Should().NotBeNull(
			because: "crt.PrintablesRequest declares authorable parameters");
		detail.Parameters!.Should().ContainKey("templateId",
			because: "templateId is the environment-valued parameter a probe fills");
		JsonElement templateId = detail.Parameters["templateId"];
		templateId.TryGetProperty("valueSource", out JsonElement valueSource).Should().BeTrue(
			because: "an environment-valued parameter must carry a valueSource so the agent routes to a probe instead of inventing the value");
		valueSource.GetProperty("kind").GetString().Should().Be("environment",
			because: "the value lives in the target environment, not the static catalog");
		valueSource.GetProperty("tool").GetString().Should().Be("list-printables",
			because: "templateId must be resolved from the list-printables probe - pinned at the data layer, not as a guide-text substring");
	}

	[Test]
	[Description("A detail response against the pinned payload must inline type definitions referenced ONLY through `keyType`/`valueType` strings — crt.OpenPageRequest's parameters map (valueType: JsonData, transitively JsonObject) and the RequestBindingConfig.params wiring hop (valueType: ...RequestParamBindingConfigValue...) — otherwise the response names types it never defines and stops being self-contained.")]
	public void Pinned_Snapshot_Detail_Should_Inline_ValueType_Referenced_TypeDefinitions() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.OpenPageRequest", out RequestRegistryEntry? openPage).Should().BeTrue(
			because: "crt.OpenPageRequest is the pinned entry whose parameters map declares valueType: JsonData");

		// Act
		RequestInfoResponse detail = RequestInfoTool.CreateDetailResponse(
			openPage!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert — the parameter-level valueType reference resolves...
		detail.References.Should().NotBeNull(
			because: "crt.OpenPageRequest references named types, so the detail must carry a typeDefinitions block");
		detail.References!.TypeDefinitions.Should().ContainKey("JsonData",
			because: "parameters.valueType names JsonData, and a named type must ship its definition");
		// ...transitively through the resolved type's own union string...
		detail.References.TypeDefinitions.Should().ContainKey("JsonObject",
			because: "JsonData's union references JsonObject, so the closure must pull it through");
		// ...and the wiring chain broken at the same valueType hop heals for every request.
		detail.References.TypeDefinitions.Should().ContainKey("RequestParamBindingConfigValue",
			because: "RequestBindingConfig.params references its value type only through a valueType string");
		// Following two more property names must not degrade the closure into "merge the whole global bag":
		// over-inclusion on real fixture data is the risk the widened tokenizer introduces.
		detail.References.TypeDefinitions.Should().NotContainKey("FilterGroup",
			because: "FilterGroup is reachable only from crt.RunBusinessProcessRequest.filters - crt.OpenPageRequest must not pull it in");
		detail.References.TypeDefinitions.Should().NotContainKey("SortColumnOptions",
			because: "sorting types belong to other requests; the closure must stay scoped to what this entry references");
		detail.References.TypeDefinitions.Should().NotContainKey("DefaultAttributeValue",
			because: "DefaultAttributeValue is crt.CreateRecordRequest's defaultValues item type, unreachable from crt.OpenPageRequest");
	}

	[Test]
	[Description("Content-level pin for the crt.CreateRecordRequest entry this fixture refresh added: defaultValues must surface as an array of DefaultAttributeValue with the item type's schema inlined through the closure, defaultValues itself must stay optional, and none of the three record-target parameters may carry a required flag — the producer models 'at least one of entityName / itemsAttributeName / entityPageName' in prose because a per-parameter flag cannot express a disjunctive contract.")]
	public void Pinned_Snapshot_Detail_Should_Pin_CreateRecordRequest_DefaultValues_Content() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.CreateRecordRequest", out RequestRegistryEntry? createRecord).Should().BeTrue(
			because: "crt.CreateRecordRequest is one of the record-page entries this fixture refresh added");

		// Act
		RequestInfoResponse detail = RequestInfoTool.CreateDetailResponse(
			createRecord!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert — the authorable surface carries exactly the four authored parameters.
		detail.Parameters.Should().NotBeNull(
			because: "crt.CreateRecordRequest declares authorable parameters");
		detail.Parameters!.Keys.Should().BeEquivalentTo(
			["defaultValues", "entityName", "entityPageName", "itemsAttributeName"],
			because: "the pinned entry authors exactly these parameters — a lost or extra key means the fixture and the producer diverged");
		// Assert — defaultValues content: an optional array whose items are DefaultAttributeValue entries.
		JsonElement defaultValues = detail.Parameters["defaultValues"];
		defaultValues.GetProperty("type").GetString().Should().Be("array",
			because: "defaultValues is a list of column pre-fills, not a single value");
		defaultValues.GetProperty("items").GetProperty("type").GetString().Should().Be("DefaultAttributeValue",
			because: "each entry follows the `{ attributeName, value }` contract named by the item type");
		defaultValues.TryGetProperty("required", out _).Should().BeFalse(
			because: "defaultValues is optional — omitting it opens the page with the entity's own defaults");
		// Assert — the disjunctive target contract stays prose, never per-parameter required flags.
		foreach (string targetParameter in new[] { "entityName", "entityPageName", "itemsAttributeName" }) {
			detail.Parameters[targetParameter].TryGetProperty("required", out _).Should().BeFalse(
				because: $"'{targetParameter}' alone is not required — the contract is 'at least one of the three', "
					+ "and a per-parameter required flag would misstate it");
		}
		// Assert — the item type named by defaultValues is inlined, so the response stays self-contained...
		detail.References.Should().NotBeNull(
			because: "the entry references named types, so the detail must carry a typeDefinitions block");
		detail.References!.TypeDefinitions.Should().ContainKey("DefaultAttributeValue",
			because: "a named item type must ship its definition on the same response");
		JsonElement attributeName = detail.References.TypeDefinitions!["DefaultAttributeValue"]
			.GetProperty("fields").GetProperty("attributeName");
		attributeName.GetProperty("required").GetBoolean().Should().BeTrue(
			because: "each defaultValues entry must name the column it pre-fills");
		// ...and the closure stays scoped: the mobile seeding twin must not ride along.
		detail.References.TypeDefinitions.Should().NotContainKey("ModelDefaultValue",
			because: "ModelDefaultValue is the model-seeding type reached from crt.OpenPageRequest's modelInitConfigs, "
				+ "unreachable from crt.CreateRecordRequest on the web flavor");
	}

	[Test]
	[Description("Content-level pin for the crt.UpdateRecordRequest entry this fixture refresh added: recordId must carry required:true and the 'string | number' union (0 is a valid Id, so number must not be dropped from the union), while entityName stays flag-free because it is disjunctively required with itemsAttributeName.")]
	public void Pinned_Snapshot_Detail_Should_Pin_UpdateRecordRequest_RecordId_Content() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.UpdateRecordRequest", out RequestRegistryEntry? updateRecord).Should().BeTrue(
			because: "crt.UpdateRecordRequest is one of the record-page entries this fixture refresh added");

		// Act
		RequestInfoResponse detail = RequestInfoTool.CreateDetailResponse(
			updateRecord!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert — the authorable surface carries exactly the three authored parameters.
		detail.Parameters.Should().NotBeNull(
			because: "crt.UpdateRecordRequest declares authorable parameters");
		detail.Parameters!.Keys.Should().BeEquivalentTo(
			["entityName", "itemsAttributeName", "recordId"],
			because: "the pinned entry authors exactly these parameters — a lost or extra key means the fixture and the producer diverged");
		// Assert — recordId content: the one genuinely required parameter, with the full Id union.
		JsonElement recordId = detail.Parameters["recordId"];
		recordId.GetProperty("required").GetBoolean().Should().BeTrue(
			because: "the handler shows a settings error and opens nothing without a recordId");
		recordId.GetProperty("type").GetString().Should().Be("string | number",
			because: "0 is a valid Id, so the number alternative must survive on the wire");
		// Assert — the disjunctive target contract stays prose, never per-parameter required flags.
		foreach (string targetParameter in new[] { "entityName", "itemsAttributeName" }) {
			detail.Parameters[targetParameter].TryGetProperty("required", out _).Should().BeFalse(
				because: $"'{targetParameter}' alone is not required — the contract is 'at least one of the two', "
					+ "and a per-parameter required flag would misstate it");
		}
	}

	[Test]
	[Description("Content-level pin for the crt.LoadDataRequest entry this fixture refresh added: refreshDataConfig — the parameter that selects the refresh scenario and the only one a \"Refresh data\" button authors — must surface typed RefreshDataConfig with its schema inlined through the closure (mode required, both wire values present), no parameter may carry a required flag because the runtime statically requires none, and the web-only showSuccessMessage must be present.")]
	public void Pinned_Snapshot_Detail_Should_Pin_LoadDataRequest_RefreshDataConfig_Content() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.LoadDataRequest", out RequestRegistryEntry? loadData).Should().BeTrue(
			because: "crt.LoadDataRequest is the refresh-action entry this fixture refresh added");

		// Act
		RequestInfoResponse detail = RequestInfoTool.CreateDetailResponse(
			loadData!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert — the authorable surface carries the refresh parameter plus the single-source load path.
		detail.Parameters.Should().NotBeNull(
			because: "crt.LoadDataRequest declares authorable parameters");
		detail.Parameters!.Keys.Should().BeEquivalentTo(
			["config", "dataSourceName", "parameters", "primaryDisplayFilterValue", "refreshDataConfig", "showSuccessMessage"],
			because: "the pinned entry authors exactly these parameters — a lost or extra key means the fixture and the producer diverged");
		// Assert — refreshDataConfig content: the typed refresh contract, and optional like every other parameter.
		JsonElement refreshDataConfig = detail.Parameters["refreshDataConfig"];
		refreshDataConfig.GetProperty("type").GetString().Should().Be("RefreshDataConfig",
			because: "the refresh scenario is carried by a named object type, not a loose JsonObject");
		detail.Parameters["showSuccessMessage"].GetProperty("type").GetString().Should().Be("boolean",
			because: "showSuccessMessage is the web-only refresh confirmation toggle — the mobile twin must not carry it");
		// Assert — no parameter is statically required: the runtime accepts a binding with any subset of them.
		foreach (string parameterName in detail.Parameters.Keys) {
			detail.Parameters[parameterName].TryGetProperty("required", out _).Should().BeFalse(
				because: $"'{parameterName}' is optional on the request class — the refresh-versus-single-source "
					+ "contract is disjunctive prose, and a per-parameter required flag would misstate it");
		}
		// Assert — the named refresh type is inlined, so the response stays self-contained...
		detail.References.Should().NotBeNull(
			because: "the entry references a named type, so the detail must carry a typeDefinitions block");
		detail.References!.TypeDefinitions.Should().ContainKey("RefreshDataConfig",
			because: "a named parameter type must ship its definition on the same response");
		JsonElement mode = detail.References.TypeDefinitions!["RefreshDataConfig"]
			.GetProperty("fields").GetProperty("mode");
		mode.GetProperty("required").GetBoolean().Should().BeTrue(
			because: "a refresh config without a mode selects no refresh scenario at all");
		mode.GetProperty("values").EnumerateArray().Select(value => value.GetString()).Should().BeEquivalentTo(
			["RefreshAll", "RefreshSpecific"],
			because: "these are the only two wire values the platform enum accepts");
		// ...and the conditional requirement of the RefreshSpecific target list stays prose, never a required flag.
		detail.References.TypeDefinitions["RefreshDataConfig"].GetProperty("fields")
			.GetProperty("targetDataSourceNames").TryGetProperty("required", out _).Should().BeFalse(
				because: "targetDataSourceNames is required only when mode is RefreshSpecific — a static flag "
					+ "would wrongly demand it for a RefreshAll binding");
	}

	private const string MobileSnapshotRelativePath = "Command/McpServer/Fixtures/MobileRequestRegistry.live-snapshot.json";

	[Test]
	[Description("The pinned MOBILE request-registry payload (https://academy.creatio.com/api/mcp/latest/MobileRequestRegistry.json) must deserialise through the same wrapped envelope as the web payload with no fields landing on an UnmappedExtensions bucket — the snapshot guard is intentionally symmetric across the web and mobile request flavors, mirroring the component-registry mobile guard.")]
	public void Pinned_Mobile_Request_Registry_Snapshot_Should_Have_No_Unmapped_Fields() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, MobileSnapshotRelativePath);
		File.Exists(snapshotPath).Should().BeTrue(
			because: $"the mobile snapshot fixture must be present at '{snapshotPath}' for this guard to be meaningful");

		// Act
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);

		// Assert — root-level envelope.
		state.GlobalReferences.Should().NotBeNull(
			because: "the mobile payload ships a top-level 'references' block (baseParameters + global typeDefinitions)");
		UnmappedKeys(state.GlobalReferences!.UnmappedExtensions).Should().BeEmpty(
			because: "any new key under root.references.* on the mobile registry must be mapped or explicitly allowlisted");

		// Assert — per-request entries.
		state.Entries.Should().NotBeEmpty(
			because: "the pinned mobile catalog must list at least one request");
		foreach (RequestRegistryEntry entry in state.Entries) {
			UnmappedKeys(entry.UnmappedExtensions).Should().BeEmpty(
				because: $"any new top-level key on mobile entry '{entry.RequestType}' must be mapped");
			if (entry.References is not null) {
				UnmappedKeys(entry.References.UnmappedExtensions).Should().BeEmpty(
					because: $"any new key under mobile '{entry.RequestType}'.references.* must be mapped");
			}
		}
	}

	[Test]
	[Description("A detail response against the pinned MOBILE payload keeps the platform-injected baseParameters separate from the authorable parameters map and surfaces the mobile-only crt.RunBusinessProcessRequest.activeRow parameter — proving the mobile registry carries a parameter surface distinct from desktop and that it flows through the shared detail factory unchanged.")]
	public void Pinned_Mobile_Snapshot_Detail_Should_Surface_MobileOnly_Parameter_And_Keep_BaseParameters_Separate() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, MobileSnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.RunBusinessProcessRequest", out RequestRegistryEntry? runProcess).Should().BeTrue(
			because: "crt.RunBusinessProcessRequest is shipped in the pinned mobile payload");

		// Act
		RequestInfoResponse detail = RequestInfoTool.CreateDetailResponse(
			runProcess!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert — the mobile-only parameter is present on the authorable surface.
		detail.Parameters.Should().NotBeNull(
			because: "crt.RunBusinessProcessRequest declares authorable parameters on mobile");
		detail.Parameters!.Should().ContainKey("activeRow",
			because: "activeRow is a mobile-only parameter with no desktop twin — it must surface from the mobile registry");
		// Assert — platform-injected base fields stay separate, never merged into parameters.
		detail.BaseParameters.Should().NotBeNull(
			because: "root.references.baseParameters must surface as its own field on the mobile flavor too");
		detail.BaseParameters!.Should().ContainKey("$context",
			because: "the platform-injected context is part of the published mobile base surface");
		detail.Parameters.Should().NotContainKey("$context",
			because: "platform-injected fields must never leak into the authorable parameters map");
		// Assert — wiring contract inlined via the closure seed.
		detail.References!.TypeDefinitions.Should().ContainKey("RequestBindingConfig",
			because: "every request is wired through RequestBindingConfig, so the mobile detail inlines its schema");
	}

	[Test]
	[Description("The MOBILE counterpart of the valueType closure pin, on the flavor whose parameter surface differs: mobile crt.OpenPageRequest declares parameters as Record<string, unknown> (the web flavor uses Record<string, JsonData>), so the closure must inline what the entry really reaches - the items type behind modelInitConfigs and the wiring chain behind RequestBindingConfig.params' valueType - while a lowercase `unknown` value type contributes nothing and unrelated globals stay out.")]
	public void Pinned_Mobile_Snapshot_Detail_Should_Resolve_TypeDefinitions_For_The_Mobile_Parameter_Shape() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, MobileSnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.OpenPageRequest", out RequestRegistryEntry? openPage).Should().BeTrue(
			because: "crt.OpenPageRequest ships in the pinned mobile payload");

		// Act
		RequestInfoResponse detail = RequestInfoTool.CreateDetailResponse(
			openPage!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert — everything the mobile entry genuinely reaches is inlined.
		detail.References.Should().NotBeNull(
			because: "the mobile entry references named types, so the detail must carry a typeDefinitions block");
		detail.References!.TypeDefinitions.Should().ContainKey("ModelInitConfig",
			because: "modelInitConfigs names its element type through items.type");
		detail.References.TypeDefinitions.Should().ContainKey("ModelDefaultValue",
			because: "ModelInitConfig.defaultValues reaches ModelDefaultValue transitively");
		detail.References.TypeDefinitions.Should().ContainKey("RequestParamBindingConfigValue",
			because: "the mobile RequestBindingConfig.params also names its value type only through a valueType string");
		detail.References.TypeDefinitions.Should().ContainKey("RequestParamsBindingConfig",
			because: "the same valueType union names the nested params config");

		// Assert — the mobile-specific parameter shape stays honest: `unknown` is a built-in, and
		// JsonData (the web value type for the same parameter) is not published on mobile at all.
		detail.References.TypeDefinitions.Should().NotContainKey("JsonData",
			because: "mobile declares parameters as Record<string, unknown>; JsonData is a web-only type definition");
		detail.References.TypeDefinitions.Should().NotContainKey("SortColumnOptions",
			because: "sorting types are reachable only from crt.RunBusinessProcessRequest on mobile");
	}

	[Test]
	[Description("Content-level pin for the MOBILE crt.CreateRecordRequest entry this fixture refresh added, on the exact points where it diverges from the web twin: defaultValues items are ModelDefaultValue (not the web-only DefaultAttributeValue), the mobile-only preventCardClose boolean is present, and the web-only entityPageName / itemsAttributeName parameters are absent because mobile resolves the entity through a page preprocessor instead.")]
	public void Pinned_Mobile_Snapshot_Detail_Should_Pin_CreateRecordRequest_Mobile_Divergence() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, MobileSnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.CreateRecordRequest", out RequestRegistryEntry? createRecord).Should().BeTrue(
			because: "crt.CreateRecordRequest is one of the record-page entries the mobile fixture refresh added");

		// Act
		RequestInfoResponse detail = RequestInfoTool.CreateDetailResponse(
			createRecord!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert — the mobile authorable surface: no entityPageName / itemsAttributeName, plus the mobile-only flag.
		detail.Parameters.Should().NotBeNull(
			because: "the mobile crt.CreateRecordRequest declares authorable parameters");
		detail.Parameters!.Keys.Should().BeEquivalentTo(
			["defaultValues", "entityName", "preventCardClose"],
			because: "mobile resolves the entity through a page preprocessor, so the web-only target parameters "
				+ "must not appear, while preventCardClose exists only on mobile");
		// Assert — defaultValues content: same array shape as web, but the mobile item type.
		JsonElement defaultValues = detail.Parameters["defaultValues"];
		defaultValues.GetProperty("type").GetString().Should().Be("array",
			because: "defaultValues is a list of attribute pre-fills on mobile too");
		defaultValues.GetProperty("items").GetProperty("type").GetString().Should().Be("ModelDefaultValue",
			because: "the mobile flavor seeds through ModelDefaultValue, not the web-only DefaultAttributeValue");
		detail.Parameters["preventCardClose"].GetProperty("type").GetString().Should().Be("boolean",
			because: "preventCardClose toggles the add-then-edit reopen behavior");
		// Assert — the item type is inlined and the web twin's item type stays out.
		detail.References.Should().NotBeNull(
			because: "the mobile entry references named types, so the detail must carry a typeDefinitions block");
		detail.References!.TypeDefinitions.Should().ContainKey("ModelDefaultValue",
			because: "a named item type must ship its definition on the same response");
		detail.References.TypeDefinitions.Should().NotContainKey("ModelInitConfig",
			because: "ModelInitConfig belongs to crt.OpenPageRequest's modelInitConfigs; crt.CreateRecordRequest must not pull it in");
	}

	[Test]
	[Description("Content-level pin for the MOBILE crt.UpdateRecordRequest entry this fixture refresh added, on the exact points where it diverges from the web twin: recordId is required but typed plain 'string' (no `| number` union on mobile), and the web-only itemsAttributeName parameter is absent because mobile resolves the entity through a page preprocessor instead.")]
	public void Pinned_Mobile_Snapshot_Detail_Should_Pin_UpdateRecordRequest_Mobile_Divergence() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, MobileSnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.UpdateRecordRequest", out RequestRegistryEntry? updateRecord).Should().BeTrue(
			because: "crt.UpdateRecordRequest is one of the record-page entries the mobile fixture refresh added");

		// Act
		RequestInfoResponse detail = RequestInfoTool.CreateDetailResponse(
			updateRecord!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert — the mobile authorable surface: no itemsAttributeName resolution on mobile.
		detail.Parameters.Should().NotBeNull(
			because: "the mobile crt.UpdateRecordRequest declares authorable parameters");
		detail.Parameters!.Keys.Should().BeEquivalentTo(
			["entityName", "recordId"],
			because: "mobile resolves the entity through a page preprocessor, so the web-only itemsAttributeName must not appear");
		// Assert — recordId content: required on both flavors, but mobile narrows the type to string.
		JsonElement recordId = detail.Parameters["recordId"];
		recordId.GetProperty("required").GetBoolean().Should().BeTrue(
			because: "the request does nothing without a recordId on mobile too");
		recordId.GetProperty("type").GetString().Should().Be("string",
			because: "the mobile flavor resolves bound values to the primary key string; the web 'string | number' union does not apply");
	}

	[Test]
	[Description("Content-level pin for the MOBILE crt.LoadDataRequest entry this fixture refresh added, on the exact points where it diverges from the web twin: the web-only showSuccessMessage is absent (no such field on the mobile request) and so is `parameters` (the mobile page format discards that key), while the mobile-only legacy `updateCache` alias is present. The shared RefreshDataConfig contract must still resolve through the closure on this flavor too.")]
	public void Pinned_Mobile_Snapshot_Detail_Should_Pin_LoadDataRequest_Mobile_Divergence() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, MobileSnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.LoadDataRequest", out RequestRegistryEntry? loadData).Should().BeTrue(
			because: "crt.LoadDataRequest is the refresh-action entry the mobile fixture refresh added");

		// Act
		RequestInfoResponse detail = RequestInfoTool.CreateDetailResponse(
			loadData!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert — the mobile authorable surface: no showSuccessMessage, no parameters, plus the legacy alias.
		detail.Parameters.Should().NotBeNull(
			because: "the mobile crt.LoadDataRequest declares authorable parameters");
		detail.Parameters!.Keys.Should().BeEquivalentTo(
			["config", "dataSourceName", "primaryDisplayFilterValue", "refreshDataConfig", "updateCache"],
			because: "mobile has no success-message field and discards a `parameters` key, while the legacy "
				+ "top-level updateCache alias exists only here");
		detail.Parameters.Should().NotContainKey("showSuccessMessage",
			because: "the mobile request class carries no such field — documenting it would promise a message that never appears");
		detail.Parameters.Should().NotContainKey("parameters",
			because: "the mobile page format drops a `parameters` key on this request, so it is not authorable there");
		detail.Parameters["updateCache"].GetProperty("type").GetString().Should().Be("boolean",
			because: "updateCache is the legacy top-level alias promoted into the config block's updateCache option");
		// Assert — the shared refresh contract resolves on the mobile flavor as well.
		detail.References.Should().NotBeNull(
			because: "the mobile entry references a named type, so the detail must carry a typeDefinitions block");
		detail.References!.TypeDefinitions.Should().ContainKey("RefreshDataConfig",
			because: "the refresh contract is published on both flavors — an agent authoring a mobile refresh needs its shape inlined");
		JsonElement mode = detail.References.TypeDefinitions!["RefreshDataConfig"]
			.GetProperty("fields").GetProperty("mode");
		mode.GetProperty("required").GetBoolean().Should().BeTrue(
			because: "a refresh config without a mode selects no refresh scenario on mobile either");
		mode.GetProperty("values").EnumerateArray().Select(value => value.GetString()).Should().BeEquivalentTo(
			["RefreshAll", "RefreshSpecific"],
			because: "the mobile runtime accepts the same two wire values as web");
		// ...and the web-only item type of the dropped `parameters` list must not ride along.
		detail.References.TypeDefinitions.Should().NotContainKey("ModelParameterConfig",
			because: "ModelParameterConfig is the item type of the web-only `parameters` list; the mobile flavor publishes no such typedef");
	}

	[TestCase(SnapshotRelativePath)]
	[TestCase(MobileSnapshotRelativePath)]
	[Description("Completeness guard against dangling type references, on BOTH request-registry flavors: every PascalCase identifier tokenised from a `type`/`keyType`/`valueType` string anywhere in the pinned payload (entry parameters, per-request typedefs, global typedefs, baseParameters) must resolve to a type definition the same payload publishes, or sit on the explicit built-in/platform allowlist. A named-but-undefined type is silently dropped from detail responses — the silent-data-loss mode the keyType/valueType closure fix removed — and this invariant catches EVERY typedef removed or renamed by a future fixture refresh, unlike the denylist of specific removed names it replaces. Prose mentions stay unchecked by design: `description` text may discuss types freely, and the closure never tokenises payload properties.")]
	public void Pinned_Snapshot_Every_Type_Reference_Should_Resolve_To_A_Published_TypeDefinition(string snapshotRelativePath) {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, snapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		List<string> typeReferences = state.Entries
			.SelectMany(entry => (entry.Parameters?.Values ?? Enumerable.Empty<JsonElement>())
				.Concat(entry.References?.TypeDefinitions?.Values ?? Enumerable.Empty<JsonElement>()))
			.Concat(state.GlobalReferences?.TypeDefinitions?.Values ?? Enumerable.Empty<JsonElement>())
			.Concat(state.GlobalReferences?.BaseParameters?.Values ?? Enumerable.Empty<JsonElement>())
			.SelectMany(TypeReferenceStrings)
			.ToList();
		HashSet<string> publishedTypeNames = new(
			(state.GlobalReferences?.TypeDefinitions?.Keys ?? Enumerable.Empty<string>())
				.Concat(state.Entries.SelectMany(entry =>
					entry.References?.TypeDefinitions?.Keys ?? Enumerable.Empty<string>())),
			System.StringComparer.Ordinal);
		// `Record` is the TypeScript built-in generic the closure silently drops. `File` is likewise a
		// platform built-in — the W3C File API interface, named by crt.UploadFileRequest.files (File[]) —
		// so no Creatio-side schema exists to publish for it (unlike the devkit's own LookupValue, which
		// the producer DOES publish). `ViewModelContext` is named only by the platform-injected
		// baseParameters.$context — never authorable, never a closure seed — so the producer
		// deliberately publishes no schema for it on either flavor.
		HashSet<string> knownUnpublishedTypeNames = new(System.StringComparer.Ordinal) { "File", "Record", "ViewModelContext" };

		// Act
		List<string> danglingTypeNames = typeReferences
			.SelectMany(PascalCaseIdentifiers)
			.Where(identifier => !publishedTypeNames.Contains(identifier))
			.Where(identifier => !knownUnpublishedTypeNames.Contains(identifier))
			.Distinct(System.StringComparer.Ordinal)
			.ToList();

		// Assert — the payload carries type references at all, so the sweep below is not vacuous...
		typeReferences.Should().NotBeEmpty(
			because: "request entries and type definitions declare types through `type` / `keyType` / `valueType` strings");
		// ...and every producer-defined-looking name resolves to a definition the payload actually ships.
		danglingTypeNames.Should().BeEmpty(
			because: "a type named by any `type`/`keyType`/`valueType` string but defined nowhere in the same payload "
				+ "would be silently dropped from detail responses — a typedef removal must take every reference "
				+ "with it, which a hand-maintained list of removed names cannot guarantee");
	}

	/// <summary>
	/// Collects every type-reference string (<c>type</c> / <c>keyType</c> / <c>valueType</c>) reachable in a
	/// payload element, skipping the producer's payload properties exactly as the production closure does.
	/// Deliberately duplicated here rather than reused: this test pins WHICH properties carry type references,
	/// so sharing the production set would make the guard agree with a regression in it.
	/// </summary>
	private static IEnumerable<string> TypeReferenceStrings(JsonElement element) {
		switch (element.ValueKind) {
			case JsonValueKind.Object:
				foreach (JsonProperty property in element.EnumerateObject()) {
					if (property.Name is "description" or "default" or "values" or "valueSource") {
						continue;
					}
					if ((property.Name is "type" or "keyType" or "valueType")
						&& property.Value.ValueKind == JsonValueKind.String) {
						string? value = property.Value.GetString();
						if (!string.IsNullOrEmpty(value)) {
							yield return value!;
						}
						continue;
					}
					foreach (string nested in TypeReferenceStrings(property.Value)) {
						yield return nested;
					}
				}
				break;
			case JsonValueKind.Array:
				foreach (JsonElement item in element.EnumerateArray()) {
					foreach (string nested in TypeReferenceStrings(item)) {
						yield return nested;
					}
				}
				break;
		}
	}

	/// <summary>
	/// Tokenises a type-reference string into candidate producer-defined type names using the same
	/// PascalCase heuristic as the production closure (identifiers starting with an uppercase letter;
	/// lowercase tokens are TypeScript built-ins like <c>string</c> or <c>unknown</c>). Deliberately
	/// duplicated for the same reason as <see cref="TypeReferenceStrings"/>: the completeness guard
	/// must not inherit a regression in the production tokenizer.
	/// </summary>
	private static IEnumerable<string> PascalCaseIdentifiers(string typeReference) =>
		Regex.Matches(typeReference, "[A-Za-z_][A-Za-z0-9_]*", RegexOptions.None, RegexTimeout)
			.Select(match => match.Value)
			.Where(token => char.IsUpper(token[0]));

	private static IEnumerable<string> UnmappedKeys(IDictionary<string, JsonElement>? bucket) =>
		bucket is null ? System.Array.Empty<string>() : bucket.Keys;
}

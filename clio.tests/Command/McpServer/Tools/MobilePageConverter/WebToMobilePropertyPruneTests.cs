using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// ENG-96589 — the converter drops source properties the target mobile component does not DECLARE.
/// <para>
/// Every case here runs against the PINNED LIVE mobile registry
/// (<c>Command/McpServer/Fixtures/MobileComponentRegistry.live-snapshot.json</c>) rather than a synthetic
/// contract, because the whole feature is an assertion about what the real producer publishes: a
/// hand-written registry would let the prune agree with a fiction. The one thing kept synthetic is the
/// GENERATION record, so the enablement gate can be driven independently of the fixture.
/// </para>
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class WebToMobilePropertyPruneTests {

	private const string LatestVersion = "latest";

	/// <summary>A stand on the last version whose published mobile registry is the web-derived generation.</summary>
	private const string FloorVersion = "10.0.0";

	// ---------------------------------------------------------------- enablement gate

	[Test]
	[Description("With no registry generation supplied at all — every pre-existing caller — nothing is pruned and the guide reports no runtime version. This is the compatibility contract the optional parameter exists for.")]
	public void Analyze_WithoutGeneration_ShouldPruneNothing() {
		// Arrange
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: null);

		// Assert
		Values(guide, "HelpTab").Should().ContainKey("icon",
			because: "a caller that supplies no registry generation must convert exactly as it did before ENG-96589");
		guide.PrunedProperties.Should().BeNull(
			because: "nothing was pruned, so the section must be omitted rather than shipped empty");
		guide.MobileRuntimeVersion.Should().BeNull(
			because: "no prune was measured, so naming a runtime would imply one was");
	}

	[Test]
	[Description("A payload WITHOUT the mobileRuntimeVersion marker is the web-derived generation: its per-component inputs describe the WEB component, so membership in it is not a valid test and nothing may be pruned.")]
	public void Analyze_WhenRegistryIsNotRuntimeDerived_ShouldPruneNothing() {
		// Arrange
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(LatestVersion, runtimeDerived: false));

		// Assert
		Values(guide, "HelpTab").Should().ContainKey("icon",
			because: "the old generation's inputs describe the web component — pruning against it would strip genuinely supported mobile properties");
		guide.PrunedProperties.Should().BeNull(
			because: "the gate refused, so there is nothing to report");
	}

	[TestCase("8.3.0")]
	[TestCase("8.3.4")]
	[TestCase("8.3.5")]
	[TestCase(FloorVersion)]
	[TestCase("10.0.0.934")]
	[TestCase("10.0")]
	[Description("A stand at or below the 10.0.0 floor never prunes, even when the loaded payload IS runtime-derived. 8.3.5 is the case that makes both halves of the gate necessary: it has no published versioned mobile registry, so the client falls back to `latest` and the marker is present — but the stand's own runtime is older than the one that catalog describes. 10.0.0.934 is the shape a real Creatio core version takes: compared raw, System.Version reads its Revision as ABOVE the floor's implicit -1 and would prune the very generation the floor is named after.")]
	public void Analyze_WhenEnvironmentIsAtOrBelowTheFloor_ShouldPruneNothing(string environmentVersion) {
		// Arrange
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(environmentVersion));

		// Assert
		Values(guide, "HelpTab").Should().ContainKey("icon",
			because: $"a stand on {environmentVersion} runs a mobile runtime older than the one `latest` was generated from, so pruning against it could strip properties that stand supports");
		guide.PrunedProperties.Should().BeNull(
			because: "backward compatibility means the conversion is byte-identical to the pre-feature one");
		guide.MobileRuntimeVersion.Should().BeNull(
			because: "an absent runtime version is how a caller tells that the prune did not run on this stand");
	}

	[TestCase(LatestVersion)]
	[TestCase("10.0.1")]
	[TestCase("10.1.0")]
	[TestCase("11.0.0")]
	[Description("`latest` and every version strictly above the 10.0.0 floor prune, and the guide names the runtime build the prune was measured against so the caller can audit it.")]
	public void Analyze_WhenEnvironmentIsAboveTheFloor_ShouldPrune(string environmentVersion) {
		// Arrange
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(environmentVersion));

		// Assert
		Values(guide, "HelpTab").Should().NotContainKey("icon",
			because: $"{environmentVersion} is above the floor and the payload is runtime-derived, so membership is a valid test");
		guide.MobileRuntimeVersion.Should().NotBeNull(
			because: "a caller must be able to see WHICH runtime build the prune was measured against");
		guide.MobileRuntimeVersion!.Commit.Should().NotBeNullOrWhiteSpace(
			because: "the commit is what makes the measurement reproducible");
	}

	[Test]
	[Description("A version string of \"latest\" that came from a FAILED probe does not open the gate. PlatformVersionResolver returns that literal for every failure class (no environment, missing CoreVersion, probe error, unparseable version), so reading it as \"this stand is newer than the floor\" would prune exactly the stand the floor protects — an 8.3.5 box whose cliogate is too old to answer is served the `latest` catalog and would be measured against a runtime it does not run.")]
	public void Analyze_WhenVersionIsNotPositivelyKnown_ShouldPruneNothing() {
		// Arrange
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act — exactly what a degraded probe produces: the string "latest", but no knowledge behind it.
		MobilePageConversionGuide guide = Analyze(
			bundle, generation: Generation(LatestVersion, versionKnown: false));

		// Assert
		Values(guide, "HelpTab").Should().ContainKey("icon",
			because: "a failed version probe says nothing about how new the stand is, so it cannot authorise pruning");
		guide.PrunedProperties.Should().BeNull(
			because: "the gate refused, so there is nothing to report");
		guide.MobileRuntimeVersion.Should().BeNull(
			because: "no measurement was made, so naming a runtime would imply one was");
	}

	[Test]
	[Description("An explicit caller-supplied version=latest DOES open the gate. This is the other side of the degraded-probe case: the two are indistinguishable by the version STRING and are separated only by whether the version is a positive statement about the target.")]
	public void Analyze_WhenCallerNamesLatestExplicitly_ShouldPrune() {
		// Arrange
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, generation: Generation(LatestVersion, versionKnown: true));

		// Assert
		Values(guide, "HelpTab").Should().NotContainKey("icon",
			because: "a caller that names the registry version is making a positive statement about what to measure against");
	}

	[Test]
	[Description("A payload whose inherited input surface is missing disables the prune entirely rather than pruning against a narrower union. Folding in an absent baseInputs would strip `visible` and `layoutConfig` from EVERY element of every page — and because the published contracts are built from the same function, the response would stay perfectly self-consistent while doing it.")]
	public void Analyze_WhenBaseInputsAreMissing_ShouldPruneNothing() {
		// Arrange
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, generation: Generation(LatestVersion, omitBaseInputs: true));

		// Assert
		JsonObject values = Values(guide, "HelpTab");
		values.Should().ContainKey("visible",
			because: "'visible' is declared ONLY in baseInputs, so a prune without it would remove it from every element");
		values.Should().ContainKey("icon",
			because: "the whole prune is disabled, not merely narrowed — a half-known contract is not a membership test");
		guide.MobileRuntimeVersion.Should().BeNull(
			because: "no usable measurement was possible, and advertising one would misrepresent what was checked");
	}

	[Test]
	[Description("The pinned fixture must stay the runtime-derived 66-component catalog. A regression to the old 35-entry curated snapshot would leave every assertion in this fixture passing vacuously, because the gate would simply switch the prune off.")]
	public void LiveMobileSnapshot_ShouldBeTheRuntimeDerivedCatalog() {
		// Arrange & Act
		ComponentCatalogState state = LiveMobileCatalog();

		// Assert
		state.MobileRuntimeVersion.Should().NotBeNull(
			because: "without the marker the gate refuses and every prune test below becomes a no-op that still passes");
		state.Entries.Count.Should().BeGreaterThan(60,
			because: "the runtime-derived catalog ships 66 components; a drop back to the 35-entry curated snapshot must fail loudly");
		state.GlobalReferences!.BaseInputs!.Keys.Should().Contain(["visible", "layoutConfig"],
			because: "these two are declared by ZERO components in their own inputs — the fixture must carry the inherited surface or the prune would strip them everywhere");
	}

	// ---------------------------------------------------------------- membership union

	[Test]
	[Description("A property declared only in the component's `outputs` survives. All 24 output keys in the live payload are absent from the same component's `inputs`, so an inputs-only union would strip every event/request binding on the page — this is the single highest-cost way to get the predicate wrong.")]
	public void Analyze_ShouldKeepPropertyDeclaredOnlyInOutputs() {
		// Arrange — crt.List declares `itemSelected` as an OUTPUT and not as an input.
		DeclaredNames("crt.List").Should().NotContain("itemSelected",
			because: "this test is only meaningful while the registry keeps itemSelected out of crt.List's own inputs");
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "Records", "type": "crt.List", "itemSelected": { "request": "crt.OpenPageRequest" } } ] } ]
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(LatestVersion));

		// Assert
		Values(guide, "Records").Should().ContainKey("itemSelected",
			because: "crt.List declares itemSelected in its outputs, and outputs are where the runtime-derived registry puts every binding");
	}

	[TestCase("crt.Toggle", "valueChange")]
	[TestCase("crt.SearchFilter", "valueChange")]
	[TestCase("crt.SegmentedButtonGroup", "changed")]
	[Description("Every binding the registry declares only under `outputs` survives the prune, across the component types that declare one.")]
	public void Analyze_ShouldKeepEveryOutputsOnlyBinding(string mobileType, string binding) {
		// Arrange
		PageBundleInfo bundle = Bundle($$"""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "Widget", "type": "{{mobileType}}", "{{binding}}": { "request": "crt.OpenPageRequest" } } ] } ]
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(LatestVersion));

		// Assert
		Values(guide, "Widget").Should().ContainKey(binding,
			because: $"{mobileType} declares {binding} in its outputs, which is part of the membership union");
	}

	[TestCase("visible")]
	[TestCase("layoutConfig")]
	[Description("A property declared ONLY in the registry's root references.baseInputs survives. `visible` and `layoutConfig` appear in zero of the 66 components' own inputs, so ignoring baseInputs would strip both from every element of every converted page — the second highest-cost way to get the predicate wrong.")]
	public void Analyze_ShouldKeepPropertyDeclaredOnlyInBaseInputs(string propertyName) {
		// Arrange — assert the premise against the live payload rather than trusting it.
		LiveMobileCatalog().Entries
			.Count(entry => entry.Inputs is not null && entry.Inputs.ContainsKey(propertyName))
			.Should().Be(0, because: $"'{propertyName}' must be inherited-only for this test to mean anything");
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "LeadName", "type": "crt.Input", "control": "$LeadName",
				  "visible": "$IsVisible", "layoutConfig": { "column": 1, "row": 2 } } ] } ]
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(LatestVersion));

		// Assert
		Values(guide, "LeadName").Should().ContainKey(propertyName,
			because: $"'{propertyName}' is declared in references.baseInputs, which every component inherits");
	}

	[Test]
	[Description("Membership is case-insensitive. The registry's own dictionaries are ordinal, so a predicate that indexed them directly — or that bound .Contains to the LINQ overload — would prune a property whose only fault is its casing.")]
	public void Analyze_ShouldMatchDeclaredPropertyNamesCaseInsensitively() {
		// Arrange — the registry spells it `caption`; the page carries `Caption`.
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "Note", "type": "crt.Label", "Caption": "Hello" } ] } ]
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(LatestVersion));

		// Assert
		Values(guide, "Note").Should().ContainKey("Caption",
			because: "the registry declares 'caption' and membership must not be decided by casing");
	}

	// ---------------------------------------------------------------- ticket regressions

	[Test]
	[Description("The reported case: a converted crt.TabContainer no longer carries icon/iconPosition. The mobile registry declares only caption + items for that type, and neither icon key is declared anywhere — not in the mobile registry, not in the web one, not on any Dart tab config class.")]
	public void Analyze_TabContainer_ShouldPruneIconAndIconPosition() {
		// Arrange
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(LatestVersion));

		// Assert
		JsonObject values = Values(guide, "HelpTab");
		values.Should().NotContainKey("icon", because: "mobile crt.TabContainer declares no icon slot");
		values.Should().NotContainKey("iconPosition", because: "mobile crt.TabContainer declares no icon-position slot");
		values.Should().ContainKey("caption", because: "caption IS declared and must survive");
		PrunedFor(guide, "HelpTab").Properties.Should().Contain(["icon", "iconPosition"],
			because: "a property the registry does not declare is REPORTED rather than dropped silently, so a genuine catalog gap stays visible");
	}

	[Test]
	[Description("A converted crt.GridContainer no longer carries `rows`. This is the case that is not merely ignored: a row track capped at a 0px maximum collapses the container's first row and the fields inside it never render, while validate-page and update-page --dry-run both pass.")]
	public void Analyze_GridContainer_ShouldPruneRows() {
		// Arrange
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "MarketingTabGridContainer", "type": "crt.GridContainer",
				  "rows": "minmax(max-content, 0)", "columns": [ "minmax(0, 1fr)" ], "items": [] } ] } ]
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(LatestVersion));

		// Assert
		JsonObject values = Values(guide, "MarketingTabGridContainer");
		values.Should().NotContainKey("rows",
			because: "mobile crt.GridContainer declares no 'rows' slot, and carrying it collapses the first row on a device");
		values.Should().ContainKey("columns",
			because: "'columns' IS declared — the prune must be surgical, not a blanket reset of the container");
	}

	[Test]
	[Description("A converted crt.ComboBox no longer carries `tooltip`. Harmless at runtime, but the same class of leak and the one most widely present on real form pages.")]
	public void Analyze_ComboBox_ShouldPruneTooltip() {
		// Arrange
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "Account", "type": "crt.ComboBox", "control": "$Account", "tooltip": "Pick an account" } ] } ]
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(LatestVersion));

		// Assert
		JsonObject values = Values(guide, "Account");
		values.Should().NotContainKey("tooltip", because: "mobile crt.ComboBox declares no tooltip slot");
		values.Should().ContainKey("control", because: "the value binding is declared and must never be pruned with it");
	}

	[Test]
	[Description("`dataSourceName` is pruned. It is declared by NO component and is not in baseInputs, and the decision taken for ENG-96589 is to trust the runtime-derived registry over the converter's older assumption that crt.Feed requires it. This test exists to make that a recorded decision rather than an accident: if the mobile runtime turns out to read the key, this is the test that must be inverted, together with the comments in WebToMobileAnalysisService and WebToMobilePageConversionRulesModels.")]
	public void Analyze_Feed_ShouldPruneDataSourceName_AndKeepWhatTheRegistryDeclares() {
		// Arrange — assert the premise across the WHOLE catalog, not just crt.Feed.
		ComponentCatalogState catalog = LiveMobileCatalog();
		catalog.Entries.Should().NotContain(
			entry => (entry.Inputs != null && entry.Inputs.ContainsKey("dataSourceName"))
				|| (entry.Outputs != null && entry.Outputs.ContainsKey("dataSourceName")),
			because: "no component declares dataSourceName in the runtime-derived catalog — that is what makes pruning it the registry's verdict rather than a guess");
		catalog.GlobalReferences!.BaseInputs!.Keys.Should().NotContain("dataSourceName",
			because: "…and it is not inherited either");
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "Feed", "type": "crt.Feed", "dataSourceName": "PDS",
				  "entitySchemaName": "SocialMessage", "primaryColumnValue": "$Id" } ] } ]
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(LatestVersion));

		// Assert
		JsonObject values = Values(guide, "Feed");
		values.Should().NotContainKey("dataSourceName",
			because: "the runtime-derived catalog is the authority on what the mobile runtime reads, and it does not name this key");
		values.Should().ContainKey("entitySchemaName",
			because: "crt.Feed DOES declare entitySchemaName — the ENG-91859 fear was that pruning would take this with it, and it must not");
		values.Should().ContainKey("primaryColumnValue",
			because: "the other declared Feed input must survive for the same reason");
		PrunedFor(guide, "Feed").Properties.Should().Contain("dataSourceName",
			because: "the removal is REPORTED, so a genuine catalog gap stays visible to the caller instead of looking like an unsupported property");
	}

	// ---------------------------------------------------------------- fail-safes

	[Test]
	[Description("A component that declares NOTHING carries everything. crt.AddressPreview ships zero inputs and zero outputs, which means the registry has no membership data for it — 'not described' must never be read as 'not supported', or the element would be reset to an empty shell.")]
	public void Analyze_ComponentDeclaringNothing_ShouldPruneNothing() {
		// Arrange — assert the premise from the live payload.
		ComponentRegistryEntry entry = LiveMobileCatalog().Lookup["crt.AddressPreview"];
		(entry.Inputs?.Count ?? 0).Should().Be(0, because: "this fail-safe is only exercised while the type declares no inputs");
		(entry.Outputs?.Count ?? 0).Should().Be(0, because: "…and no outputs either");
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "Where", "type": "crt.AddressPreview", "usrAnything": "x", "alsoThis": 1 } ] } ]
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(LatestVersion));

		// Assert
		JsonObject values = Values(guide, "Where");
		values.Should().ContainKey("usrAnything", because: "a type with an empty contract yields no membership information at all");
		values.Should().ContainKey("alsoThis", because: "the same reason — the prune fails open, never closed");
	}

	[Test]
	[Description("A component type the registry does not know at all carries everything. crt.Menu exists in the older catalog and was dropped from the runtime-derived one, so a page converted against a newer catalog must not have such an element stripped bare.")]
	public void Analyze_UnknownComponentType_ShouldPruneNothing() {
		// Arrange
		LiveMobileCatalog().Lookup.ContainsKey("crt.Menu").Should().BeFalse(
			because: "crt.Menu is absent from the runtime-derived catalog, which is what makes it the unknown-type case");
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "Actions", "type": "crt.Menu", "usrAnything": "x" } ] } ]
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(LatestVersion), extraMobileTypes: ["crt.Menu"]);

		// Assert
		Values(guide, "Actions").Should().ContainKey("usrAnything",
			because: "an unknown type yields no membership data, so nothing about it can be called undeclared");
	}

	[Test]
	[Description("The element's identity survives the prune: `type` stays inside values and `name` stays on the operation. `name` is never written into values in the first place (the operation carries it), so this pins the pair a caller actually applies — an operation that lost either would be unapplicable.")]
	public void Analyze_ShouldNeverPruneElementIdentity() {
		// Arrange
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(LatestVersion));

		// Assert
		ViewConfigDiffOperation operation = guide.ViewConfigDiff.Single(o => o.Name == "HelpTab");
		operation.Name.Should().Be("HelpTab",
			because: "the operation names the element it applies to, and the prune must never touch that");
		operation.Values!.AsObject()["type"]!.GetValue<string>().Should().Be("crt.TabContainer",
			because: "`type` is the one identity key that lives INSIDE values, so the prune guards it explicitly rather than trusting it to stay declared in baseInputs");
	}

	// ---------------------------------------------------------------- nesting boundary

	[Test]
	[Description("The prune is TOP-LEVEL only, so layoutConfig's inner keys are untouched even though the runtime-derived GridLayoutConfig declares only column/row/adaptive. The mobile Designer refuses to open a page whose layoutConfig omits colSpan/rowSpan (ENG-96114), and this is what keeps that requirement intact without an exemption list.")]
	public void Analyze_ShouldNotPruneInsideLayoutConfig() {
		// Arrange — the premise: the runtime's own layout type no longer names the spans.
		JsonElement gridLayout = LiveMobileCatalog().GlobalReferences!.TypeDefinitions!["GridLayoutConfig"];
		gridLayout.GetProperty("fields").TryGetProperty("colSpan", out _).Should().BeFalse(
			because: "the runtime-derived GridLayoutConfig declares only column/row/adaptive — which is exactly why a nested prune would break the Designer");
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.GridContainer", "columns": [ "minmax(0, 1fr)" ], "items": [
				{ "name": "LeadName", "type": "crt.Input", "control": "$LeadName",
				  "layoutConfig": { "column": 1, "row": 2, "colSpan": 1, "rowSpan": 1 } } ] } ]
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(LatestVersion));

		// Assert
		JsonObject layout = Values(guide, "LeadName")["layoutConfig"]!.AsObject();
		layout.Should().ContainKey("row", because: "placement survives the prune untouched");
		layout.Should().ContainKey("column", because: "placement survives the prune untouched");
		layout.Should().ContainKey("colSpan",
			because: "the mobile Designer refuses to open a page whose layoutConfig omits colSpan (ENG-96114), and the runtime-derived GridLayoutConfig no longer declares it — only the top-level-only rule keeps it");
		layout.Should().ContainKey("rowSpan",
			because: "same as colSpan: a nested prune would have removed it and made the page uneditable in the Designer");
	}

	// ---------------------------------------------------------------- pruned event bindings

	[Test]
	[Description("A binding on a component that DOES declare the property is untouched, and the request report still names it. This is the control for the test below: it proves the reclassification is driven by membership and not by the presence of a binding.")]
	public void Analyze_DeclaredBinding_ShouldSurviveAndStayReportedAsConverted() {
		// Arrange — crt.Button declares `clicked` as an input.
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "SaveButton", "type": "crt.Button", "caption": "Save",
				  "clicked": { "request": "crt.SaveRecordRequest" } } ] } ]
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, generation: Generation(LatestVersion), rules: RulesWithSaveRequest());

		// Assert
		Values(guide, "SaveButton").Should().ContainKey("clicked",
			because: "crt.Button declares clicked, so the action converts like any other");
		guide.RequestConversions!.ConvertedRequests.Should()
			.Contain(r => r.ElementName == "SaveButton" && r.Binding == "clicked",
				because: "a surviving binding must still be reported as converted");
		guide.PrunedProperties.Should().BeNull(
			because: "nothing on this page is undeclared");
	}

	[Test]
	[Description("When a pruned property carried an event binding, the ACTION is gone — not just an inert value. The guide must say so in the section callers read for actions: the record moves out of convertedRequests into droppedRequests with its own reason code, and prunedProperties marks it as a binding. Otherwise the guide names an action that the viewConfigDiff it ships does not contain.")]
	public void Analyze_PrunedBinding_ShouldMoveFromConvertedToDroppedRequests() {
		// Arrange — crt.Label declares no `clicked`, in inputs, outputs or baseInputs.
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "Note", "type": "crt.Label", "caption": "Save now",
				  "clicked": { "request": "crt.SaveRecordRequest" } } ] } ]
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, generation: Generation(LatestVersion), rules: RulesWithSaveRequest());

		// Assert
		Values(guide, "Note").Should().NotContainKey("clicked",
			because: "crt.Label declares no clicked slot, so the binding cannot reach the mobile runtime");
		guide.RequestConversions!.ConvertedRequests.Should()
			.NotContain(r => r.ElementName == "Note" && r.Binding == "clicked",
				because: "reporting it as converted would contradict the viewConfigDiff shipped in the same response");
		guide.RequestConversions.DroppedRequests.Should()
			.Contain(r => r.ElementName == "Note" && r.Binding == "clicked"
				&& r.Reason.Any(code => code.Code == "drop-request-property-not-declared"),
				because: "a lost ACTION must surface in the section a caller reads for actions, with the coded cause");
		PrunedFor(guide, "Note").Bindings.Should().Contain("clicked",
			because: "the pruned-properties record distinguishes a lost binding from an inert property");
		Values(guide, "Note").Should().ContainKey("caption",
			because: "the element itself still renders — only the action it could not carry is gone");
	}

	// ---------------------------------------------------------------- report self-consistency

	[Test]
	[Description("Nothing the conversion ships contradicts the contract it ships alongside it: every top-level key in every emitted values block is in that type's allowedProperties. The report and the prune are the same function, and this is the test that catches them diverging.")]
	public void Analyze_EveryEmittedValueKey_ShouldBeInTheTypeContract() {
		// Arrange
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "LeadName", "type": "crt.Input", "control": "$LeadName", "tooltip": "t", "usrWebOnly": "x" },
				{ "name": "Account", "type": "crt.ComboBox", "control": "$Account", "appearance": "outlined" },
				{ "name": "Note", "type": "crt.Label", "caption": "Hi", "labelStyle": "bold" } ] } ]
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(LatestVersion));

		// Assert
		Dictionary<string, IReadOnlyList<string>> contracts = guide.MobileContracts
			.ToDictionary(c => c.ComponentType, c => c.AllowedProperties, StringComparer.OrdinalIgnoreCase);
		foreach (ViewConfigDiffOperation operation in guide.ViewConfigDiff.Where(o => o.Values is JsonObject)) {
			string type = operation.Values!["type"]?.GetValue<string>();
			if (type is null || !contracts.TryGetValue(type, out IReadOnlyList<string> allowed)) {
				continue;
			}
			foreach (string key in operation.Values.AsObject().Select(p => p.Key)) {
				allowed.Should().Contain(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase),
					because: $"'{key}' survived on {operation.Name} ({type}), so the contract the same response publishes must declare it — otherwise the guide contradicts itself");
			}
		}
	}

	// ---------------------------------------------------------------- helpers

	/// <summary>
	/// A page whose tab carries the two keys from the reported defect. `icon`/`iconPosition` are declared
	/// nowhere — the converter simply carried them over from the web page.
	/// </summary>
	private static PageBundleInfo TabContainerCarryingIcons() => Bundle("""
		[ { "name": "Main", "type": "crt.FlexContainer", "items": [
			{ "name": "HelpTab", "type": "crt.TabContainer", "caption": "Help",
			  "icon": "help-icon", "iconPosition": "only-text", "visible": true, "items": [] } ] } ]
		""");

	private static PageBundleInfo Bundle(string viewConfigJson) => new() {
		ViewConfig = JsonNode.Parse(viewConfigJson)!.AsArray(),
		ModelConfig = new JsonObject(),
		ViewModelConfig = new JsonObject(),
		Resources = new PageResourceInfo(),
	};

	/// <summary>
	/// A generation record. Defaults to the runtime-derived case so a test names only what it varies; the
	/// baseInputs always come from the live fixture, since that is the half the prune must not invent.
	/// </summary>
	private static WebToMobileAnalysisService.MobileRegistryGeneration Generation(
		string requestedVersion,
		bool runtimeDerived = true,
		bool versionKnown = true,
		IReadOnlyDictionary<string, JsonElement> baseInputs = null,
		bool omitBaseInputs = false) =>
		new(requestedVersion, versionKnown, runtimeDerived, "main", "d7a0c3bb6796a1cde204ab8762b04d8940e38726",
			omitBaseInputs ? baseInputs : baseInputs ?? LiveMobileCatalog().GlobalReferences?.BaseInputs);

	/// <summary>
	/// A rules object carrying ONE direct request mapping, so a binding actually converts and lands in the
	/// request collectors. Without it every binding is unmapped and the reclassification below would be
	/// asserting against an empty collection.
	/// </summary>
	private static WebToMobilePageConversionRules RulesWithSaveRequest() => new() {
		Requests = [
			new RequestMappingRule {
				Web = "crt.SaveRecordRequest", Mobile = "crt.SaveRecordRequest", Category = "DirectMapping",
			}
		],
	};

	private static MobilePageConversionGuide Analyze(
		PageBundleInfo bundle,
		WebToMobileAnalysisService.MobileRegistryGeneration generation,
		IReadOnlyCollection<string> extraMobileTypes = null,
		WebToMobilePageConversionRules rules = null) {
		ComponentCatalogState mobile = LiveMobileCatalog();
		var mobileByType = mobile.Entries.ToDictionary(e => e.ComponentType, e => e, StringComparer.OrdinalIgnoreCase);
		var mobileTypes = new HashSet<string>(mobileByType.Keys, StringComparer.OrdinalIgnoreCase);
		foreach (string extra in extraMobileTypes ?? []) {
			// A type the conversion is allowed to TARGET but the registry does not describe — the unknown-type
			// fail-safe needs exactly that split, which is why the two sets are not the same object.
			mobileTypes.Add(extra);
		}
		return WebToMobileAnalysisService.Analyze(
			bundle, mobileTypes, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			webByType: new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase),
			mobileByType: mobileByType,
			rules: rules ?? new WebToMobilePageConversionRules(),
			templateRule: null,
			sourcePage: "UsrApp_FormPage",
			sourceTemplate: "PageWithTabsFreedomTemplate",
			suggestedTarget: "UsrApp_MobileFormPage",
			containerNameMap: null,
			mobileRegistryGeneration: generation);
	}

	private static JsonObject Values(MobilePageConversionGuide guide, string sourceName) {
		string target = guide.NameMap is not null && guide.NameMap.TryGetValue(sourceName, out string renamed)
			? renamed
			: sourceName;
		return guide.ViewConfigDiff.Single(operation => operation.Name == target).Values!.AsObject();
	}

	private static PrunedPropertyEntry PrunedFor(MobilePageConversionGuide guide, string name) {
		guide.PrunedProperties.Should().NotBeNull(because: "this element lost properties, so the section must be present");
		return guide.PrunedProperties!.Single(e => e.Name == name);
	}

	/// <summary>The names a component declares in its OWN inputs — deliberately not the full union.</summary>
	private static IEnumerable<string> DeclaredNames(string componentType) =>
		LiveMobileCatalog().Lookup[componentType].Inputs?.Keys ?? [];

	private static ComponentCatalogState LiveMobileCatalog() {
		string path = Path.Combine(
			TestContext.CurrentContext.TestDirectory, "Command", "McpServer", "Fixtures",
			"MobileComponentRegistry.live-snapshot.json");
		using FileStream stream = File.OpenRead(path);
		return ComponentInfoCatalog.LoadFromStream(stream);
	}
}

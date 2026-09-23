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
using JObject = Newtonsoft.Json.Linq.JObject;

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

	// ---------------------------------------------------------------- enablement gate

	[Test]
	[Description("With no registry generation supplied at all — every pre-existing caller — nothing is pruned and the guide says so. This is the compatibility contract the optional parameter exists for.")]
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
		guide.PropertyPruneApplied.Should().BeFalse(
			because: "this is the field a caller branches on, and it must say the prune did not run");
	}

	[Test]
	[Description("A payload carrying the WEB-derived inherited surface is the old generation — its per-component inputs describe the Angular component, so membership in it is not a valid support test and nothing may be pruned. The surface is what identifies the generation: the two sets are disjoint apart from name/type, and no web-derived payload has ever carried layoutConfig.")]
	public void Analyze_WhenInheritedSurfaceIsWebDerived_ShouldPruneNothing() {
		// Arrange — the real baseInputs of the published web-derived catalog (8.3.x / 10.0.0).
		var webBaseInputs = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
		foreach (string key in new[] { "classes", "id", "loading", "name", "shape", "styles", "tabIndex", "type" }) {
			webBaseInputs[key] = JsonSerializer.SerializeToElement(new { type = "string" });
		}
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, generation: Generation(baseInputs: webBaseInputs));

		// Assert
		Values(guide, "HelpTab").Should().ContainKey("icon",
			because: "the old generation's inputs describe the web component — pruning against it would strip genuinely supported mobile properties");
		guide.PrunedProperties.Should().BeNull(
			because: "the gate refused, so there is nothing to report");
		guide.MobileContracts.Should().NotBeEmpty(
			because: "a NotContain over an empty sequence is satisfied by having nothing to check — the page must "
				+ "produce contracts for the assertion below to mean anything");
		guide.MobileContracts.SelectMany(contract => contract.AllowedProperties)
			.Should().NotContain(declared => WebOnlyAttributes.Contains(declared),
				because: "allowedProperties is fed from the SAME baseInputs the prune folds in, so a refused gate must "
					+ "withhold them from the contract too — otherwise the guide advertises Angular element attributes "
					+ "as accepted mobile properties in the very field the guidance tells the agent to build values from");
	}

	[Test]
	[Description("Above the floor the inherited surface IS folded into allowedProperties. The counterpart to the web-derived case: without this, that assertion would pass on a contract that never folds baseInputs at all, and a caller could not see why `visible` survived the prune.")]
	public void Analyze_WhenGateIsOpen_ShouldPublishTheInheritedSurfaceInTheContract() {
		// Arrange
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation());

		// Assert
		guide.MobileContracts.Should().NotBeEmpty(because: "the page converts to known mobile types");
		guide.MobileContracts.Should().OnlyContain(
			contract => contract.AllowedProperties.Any(p => string.Equals(p, "visible", StringComparison.OrdinalIgnoreCase)),
			because: "`visible` is declared ONLY in references.baseInputs, so every contract must carry it once the "
				+ "gate is open — that fold is what makes allowedProperties the same set the prune enforces");
	}

	[Test]
	[Description("A runtime-derived payload prunes, and the guide reports that it did. There is no version dimension left to parameterise: the gate reads the CONTENT of the registry the chain served and nothing about the stand, so each platform version is covered by its own published file describing the runtime that version runs. The fixture also carries no mobileRuntimeVersion marker — exactly like the published catalog since 2026-09-17 — so this proves the prune never depended on that either.")]
	public void Analyze_WhenTheCatalogIsRuntimeDerived_ShouldPrune() {
		// Arrange
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation());

		// Assert
		Values(guide, "HelpTab").Should().NotContainKey("icon",
			because: "the payload is runtime-derived, so membership in it is a valid statement of mobile support");
		guide.PrunedProperties.Should().NotBeNull(
			because: "an undeclared property was carried, so the removal must be reported rather than done silently");
		guide.PropertyPruneApplied.Should().BeTrue(
			because: "the gate opened, so the caller-facing flag must say so — it is the only prune signal the response carries");
	}

	[Test]
	[Description("A payload whose inherited input surface is missing disables the prune entirely rather than pruning against a narrower union. Folding in an absent baseInputs would strip `visible` and `layoutConfig` from EVERY element of every page — and because the published contracts are built from the same function, the response would stay perfectly self-consistent while doing it.")]
	public void Analyze_WhenBaseInputsAreMissing_ShouldPruneNothing() {
		// Arrange
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, generation: Generation(omitBaseInputs: true));

		// Assert
		JsonObject values = Values(guide, "HelpTab");
		values.Should().ContainKey("visible",
			because: "'visible' is declared ONLY in baseInputs, so a prune without it would remove it from every element");
		values.Should().ContainKey("icon",
			because: "the whole prune is disabled, not merely narrowed — a half-known contract is not a membership test");
		guide.PropertyPruneApplied.Should().BeFalse(
			because: "a half-known contract disables the prune, and the flag must report that rather than look like a clean page");
	}

	[Test]
	[Description("The pinned fixture must stay the runtime-derived catalog. It is identified by CONTENT — the Flutter inherited surface — and not by the mobileRuntimeVersion marker, which the producer removed on 2026-09-17 while the content was unchanged. A regression to the web-derived or the old curated snapshot would leave every assertion in this fixture passing vacuously, because the gate would simply switch the prune off.")]
	public void LiveMobileSnapshot_ShouldBeTheRuntimeDerivedCatalog() {
		// Arrange & Act
		ComponentCatalogState state = LiveMobileCatalog();

		// Assert
		state.Entries.Count.Should().BeGreaterThan(60,
			because: "the runtime-derived catalog ships ~65 components; a drop back to the 35-entry curated snapshot must fail loudly");
		state.GlobalReferences!.BaseInputs!.Keys.Should().Contain(["visible", "layoutConfig"],
			because: "these two are declared by ZERO components in their own inputs — the fixture must carry the Flutter inherited surface, which is BOTH half the membership union and how the generation is recognised");
		state.GlobalReferences.BaseInputs.Keys.Should().NotContain("classes",
			because: "'classes' is an Angular element attribute carried only by the WEB-derived generation — its presence would mean the fixture regressed to the wrong catalog");
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
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation());

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
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation());

		// Assert
		Values(guide, "Widget").Should().ContainKey(binding,
			because: $"{mobileType} declares {binding} in its outputs, which is part of the membership union");
	}

	[TestCase("visible")]
	[TestCase("layoutConfig")]
	[Description("A property declared ONLY in the registry's root references.baseInputs survives. No component declares `visible` or `layoutConfig` in its own inputs, so ignoring baseInputs would strip both from every element of every converted page — the second highest-cost way to get the predicate wrong.")]
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
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation());

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
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation());

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
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation());

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
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation());

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
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation());

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
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation());

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
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation());

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
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation(), extraMobileTypes: ["crt.Menu"]);

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
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation());

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
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation());

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
			bundle, generation: Generation(), rules: RulesWithSaveRequest());

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
			bundle, generation: Generation(), rules: RulesWithSaveRequest());

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
		MobilePageConversionGuide guide = Analyze(bundle, generation: Generation());

		// Assert
		Dictionary<string, IReadOnlyList<string>> contracts = guide.MobileContracts
			.ToDictionary(c => c.ComponentType, c => c.AllowedProperties, StringComparer.OrdinalIgnoreCase);
		int checkedKeys = 0;
		foreach (ViewConfigDiffOperation operation in guide.ViewConfigDiff.Where(o => o.Values is JsonObject)) {
			string type = operation.Values!["type"]?.GetValue<string>();
			if (type is null || !contracts.TryGetValue(type, out IReadOnlyList<string> allowed)
				|| !PruneGovernsType(type)) {
				continue;
			}
			foreach (string key in operation.Values.AsObject().Select(p => p.Key)) {
				checkedKeys++;
				allowed.Should().Contain(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase),
					because: $"'{key}' survived on {operation.Name} ({type}), so the contract the same response publishes must declare it — otherwise the guide contradicts itself");
			}
		}
		checkedKeys.Should().BeGreaterThan(0,
			because: "the loop has to actually reach keys — one that iterated nothing would assert the invariant vacuously");
	}

	[Test]
	[Description("On a MERGE, a pruned binding is reported in prunedProperties but files NO droppedRequests record. Omitting a key from a merge payload means 'keep the template element's value', so the template's own binding may well go on firing — claiming the action was lost would be a statement the merge cannot support. This is the only case that separates the two branches, and without it deleting the distinction keeps every other test green.")]
	public void Analyze_PrunedBindingOnAMerge_ShouldNotClaimTheActionWasDropped() {
		// Arrange — crt.Label declares no `clicked`, and the mobile template already owns 'Note', so the
		// walk emits a merge rather than an insert.
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "Note", "type": "crt.Label", "caption": "Save now",
				  "clicked": { "request": "crt.SaveRecordRequest" } } ] } ]
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, generation: Generation(), rules: RulesWithSaveRequest(),
			autoTwinNames: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
				["Note"] = "crt.Label",
			});

		// Assert — RequestConversions is NULL here and that is the correct outcome, not a missing precondition:
		// the merge writer classifies the binding as converted, the prune reclassifies it back out, and the
		// section is omitted once every collection is empty. Analyze_DeclaredBindingOnAMerge_ShouldStillConvert
		// is what proves the writer classifies at all, so this case cannot pass through an unreached collector.
		guide.ViewConfigDiff.Single(o => o.Name == "Note").Operation.Should().Be("merge",
			because: "the whole point of this case is the merge branch — an insert here would assert the opposite rule");
		Values(guide, "Note").Should().NotContainKey("clicked",
			because: "the prune governs a merge payload exactly as it governs an insert: an undeclared slot is removed");
		PrunedFor(guide, "Note").Bindings.Should().Contain("clicked",
			because: "the caller still has to learn the key carried an ACTION rather than an inert value");
		(guide.RequestConversions?.DroppedRequests ?? []).Should()
			.NotContain(r => r.ElementName == "Note" && r.Binding == "clicked",
				because: "a merge that omits a key leaves the template's own value in place, so reporting the action "
					+ "as LOST would be a claim about the mobile template that this conversion never read");
	}

	[Test]
	[Description("`type` survives on a component that declares it nowhere — not in its own inputs, not in an inherited surface. Live baseInputs happens to declare it, so every other test would pass with the ExcludedSourceProps guard deleted; this is the only case that separates that guard from producer data it is not allowed to depend on. An insert without `type` names no component, so JsonDiffApplier cannot create the element. The list's other entry, `name`, is NOT exercised here and cannot be: no values writer copies `name` into a payload (BuildMobileValues, OverlayRenderedValues and BuildDeltaTwinMergeValues all exclude it), so the prune loop never sees that key. It stays in the list as a statement of intent, not as a reachable branch.")]
	public void Analyze_ShouldNeverPruneTheOperationsOwnIdentity() {
		// Arrange — a baseInputs surface that still identifies the runtime-derived generation (layoutConfig +
		// visible) but deliberately omits `name` and `type`.
		var withoutIdentity = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase) {
			["layoutConfig"] = JsonSerializer.SerializeToElement(new { type = "GridLayoutConfig" }),
			["visible"] = JsonSerializer.SerializeToElement(new { type = "boolean" }),
		};
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, generation: Generation(baseInputs: withoutIdentity));

		// Assert
		JsonObject values = Values(guide, "HelpTab");
		values.Should().ContainKey("type",
			because: "an insert without `type` names no component, so the applier cannot create the element at all");
		values.Should().NotContainKey("icon",
			because: "the prune must still be RUNNING here — otherwise the assertion above holds for the trivial "
				+ "reason that nothing was removed");
	}

	[Test]
	[Description("The control for the pruned-binding merge case: a DECLARED binding on the same merge path converts and is reported. Without it, that test's 'no dropped record' assertion could be satisfied by a merge writer which never classifies bindings at all — in which case there would be nothing to drop and nothing proved.")]
	public void Analyze_DeclaredBindingOnAMerge_ShouldStillConvert() {
		// Arrange — crt.Button DOES declare `clicked`, and the mobile template already owns 'SaveButton'.
		PageBundleInfo bundle = Bundle("""
			[ { "name": "Main", "type": "crt.FlexContainer", "items": [
				{ "name": "SaveButton", "type": "crt.Button", "caption": "Save",
				  "clicked": { "request": "crt.SaveRecordRequest" } } ] } ]
			""");

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, generation: Generation(), rules: RulesWithSaveRequest(),
			autoTwinNames: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
				["SaveButton"] = "crt.Button",
			});

		// Assert
		guide.ViewConfigDiff.Single(o => o.Name == "SaveButton").Operation.Should().Be("merge",
			because: "this control is only meaningful on the same path as the case it controls for");
		guide.RequestConversions!.ConvertedRequests.Should()
			.Contain(r => r.ElementName == "SaveButton" && r.Binding == "clicked",
				because: "the merge writer DOES classify bindings, which is what makes 'no dropped record' on a "
					+ "pruned merge a statement about the prune rather than about a collector never reached");
	}

	// ---------------------------------------------------------------- generation identification

	[Test]
	[Description("An inherited surface that keeps layoutConfig but loses `visible` disables the prune. Requiring BOTH is what makes the surface a generation test rather than a single-key guess — and a partial surface is exactly the shape a producer cleanup (moving `visible` into per-component inputs) would produce.")]
	public void Analyze_WhenInheritedSurfaceIsPartial_ShouldPruneNothing() {
		// Arrange
		var partial = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase) {
			["layoutConfig"] = JsonSerializer.SerializeToElement(new { type = "GridLayoutConfig" }),
		};
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, generation: Generation(baseInputs: partial));

		// Assert
		Values(guide, "HelpTab").Should().ContainKey("icon",
			because: "half an inherited surface is not a membership test — pruning against it would strip whatever the missing half declared");
		guide.PropertyPruneApplied.Should().BeFalse(
			because: "the refusal must be visible to the caller rather than looking like a page with nothing to prune");
	}

	[Test]
	[Description("The inherited surface is recognised case-insensitively. The registry's dictionaries come from System.Text.Json with the ORDINAL comparer, so an indexed lookup would make the whole feature hinge on the producer's casing — the same single-string fragility that made the provenance marker unusable as a gate.")]
	public void Analyze_ShouldRecogniseTheInheritedSurfaceRegardlessOfCasing() {
		// Arrange
		var oddCasing = new Dictionary<string, JsonElement>(StringComparer.Ordinal) {
			["LayoutConfig"] = JsonSerializer.SerializeToElement(new { type = "GridLayoutConfig" }),
			["Visible"] = JsonSerializer.SerializeToElement(new { type = "boolean" }),
		};
		PageBundleInfo bundle = TabContainerCarryingIcons();

		// Act
		MobilePageConversionGuide guide = Analyze(
			bundle, generation: Generation(baseInputs: oddCasing));

		// Assert
		guide.PropertyPruneApplied.Should().BeTrue(
			because: "the surface is present; only its casing differs, and casing must not decide whether a feature runs");
	}

	// ---------------------------------------------------------------- helpers

	/// <summary>
	/// True when the prune has membership data for <paramref name="mobileType"/>. A component the registry
	/// describes with NO properties of its own yields none, so the prune fails OPEN on it while its contract
	/// row still lists the inherited surface — that asymmetry is correct, and an invariant that ignored it
	/// would fail on a correct conversion.
	/// </summary>
	private static bool PruneGovernsType(string mobileType) {
		ComponentRegistryEntry entry = LiveMobileCatalog().Lookup.TryGetValue(mobileType, out ComponentRegistryEntry found)
			? found
			: null;
		return entry is not null
			&& ((entry.Inputs?.Count ?? 0) > 0 || (entry.Outputs?.Count ?? 0) > 0);
	}


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
	/// baseInputs come from the live fixture, since that is the half the prune must not invent.
	/// </summary>
	/// <remarks>
	/// It takes no version, because the gate has none: the record carries the loaded payload's inherited
	/// surface and nothing else. A test that wants the prune refused supplies a different SURFACE.
	/// </remarks>
	private static WebToMobileAnalysisService.MobileRegistryGeneration Generation(
		IReadOnlyDictionary<string, JsonElement> baseInputs = null,
		bool omitBaseInputs = false) =>
		new(omitBaseInputs ? null : baseInputs ?? LiveMobileCatalog().GlobalReferences?.BaseInputs);

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

	/// <summary>
	/// Runs the analysis. <paramref name="autoTwinNames"/> is what makes an element MERGE instead of insert:
	/// the walk emits an auto-twin when the web template declares a baseline node of that name AND the mobile
	/// template already owns an element of the same name and type. Without it every entry in this fixture is
	/// an insert, and the prune's merge branch — which deliberately files no droppedRequests record — is
	/// unreachable, so deleting that distinction would keep the suite green.
	/// </summary>
	private static MobilePageConversionGuide Analyze(
		PageBundleInfo bundle,
		WebToMobileAnalysisService.MobileRegistryGeneration generation,
		IReadOnlyCollection<string> extraMobileTypes = null,
		WebToMobilePageConversionRules rules = null,
		IReadOnlyDictionary<string, string> autoTwinNames = null) {
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
			mobileTemplateTypesByName: autoTwinNames,
			// The baseline is the WEB template's node for the same name. It is deliberately EMPTY: the merge
			// payload is the page's delta over it, so an empty baseline makes the delta the page's own values —
			// which is what puts the undeclared key into a merge payload at all.
			webTemplateBaselineNodes: autoTwinNames?.ToDictionary(pair => pair.Key, _ => new JObject()),
			mobileRegistryGeneration: generation);
	}

	/// <summary>
	/// The web-derived generation's <c>references.baseInputs</c> — Angular element attributes that no mobile
	/// component accepts. Named once so the contract assertion and the payload that produces it cannot drift.
	/// </summary>
	private static readonly HashSet<string> WebOnlyAttributes =
		new(["classes", "id", "loading", "shape", "styles", "tabIndex"], StringComparer.OrdinalIgnoreCase);

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

	/// <summary>
	/// The pinned live catalog, parsed ONCE. Nearly every test reads it — through <c>Generation</c>, through
	/// <c>Analyze</c>, and in its own premise assertion — so re-parsing per call meant parsing the whole
	/// fixture several times per test case. The state is immutable, so sharing it is safe.
	/// </summary>
	private static readonly Lazy<ComponentCatalogState> LiveCatalog = new(() => {
		string path = Path.Combine(
			TestContext.CurrentContext.TestDirectory, "Command", "McpServer", "Fixtures",
			"MobileComponentRegistry.live-snapshot.json");
		using FileStream stream = File.OpenRead(path);
		return ComponentInfoCatalog.LoadFromStream(stream);
	});

	private static ComponentCatalogState LiveMobileCatalog() => LiveCatalog.Value;
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobileComponentRegistry;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-101924 — the mobile binding check reads only the properties the mobile registry declares for the
/// element's type. Runs against the pinned live mobile registry and the body the Mobile Designer saved for
/// <c>UsrLeads_MobileFormPage</c> on a dev stand.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class MobileFieldBindingDeclaredInputsTests {

	private const string DeclaredAttributes =
		"""[{"operation":"merge","path":["attributes"],"values":{"UsrName":{}}}]""";

	private static readonly string[] TimelineTileInputs =
		["data", "linkedColumn", "sortedByColumn", "ownerColumn", "columnsFlexConfig"];

	[Test]
	[Description("Without the registry the Designer-saved Lead page fails on the eight bindings the platform provides — the defect ENG-101924 reproduces.")]
	public void ValidateMobileFieldBindings_DesignerLeadPageWithoutRegistry_ReportsEightPlatformBindings() {
		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(DesignerLeadBody());

		// Assert
		result.Errors.Should().HaveCount(8, because: "five list selection states and three timeline tile filters are undeclared");
		result.Errors.Count(e => e.Contains("_SelectionState")).Should().Be(5,
			because: "each of the five crt.List elements binds selectionState to its own undeclared attribute");
		result.Errors.Count(e => e.Contains("TimelineTile_")).Should().Be(3,
			because: "the Call, Email and Task tiles bind filters to their own undeclared _Items attribute");
	}

	[Test]
	[Description("With today's registry crt.List.selectionState is skipped, but crt.TimelineTile publishes no inputs yet, so its filters are still checked.")]
	public void ValidateMobileFieldBindings_DesignerLeadPageWithLiveRegistry_KeepsOnlyTimelineTileErrors() {
		// Arrange
		DeclaredPropertyIndex index = LiveIndex();
		index.ByType.Should().NotContainKey("crt.TimelineTile",
			because: "the premise is that the pinned registry publishes no crt.TimelineTile inputs");
		LiveCatalog().Lookup["crt.List"].Inputs!.Keys.Should().NotContain("selectionState",
			because: "the premise is that the mobile list never reads selectionState");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(DesignerLeadBody(), index);

		// Assert
		result.Errors.Should().HaveCount(3, because: "only the three timeline tile filters remain unprovable")
			.And.OnlyContain(e => e.Contains("TimelineTile_"),
				because: "a type with no registry inputs fails open, so all of its bindings are still checked");
	}

	[Test]
	[Description("Once the registry publishes the inputs TimelineTileConfig.fromJson reads, the Designer-saved Lead page validates with no errors.")]
	public void ValidateMobileFieldBindings_DesignerLeadPageWhenRegistryDeclaresTileInputs_ReturnsValid() {
		// Arrange
		DeclaredPropertyIndex index = IndexWithInputs("crt.TimelineTile", TimelineTileInputs);

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(DesignerLeadBody(), index);

		// Assert
		result.IsValid.Should().BeTrue(because: "filters is not a tile input, so its Designer binding is not checked");
		result.Errors.Should().BeEmpty(because: "every remaining binding on the page is declared");
	}

	[Test]
	[Description("A declared input bound to an undeclared attribute is still an error: a field whose control has no attribute renders empty and never saves.")]
	public void ValidateMobileFieldBindings_WhenDeclaredControlBindsUndeclaredAttribute_ReturnsError() {
		// Arrange
		LiveCatalog().Lookup["crt.Input"].Inputs!.Keys.Should().Contain("control",
			because: "the premise is that the registry declares the field's value binding");
		string body = Body("""{"operation":"insert","name":"Field","values":{"type":"crt.Input","control":"$UsrMissing"}}""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(body, LiveIndex());

		// Assert
		result.Errors.Should().ContainSingle(e => e.Contains("UsrMissing"),
			because: "narrowing the check to declared inputs must not hide a genuinely undeclared binding");
	}

	[Test]
	[Description("control is checked on every element, because the runtime copies it to bindTo whether or not the type declares it.")]
	public void ValidateMobileFieldBindings_WhenUndeclaredControlBindsUndeclaredAttribute_ReturnsError() {
		// Arrange
		LiveCatalog().Lookup["crt.Label"].Inputs!.Keys.Should().NotContain("control",
			because: "the premise is that crt.Label does not declare control");
		string body = Body("""{"operation":"insert","name":"Caption","values":{"type":"crt.Label","control":"$UsrMissing"}}""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(body, LiveIndex());

		// Assert
		result.Errors.Should().ContainSingle(e => e.Contains("UsrMissing"),
			because: "properties_attribute_preprocessor binds control on any named element");
	}

	[Test]
	[Description("Pins today's gap: the pinned registry does not declare crt.IndicatorWidget.sectionBindingColumnRecordId, so a binding there is not checked until mobile-app#813 is published.")]
	public void ValidateMobileFieldBindings_WhenLiveRegistryOmitsIndicatorRecordBinding_SkipsIt() {
		// Arrange
		string body = Body(
			"""{"operation":"insert","name":"Metric","values":{"type":"crt.IndicatorWidget","sectionBindingColumnRecordId":"$UsrMissing"}}""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(body, LiveIndex());

		// Assert
		result.Errors.Should().BeEmpty(
			because: "the key is not declared yet; this test should flip once the regenerated registry is pinned");
	}

	[Test]
	[Description("Once the registry declares the indicator's record binding, an indicator scoped to a missing attribute fails.")]
	public void ValidateMobileFieldBindings_WhenRegistryDeclaresIndicatorRecordBinding_ReturnsError() {
		// Arrange
		DeclaredPropertyIndex index = IndexWithInputs("crt.IndicatorWidget", "sectionBindingColumnRecordId");
		string body = Body(
			"""{"operation":"insert","name":"Metric","values":{"type":"crt.IndicatorWidget","sectionBindingColumnRecordId":"$UsrMissing"}}""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(body, index);

		// Assert
		result.Errors.Should().ContainSingle(e => e.Contains("UsrMissing"),
			because: "the indicator preprocessor reads this key, and an unbound value shows totals for every record");
	}

	[Test]
	[Description("A merge carries no type, so the registry cannot rule a property out and every binding in it is still checked.")]
	public void ValidateMobileFieldBindings_WhenMergeBindsUndeclaredAttribute_ReturnsError() {
		// Arrange
		string body = Body("""{"operation":"merge","name":"LeadList","values":{"selectionState":"$LeadList_SelectionState"}}""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(body, LiveIndex());

		// Assert
		result.Errors.Should().ContainSingle(e => e.Contains("LeadList_SelectionState"),
			because: "without a type the check fails open and keeps reporting the binding");
	}

	[Test]
	[Description("A disabled index — no registry was loaded — leaves the check exactly as it was.")]
	public void ValidateMobileFieldBindings_WhenIndexIsDisabled_ChecksEveryProperty() {
		// Arrange
		string body = Body("""{"operation":"insert","name":"LeadList","values":{"type":"crt.List","selectionState":"$LeadList_SelectionState"}}""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(
			body, DeclaredPropertyIndex.Disabled);

		// Assert
		result.Errors.Should().ContainSingle(e => e.Contains("LeadList_SelectionState"),
			because: "a disabled index declares everything, which is the pre-ENG-101924 behaviour");
	}

	[Test]
	[Description("Type and property names match case-insensitively, the way the registry lookup and the runtime treat them.")]
	public void ValidateMobileFieldBindings_WhenTypeAndPropertyCaseDiffers_MatchesCaseInsensitively() {
		// Arrange
		string body = Body("""
			{"operation":"insert","name":"LeadList","values":{"type":"CRT.LIST","SelectionState":"$LeadList_SelectionState"}},
			{"operation":"insert","name":"Field","values":{"type":"Crt.Input","Visible":"$UsrMissing"}}
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(body, LiveIndex());

		// Assert
		result.Errors.Should().ContainSingle(because: "only the declared visible binding is checked")
			.Which.Should().Contain("UsrMissing", because: "Visible matches the inherited visible input regardless of case");
	}

	[Test]
	[Description("Nested objects follow their top-level key: a binding under a declared key is checked, one under an undeclared key is not.")]
	public void ValidateMobileFieldBindings_WhenBindingIsNested_FollowsTopLevelDeclaration() {
		// Arrange
		string body = Body("""
			{"operation":"insert","name":"LeadList","values":{"type":"crt.List",
			  "itemLayout":{"type":"crt.ListItem","title":"$UsrDeclaredNestedMissing"},
			  "selectionState":{"inner":"$UsrUndeclaredNestedMissing"}}}
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(body, LiveIndex());

		// Assert
		result.Errors.Should().ContainSingle(e => e.Contains("UsrDeclaredNestedMissing"),
			because: "itemLayout is a declared crt.List input, so bindings inside it are checked");
		result.Errors.Should().NotContain(e => e.Contains("UsrUndeclaredNestedMissing"),
			because: "selectionState is not declared, so nothing under it is checked");
	}

	[Test]
	[TestCase("visible")]
	[TestCase("bindTo")]
	[Description("baseInputs keys count as declared on every type that declares something of its own.")]
	public void ValidateMobileFieldBindings_WhenBaseInputBindsUndeclaredAttribute_ReturnsError(string baseInput) {
		// Arrange
		LiveCatalog().GlobalReferences!.BaseInputs!.Keys.Should().Contain(baseInput,
			because: "the premise is that the key is declared in references.baseInputs");
		LiveCatalog().Lookup["crt.Input"].Inputs!.Keys.Should().NotContain(baseInput,
			because: "the premise is that crt.Input does not declare the key itself");
		string body = Body($$$"""{"operation":"insert","name":"Field","values":{"type":"crt.Input","{{{baseInput}}}":"$UsrMissing"}}""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(body, LiveIndex());

		// Assert
		result.Errors.Should().ContainSingle(e => e.Contains("UsrMissing"),
			because: "an inherited input is read by the runtime like any other declared input");
	}

	[Test]
	[Description("A custom type the registry does not know keeps the full check.")]
	public void ValidateMobileFieldBindings_WhenTypeIsUnknown_ChecksEveryProperty() {
		// Arrange
		string body = Body("""{"operation":"insert","name":"Custom","values":{"type":"usr.CustomWidget","anything":"$UsrMissing"}}""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateMobileFieldBindings(body, LiveIndex());

		// Assert
		result.Errors.Should().ContainSingle(e => e.Contains("UsrMissing"),
			because: "an unknown type has no membership data, so the check fails open");
	}

	[Test]
	[Description("validate-page builds the index from the mobile catalog it loads, so a binding in a property the registry does not declare is not reported.")]
	public async Task ValidatePage_WhenMobileCatalogIsLoaded_SkipsBindingInUndeclaredProperty() {
		// Arrange
		PageValidateTool tool = ToolWithLiveMobileCatalog();
		string body = Body("""{"operation":"insert","name":"LeadList","values":{"type":"crt.List","selectionState":"$LeadList_SelectionState"}}""");

		// Act
		PageValidateResponse response = await tool.ValidatePage(new PageValidateArgs(Body: body));

		// Assert
		response.Validation.Errors.Should().NotContain(e => e.Contains("LeadList_SelectionState"),
			because: "the tool must hand the loaded registry to the binding check, or the ENG-101924 defect returns");
	}

	[Test]
	[Description("The same wiring still reports a declared input bound to an undeclared attribute.")]
	public async Task ValidatePage_WhenMobileCatalogIsLoaded_ReportsUndeclaredControlBinding() {
		// Arrange
		PageValidateTool tool = ToolWithLiveMobileCatalog();
		string body = Body("""{"operation":"insert","name":"Field","values":{"type":"crt.Input","control":"$UsrMissing"}}""");

		// Act
		PageValidateResponse response = await tool.ValidatePage(new PageValidateArgs(Body: body));

		// Assert
		response.Validation.Errors.Should().Contain(e => e.Contains("UsrMissing"),
			because: "the skipped-property case above must not come from the tool dropping the binding check altogether");
	}

	[Test]
	[Description("validate-page reads the mobile registry for the version it is given, not always latest.")]
	public async Task ValidatePage_WhenVersionIsSupplied_LoadsThatRegistryVersion() {
		// Arrange
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		mobileCatalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(LiveCatalog());
		PageValidateTool tool = new(mobileCatalog, Substitute.For<IComponentInfoCatalog>(), new MockFileSystem(), new PageDataSourceReferenceValidator(new PageSchemaBodyParser()));
		string body = Body("""{"operation":"insert","name":"Field","values":{"type":"crt.Input","control":"$UsrName"}}""");

		// Act
		await tool.ValidatePage(new PageValidateArgs(Body: body, Version: "8.3.2.4199"));

		// Assert
		await mobileCatalog.Received(1).LoadAsync("8.3.2", Arg.Any<CancellationToken>());
	}

	internal static string Body(string viewConfigDiffEntries) =>
		$$"""{"viewConfigDiff":[{{viewConfigDiffEntries}}],"viewModelConfigDiff":{{DeclaredAttributes}}}""";

	private static string DesignerLeadBody() => File.ReadAllText(Path.Combine(
		TestContext.CurrentContext.TestDirectory, "Command", "McpServer", "Fixtures",
		"UsrLeadsMobileFormPage.designer-body.json"));

	private static PageValidateTool ToolWithLiveMobileCatalog() {
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		mobileCatalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(LiveCatalog());
		return new PageValidateTool(mobileCatalog, Substitute.For<IComponentInfoCatalog>(), new MockFileSystem(), new PageDataSourceReferenceValidator(new PageSchemaBodyParser()));
	}

	private static DeclaredPropertyIndex LiveIndex() {
		ComponentCatalogState state = LiveCatalog();
		return DeclaredPropertyIndex.FromRegistry(state.Lookup, state.GlobalReferences!.BaseInputs);
	}

	private static DeclaredPropertyIndex IndexWithInputs(string componentType, params string[] inputs) {
		ComponentCatalogState state = LiveCatalog();
		var lookup = new Dictionary<string, ComponentRegistryEntry>(state.Lookup, StringComparer.OrdinalIgnoreCase);
		Dictionary<string, JsonElement> declared = lookup.TryGetValue(componentType, out ComponentRegistryEntry existing)
			&& existing.Inputs is not null
				? new Dictionary<string, JsonElement>(existing.Inputs)
				: [];
		foreach (string input in inputs) {
			declared[input] = JsonSerializer.SerializeToElement(new { type = "string" });
		}
		lookup[componentType] = new ComponentRegistryEntry { ComponentType = componentType, Inputs = declared };
		return DeclaredPropertyIndex.FromRegistry(lookup, state.GlobalReferences!.BaseInputs);
	}

	internal static ComponentCatalogState LiveCatalog() => Catalog.Value;

	private static readonly Lazy<ComponentCatalogState> Catalog = new(() => {
		string path = Path.Combine(
			TestContext.CurrentContext.TestDirectory, "Command", "McpServer", "Fixtures",
			"MobileComponentRegistry.live-snapshot.json");
		using FileStream stream = File.OpenRead(path);
		return ComponentInfoCatalog.LoadFromStream(stream);
	});
}

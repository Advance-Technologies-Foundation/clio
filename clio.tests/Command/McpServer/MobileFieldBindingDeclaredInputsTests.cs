using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
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
	[Description("A type the registry has no inputs for keeps today's check: crt.List.selectionState is skipped, crt.TimelineTile.filters is not.")]
	public void ValidateMobileFieldBindings_DesignerLeadPageWhenTileTypeHasNoRegistryData_KeepsOnlyTimelineTileErrors() {
		// Arrange
		LiveCatalog().Lookup["crt.List"].Inputs!.Keys.Should().NotContain("selectionState",
			because: "the premise is that the mobile list never reads selectionState");
		DeclaredPropertyIndex index = WithoutType(LiveIndex(), "crt.TimelineTile");

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
		DeclaredPropertyIndex index = WithDeclaredInputs(LiveIndex(), "crt.TimelineTile", TimelineTileInputs);

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
	[Description("An indicator's record binding is checked once the registry declares it, so an indicator scoped to a missing attribute still fails.")]
	public void ValidateMobileFieldBindings_WhenIndicatorRecordBindingIsUndeclared_ReturnsError() {
		// Arrange
		DeclaredPropertyIndex index = WithDeclaredInputs(LiveIndex(), "crt.IndicatorWidget", "sectionBindingColumnRecordId");
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
	[Description("A disabled index — no registry, or the web-derived generation — leaves the check exactly as it was.")]
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

	private static string Body(string viewConfigDiffEntry) =>
		$$"""{"viewConfigDiff":[{{viewConfigDiffEntry}}],"viewModelConfigDiff":{{DeclaredAttributes}}}""";

	private static string DesignerLeadBody() => File.ReadAllText(Path.Combine(
		TestContext.CurrentContext.TestDirectory, "Command", "McpServer", "Fixtures",
		"UsrLeadsMobileFormPage.designer-body.json"));

	private static PageValidateTool ToolWithLiveMobileCatalog() {
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		mobileCatalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(LiveCatalog());
		return new PageValidateTool(mobileCatalog, Substitute.For<IComponentInfoCatalog>(), new MockFileSystem());
	}

	private static DeclaredPropertyIndex LiveIndex() {
		ComponentCatalogState state = LiveCatalog();
		return DeclaredPropertyIndex.Build(
			state.Lookup, new MobileRegistryGeneration(state.GlobalReferences!.BaseInputs));
	}

	private static DeclaredPropertyIndex WithDeclaredInputs(
		DeclaredPropertyIndex index, string componentType, params string[] inputs) {
		IEnumerable<string> existing = index.ByType.TryGetValue(componentType, out IReadOnlySet<string> declared)
			? declared
			: LiveCatalog().GlobalReferences!.BaseInputs!.Keys;
		var byType = new Dictionary<string, IReadOnlySet<string>>(index.ByType, StringComparer.OrdinalIgnoreCase) {
			[componentType] = new HashSet<string>(existing.Concat(inputs), StringComparer.OrdinalIgnoreCase)
		};
		return new DeclaredPropertyIndex(true, byType);
	}

	private static DeclaredPropertyIndex WithoutType(DeclaredPropertyIndex index, string componentType) {
		var byType = new Dictionary<string, IReadOnlySet<string>>(index.ByType, StringComparer.OrdinalIgnoreCase);
		byType.Remove(componentType);
		return new DeclaredPropertyIndex(true, byType);
	}

	private static readonly Lazy<ComponentCatalogState> Catalog = new(() => {
		string path = Path.Combine(
			TestContext.CurrentContext.TestDirectory, "Command", "McpServer", "Fixtures",
			"MobileComponentRegistry.live-snapshot.json");
		using FileStream stream = File.OpenRead(path);
		return ComponentInfoCatalog.LoadFromStream(stream);
	});

	private static ComponentCatalogState LiveCatalog() => Catalog.Value;
}

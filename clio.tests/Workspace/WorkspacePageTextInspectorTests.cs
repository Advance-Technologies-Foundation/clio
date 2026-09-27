using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Tests.Command;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Workspace;

[TestFixture]
[Property("Module", "Workspace")]
public class WorkspacePageTextInspectorTests : BaseClioModuleTests {
	private const string PackageName = "UsrPkg";
	private static readonly string PackagesFolderPath = Path.Combine(Path.GetTempPath(), "ws", "packages");
	private IWorkspacePathBuilder _workspacePathBuilder = null!;
	private IWorkspacePageTextInspector _inspector = null!;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		_workspacePathBuilder.PackagesFolderPath.Returns(PackagesFolderPath);
		containerBuilder.AddSingleton(_workspacePathBuilder);
	}

	public override void Setup() {
		base.Setup();
		_inspector = Container.GetRequiredService<IWorkspacePageTextInspector>();
	}

	[Test]
	[Description("Reports the schema and the offending node.property when a Freedom UI page sets a caption as an inline literal.")]
	public void Inspect_ShouldReportSchemaAndElement_WhenPageCaptionIsInlineLiteral() {
		// Arrange
		AddSchema("UsrPkg_FormPage", PageBody("\"caption\": \"Hello\""));

		// Act
		IReadOnlyList<PageTextFinding> findings = _inspector.Inspect([PackageName]);

		// Assert
		findings.Should().ContainSingle(
			because: "exactly one page schema in the package carries an inline user-visible literal");
		findings[0].PackageName.Should().Be(PackageName,
			because: "the finding must name the package that owns the schema");
		findings[0].SchemaName.Should().Be("UsrPkg_FormPage",
			because: "the finding must name the schema so the warning points at the file to fix");
		findings[0].Elements.Should().Equal(["UsrLabel.caption"],
			because: "the finding must name the offending view node and property, as the update-page gate does");
	}

	[Test]
	[Description("Reports nothing when the page caption is bound to a localizable resource.")]
	public void Inspect_ShouldReportNothing_WhenPageCaptionIsResourceBound() {
		// Arrange
		AddSchema("UsrPkg_FormPage", PageBody("\"caption\": \"$Resources.Strings.UsrLabel_caption\""));
		AddSchema("UsrPkg_ListPage", PageBody("\"caption\": \"#ResourceString(UsrLabel_caption)#\""));

		// Act
		IReadOnlyList<PageTextFinding> findings = _inspector.Inspect([PackageName]);

		// Assert
		findings.Should().BeEmpty(
			because: "both resource-binding forms are accepted by update-page, so push-workspace must not warn about them");
	}

	[Test]
	[Description("Skips schemas without a SCHEMA_VIEW_CONFIG_DIFF section and packages without a Schemas folder.")]
	public void Inspect_ShouldSkipNonPageSchemas_AndPackagesWithoutSchemas() {
		// Arrange
		AddSchema("UsrClassicModule", "define(\"UsrClassicModule\", [], function() { return { caption: \"Hello\" }; });");
		FileSystem.AddDirectory(Path.Combine(PackagesFolderPath, "UsrEmptyPkg"));

		// Act
		IReadOnlyList<PageTextFinding> findings = _inspector.Inspect([PackageName, "UsrEmptyPkg"]);

		// Assert
		findings.Should().BeEmpty(
			because: "only Freedom UI page bodies are subject to the localizable-text rule, and a package without schemas has nothing to check");
	}

	[Test]
	[Description("Reports a mobile Freedom UI page (plain-JSON body) whose caption is an inline literal, as update-page does for mobile pages.")]
	public void Inspect_ShouldReportMobilePage_WhenCaptionIsInlineLiteral() {
		// Arrange
		AddSchema("UsrMobile",
			"{\"viewConfigDiff\": [{\"operation\": \"insert\", \"name\": \"UsrLabel\", " +
			"\"values\": {\"type\": \"crt.Label\", \"caption\": \"Hello\"}}]}");

		// Act
		IReadOnlyList<PageTextFinding> findings = _inspector.Inspect([PackageName]);

		// Assert
		findings.Should().ContainSingle(
			because: "update-page rejects inline literals on mobile pages too, so push-workspace must warn about them");
		findings[0].SchemaName.Should().Be("UsrMobile",
			because: "the finding must name the mobile page schema");
		findings[0].Elements.Should().Equal(["UsrLabel.caption"],
			because: "the finding must name the offending mobile view node and property");
		findings[0].LiteralOnlyElements.Should().BeEmpty(
			because: "the mobile body carries no resource binding on a literal-only property");
	}

	[Test]
	[Description("Reports a resource binding on a literal-only property (crt.ImageInput.tooltip) separately from inline literals.")]
	public void Inspect_ShouldReportLiteralOnlyElements_WhenImageInputTooltipIsResourceBound() {
		// Arrange
		AddSchema("UsrPkg_FormPage", PageBody("\"caption\": \"$Resources.Strings.UsrLabel_caption\"",
			"{\"operation\": \"insert\", \"name\": \"UsrPhoto\", \"values\": {\"type\": \"crt.ImageInput\", " +
			"\"tooltip\": \"$Resources.Strings.UsrPhoto_tooltip\"}, \"parentName\": \"MainContainer\", " +
			"\"propertyName\": \"items\", \"index\": 1}"));

		// Act
		IReadOnlyList<PageTextFinding> findings = _inspector.Inspect([PackageName]);

		// Assert
		findings.Should().ContainSingle(
			because: "update-page rejects a resource binding on crt.ImageInput.tooltip because it renders empty");
		findings[0].Elements.Should().BeEmpty(
			because: "the label caption is correctly bound and must not be reported as an inline literal");
		findings[0].LiteralOnlyElements.Should().Equal(["UsrPhoto.tooltip"],
			because: "the literal-only binding must be reported on its own list so the warning can say to use a literal");
	}

	private void AddSchema(string schemaName, string body) =>
		FileSystem.AddFile(
			Path.Combine(PackagesFolderPath, PackageName, "Schemas", schemaName, $"{schemaName}.js"),
			new MockFileData(body));

	private static string PageBody(string captionProperty, string? extraOperation = null) =>
		"define(\"UsrPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {\n" +
		"\treturn {\n" +
		"\t\tviewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\": \"insert\", \"name\": \"UsrLabel\", " +
		"\"values\": {\"type\": \"crt.Label\", " + captionProperty + "}, \"parentName\": \"MainContainer\", " +
		"\"propertyName\": \"items\", \"index\": 0}" +
		(extraOperation is null ? string.Empty : ", " + extraOperation) + "]/**SCHEMA_VIEW_CONFIG_DIFF*/,\n" +
		"\t\tviewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,\n" +
		"\t\tmodelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,\n" +
		"\t\thandlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,\n" +
		"\t\tconverters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,\n" +
		"\t\tvalidators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/\n" +
		"\t};\n" +
		"});\n";
}

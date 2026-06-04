using System;
using System.IO;
using System.Text.Json.Nodes;
using Clio.Common;
using Clio.Package;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using AbstractionsFileSystem = System.IO.Abstractions.FileSystem;

namespace Clio.Tests.Package;

/// <summary>
/// Exercises <see cref="ThemeCreator"/> against the <b>real</b> shipped template
/// (<c>tpl/themes/theme.css.tpl</c>) and a real filesystem in a temp workspace, then asserts the
/// theme.json / theme.css artifacts on disk. Catches a malformed or not-copied-to-output template.
/// </summary>
[TestFixture]
[Category("Integration")]
[Property("Module", "Package")]
public class ThemeCreatorIntegrationTests {

	#region Constants: Private

	private const string CssClassName = "acme-dark-theme";
	private const string PackageName = "UsrThemes";

	#endregion

	#region Fields: Private

	private string _tempDir;
	private ThemeCreator _creator;

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_tempDir = Path.Combine(Path.GetTempPath(), "clio-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDir);

		AbstractionsFileSystem abstractionsFileSystem = new();
		IFileSystem fileSystem = new FileSystem(abstractionsFileSystem);
		ILogger logger = Substitute.For<ILogger>();
		IWorkingDirectoriesProvider workingDirectoriesProvider =
			new WorkingDirectoriesProvider(logger, abstractionsFileSystem);
		ITemplateProvider templateProvider = new TemplateProvider(workingDirectoriesProvider, fileSystem);
		IThemeArtifactBuilder artifactBuilder = new ThemeArtifactBuilder(templateProvider);

		IWorkspacePackageProvisioner packageProvisioner = Substitute.For<IWorkspacePackageProvisioner>();
		packageProvisioner.PackagesPath.Returns(Path.Combine(_tempDir, "packages"));

		_creator = new ThemeCreator(packageProvisioner, artifactBuilder, fileSystem);
	}

	[TearDown]
	public void TearDown() {
		if (Directory.Exists(_tempDir)) {
			Directory.Delete(_tempDir, true);
		}
	}

	[Test]
	[Description("Scaffolds theme.json and theme.css from the real shipped baseline into Files/themes/<cssClassName>/.")]
	public void CreateTheme_ShouldProduceThemeArtifacts_FromRealTemplate() {
		// Act
		ThemeIdentifiers ids = _creator.CreateTheme(CssClassName, PackageName);

		// Assert
		string themeDir = Path.Combine(_tempDir, "packages", PackageName, "Files", "themes", CssClassName);
		string themeJsonPath = Path.Combine(themeDir, "theme.json");
		string themeCssPath = Path.Combine(themeDir, "theme.css");

		File.Exists(themeJsonPath).Should().BeTrue("because theme.json must be scaffolded in the theme folder");
		File.Exists(themeCssPath).Should().BeTrue("because theme.css must be scaffolded in the theme folder");

		JsonObject json = JsonNode.Parse(File.ReadAllText(themeJsonPath)).AsObject();
		json["caption"]!.GetValue<string>().Should().Be("Acme Dark", "because the caption is derived from the class name");
		json["cssClassName"]!.GetValue<string>().Should().Be(CssClassName, "because the class name is persisted verbatim");
		Guid.TryParse(json["id"]!.GetValue<string>(), out _).Should().BeTrue("because id defaults to a generated UUID");

		string css = File.ReadAllText(themeCssPath);
		css.Should().Contain($".{CssClassName} {{", "because the baseline is scoped under the theme class")
			.And.NotContain("<%", "because all template tokens must be substituted")
			.And.Contain("--crt-palette-primary-500", "because the real baseline palette must be present");

		ids.CssClassName.Should().Be(CssClassName, "because the creator returns the resolved identifiers");
	}

	#endregion

}

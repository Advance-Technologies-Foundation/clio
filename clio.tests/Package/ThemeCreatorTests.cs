using System;
using System.Collections.Generic;
using System.IO;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class ThemeCreatorTests {

	#region Constants: Private

	private const string PackagesPath = @"C:\ws\packages";
	private const string CssClassName = "acme-dark-theme";
	private const string PackageName = "UsrThemes";

	#endregion

	#region Fields: Private

	private IWorkspacePackageProvisioner _packageProvisioner;
	private IThemeArtifactBuilder _artifactBuilder;
	private IFileSystem _fileSystem;
	private Dictionary<string, string> _writtenFiles;
	private ThemeCreator _creator;

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_writtenFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		_packageProvisioner = Substitute.For<IWorkspacePackageProvisioner>();
		_packageProvisioner.PackagesPath.Returns(PackagesPath);

		_artifactBuilder = Substitute.For<IThemeArtifactBuilder>();
		_artifactBuilder.DeriveIdentifiers(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(ci => new ThemeIdentifiers("AcmeDark", "Acme Dark", ci.ArgAt<string>(0)));
		_artifactBuilder.BuildThemeJson(Arg.Any<ThemeIdentifiers>()).Returns("{\"id\":\"AcmeDark\"}");
		_artifactBuilder.BuildThemeCss(Arg.Any<ThemeIdentifiers>()).Returns(".acme-dark-theme { }");

		_fileSystem = Substitute.For<IFileSystem>();
		_fileSystem.WhenForAnyArgs(fs => fs.WriteAllTextToFile(default, default))
			.Do(ci => _writtenFiles[ci.ArgAt<string>(0)] = ci.ArgAt<string>(1));

		_creator = new ThemeCreator(_packageProvisioner, _artifactBuilder, _fileSystem);
	}

	[Test]
	[Description("Validates identifiers, ensures the hosting package via the provisioner, and writes both theme files under Files/themes/<cssClassName>/.")]
	public void CreateTheme_ShouldValidateEnsurePackageAndWriteArtifacts() {
		// Act
		_creator.CreateTheme(CssClassName, PackageName);

		// Assert
		_artifactBuilder.Received(1).Validate(Arg.Any<ThemeIdentifiers>());
		_packageProvisioner.Received(1).EnsurePackage(PackageName, Arg.Any<Func<string, bool>>());

		string themeDir = Path.Combine(PackagesPath, PackageName, "Files", "themes", CssClassName);
		_writtenFiles.Should().ContainKey(Path.Combine(themeDir, "theme.json"),
			"because theme.json is scaffolded in the theme folder");
		_writtenFiles.Should().ContainKey(Path.Combine(themeDir, "theme.css"),
			"because theme.css is scaffolded in the theme folder");
	}

	[Test]
	[Description("Defaults the download decision to false when no callback is supplied so the provisioner never auto-downloads.")]
	public void CreateTheme_ShouldDefaultDownloadDecisionToFalse_WhenNoCallbackGiven() {
		// Arrange
		Func<string, bool> capturedDecision = null;
		_packageProvisioner.WhenForAnyArgs(p => p.EnsurePackage(default, default))
			.Do(ci => capturedDecision = ci.ArgAt<Func<string, bool>>(1));

		// Act
		_creator.CreateTheme(CssClassName, PackageName);

		// Assert
		capturedDecision.Should().NotBeNull(
			because: "the creator must always pass a decision callback to the provisioner");
		capturedDecision(PackageName).Should().BeFalse(
			because: "an omitted download callback must default to not downloading");
	}

	#endregion

}

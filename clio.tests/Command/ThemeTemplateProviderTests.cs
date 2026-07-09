namespace Clio.Tests.Command;

using System;
using System.IO;
using Clio.Command.Theming;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ThemeTemplateProviderTests : BaseClioModuleTests
{
	private static readonly string TemplateDir = Path.Combine("X", "tpl");
	private static readonly string ThemesRoot = Path.Combine(TemplateDir, "themes");

	private IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private IFileSystem _fileSystem;
	private IThemeTemplateProvider _provider;

	public override void Setup() {
		base.Setup();
		_provider = Container.GetRequiredService<IThemeTemplateProvider>();
		_workingDirectoriesProvider.TemplateDirectory.Returns(TemplateDir);
	}

	public override void TearDown() {
		_workingDirectoriesProvider.ClearReceivedCalls();
		_fileSystem.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		_fileSystem = Substitute.For<IFileSystem>();
		containerBuilder.AddTransient<IWorkingDirectoriesProvider>(_ => _workingDirectoriesProvider);
		containerBuilder.AddTransient<IFileSystem>(_ => _fileSystem);
	}

	private static string TemplatePath(string version, string fileName) => Path.Combine(ThemesRoot, version, fileName);

	private void StubBundledVersions(params string[] versions) {
		string[] dirs = new string[versions.Length];
		for (int i = 0; i < versions.Length; i++) {
			dirs[i] = Path.Combine(ThemesRoot, versions[i]);
		}
		_fileSystem.GetDirectories(ThemesRoot).Returns(dirs);
	}

	[Test, Category("Unit")]
	[Description("Returns the highest bundled version's CSS template when the target Creatio version is empty.")]
	public void GetCssTemplate_ShouldReturnHighestBundled_WhenTargetVersionEmpty() {
		// Arrange
		StubBundledVersions("9.0", "10.0");
		_fileSystem.ExistsFile(TemplatePath("10.0", "theme.css.tpl")).Returns(true);
		_fileSystem.ReadAllText(TemplatePath("10.0", "theme.css.tpl")).Returns("css-10");

		// Act
		string result = _provider.GetCssTemplate(string.Empty);

		// Assert
		result.Should().Be("css-10", because: "an empty target must resolve to the highest bundled version (10.0)");
	}

	[Test, Category("Unit")]
	[Description("Picks the highest bundled version not newer than the target when the target falls between bundled versions.")]
	public void GetCssTemplate_ShouldPickHighestNotNewerThanTarget_WhenTargetBetweenVersions() {
		// Arrange
		StubBundledVersions("8.0", "10.0");
		_fileSystem.ExistsFile(TemplatePath("8.0", "theme.css.tpl")).Returns(true);
		_fileSystem.ReadAllText(TemplatePath("8.0", "theme.css.tpl")).Returns("css-8");

		// Act
		string result = _provider.GetCssTemplate("9.0");

		// Assert
		result.Should().Be("css-8", because: "9.0 has no exact bundle, so the highest version not newer than 9.0 (8.0) is used");
	}

	[Test, Category("Unit")]
	[Description("Throws ArgumentException naming the minimum supported version when the target is below the lowest bundled version.")]
	public void GetCssTemplate_ShouldThrowArgumentException_WhenTargetBelowLowestBundled() {
		// Arrange
		StubBundledVersions("10.0");

		// Act
		Action act = () => _provider.GetCssTemplate("9.0");

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*Themes require Creatio 10.0 or newer*",
				because: "a target older than every bundled template is unsupported and the message must say so");
	}

	[Test, Category("Unit")]
	[Description("Throws InvalidOperationException when the resolved version folder is missing its template file.")]
	public void GetCssTemplate_ShouldThrowInvalidOperationException_WhenTemplateFileMissing() {
		// Arrange
		StubBundledVersions("10.0");

		// Act
		Action act = () => _provider.GetCssTemplate("10.0");

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a bundled version folder without its template file is a broken install, not a usable template");
	}

	[Test, Category("Unit")]
	[Description("Throws ArgumentException when the supplied Creatio version is not a valid version string.")]
	public void GetCssTemplate_ShouldThrowArgumentException_WhenVersionMalformed() {
		// Arrange
		StubBundledVersions("10.0");

		// Act
		Action act = () => _provider.GetCssTemplate("not-a-version");

		// Assert
		act.Should().Throw<ArgumentException>(
			because: "a malformed version cannot be matched against the bundled templates");
	}

	[Test, Category("Unit")]
	[Description("Reads the version-matched theme.json descriptor template for the resolved version.")]
	public void GetJsonTemplate_ShouldReturnResolvedVersionDescriptor_WhenTargetMatchesBundled() {
		// Arrange
		StubBundledVersions("10.0");
		_fileSystem.ExistsFile(TemplatePath("10.0", "theme.json.tpl")).Returns(true);
		_fileSystem.ReadAllText(TemplatePath("10.0", "theme.json.tpl")).Returns("{json-10}");

		// Act
		string result = _provider.GetJsonTemplate("10.0");

		// Assert
		result.Should().Be("{json-10}", because: "the descriptor template for the matched version (10.0) must be returned");
	}

	[Test, Category("Unit")]
	[Description("ResolveCompatibleVersion returns the highest bundled version (as a string) when the target is empty, without touching the network.")]
	public void ResolveCompatibleVersion_ShouldReturnHighestBundled_WhenTargetEmpty() {
		// Arrange
		StubBundledVersions("9.0", "10.0");

		// Act
		string resolved = _provider.ResolveCompatibleVersion(string.Empty);

		// Assert
		resolved.Should().Be("10.0", because: "an empty target resolves offline to the highest bundled version");
	}

	[Test, Category("Unit")]
	[Description("ResolveCompatibleVersion picks the highest bundled version not newer than the target.")]
	public void ResolveCompatibleVersion_ShouldPickHighestNotNewer_WhenTargetBetween() {
		// Arrange
		StubBundledVersions("8.0", "10.0");

		// Act
		string resolved = _provider.ResolveCompatibleVersion("9.0");

		// Assert
		resolved.Should().Be("8.0", because: "9.0 has no exact bundle, so the highest not-newer version (8.0) is used");
	}

	[Test, Category("Unit")]
	[Description("ResolveCompatibleVersion throws ArgumentException when the target is below the lowest bundled version.")]
	public void ResolveCompatibleVersion_ShouldThrow_WhenTargetBelowLowest() {
		// Arrange
		StubBundledVersions("10.0");

		// Act
		Action act = () => _provider.ResolveCompatibleVersion("9.0");

		// Assert
		act.Should().Throw<ArgumentException>(
			because: "a target older than every bundled template is unsupported");
	}

}

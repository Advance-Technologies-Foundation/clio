using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Package;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public sealed class EnvironmentPackageFolderResolverTests {

	private static readonly string SiteRoot = Path.Combine(Path.GetTempPath(), "clio-1746-site");

	private MockFileSystem _mockFileSystem;
	private ISettingsRepository _settingsRepository;
	private EnvironmentPackageFolderResolver _resolver;

	[SetUp]
	public void SetUp() {
		_mockFileSystem = new MockFileSystem();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_resolver = new EnvironmentPackageFolderResolver(_settingsRepository, new Clio.Common.FileSystem(_mockFileSystem));
	}

	[Test]
	[Description("A .NET 8 site keeps its packages in Terrasoft.Configuration/Pkg under the registered EnvironmentPath.")]
	public void Resolve_ShouldReturnPackageFolder_WhenNetCoreSiteHasThePackage() {
		// Arrange
		string packageFolder = Path.Combine(SiteRoot, "Terrasoft.Configuration", "Pkg", "UsrPkg");
		_mockFileSystem.AddDirectory(packageFolder);
		Register(new EnvironmentSettings { EnvironmentPath = SiteRoot, IsNetCore = true });

		// Act
		EnvironmentPackageFolderResolution result = _resolver.Resolve("dev", "UsrPkg");

		// Assert
		result.IsResolved.Should().BeTrue(because: "the package folder exists under the registered site folder");
		result.PackageFolderPath.Should().Be(packageFolder, because: "the .NET 8 layout has no Terrasoft.WebApp level");
	}

	[Test]
	[Description("A .NET Framework site keeps its packages in Terrasoft.WebApp/Terrasoft.Configuration/Pkg.")]
	public void Resolve_ShouldReturnPackageFolder_WhenNetFrameworkSiteHasThePackage() {
		// Arrange
		string packageFolder = Path.Combine(SiteRoot, "Terrasoft.WebApp", "Terrasoft.Configuration", "Pkg", "UsrPkg");
		_mockFileSystem.AddDirectory(packageFolder);
		Register(new EnvironmentSettings { EnvironmentPath = SiteRoot, IsNetCore = false });

		// Act
		EnvironmentPackageFolderResolution result = _resolver.Resolve("dev", "UsrPkg");

		// Assert
		result.PackageFolderPath.Should().Be(packageFolder,
			because: "a .NET Framework site nests the configuration under Terrasoft.WebApp");
	}

	[Test]
	[Description("A site folder passed by the caller wins over the registered EnvironmentPath.")]
	public void Resolve_ShouldUseOverride_WhenCallerPassesASiteFolder() {
		// Arrange
		string otherSite = Path.Combine(Path.GetTempPath(), "clio-1746-other-site");
		string packageFolder = Path.Combine(otherSite, "Terrasoft.Configuration", "Pkg", "UsrPkg");
		_mockFileSystem.AddDirectory(packageFolder);
		_mockFileSystem.AddDirectory(Path.Combine(SiteRoot, "Terrasoft.Configuration", "Pkg", "UsrPkg"));
		Register(new EnvironmentSettings { EnvironmentPath = SiteRoot, IsNetCore = true });

		// Act
		EnvironmentPackageFolderResolution result = _resolver.Resolve("dev", "UsrPkg", otherSite);

		// Assert
		result.PackageFolderPath.Should().Be(packageFolder, because: "an explicit --ep names the site the caller means");
	}

	[Test]
	[Description("Without a registered EnvironmentPath the resolver says so and names how to register it.")]
	public void Resolve_ShouldExplainHowToRegisterSiteFolder_WhenEnvironmentPathIsMissing() {
		// Arrange
		Register(new EnvironmentSettings { EnvironmentPath = string.Empty });

		// Act
		EnvironmentPackageFolderResolution result = _resolver.Resolve("dev", "UsrPkg");

		// Assert
		result.IsResolved.Should().BeFalse(because: "there is no site folder to look in");
		result.Problem.Should().Contain("EnvironmentPath", because: "the caller must learn which setting is missing")
			.And.Contain("--ep", because: "the problem names how to supply the folder");
	}

	[Test]
	[Description("A site registered with a folder that is not on this machine is reported, not thrown.")]
	public void Resolve_ShouldReportMissingPkgFolder_WhenSiteFolderIsNotOnThisMachine() {
		// Arrange
		Register(new EnvironmentSettings { EnvironmentPath = SiteRoot, IsNetCore = true });

		// Act
		EnvironmentPackageFolderResolution result = _resolver.Resolve("dev", "UsrPkg");

		// Assert
		result.IsResolved.Should().BeFalse(because: "neither Pkg layout exists under the site folder");
		result.Problem.Should().Contain(SiteRoot, because: "the problem names the folder that was searched");
	}

	[Test]
	[Description("A package that is not in the Pkg folder is reported with the path that was checked.")]
	public void Resolve_ShouldReportMissingPackageFolder_WhenPackageIsNotInPkg() {
		// Arrange
		_mockFileSystem.AddDirectory(Path.Combine(SiteRoot, "Terrasoft.Configuration", "Pkg", "OtherPkg"));
		Register(new EnvironmentSettings { EnvironmentPath = SiteRoot, IsNetCore = true });

		// Act
		EnvironmentPackageFolderResolution result = _resolver.Resolve("dev", "UsrPkg");

		// Assert
		result.IsResolved.Should().BeFalse(because: "the package has no folder in Pkg");
		result.Problem.Should().Contain("UsrPkg", because: "the problem names the missing package folder");
	}

	[TestCase("..")]
	[TestCase("../Other")]
	[TestCase("")]
	[Description("A package name that is not a single folder name is refused before any path is built.")]
	public void Resolve_ShouldRefuse_WhenPackageNameIsNotASingleFolderName(string packageName) {
		// Arrange
		_mockFileSystem.AddDirectory(Path.Combine(SiteRoot, "Terrasoft.Configuration", "Pkg"));
		Register(new EnvironmentSettings { EnvironmentPath = SiteRoot, IsNetCore = true });

		// Act
		EnvironmentPackageFolderResolution result = _resolver.Resolve("dev", packageName);

		// Assert
		result.IsResolved.Should().BeFalse(because: "a relative or empty name could point outside the Pkg folder");
		_settingsRepository.DidNotReceiveWithAnyArgs().FindEnvironment(default);
	}

	private void Register(EnvironmentSettings settings) {
		_settingsRepository.FindEnvironment("dev").Returns(settings);
	}
}

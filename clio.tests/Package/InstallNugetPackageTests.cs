using System;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

/// <summary>
/// install-nuget-pkg reports packages the environment did not install as a failure.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public sealed class InstallNugetPackageTests {

	[Test]
	[Description("When the package installer reports the installation as failed, Install throws naming the NuGet packages, so install-nuget-pkg exits 1 instead of logging Done.")]
	public void Install_ShouldThrowNamingThePackages_WhenTheInstallerReportsAFailure() {
		// Arrange
		IPackageInstaller packageInstaller = Substitute.For<IPackageInstaller>();
		packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), Arg.Any<PackageInstallOptions>(),
			Arg.Any<string>(), Arg.Any<bool>()).Returns(false);
		IPackageArchiver packageArchiver = Substitute.For<IPackageArchiver>();
		packageArchiver.GetPackedGroupPackagesFileName(Arg.Any<string>()).Returns("packages.zip");
		IWorkingDirectoriesProvider workingDirectories = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectories
			.When(provider => provider.CreateTempDirectory(Arg.Any<Action<string>>()))
			.Do(call => call.Arg<Action<string>>()(System.IO.Path.GetTempPath()));
		InstallNugetPackage sut = new(new EnvironmentSettings(), Substitute.For<INuGetManager>(), packageInstaller,
			packageArchiver, workingDirectories);

		// Act
		Action act = () => sut.Install("UsrNuget", "1.0.0", "https://feed.example.invalid");

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the environment reported the installation as failed")
			.WithMessage("NuGet packages may not have been installed: UsrNuget*",
				because: "the closing error names what was not installed; the installer has already logged why");
	}
}

using System;
using System.Linq;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using Clio.WebApplication;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IClioFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Package;

/// <summary>
/// Covers package-root preservation while packages are downloaded and overwritten individually.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public sealed class PackageDownloaderTests : BaseClioModuleTests {

	private IApplicationPing _applicationPing;
	private IClioFileSystem _clioFileSystem;
	private IPackageArchiver _packageArchiver;
	private IPackageDownloader _packageDownloader;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		IOwnedApplicationClient applicationClient = Substitute.For<IOwnedApplicationClient>();
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		applicationClientFactory.CreateClient(EnvironmentSettings).Returns(applicationClient);
		_packageArchiver = Substitute.For<IPackageArchiver>();
		IApplicationDownloader applicationDownloader = Substitute.For<IApplicationDownloader>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>(), EnvironmentSettings)
			.Returns("https://example.invalid/package");
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider
			.When(provider => provider.CreateTempDirectory(Arg.Any<Action<string>>()))
			.Do(call => call.Arg<Action<string>>()(FileSystem.Path.Combine("temp")));
		_applicationPing = Substitute.For<IApplicationPing>();
		_applicationPing.TryPing(EnvironmentSettings, out Arg.Any<string>()).Returns(true);
		_clioFileSystem = Substitute.For<IClioFileSystem>();

		containerBuilder.AddSingleton(applicationClientFactory);
		containerBuilder.AddSingleton(_packageArchiver);
		containerBuilder.AddSingleton(applicationDownloader);
		containerBuilder.AddSingleton(serviceUrlBuilder);
		containerBuilder.AddSingleton(workingDirectoriesProvider);
		containerBuilder.AddSingleton(_applicationPing);
		containerBuilder.AddSingleton(_clioFileSystem);
		containerBuilder.AddTransient<IPackageDownloader, PackageDownloader>();
	}

	public override void Setup() {
		base.Setup();
		_packageDownloader = Container.GetRequiredService<IPackageDownloader>();
	}

	[Test]
	[Description("Downloads and overwrites the requested package directory without clearing unrelated content from the shared destination root.")]
	public void DownloadPackages_ShouldPreserveSharedDestinationRoot_WhenPackageIsRequested() {
		// Arrange
		const string packageName = "PkgOne";
		string destinationPath = FileSystem.Path.Combine("workspace", "packages");
		string packageZipPath = FileSystem.Path.Combine("temp", $"{packageName}.zip");
		_clioFileSystem.GetCurrentDirectoryIfEmpty(destinationPath).Returns(destinationPath);

		// Act
		_packageDownloader.DownloadPackages([packageName], EnvironmentSettings, destinationPath);

		// Assert
		_clioFileSystem.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(IClioFileSystem.CreateOrOverwriteExistsDirectoryIfNeeded))
			.Should().BeEmpty(
				because: "restore must preserve ignored, external, and placeholder content in the shared packages root");
		_packageArchiver.ReceivedCalls().Should().ContainSingle(call =>
			call.GetMethodInfo().Name == nameof(IPackageArchiver.UnZipPackages) &&
			call.GetArguments().SequenceEqual(new object[] {
				packageZipPath, true, true, true, false, destinationPath
			}), because: "the requested package must still be overwritten through the package-scoped archiver path");
	}

	[Test]
	[Description("An environment that does not answer the ping - here a rejected login - fails the download with the ping's own reason and touches nothing, instead of returning as if the packages had been restored.")]
	public void DownloadPackages_ShouldThrowWithThePingFailure_WhenTheEnvironmentDoesNotAnswer() {
		// Arrange
		const string rejection = "Unauthorized Supervisor for https://example.invalid";
		_applicationPing.TryPing(EnvironmentSettings, out Arg.Any<string>())
			.Returns(call => {
				call[1] = rejection;
				return false;
			});

		// Act
		Action act = () => _packageDownloader.DownloadPackages(["PkgOne"], EnvironmentSettings,
			FileSystem.Path.Combine("workspace", "packages"));

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a restore that downloaded nothing must not be reported as done")
			.WithMessage($"*{rejection}*", because: "the caller must see why the environment did not answer");
		_packageArchiver.ReceivedCalls().Should().BeEmpty(
			because: "nothing is unpacked into the destination when the environment did not answer");
	}
}

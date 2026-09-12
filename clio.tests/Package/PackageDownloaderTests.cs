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
		IApplicationPing applicationPing = Substitute.For<IApplicationPing>();
		applicationPing.Ping(EnvironmentSettings).Returns(true);
		_clioFileSystem = Substitute.For<IClioFileSystem>();

		containerBuilder.AddSingleton(applicationClientFactory);
		containerBuilder.AddSingleton(_packageArchiver);
		containerBuilder.AddSingleton(applicationDownloader);
		containerBuilder.AddSingleton(serviceUrlBuilder);
		containerBuilder.AddSingleton(workingDirectoriesProvider);
		containerBuilder.AddSingleton(applicationPing);
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
}

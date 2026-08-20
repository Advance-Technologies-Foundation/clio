using System;
using System.Linq;
using Clio.Common;
using Clio.Package;
using Clio.WebApplication;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IAbstractionsFileSystem = System.IO.Abstractions.IFileSystem;
using IClioFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Package;

/// <summary>
/// Covers package-root preservation while packages are downloaded and overwritten individually.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public sealed class PackageDownloaderTests {

	[Test]
	[Description("Downloads and overwrites the requested package directory without clearing unrelated content from the shared destination root.")]
	public void DownloadPackages_Should_Not_Clear_Shared_Destination_Root() {
		// Arrange
		const string destinationPath = @"C:\workspace\packages";
		const string tempPath = @"C:\temp";
		const string packageName = "PkgOne";
		const string packageZipPath = @"C:\temp\PkgOne.zip";
		EnvironmentSettings environmentSettings = new();
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		applicationClientFactory.CreateClient(environmentSettings).Returns(applicationClient);
		IPackageArchiver packageArchiver = Substitute.For<IPackageArchiver>();
		IApplicationDownloader applicationDownloader = Substitute.For<IApplicationDownloader>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>(), environmentSettings)
			.Returns("https://example.invalid/package");
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider
			.When(provider => provider.CreateTempDirectory(Arg.Any<Action<string>>()))
			.Do(call => call.Arg<Action<string>>()(tempPath));
		IApplicationPing applicationPing = Substitute.For<IApplicationPing>();
		applicationPing.Ping(environmentSettings).Returns(true);
		IClioFileSystem fileSystem = Substitute.For<IClioFileSystem>();
		fileSystem.GetCurrentDirectoryIfEmpty(destinationPath).Returns(destinationPath);
		IAbstractionsFileSystem abstractionsFileSystem = Substitute.For<IAbstractionsFileSystem>();
		abstractionsFileSystem.Path.Combine(tempPath, $"{packageName}.zip").Returns(packageZipPath);
		PackageDownloader downloader = new(
			environmentSettings,
			applicationClientFactory,
			packageArchiver,
			applicationDownloader,
			serviceUrlBuilder,
			workingDirectoriesProvider,
			applicationPing,
			fileSystem,
			abstractionsFileSystem,
			Substitute.For<ILogger>());

		// Act
		downloader.DownloadPackages([packageName], environmentSettings, destinationPath);

		// Assert
		fileSystem.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(IClioFileSystem.CreateOrOverwriteExistsDirectoryIfNeeded))
			.Should().BeEmpty(
				because: "restore must preserve ignored, external, and placeholder content in the shared packages root");
		packageArchiver.ReceivedCalls().Should().ContainSingle(call =>
			call.GetMethodInfo().Name == nameof(IPackageArchiver.UnZipPackages) &&
			Equals(call.GetArguments()[0], packageZipPath) &&
			Equals(call.GetArguments()[5], destinationPath),
			because: "the requested package should still be overwritten through the package-scoped archiver path");
	}
}

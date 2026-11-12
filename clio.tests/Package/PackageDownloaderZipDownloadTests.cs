using System;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using Clio.WebApplication;
using Clio.Workspaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using IClioFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Package;

/// <summary>
/// A zip download that fails throws, so no caller of <see cref="IPackageDownloader.DownloadZipPackages"/> or
/// <see cref="IPackageDownloader.DownloadZipPackage"/> carries on with a missing archive.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public sealed class PackageDownloaderZipDownloadTests : BaseClioModuleTests {

	private IApplicationClientFactory _applicationClientFactory;
	private IPackageDownloader _packageDownloader;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>(), Arg.Any<EnvironmentSettings>())
			.Returns("https://example.invalid/package");
		containerBuilder.AddSingleton(_applicationClientFactory);
		containerBuilder.AddSingleton(serviceUrlBuilder);
		containerBuilder.AddSingleton(Substitute.For<IPackageArchiver>());
		containerBuilder.AddSingleton(Substitute.For<IApplicationDownloader>());
		containerBuilder.AddSingleton(Substitute.For<IWorkingDirectoriesProvider>());
		containerBuilder.AddSingleton(Substitute.For<IApplicationPing>());
		containerBuilder.AddSingleton(Substitute.For<IClioFileSystem>());
		containerBuilder.AddTransient<IPackageDownloader, PackageDownloader>();
	}

	public override void Setup() {
		base.Setup();
		_packageDownloader = Container.GetRequiredService<IPackageDownloader>();
	}

	[Test]
	[Description("A zip download that fails throws after its 'not completed' line, so a caller never carries on with a missing archive.")]
	public void DownloadZipPackage_ShouldThrow_WhenTheDownloadFails() {
		// Arrange
		IOwnedApplicationClient failingClient = Substitute.For<IOwnedApplicationClient>();
		failingClient.When(client => client.DownloadFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()))
			.Do(_ => throw new InvalidOperationException("the archive could not be downloaded"));
		_applicationClientFactory.CreateOwnedClient(Arg.Any<EnvironmentSettings>()).Returns(failingClient);

		// Act
		Action act = () => _packageDownloader.DownloadZipPackage("PkgOne", EnvironmentSettings,
			FileSystem.Path.Combine("packages"));

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a download that did not complete must not return as if the archive were there")
			.WithMessage("the archive could not be downloaded", because: "the original failure is rethrown unchanged");
	}
}

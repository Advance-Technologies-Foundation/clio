using System;
using System.Collections.Generic;
using System.IO;
using Clio.Common;
using Clio.Package;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class WorkspacePackageProvisionerTests {

	#region Constants: Private

	private const string PackageName = "UsrThemes";
	private const string RootPath = @"C:\ws";

	#endregion

	#region Fields: Private

	private EnvironmentSettings _environmentSettings;
	private IApplicationPackageListProvider _packageListProvider;
	private IPackageCreator _packageCreator;
	private IPackageDownloader _packageDownloader;
	private IWorkspace _workspace;
	private IWorkspacePathBuilder _workspacePathBuilder;
	private IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private ILogger _logger;
	private WorkspacePackageProvisioner _provisioner;
	private string _packagesFolder;

	#endregion

	#region Methods: Private

	private static IEnumerable<PackageInfo> PackageList(string packageName) =>
		new List<PackageInfo> {
			new(new PackageDescriptor { Name = packageName }, string.Empty, new List<string>())
		};

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_packagesFolder = Path.Combine(RootPath, "packages");
		_environmentSettings = new EnvironmentSettings();
		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_packageCreator = Substitute.For<IPackageCreator>();
		_packageDownloader = Substitute.For<IPackageDownloader>();
		_workspace = Substitute.For<IWorkspace>();
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		_workspacePathBuilder.IsWorkspace.Returns(true);
		_workspacePathBuilder.PackagesFolderPath.Returns(_packagesFolder);
		_workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		_workingDirectoriesProvider.CurrentDirectory.Returns(RootPath);
		_logger = Substitute.For<ILogger>();

		_provisioner = new WorkspacePackageProvisioner(_environmentSettings, _packageListProvider, _packageCreator,
			_packageDownloader, _workspace, _workspacePathBuilder, _workingDirectoriesProvider, _logger);
	}

	[Test]
	[Description("PackagesPath resolves to the workspace packages folder inside a workspace.")]
	public void PackagesPath_ShouldBeWorkspacePackagesFolder_WhenInsideWorkspace() {
		// Assert
		_provisioner.PackagesPath.Should().Be(_packagesFolder,
			because: "inside a workspace packages live in the workspace packages folder");
	}

	[Test]
	[Description("PackagesPath falls back to a packages folder under the current directory outside a workspace.")]
	public void PackagesPath_ShouldFallBackToCurrentDirectory_WhenNotInWorkspace() {
		// Arrange
		_workspacePathBuilder.IsWorkspace.Returns(false);

		// Assert
		_provisioner.PackagesPath.Should().Be(Path.Combine(RootPath, "packages"),
			because: "outside a workspace packages live under the current directory");
	}

	[Test]
	[Description("Downloads an existing remote package and registers it in the workspace when the caller opts in.")]
	public void EnsurePackage_ShouldDownloadAndRegister_WhenPackageExistsAndDownloadEnabled() {
		// Arrange
		_packageListProvider.GetPackages().Returns(PackageList(PackageName));

		// Act
		_provisioner.EnsurePackage(PackageName, _ => true);

		// Assert
		_packageDownloader.Received(1).DownloadPackage(PackageName, _environmentSettings, _packagesFolder);
		_workspace.Received(1).AddPackageIfNeeded(PackageName);
		_packageCreator.DidNotReceiveWithAnyArgs().Create(default(string), default(string));
	}

	[Test]
	[Description("Creates a new local package when an existing remote package should not be downloaded.")]
	public void EnsurePackage_ShouldCreateLocally_WhenDownloadDeclined() {
		// Arrange
		_packageListProvider.GetPackages().Returns(PackageList(PackageName));

		// Act
		_provisioner.EnsurePackage(PackageName, _ => false);

		// Assert
		_packageCreator.Received(1).Create(_packagesFolder, PackageName);
		_packageDownloader.DidNotReceiveWithAnyArgs().DownloadPackage(default, default, default);
	}

	[Test]
	[Description("Creates a new local package when no matching remote package exists.")]
	public void EnsurePackage_ShouldCreateLocally_WhenPackageDoesNotExistRemotely() {
		// Arrange
		_packageListProvider.GetPackages().Returns(new List<PackageInfo>());

		// Act
		_provisioner.EnsurePackage(PackageName, _ => true);

		// Assert
		_packageCreator.Received(1).Create(_packagesFolder, PackageName);
		_packageDownloader.DidNotReceiveWithAnyArgs().DownloadPackage(default, default, default);
	}

	[Test]
	[Description("Falls back to local package creation and logs a warning when the environment cannot be queried.")]
	public void EnsurePackage_ShouldFallBackToLocalCreateAndWarn_WhenPackageQueryThrows() {
		// Arrange
		_packageListProvider.When(p => p.GetPackages())
			.Do(_ => throw new InvalidOperationException("no environment"));

		// Act
		_provisioner.EnsurePackage(PackageName, _ => true);

		// Assert
		_packageCreator.Received(1).Create(_packagesFolder, PackageName);
		_logger.ReceivedWithAnyArgs(1).WriteWarning(default);
		_packageDownloader.DidNotReceiveWithAnyArgs().DownloadPackage(default, default, default);
	}

	#endregion

}

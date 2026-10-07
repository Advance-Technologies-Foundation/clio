using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using Clio.Tests.Infrastructure;
using Clio.Utilities;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Workspace;

/// <summary>
/// Issue #1749: <c>push-workspace</c> packs every package before it talks to the environment, so a package Creatio
/// would reject with "Invalid descriptor" stops the push early, and one run names the folders of all packages.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Workspace")]
public sealed class WorkspaceInstallerDescriptorCheckTests : BaseClioModuleTests {

	private static readonly string PackagesPath = TestFileSystem.GetRootedPath("ws-1749", "packages");

	private IPackageArchiver _packageArchiver;
	private IPackageInstaller _packageInstaller;
	private IApplicationClientFactory _applicationClientFactory;
	private IOwnedApplicationClient _applicationClient;
	private IWorkspacePathBuilder _workspacePathBuilder;
	private IOSPlatformChecker _osPlatformChecker;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_packageArchiver = Substitute.For<IPackageArchiver>();
		_packageInstaller = Substitute.For<IPackageInstaller>();
		_applicationClient = Substitute.For<IOwnedApplicationClient>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClientFactory.CreateClient(Arg.Any<EnvironmentSettings>()).Returns(_applicationClient);
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		_workspacePathBuilder.PackagesFolderPath.Returns(PackagesPath);
		containerBuilder.AddSingleton(_packageArchiver);
		containerBuilder.AddSingleton(_packageInstaller);
		containerBuilder.AddSingleton(_applicationClientFactory);
		containerBuilder.AddSingleton(_workspacePathBuilder);
		// The post-install standalone build only runs off Windows; skipping it keeps the test on the push itself.
		_osPlatformChecker = Substitute.For<IOSPlatformChecker>();
		_osPlatformChecker.IsWindowsEnvironment.Returns(true);
		containerBuilder.AddSingleton(_osPlatformChecker);
	}

	public override void TearDown() {
		_packageArchiver.ClearReceivedCalls();
		_packageInstaller.ClearReceivedCalls();
		_applicationClient.ClearReceivedCalls();
		_applicationClient.Dispose();
		base.TearDown();
	}

	private void RefusePackage(string packageName, string folderName) {
		string folderPath = FileSystem.Path.Combine(PackagesPath, packageName, "Data", folderName);
		_packageArchiver
			.When(archiver => archiver.Pack(FileSystem.Path.Combine(PackagesPath, packageName), Arg.Any<string>(),
				Arg.Any<bool>(), Arg.Any<bool>()))
			.Do(_ => throw new PackageItemDescriptorMissingException([
				new PackageItemFolderWithoutDescriptor(folderPath, ["Localization/data.en-US.json"])
			]));
	}

	[Test]
	[Description("When two packages hold a folder without descriptor.json, Install reports both folders in one error and sends nothing to the environment.")]
	public void Install_ShouldReportEveryPackageAndContactNothing_WhenPackagesHoldFoldersWithoutDescriptor() {
		// Arrange
		RefusePackage("UsrA", "Lookup_A");
		RefusePackage("UsrC", "Lookup_C");
		IWorkspaceInstaller sut = Container.GetRequiredService<IWorkspaceInstaller>();

		// Act
		Action act = () => sut.Install(["UsrA", "UsrB", "UsrC"]);

		// Assert
		PackageItemDescriptorMissingException exception = act.Should().Throw<PackageItemDescriptorMissingException>(
			"because the workspace holds packages Creatio would reject").Which;
		exception.Folders.Should().HaveCount(2, "because the folders of every refused package are collected before failing");
		exception.Message.Should().Contain("Lookup_A").And.Contain("Lookup_C",
			"because one run must name the folders of all packages");
		_packageArchiver.Received(3).Pack(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
		_applicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>());
		_packageInstaller.DidNotReceiveWithAnyArgs().Install(default, default, default, default, default);
	}

	[Test]
	[Description("When every package packs, Install still resets the schema change state of each package and installs the archive.")]
	public void Install_ShouldResetEveryPackageAndInstall_WhenAllPackagesPack() {
		// Arrange
		IWorkspaceInstaller sut = Container.GetRequiredService<IWorkspaceInstaller>();

		// Act
		sut.Install(["UsrA", "UsrB"]);

		// Assert
		_applicationClient.Received(2).ExecutePostRequest(Arg.Is<string>(url => url.Contains("ResetSchemaChangeState")),
			Arg.Any<string>());
		_packageInstaller.ReceivedWithAnyArgs(1).Install(default, default, default, default, default);
	}

}

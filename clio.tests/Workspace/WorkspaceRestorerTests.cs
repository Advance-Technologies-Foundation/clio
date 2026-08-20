using System;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using Clio.Workspace;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IAbstractionsFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Workspace;

/// <summary>
/// Covers when <see cref="WorkspaceRestorer" /> reaches the CreatioSDK feed, which decides whether a restore
/// works on a host that cannot reach api.nuget.org (issue #1119).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Workspace")]
public class WorkspaceRestorerTests {

	#region Fields: Private

	private ICreatioSdk _creatioSdk;
	private IPackageDownloader _packageDownloader;
	private WorkspaceRestorer _workspaceRestorer;

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_creatioSdk = Substitute.For<ICreatioSdk>();
		_packageDownloader = Substitute.For<IPackageDownloader>();
		_workspaceRestorer = new WorkspaceRestorer(Substitute.For<INuGetManager>(),
			Substitute.For<IWorkspacePathBuilder>(), Substitute.For<IEnvironmentScriptCreator>(),
			Substitute.For<IWorkspaceSolutionCreator>(), _packageDownloader, _creatioSdk,
			Substitute.For<IAbstractionsFileSystem>(), Substitute.For<ILogger>(),
			Substitute.For<ITemplateProvider>(), Substitute.For<IWorkspacePackageFilter>());
	}

	[TearDown]
	public void TearDown() {
		_creatioSdk.ClearReceivedCalls();
		_packageDownloader.ClearReceivedCalls();
	}

	[Test]
	[Description("Never reaches the CreatioSDK feed when the NuGet restore step is switched off, so a restore that only downloads packages from the environment works on a host that cannot reach api.nuget.org.")]
	public void Restore_Should_Not_Resolve_Sdk_Version_When_Nuget_Restore_Is_Off() {
		// Arrange
		WorkspaceOptions options = new() {
			IsNugetRestore = false, IsCreateSolution = false, AddBuildProps = false
		};

		// Act
		_workspaceRestorer.Restore(new WorkspaceSettings(), new EnvironmentSettings(), options);

		// Assert
		_creatioSdk.Received(0).FindLatestSdkVersion(Arg.Any<Version>());
	}

	[Test]
	[Description("Resolves the SDK version before any package is written to disk, so an unreachable feed fails the restore instead of leaving a half-restored workspace behind.")]
	public void Restore_Should_Fail_Before_Downloading_When_Sdk_Feed_Is_Unreachable() {
		// Arrange
		WorkspaceOptions options = new() {
			IsNugetRestore = true, IsCreateSolution = false, AddBuildProps = false
		};
		_creatioSdk.FindLatestSdkVersion(Arg.Any<Version>()).Returns(_ => throw new InvalidOperationException(
			"Creatio SDK versions could not be read from https://api.nuget.org"));

		// Act
		Action act = () => _workspaceRestorer.Restore(new WorkspaceSettings(), new EnvironmentSettings(), options);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "the restore cannot complete without the SDK, so it must say so rather than proceed");
		_packageDownloader.ReceivedCalls().Should().BeEmpty(
			because: "failing after packages are on disk would leave a half-restored workspace behind");
	}

	#endregion

}

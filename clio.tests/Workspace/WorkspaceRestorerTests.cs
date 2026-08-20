using System;
using System.Collections.Generic;
using System.Linq;
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
	private ILogger _logger;
	private IPackageDownloader _packageDownloader;
	private IWorkspacePackageFilter _workspacePackageFilter;
	private WorkspaceRestorer _workspaceRestorer;

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_creatioSdk = Substitute.For<ICreatioSdk>();
		_logger = Substitute.For<ILogger>();
		_packageDownloader = Substitute.For<IPackageDownloader>();
		_workspacePackageFilter = Substitute.For<IWorkspacePackageFilter>();
		_workspacePackageFilter
			.FilterPackages(Arg.Any<IEnumerable<string>>(), Arg.Any<WorkspaceSettings>())
			.Returns(call => call.ArgAt<IEnumerable<string>>(0));
		_workspaceRestorer = new WorkspaceRestorer(Substitute.For<INuGetManager>(),
			Substitute.For<IWorkspacePathBuilder>(), Substitute.For<IEnvironmentScriptCreator>(),
			Substitute.For<IWorkspaceSolutionCreator>(), _packageDownloader, _creatioSdk,
			Substitute.For<IAbstractionsFileSystem>(), _logger,
			Substitute.For<ITemplateProvider>(), _workspacePackageFilter);
	}

	[TearDown]
	public void TearDown() {
		_creatioSdk.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		_packageDownloader.ClearReceivedCalls();
		_workspacePackageFilter.ClearReceivedCalls();
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

	[Test]
	[Description("Warns and skips package download when workspace settings contain no eligible packages, preventing a no-op restore from clearing local package content.")]
	public void Restore_Should_Warn_And_Skip_Package_Download_When_No_Packages_Are_Eligible() {
		// Arrange
		WorkspaceSettings workspaceSettings = new();
		WorkspaceOptions options = new() {
			IsNugetRestore = false, IsCreateSolution = false, AddBuildProps = false
		};

		// Act
		_workspaceRestorer.Restore(workspaceSettings, new EnvironmentSettings(), options);

		// Assert
		_packageDownloader.ReceivedCalls().Should().BeEmpty(
			because: "an empty restore must not enter the downloader that can replace local package content");
		_logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteWarning))
			.Select(call => call.GetArguments().SingleOrDefault()?.ToString())
			.Should().ContainSingle(message =>
				message != null &&
				message.Contains("workspaceSettings.json", StringComparison.Ordinal) &&
				message.Contains("package download was skipped", StringComparison.Ordinal),
				because: "the successful no-op must plainly explain why no packages were restored");
	}

	#endregion

}

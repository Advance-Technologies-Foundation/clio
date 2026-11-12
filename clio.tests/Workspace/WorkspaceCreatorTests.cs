using System;
using System.Linq;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using Clio.Utilities;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Workspace;

/// <summary>
/// Covers how <see cref="WorkspaceCreator" /> records the application version, including the case where the
/// SDK version list cannot be read because api.nuget.org is unreachable (issue #1119).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Workspace")]
public class WorkspaceCreatorTests {

	#region Constants: Private

	private const string RootPath = "/tmp/clio-tests/workspace";
	private const string WorkspaceSettingsPath = "/tmp/clio-tests/workspace/.clio/workspaceSettings.json";

	#endregion

	#region Fields: Private

	private IApplicationPackageListProvider _applicationPackageListProvider;
	private ICreatioSdk _creatioSdk;
	private ITemplateProvider _templateProvider;
	private IJsonConverter _jsonConverter;
	private ILogger _logger;
	private WorkspaceCreator _workspaceCreator;

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		workspacePathBuilder.RootPath.Returns(RootPath);
		workspacePathBuilder.WorkspaceSettingsPath.Returns(WorkspaceSettingsPath);
		workspacePathBuilder.IsWorkspace.Returns(false);
		_creatioSdk = Substitute.For<ICreatioSdk>();
		_templateProvider = Substitute.For<ITemplateProvider>();
		_templateProvider.GetTemplateDirectories("workspace").Returns([]);
		_applicationPackageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.GetDirectories(RootPath).Returns([]);
		fileSystem.ExistsFile(Arg.Any<string>()).Returns(false);
		IOSPlatformChecker osPlatformChecker = Substitute.For<IOSPlatformChecker>();
		// Windows short-circuits the executable-permissions step, which is irrelevant to what is asserted here.
		osPlatformChecker.IsWindowsEnvironment.Returns(true);
		_logger = Substitute.For<ILogger>();
		_workspaceCreator = new WorkspaceCreator(workspacePathBuilder, _creatioSdk, _templateProvider,
			_jsonConverter, fileSystem, _applicationPackageListProvider,
			Substitute.For<IExecutablePermissionsActualizer>(), osPlatformChecker, _logger);
	}

	[TearDown]
	public void TearDown() {
		_creatioSdk.ClearReceivedCalls();
		_jsonConverter.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		_templateProvider.ClearReceivedCalls();
		_applicationPackageListProvider.ClearReceivedCalls();
	}

	[Test]
	[Description("The package list is read from the environment before the template is copied, so a login that passes but a list that cannot be read (no cliogate, no rights) leaves no skeleton behind.")]
	public void Create_ShouldWriteNothing_WhenThePackageListCannotBeRead() {
		// Arrange
		_applicationPackageListProvider.GetPackages(Arg.Any<string>())
			.Returns(_ => throw new InvalidOperationException("cliogate is not installed"));

		// Act
		Action act = () => _workspaceCreator.Create("dev", isAddingPackageNames: true);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a workspace without its package list must not be reported as created");
		_templateProvider.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ITemplateProvider.CopyTemplateFolder))
			.Should().BeEmpty(because: "the template is copied only after everything read from the environment is in hand");
		_jsonConverter.ReceivedCalls().Should().BeEmpty(because: "no settings file is written for a workspace that was not created");
	}

	[Test]
	[Description("Records the newest SDK version, trimmed to major.minor.build, when the SDK version list is readable.")]
	public void Create_Should_Record_Latest_Sdk_Version() {
		// Arrange
		_creatioSdk.LastVersion.Returns(new Version(8, 1, 3, 4567));

		// Act
		_workspaceCreator.Create(environmentName: null);

		// Assert
		_jsonConverter.Received(1).SerializeObjectToFile(
			Arg.Is<WorkspaceSettings>(settings => settings.ApplicationVersion == new Version(8, 1, 3)),
			WorkspaceSettingsPath);
	}

	[Test]
	[Description("Creates the workspace without an application version, and warns, when the SDK version list cannot be read - so an unreachable api.nuget.org no longer stops workspace creation.")]
	public void Create_Should_Succeed_Without_Application_Version_When_Sdk_Versions_Are_Unavailable() {
		// Arrange
		_creatioSdk.LastVersion.Returns(_ => throw new InvalidOperationException(
			"Creatio SDK versions could not be read from https://api.nuget.org"));

		// Act
		Action act = () => _workspaceCreator.Create(environmentName: null);

		// Assert
		act.Should().NotThrow(
			because: "creating a workspace needs no SDK version, so an unreachable NuGet feed must not stop it");
		_jsonConverter.Received(1).SerializeObjectToFile(
			Arg.Is<WorkspaceSettings>(settings => settings.ApplicationVersion == null),
			WorkspaceSettingsPath);
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("application version could not be resolved")));
	}

	#endregion

}

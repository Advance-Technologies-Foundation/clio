using System;
using Clio.Common;
using Clio.Package;
using Clio.Requests;
using Clio.Tests.Command;
using Clio.WebApplication;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

/// <summary>
/// GH-1299: binding coverage for the classification the installers apply to the answer of
/// <c>PackageInstallerService.svc/InstallPackage</c>. <see cref="InstallLogAnalyzerTests"/> pins the pure
/// classifier; these tests prove that <see cref="BasePackageInstaller"/> actually asks it, and that the
/// dedicated invalid-archive exit code still wins over the downgrade.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
internal sealed class InstallerFailureClassificationTests : BaseClioModuleTests {

	private const string GenericFailureResponse =
		"{\"success\":false,\"errorInfo\":{\"errorCode\":\"Exception\",\"message\":\"Packages installation failed\"}}";

	private const string LocallyModifiedLog =
		"Start package installation\r\n"
		+ "Unable to install Schema \"UsrI1299Svc\" into package \"UsrIssue1299\", because the element has been"
		+ " modified locally. Resolve the conflict and mark the element as unchanged.\r\n"
		+ "Package installation finished\r\n";

	private bool _originalFailOnError;
	private ILogger _logger;
	private IApplicationLogProvider _applicationLogProvider;
	private IOwnedApplicationClient _applicationClient;
	private IApplicationClientFactory _applicationClientFactory;

	public override void Setup() {
		base.Setup();
		_originalFailOnError = GlobalContext.FailOnError;
		GlobalContext.FailOnError = false;
		_logger = Substitute.For<ILogger>();
		_applicationLogProvider = Substitute.For<IApplicationLogProvider>();
		_applicationClient = Substitute.For<IOwnedApplicationClient>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClientFactory.CreateClient(Arg.Any<EnvironmentSettings>()).Returns(_applicationClient);
	}

	public override void TearDown() {
		GlobalContext.FailOnError = _originalFailOnError;
		_applicationClient?.Dispose();
		base.TearDown();
	}

	private void ArrangeServer(string response, string installLog) {
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(response);
		_applicationLogProvider.GetInstallationLog(Arg.Any<EnvironmentSettings>())
			.Returns(string.Empty, installLog, installLog, installLog);
	}

	private TInstaller CreateInstaller<TInstaller>(EnvironmentSettings environmentSettings,
		Func<IApplicationLogProvider, EnvironmentSettings, IApplicationClientFactory, IApplication,
			IPackageArchiver, ISqlScriptExecutor, IServiceUrlBuilder, IFileSystem, ILogger, IPackageLockManager,
			TInstaller> factory) {
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>(), Arg.Any<EnvironmentSettings>())
			.Returns(callInfo => callInfo.ArgAt<string>(0));
		return factory(_applicationLogProvider, environmentSettings, _applicationClientFactory,
			Substitute.For<IApplication>(), Substitute.For<IPackageArchiver>(),
			Substitute.For<ISqlScriptExecutor>(), serviceUrlBuilder, new FileSystem(FileSystem), _logger,
			Substitute.For<IPackageLockManager>());
	}

	private string AddPackageArchive(string fileName) {
		string packagePath = $"T:\\{fileName}";
		FileSystem.AddFile(packagePath, new System.IO.Abstractions.TestingHelpers.MockFileData(new byte[0]));
		return packagePath;
	}

	[Test]
	[Description("GH-1299: the installer reports success when the service answered with the generic failure message but the run finished and only skipped a locally modified schema.")]
	public void Install_ShouldReturnTrue_WhenTheOnlyReportedProblemWasALocallyModifiedSchema() {
		// Arrange
		EnvironmentSettings environmentSettings = new();
		string packagePath = AddPackageArchive("UsrIssue1299.gz");
		ArrangeServer(GenericFailureResponse, LocallyModifiedLog);
		PackageInstaller installer = CreateInstaller(environmentSettings,
			(logProvider, settings, clientFactory, application, archiver, scriptExecutor, urlBuilder, fileSystem,
					logger, lockManager) =>
				new PackageInstaller(logProvider, settings, clientFactory, application, archiver, scriptExecutor,
					urlBuilder, fileSystem, logger, lockManager));

		// Act
		bool result = installer.Install(packagePath, environmentSettings);

		// Assert
		result.Should().BeTrue(
			because: "the package was installed and a schema skipped as modified locally is a warning (GH-1299)");
		_logger.Received().WriteWarning(Arg.Is<string>(value => value.Contains("UsrI1299Svc")));
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("GH-1299: a service failure that names a concrete reason stays a failure and the reason reaches the operator.")]
	public void Install_ShouldReturnFalseAndNameTheReason_WhenTheServiceReportedASpecificFailure() {
		// Arrange
		EnvironmentSettings environmentSettings = new();
		string packagePath = AddPackageArchive("UsrIssue1299.gz");
		ArrangeServer(
			"{\"success\":false,\"errorInfo\":{\"errorCode\":\"Exception\",\"message\":\"Cannot compile configuration\"}}",
			LocallyModifiedLog);
		PackageInstaller installer = CreateInstaller(environmentSettings,
			(logProvider, settings, clientFactory, application, archiver, scriptExecutor, urlBuilder, fileSystem,
					logger, lockManager) =>
				new PackageInstaller(logProvider, settings, clientFactory, application, archiver, scriptExecutor,
					urlBuilder, fileSystem, logger, lockManager));

		// Act
		bool result = installer.Install(packagePath, environmentSettings);

		// Assert
		result.Should().BeFalse(
			because: "a named reason is a real failure that a locally-modified skip does not explain away");
		_logger.Received().WriteError(Arg.Is<string>(value => value.Contains("Cannot compile configuration")));
	}

	[Test]
	[Description("GH-1299: the dedicated invalid-archive exception still wins when the same run also skipped a locally modified schema.")]
	public void Install_ShouldStillThrowInvalidGZipArchive_WhenTheRunAlsoSkippedALocallyModifiedSchema() {
		// Arrange
		EnvironmentSettings environmentSettings = new();
		string packagePath = AddPackageArchive("UsrIssue1299App.gz");
		ArrangeServer("{\"success\":false}",
			LocallyModifiedLog
			+ "Terrasoft.Common.InvalidGZipArchiveException: Unable to open \"UsrIssue1299App.gz\"."
			+ " The file is invalid or corrupted.\r\n");
		ApplicationInstaller installer = CreateInstaller(environmentSettings,
			(logProvider, settings, clientFactory, application, archiver, scriptExecutor, urlBuilder, fileSystem,
					logger, lockManager) =>
				new ApplicationInstaller(logProvider, settings, clientFactory, application, archiver, scriptExecutor,
					urlBuilder, fileSystem, logger, lockManager));

		// Act
		Action act = () => installer.Install(packagePath, environmentSettings);

		// Assert
		act.Should().Throw<InvalidGZipArchiveInstallException>(
			because: "install-application maps an invalid archive to its own exit code, which the locally-modified downgrade must never swallow");
	}

}

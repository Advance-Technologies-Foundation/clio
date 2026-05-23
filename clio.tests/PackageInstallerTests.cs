using Clio.Common;
using Clio.Package;
using Clio.Requests;
using Clio.Tests.Command;
using Clio.WebApplication;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using System;

namespace Clio.Tests;

[TestFixture]
[Property("Module", "Package")]
internal class PackageInstallerTests : BaseClioModuleTests
{
	[Test]
	[Description("Install should keep package archive failures as a regular failed install without application-specific exception mapping.")]
	public void Install_Should_Not_Throw_InvalidGZipArchiveInstallException_When_Package_Log_Contains_Invalid_GZip_Exception() {
		// Arrange
		string packagePath = "T:\\Package.gz";
		FileSystem.AddFile(packagePath, new System.IO.Abstractions.TestingHelpers.MockFileData(new byte[0]));
		EnvironmentSettings environmentSettings = new EnvironmentSettings();
		var applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		var applicationClient = Substitute.For<IApplicationClient>();
		applicationClientFactory.CreateClient(Arg.Any<EnvironmentSettings>()).Returns(applicationClient);
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns("{\"success\":false}");
		var application = Substitute.For<IApplication>();
		var packageArchiver = Substitute.For<IPackageArchiver>();
		var scriptExecutor = Substitute.For<ISqlScriptExecutor>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>(), Arg.Any<EnvironmentSettings>())
			.Returns(callInfo => callInfo.ArgAt<string>(0));
		var logger = Substitute.For<ILogger>();
		var packageLockManager = Substitute.For<IPackageLockManager>();
		var clioFileSystem = new FileSystem(FileSystem);
		var applicationLogProvider = Substitute.For<IApplicationLogProvider>();
		applicationLogProvider.GetInstallationLog(Arg.Any<EnvironmentSettings>())
			.Returns(string.Empty,
				"Terrasoft.Common.InvalidGZipArchiveException: Unable to open \"Package.gz\". The file is invalid or corrupted.");
		PackageInstaller packageInstaller = new PackageInstaller(applicationLogProvider,
			environmentSettings,
			applicationClientFactory,
			application,
			packageArchiver,
			scriptExecutor,
			serviceUrlBuilder,
			clioFileSystem,
			logger,
			packageLockManager
		);
		bool result = true;

		// Act
		Action act = () => result = packageInstaller.Install(packagePath, environmentSettings);

		// Assert
		act.Should().NotThrow(
			because: "exit code five is scoped to application installation");
		result.Should().BeFalse(
			because: "package installation should keep the existing general failed-install behavior");
	}
}

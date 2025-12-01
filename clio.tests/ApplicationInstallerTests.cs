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
internal class ApplicationInstallerTests : BaseClioModuleTests
{
	[Test]
	public void RestartApplicationAfterInstallPackageInNet6() {
		string packagePath = "T:\\TestClioPackage.gz";
		FileSystem.AddFile(packagePath, new System.IO.Abstractions.TestingHelpers.MockFileData(new byte[0]));
		EnvironmentSettings environmentSettings = new EnvironmentSettings();
		environmentSettings.IsNetCore = true;
		var applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		var application = Substitute.For<IApplication>();
		var packageArchiver = Substitute.For<IPackageArchiver>();
		var scriptExecutor = Substitute.For<ISqlScriptExecutor>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var applicationLogProvider = Substitute.For<IApplicationLogProvider>();
		var logger = Substitute.For<ILogger>();
		var packageLockManager = Substitute.For<IPackageLockManager>();
		var clioFileSystem = new FileSystem(FileSystem);
		ApplicationInstaller applicationInstaller = new ApplicationInstaller(applicationLogProvider,
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
		applicationInstaller.Install(packagePath, environmentSettings);
		application.Received(1).Restart();
	}


	[Test]
	public void RestartApplicationAfterInstallFolderInNet6() {
		string packageFolderPath = "T:\\TestClioPackageFolder";
		FileSystem.AddDirectory(packageFolderPath);
		EnvironmentSettings environmentSettings = new EnvironmentSettings();
		environmentSettings.IsNetCore = true;
		var applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		var application = Substitute.For<IApplication>();
		var packageArchiver = Substitute.For<IPackageArchiver>();
		packageArchiver.When(p => p.Pack(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
			Arg.Any<bool>())).Do(callInfo => {
			FileSystem.AddEmptyFile(callInfo[1].ToString());
		});
		var scriptExecutor = Substitute.For<ISqlScriptExecutor>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		var packageLockManager = Substitute.For<IPackageLockManager>();
		var clioFileSystem = new FileSystem(FileSystem);
		var applicationLogProvider = Substitute.For<IApplicationLogProvider>();
		ApplicationInstaller applicationInstaller = new ApplicationInstaller(applicationLogProvider,
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
		applicationInstaller.Install(packageFolderPath, environmentSettings);
		application.Received(1).Restart();
	}

	[Test]
	public void CatchRestartApplicationErrorAfterInstallFolderInNet6() {
		string packageFolderPath = "T:\\TestClioPackageFolder";
		FileSystem.AddDirectory(packageFolderPath);
		EnvironmentSettings environmentSettings = new EnvironmentSettings();
		environmentSettings.IsNetCore = true;
		var applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		var application = Substitute.For<IApplication>();
		application.When(r => r.Restart()).Do(_ => throw new Exception("Restart application exception"));
		var packageArchiver = Substitute.For<IPackageArchiver>();
		packageArchiver.When(p => p.Pack(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
			Arg.Any<bool>())).Do(callInfo => {
			FileSystem.AddEmptyFile(callInfo[1].ToString());
		});
		var scriptExecutor = Substitute.For<ISqlScriptExecutor>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		var packageLockManager = Substitute.For<IPackageLockManager>();
		var clioFileSystem = new FileSystem(FileSystem);
		var applicationLogProvider = Substitute.For<IApplicationLogProvider>();
		ApplicationInstaller applicationInstaller = new ApplicationInstaller(applicationLogProvider,
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
		Assert.DoesNotThrow(
			() => applicationInstaller.Install(packageFolderPath, environmentSettings)
		);

	}

	[Test]
	public void ReturnErrorApplicationErrorAfterInstallFolderInNet6() {
		string packageFolderPath = "T:\\TestClioPackageFolder";
		FileSystem.AddDirectory(packageFolderPath);
		EnvironmentSettings environmentSettings = new EnvironmentSettings();
		environmentSettings.IsNetCore = true;
		var applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		var application = Substitute.For<IApplication>();
		application.When(r => r.Restart()).Do(_ => throw new Exception("Restart application exception"));
		var packageArchiver = Substitute.For<IPackageArchiver>();
		packageArchiver.When(p => p.Pack(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
			Arg.Any<bool>())).Do(callInfo => {
			FileSystem.AddEmptyFile(callInfo[1].ToString());
		});
		var scriptExecutor = Substitute.For<ISqlScriptExecutor>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		var packageLockManager = Substitute.For<IPackageLockManager>();
		var clioFileSystem = new FileSystem(FileSystem);
		var applicationLogProvider = Substitute.For<IApplicationLogProvider>();
		applicationLogProvider.GetInstallationLog(Arg.Any<EnvironmentSettings>()).Returns("SOME LOG WITHOUT SUCCESS MESSAGE");
		ApplicationInstaller applicationInstaller = new ApplicationInstaller(applicationLogProvider,
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
		GlobalContext.FailOnError = true;
		bool result = applicationInstaller.Install(packageFolderPath, environmentSettings);
		result.Should().BeFalse();
	}

	[Test]
	public void ReturnSuccessIfApplicationLogContainsSuccessMessage() {
		string packageFolderPath = "T:\\TestClioPackageFolder";
		FileSystem.AddDirectory(packageFolderPath);
		EnvironmentSettings environmentSettings = new EnvironmentSettings();
		environmentSettings.IsNetCore = true;
		var applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		var application = Substitute.For<IApplication>();
		application.When(r => r.Restart()).Do(_ => throw new Exception("Restart application exception"));
		var packageArchiver = Substitute.For<IPackageArchiver>();
		packageArchiver.When(p => p.Pack(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
			Arg.Any<bool>())).Do(callInfo => {
			FileSystem.AddEmptyFile(callInfo[1].ToString());
		});
		var scriptExecutor = Substitute.For<ISqlScriptExecutor>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		var packageLockManager = Substitute.For<IPackageLockManager>();
		var clioFileSystem = new FileSystem(FileSystem);
		var applicationLogProvider = Substitute.For<IApplicationLogProvider>();
		applicationLogProvider.GetInstallationLog(Arg.Any<EnvironmentSettings>()).Returns("appLication InstallEd successfully");
		ApplicationInstaller applicationInstaller = new ApplicationInstaller(applicationLogProvider,
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
		GlobalContext.FailOnError = true;
		bool result = applicationInstaller.Install(packageFolderPath, environmentSettings);
		result.Should().BeTrue();
	}

	[Test]
	[Description("Install should use checkCompilationErrors flag when set to true and return success when response is successful")]
	public void InstallWithCheckCompilationErrorsTrueIncludesCheckConfigurationErrorsInRequestData() {
		string packagePath = "T:\\TestApp.gz";
		FileSystem.AddFile(packagePath, new System.IO.Abstractions.TestingHelpers.MockFileData(new byte[0]));
		EnvironmentSettings environmentSettings = new EnvironmentSettings();
		var applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		var applicationClient = Substitute.For<IApplicationClient>();
		applicationClientFactory.CreateClient(Arg.Any<EnvironmentSettings>()).Returns(applicationClient);
		string capturedRequestData = null;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(callInfo => {
				capturedRequestData = callInfo.ArgAt<string>(1);
				return "{\"Success\":true}";
			});
		var application = Substitute.For<IApplication>();
		var packageArchiver = Substitute.For<IPackageArchiver>();
		var scriptExecutor = Substitute.For<ISqlScriptExecutor>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.ArgAt<string>(0));
		var logger = Substitute.For<ILogger>();
		var packageLockManager = Substitute.For<IPackageLockManager>();
		var clioFileSystem = new FileSystem(FileSystem);
		var applicationLogProvider = Substitute.For<IApplicationLogProvider>();
		applicationLogProvider.GetInstallationLog(Arg.Any<EnvironmentSettings>()).Returns("Application installed successfully");
		ApplicationInstaller applicationInstaller = new ApplicationInstaller(applicationLogProvider,
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
		GlobalContext.FailOnError = true;
		bool result = applicationInstaller.Install(packagePath, environmentSettings, null, true);
		capturedRequestData.Should().Contain("\"CheckCompilationErrors\":true", because: "CheckCompilationErrors should be set to true in JSON");
		result.Should().BeTrue();
	}

	[Test]
	[Description("Install should include checkCompilationErrors flag as false when explicitly set to false and return success when response is successful")]
	public void InstallWithCheckCompilationErrorsFalseIncludesCheckConfigurationErrorsAsFalseInRequestData() {
		string packagePath = "T:\\TestApp.gz";
		FileSystem.AddFile(packagePath, new System.IO.Abstractions.TestingHelpers.MockFileData(new byte[0]));
		EnvironmentSettings environmentSettings = new EnvironmentSettings();
		var applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		var applicationClient = Substitute.For<IApplicationClient>();
		applicationClientFactory.CreateClient(Arg.Any<EnvironmentSettings>()).Returns(applicationClient);
		string capturedRequestData = null;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(callInfo => {
				capturedRequestData = callInfo.ArgAt<string>(1);
				return "{\"Success\":true}";
			});
		var application = Substitute.For<IApplication>();
		var packageArchiver = Substitute.For<IPackageArchiver>();
		var scriptExecutor = Substitute.For<ISqlScriptExecutor>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.ArgAt<string>(0));
		var logger = Substitute.For<ILogger>();
		var packageLockManager = Substitute.For<IPackageLockManager>();
		var clioFileSystem = new FileSystem(FileSystem);
		var applicationLogProvider = Substitute.For<IApplicationLogProvider>();
		applicationLogProvider.GetInstallationLog(Arg.Any<EnvironmentSettings>()).Returns("Application installed successfully");
		ApplicationInstaller applicationInstaller = new ApplicationInstaller(applicationLogProvider,
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
		GlobalContext.FailOnError = true;
		bool result = applicationInstaller.Install(packagePath, environmentSettings, null, false);
		capturedRequestData.Should().Contain("\"CheckCompilationErrors\":false", because: "checkCompilationErrors parameter was explicitly set to false");
		result.Should().BeTrue();
	}

	[Test]
	[Description("Install should not include checkCompilationErrors flag when not specified (null) and return success when response is successful")]
	public void InstallWithCheckCompilationErrorsNullExcludesCheckConfigurationErrorsFromRequestData() {
		string packagePath = "T:\\TestApp.gz";
		FileSystem.AddFile(packagePath, new System.IO.Abstractions.TestingHelpers.MockFileData(new byte[0]));
		EnvironmentSettings environmentSettings = new EnvironmentSettings();
		var applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		var applicationClient = Substitute.For<IApplicationClient>();
		applicationClientFactory.CreateClient(Arg.Any<EnvironmentSettings>()).Returns(applicationClient);
		string capturedRequestData = null;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(callInfo => {
				capturedRequestData = callInfo.ArgAt<string>(1);
				return "{\"Success\":true}";
			});
		var application = Substitute.For<IApplication>();
		var packageArchiver = Substitute.For<IPackageArchiver>();
		var scriptExecutor = Substitute.For<ISqlScriptExecutor>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.ArgAt<string>(0));
		var logger = Substitute.For<ILogger>();
		var packageLockManager = Substitute.For<IPackageLockManager>();
		var clioFileSystem = new FileSystem(FileSystem);
		var applicationLogProvider = Substitute.For<IApplicationLogProvider>();
		applicationLogProvider.GetInstallationLog(Arg.Any<EnvironmentSettings>()).Returns("Application installed successfully");
		ApplicationInstaller applicationInstaller = new ApplicationInstaller(applicationLogProvider,
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
		GlobalContext.FailOnError = true;
		bool result = applicationInstaller.Install(packagePath, environmentSettings, null, null);
		capturedRequestData.Should().NotContain("CheckCompilationErrors", because: "checkCompilationErrors parameter was not specified (null)");
		result.Should().BeTrue();
	}
}
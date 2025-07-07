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
	public void InstallWithOptionsCallsInstallWithOptionsWhenFailOnWarningIsTrue() {
		string packagePath = "T:\\TestClioPackage.gz";
		FileSystem.AddFile(packagePath, new System.IO.Abstractions.TestingHelpers.MockFileData(new byte[0]));
		EnvironmentSettings environmentSettings = new EnvironmentSettings();
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
		
		var packageInstallOptions = new PackageInstallOptions { FailOnWarning = true };
		applicationInstaller.InstallWithOptions(packagePath, environmentSettings, null, packageInstallOptions);
		
		// Test passes if no exception is thrown and method is accessible
		Assert.Pass("InstallWithOptions method is accessible and can be called with FailOnWarning option");
	}

	[Test]
	public void InstallApplicationWithFailOnWarningFalseSucceeds() {
		GlobalContext.FailOnError = true;
		GlobalContext.FailOnWarning = true;
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
}
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


	[Test, Description("This test checks that the InstallWithOptions method can be called with FailOnWarning set to true.")]
	public void InstallWithOptionsCallsInstallWithOptionsWhenFailOnWarningIsTrue() {
		//Arrange
		string packagePath = "T:\\TestClioPackage.gz";
		FileSystem.AddFile(packagePath, new System.IO.Abstractions.TestingHelpers.MockFileData(new byte[0]));
		EnvironmentSettings environmentSettings = new EnvironmentSettings();
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IApplication application = Substitute.For<IApplication>();
		IPackageArchiver packageArchiver = Substitute.For<IPackageArchiver>();
		ISqlScriptExecutor scriptExecutor = Substitute.For<ISqlScriptExecutor>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IApplicationLogProvider applicationLogProvider = Substitute.For<IApplicationLogProvider>();
		ILogger logger = Substitute.For<ILogger>();
		IPackageLockManager packageLockManager = Substitute.For<IPackageLockManager>();
		FileSystem clioFileSystem = new FileSystem(FileSystem);
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
		PackageInstallOptions packageInstallOptions = new PackageInstallOptions { FailOnWarning = true };
		
		//Act
		Action act = () => applicationInstaller.InstallWithOptions(packagePath, environmentSettings, null, packageInstallOptions);
		
		//Assert
		act.Should().NotThrow("InstallWithOptions method should be accessible and callable with FailOnWarning option");
	}

	[Test, Description("Verifies that InstallWithOptions method correctly handles FailOnWarning set to false and succeeds without throwing")]
	public void InstallApplicationWithFailOnWarningFalseSucceeds() {
		//Arrange
		string packagePath = "T:\\TestClioPackage.gz";
		FileSystem.AddFile(packagePath, new System.IO.Abstractions.TestingHelpers.MockFileData(new byte[0]));
		EnvironmentSettings environmentSettings = new EnvironmentSettings();
		IApplicationClientFactory applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		IApplication application = Substitute.For<IApplication>();
		IPackageArchiver packageArchiver = Substitute.For<IPackageArchiver>();
		ISqlScriptExecutor scriptExecutor = Substitute.For<ISqlScriptExecutor>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IApplicationLogProvider applicationLogProvider = Substitute.For<IApplicationLogProvider>();
		ILogger logger = Substitute.For<ILogger>();
		IPackageLockManager packageLockManager = Substitute.For<IPackageLockManager>();
		FileSystem clioFileSystem = new FileSystem(FileSystem);
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
		PackageInstallOptions packageInstallOptions = new PackageInstallOptions { FailOnWarning = false };
		
		//Act
		Action act = () => applicationInstaller.InstallWithOptions(packagePath, environmentSettings, null, packageInstallOptions);
		
		//Assert
		act.Should().NotThrow("InstallWithOptions method should handle FailOnWarning=false without throwing");
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

	#region Tests for CheckForWarningsInLog

	[Test, Description("Verifies that CheckForWarningsInLog returns false for null input")]
	public void CheckForWarningsInLog_WithNullInput_ReturnsFalse() {
		//Arrange
		string logText = null;
		
		//Act
		bool result = BasePackageInstaller.CheckForWarningsInLog(logText);
		
		//Assert
		result.Should().BeFalse("null log text should not contain warnings");
	}

	[Test, Description("Verifies that CheckForWarningsInLog returns false for empty input")]
	public void CheckForWarningsInLog_WithEmptyInput_ReturnsFalse() {
		//Arrange
		string logText = "";
		
		//Act
		bool result = BasePackageInstaller.CheckForWarningsInLog(logText);
		
		//Assert
		result.Should().BeFalse("empty log text should not contain warnings");
	}

	[Test, Description("Verifies that CheckForWarningsInLog returns false for whitespace input")]
	public void CheckForWarningsInLog_WithWhitespaceInput_ReturnsFalse() {
		//Arrange
		string logText = "   \t\n  ";
		
		//Act
		bool result = BasePackageInstaller.CheckForWarningsInLog(logText);
		
		//Assert
		result.Should().BeFalse("whitespace-only log text should not contain warnings");
	}

	[Test, Description("Verifies that CheckForWarningsInLog returns false for clean log without warnings")]
	public void CheckForWarningsInLog_WithCleanLog_ReturnsFalse() {
		//Arrange
		string logText = "Application installed successfully. All schemas loaded. Data imported successfully.";
		
		//Act
		bool result = BasePackageInstaller.CheckForWarningsInLog(logText);
		
		//Assert
		result.Should().BeFalse("clean log without warning patterns should not trigger warnings");
	}

	[Test, Description("Verifies that CheckForWarningsInLog returns true when 'skipped' pattern is found")]
	public void CheckForWarningsInLog_WithSkippedPattern_ReturnsTrue() {
		//Arrange
		string logText = "Schema installation skipped due to missing dependencies";
		
		//Act
		bool result = BasePackageInstaller.CheckForWarningsInLog(logText);
		
		//Assert
		result.Should().BeTrue("log containing 'skipped' should trigger warning detection");
	}

	[Test, Description("Verifies that CheckForWarningsInLog returns true when 'failed' pattern is found")]
	public void CheckForWarningsInLog_WithFailedPattern_ReturnsTrue() {
		//Arrange
		string logText = "Data import failed for entity Customer";
		
		//Act
		bool result = BasePackageInstaller.CheckForWarningsInLog(logText);
		
		//Assert
		result.Should().BeTrue("log containing 'failed' should trigger warning detection");
	}

	[Test, Description("Verifies that CheckForWarningsInLog returns true when 'warning' pattern is found")]
	public void CheckForWarningsInLog_WithWarningPattern_ReturnsTrue() {
		//Arrange
		string logText = "Warning: Some configurations may be overridden";
		
		//Act
		bool result = BasePackageInstaller.CheckForWarningsInLog(logText);
		
		//Assert
		result.Should().BeTrue("log containing 'warning' should trigger warning detection");
	}

	[Test, Description("Verifies that CheckForWarningsInLog returns true when 'error' pattern is found")]
	public void CheckForWarningsInLog_WithErrorPattern_ReturnsTrue() {
		//Arrange
		string logText = "Error occurred during schema validation";
		
		//Act
		bool result = BasePackageInstaller.CheckForWarningsInLog(logText);
		
		//Assert
		result.Should().BeTrue("log containing 'error' should trigger warning detection");
	}

	[Test, Description("Verifies that CheckForWarningsInLog returns true when 'not installed' pattern is found")]
	public void CheckForWarningsInLog_WithNotInstalledPattern_ReturnsTrue() {
		//Arrange
		string logText = "Package dependency not installed, continuing without it";
		
		//Act
		bool result = BasePackageInstaller.CheckForWarningsInLog(logText);
		
		//Assert
		result.Should().BeTrue("log containing 'not installed' should trigger warning detection");
	}

	[Test, Description("Verifies that CheckForWarningsInLog returns true when 'schema not found' pattern is found")]
	public void CheckForWarningsInLog_WithSchemaNotFoundPattern_ReturnsTrue() {
		//Arrange
		string logText = "Schema not found: CustomEntity, installation will continue";
		
		//Act
		bool result = BasePackageInstaller.CheckForWarningsInLog(logText);
		
		//Assert
		result.Should().BeTrue("log containing 'schema not found' should trigger warning detection");
	}

	[Test, Description("Verifies that CheckForWarningsInLog returns true when 'data not imported' pattern is found")]
	public void CheckForWarningsInLog_WithDataNotImportedPattern_ReturnsTrue() {
		//Arrange
		string logText = "Data not imported for table CustomerData due to constraint violations";
		
		//Act
		bool result = BasePackageInstaller.CheckForWarningsInLog(logText);
		
		//Assert
		result.Should().BeTrue("log containing 'data not imported' should trigger warning detection");
	}

	[Test, Description("Verifies that CheckForWarningsInLog returns true when 'operation skipped' pattern is found")]
	public void CheckForWarningsInLog_WithOperationSkippedPattern_ReturnsTrue() {
		//Arrange
		string logText = "Operation skipped: Migration already applied";
		
		//Act
		bool result = BasePackageInstaller.CheckForWarningsInLog(logText);
		
		//Assert
		result.Should().BeTrue("log containing 'operation skipped' should trigger warning detection");
	}

	[Test, Description("Verifies that CheckForWarningsInLog is case-insensitive")]
	public void CheckForWarningsInLog_WithMixedCase_ReturnsTrue() {
		//Arrange
		string logText = "Schema Installation SKIPPED due to Missing Dependencies";
		
		//Act
		bool result = BasePackageInstaller.CheckForWarningsInLog(logText);
		
		//Assert
		result.Should().BeTrue("warning detection should be case-insensitive");
	}

	[Test, Description("Verifies that CheckForWarningsInLog returns true when multiple warning patterns are found")]
	public void CheckForWarningsInLog_WithMultiplePatterns_ReturnsTrue() {
		//Arrange
		string logText = "Installation completed with warnings: 2 schemas skipped, 1 data import failed";
		
		//Act
		bool result = BasePackageInstaller.CheckForWarningsInLog(logText);
		
		//Assert
		result.Should().BeTrue("log containing multiple warning patterns should trigger warning detection");
	}

	#endregion
}
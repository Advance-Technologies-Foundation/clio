using System;

using Clio.Common;
using Clio.Package;
using Clio.Requests;
using Clio.Tests.Command;
using Clio.WebApplication;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
internal class ApplicationInstallerTests : BaseClioModuleTests
{
    [Test]
    public void RestartApplicationAfterInstallPackageInNet6()
    {
        string packagePath = "T:\\TestClioPackage.gz";
        fileSystem.AddFile(packagePath, new System.IO.Abstractions.TestingHelpers.MockFileData(new byte[0]));
        EnvironmentSettings environmentSettings = new ()
        {
            IsNetCore = true
        };
        IApplicationClientFactory? applicationClientFactory = Substitute.For<IApplicationClientFactory>();
        IApplication? application = Substitute.For<IApplication>();
        IPackageArchiver? packageArchiver = Substitute.For<IPackageArchiver>();
        ISqlScriptExecutor? scriptExecutor = Substitute.For<ISqlScriptExecutor>();
        IServiceUrlBuilder? serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
        IApplicationLogProvider? applicationLogProvider = Substitute.For<IApplicationLogProvider>();
        ILogger? logger = Substitute.For<ILogger>();
        IPackageLockManager? packageLockManager = Substitute.For<IPackageLockManager>();
        FileSystem clioFileSystem = new (fileSystem);
        ApplicationInstaller applicationInstaller = new (applicationLogProvider,
            environmentSettings,
            applicationClientFactory,
            application,
            packageArchiver,
            scriptExecutor,
            serviceUrlBuilder,
            clioFileSystem,
            logger,
            packageLockManager);
        applicationInstaller.Install(packagePath, environmentSettings);
        application.Received(1).Restart();
    }

    [Test]
    public void RestartApplicationAfterInstallFolderInNet6()
    {
        string packageFolderPath = "T:\\TestClioPackageFolder";
        fileSystem.AddDirectory(packageFolderPath);
        EnvironmentSettings environmentSettings = new ()
        {
            IsNetCore = true
        };
        IApplicationClientFactory? applicationClientFactory = Substitute.For<IApplicationClientFactory>();
        IApplication? application = Substitute.For<IApplication>();
        IPackageArchiver? packageArchiver = Substitute.For<IPackageArchiver>();
        packageArchiver.When(p => p.Pack(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
            Arg.Any<bool>())).Do(callInfo => { fileSystem.AddEmptyFile(callInfo[1].ToString()); });
        ISqlScriptExecutor? scriptExecutor = Substitute.For<ISqlScriptExecutor>();
        IServiceUrlBuilder? serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
        ILogger? logger = Substitute.For<ILogger>();
        IPackageLockManager? packageLockManager = Substitute.For<IPackageLockManager>();
        FileSystem clioFileSystem = new (fileSystem);
        IApplicationLogProvider? applicationLogProvider = Substitute.For<IApplicationLogProvider>();
        ApplicationInstaller applicationInstaller = new (applicationLogProvider,
            environmentSettings,
            applicationClientFactory,
            application,
            packageArchiver,
            scriptExecutor,
            serviceUrlBuilder,
            clioFileSystem,
            logger,
            packageLockManager);
        applicationInstaller.Install(packageFolderPath, environmentSettings);
        application.Received(1).Restart();
    }

    [Test]
    public void CatchRestartApplicationErrorAfterInstallFolderInNet6()
    {
        string packageFolderPath = "T:\\TestClioPackageFolder";
        fileSystem.AddDirectory(packageFolderPath);
        EnvironmentSettings environmentSettings = new ()
        {
            IsNetCore = true
        };
        IApplicationClientFactory? applicationClientFactory = Substitute.For<IApplicationClientFactory>();
        IApplication? application = Substitute.For<IApplication>();
        application.When(r => r.Restart()).Do(_ => throw new Exception("Restart application exception"));
        IPackageArchiver? packageArchiver = Substitute.For<IPackageArchiver>();
        packageArchiver.When(p => p.Pack(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
            Arg.Any<bool>())).Do(callInfo => { fileSystem.AddEmptyFile(callInfo[1].ToString()); });
        ISqlScriptExecutor? scriptExecutor = Substitute.For<ISqlScriptExecutor>();
        IServiceUrlBuilder? serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
        ILogger? logger = Substitute.For<ILogger>();
        IPackageLockManager? packageLockManager = Substitute.For<IPackageLockManager>();
        FileSystem clioFileSystem = new (fileSystem);
        IApplicationLogProvider? applicationLogProvider = Substitute.For<IApplicationLogProvider>();
        ApplicationInstaller applicationInstaller = new (applicationLogProvider,
            environmentSettings,
            applicationClientFactory,
            application,
            packageArchiver,
            scriptExecutor,
            serviceUrlBuilder,
            clioFileSystem,
            logger,
            packageLockManager);
        Assert.DoesNotThrow(() => applicationInstaller.Install(packageFolderPath, environmentSettings));
    }

    [Test]
    public void ReturnErrorApplicationErrorAfterInstallFolderInNet6()
    {
        string packageFolderPath = "T:\\TestClioPackageFolder";
        fileSystem.AddDirectory(packageFolderPath);
        EnvironmentSettings environmentSettings = new ()
        {
            IsNetCore = true
        };
        IApplicationClientFactory? applicationClientFactory = Substitute.For<IApplicationClientFactory>();
        IApplication? application = Substitute.For<IApplication>();
        application.When(r => r.Restart()).Do(_ => throw new Exception("Restart application exception"));
        IPackageArchiver? packageArchiver = Substitute.For<IPackageArchiver>();
        packageArchiver.When(p => p.Pack(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
            Arg.Any<bool>())).Do(callInfo => { fileSystem.AddEmptyFile(callInfo[1].ToString()); });
        ISqlScriptExecutor? scriptExecutor = Substitute.For<ISqlScriptExecutor>();
        IServiceUrlBuilder? serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
        ILogger? logger = Substitute.For<ILogger>();
        IPackageLockManager? packageLockManager = Substitute.For<IPackageLockManager>();
        FileSystem clioFileSystem = new (fileSystem);
        IApplicationLogProvider? applicationLogProvider = Substitute.For<IApplicationLogProvider>();
        applicationLogProvider.GetInstallationLog(Arg.Any<EnvironmentSettings>())
            .Returns("SOME LOG WITHOUT SUCCESS MESSAGE");
        ApplicationInstaller applicationInstaller = new (applicationLogProvider,
            environmentSettings,
            applicationClientFactory,
            application,
            packageArchiver,
            scriptExecutor,
            serviceUrlBuilder,
            clioFileSystem,
            logger,
            packageLockManager);
        GlobalContext.FailOnError = true;
        bool result = applicationInstaller.Install(packageFolderPath, environmentSettings);
        result.Should().BeFalse();
    }

    [Test]
    public void ReturnSuccessIfApplicationLogContainsSuccessMessage()
    {
        string packageFolderPath = "T:\\TestClioPackageFolder";
        fileSystem.AddDirectory(packageFolderPath);
        EnvironmentSettings environmentSettings = new ()
        {
            IsNetCore = true
        };
        IApplicationClientFactory? applicationClientFactory = Substitute.For<IApplicationClientFactory>();
        IApplication? application = Substitute.For<IApplication>();
        application.When(r => r.Restart()).Do(_ => throw new Exception("Restart application exception"));
        IPackageArchiver? packageArchiver = Substitute.For<IPackageArchiver>();
        packageArchiver.When(p => p.Pack(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
            Arg.Any<bool>())).Do(callInfo => { fileSystem.AddEmptyFile(callInfo[1].ToString()); });
        ISqlScriptExecutor? scriptExecutor = Substitute.For<ISqlScriptExecutor>();
        IServiceUrlBuilder? serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
        ILogger? logger = Substitute.For<ILogger>();
        IPackageLockManager? packageLockManager = Substitute.For<IPackageLockManager>();
        FileSystem clioFileSystem = new (fileSystem);
        IApplicationLogProvider? applicationLogProvider = Substitute.For<IApplicationLogProvider>();
        applicationLogProvider.GetInstallationLog(Arg.Any<EnvironmentSettings>())
            .Returns("appLication InstallEd successfully");
        ApplicationInstaller applicationInstaller = new (applicationLogProvider,
            environmentSettings,
            applicationClientFactory,
            application,
            packageArchiver,
            scriptExecutor,
            serviceUrlBuilder,
            clioFileSystem,
            logger,
            packageLockManager);
        GlobalContext.FailOnError = true;
        bool result = applicationInstaller.Install(packageFolderPath, environmentSettings);
        result.Should().BeTrue();
    }
}

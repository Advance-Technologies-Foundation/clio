using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using Clio.WebApplication;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clio.Tests
{
	[TestFixture]
	internal class ApplicationInstallerTests : BaseClioModuleTests
	{
		[Test]
		public void RestartApplicationAfterInstallInNet6() {
			string packagePath = "T:\\TestClioPackage.gz";
			_fileSystem.AddFile(packagePath, new System.IO.Abstractions.TestingHelpers.MockFileData(new byte[0]));
			EnvironmentSettings environmentSettings = new EnvironmentSettings();
			environmentSettings.IsNetCore = true;
			var applicationClientFactory = Substitute.For<IApplicationClientFactory>();
			var application = Substitute.For<IApplication>();
			var packageArchiver = Substitute.For<IPackageArchiver>();
			var scriptExecutor = Substitute.For<ISqlScriptExecutor>();
			var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
			var logger = Substitute.For<ILogger>();
			var clioFileSystem = new FileSystem(_fileSystem);
			ApplicationInstaller applicationInstaller = new ApplicationInstaller(environmentSettings,
				applicationClientFactory,
				application,
				packageArchiver,
				scriptExecutor,
				serviceUrlBuilder,
				clioFileSystem,
				logger
			);
			applicationInstaller.Install(packagePath, environmentSettings);
			application.Received(1).Restart();
		}
	}
}

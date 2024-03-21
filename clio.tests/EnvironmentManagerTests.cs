using Clio.Command;
using NUnit.Framework;
using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Clio.Tests.Extensions;
using Clio.Tests.Infrastructure;
using Autofac;
using CreatioModel;

namespace Clio.Tests
{
	[TestFixture]
	internal class EnvironmentManagerTest
	{
		IFileSystem _fileSystem = TestFileSystem.MockExamplesFolder("deployments-manifest");
		IContainer _container;

		[SetUp]
		public void Setup() {
			var bindingModule = new BindingsModule(_fileSystem);
			_container = bindingModule.Register();
		}


		[TestCase("easy-creatio-config.yaml", 3)]
		[TestCase("full-creatio-config.yaml", 2)]
		public void GetApplicationsFrommanifest_if_applicationExists(string fileName, int appCount) {
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{fileName}";
			var applications = environmentManager.GetApplicationsFromManifest(manifestFilePath);
			Assert.AreEqual(appCount, applications.Count);
		}

		[TestCase(0, "CrtCustomer360", "1.0.1", "easy-creatio-config.yaml")]
		[TestCase(1, "CrtCaseManagment", "1.0.2", "easy-creatio-config.yaml")]
		[TestCase(0, "CrtCustomer360", "1.0.1", "full-creatio-config.yaml")]
		[TestCase(1, "CrtCaseManagment", "1.0.2", "full-creatio-config.yaml")]
		public void GetApplicationsFrommanifest_if_applicationExists(int appIndex, string appName, string appVersion, string manifestFileName) {
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";
			var applications = environmentManager.GetApplicationsFromManifest(manifestFilePath);
			Assert.AreEqual(appName, applications[appIndex].Name);
			Assert.AreEqual(appVersion, applications[appIndex].Version);
		}

		[TestCase("easy-creatio-config.yaml")]
		public void FindApplicationsFromManifest_In_AppHub(string manifestFileName) {
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";
			var applicationsFromAppHub = environmentManager.FindApllicationsInAppHub(manifestFilePath);
			Assert.AreEqual(2, applicationsFromAppHub.Count);

		}

	}
}

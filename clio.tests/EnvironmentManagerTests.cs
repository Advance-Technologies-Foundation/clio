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
using System.Collections.Generic;
using Quartz.Impl.Matchers;
using System.Linq;
using FluentAssertions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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

		[TestCase("easy-creatio-config.yaml", "CrtCustomer360", "//tscrm.com/dfs-ts/MyAppHub/CrtCustomer360/1.0.1/CrtCustomer360_1.0.1.zip")]
		[TestCase("easy-creatio-config.yaml", "CrtCaseManagment", "//tscrm.com/dfs-ts/MyAppHub/CrtCaseManagment/1.0.2/CrtCaseManagment_1.0.2.zip")]
		public void FindAppHubPath_In_FromManifest(string manifestFileName, string appName, string path) {
			string resultPath = path.Replace('/', Path.DirectorySeparatorChar);
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";
			var app = environmentManager.FindApllicationsInAppHub(manifestFilePath).Where(s => s.Name == appName).FirstOrDefault();
			Assert.AreEqual(resultPath, app.ZipFileName);
		}

		[TestCase("easy-creatio-config.yaml", "https://preprod.creatio.com", "https://preprod.creatio.com/0/ServiceModel/AuthService.svc/Login")]
		[TestCase("full-creatio-config.yaml", "https://production.creatio.com", "https://production.creatio.com/0/ServiceModel/AuthService.svc/Login")]
		public void GetEnvironmentUrl_FromManifest(string manifestFileName, string url, string authAppUrl) {
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";
			EnvironmentSettings env = environmentManager.GetEnvironmentFromManifest(manifestFilePath);
			Assert.AreEqual(url, env.Uri);
			Assert.AreEqual(authAppUrl, env.AuthAppUri);
		}
		
		[TestCase("feature-creatio-config.yaml", 3)]
		public void testOne(string manifestFileName, int count) {
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";
			IEnumerable<Feature> features = environmentManager.GetFeaturesFromManifest(manifestFilePath);
			features.Count().Should().Be(count);
			
			List<Feature> expected = [
				new Feature {
					Code = "Feature1",
					Value = true
				},

				new Feature {
					Code = "Feature2",
					Value = false,
					UserValues = new Dictionary<string, bool>() {
						{"user1", true},
						{"user2", false},
						{"user3", true},
						{"user4", true},
					}
				},

				new Feature {
					Code = "Feature3",
					Value = false
				}
			];
			features.Should().BeEquivalentTo(expected);	
		}


		[TestCase("settings-creatio-config.yaml", 7)]
		public void GetSettingsFromManifest(string manifestFileName, int count) {
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";
			IEnumerable<CreatioManifestSetting> settings = environmentManager.GetSettingsFromManifest(manifestFilePath);
			settings.Count().Should().Be(count);
		}
	}
}

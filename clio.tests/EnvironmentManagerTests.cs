using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Autofac;
using Clio.Command;
using Clio.Tests.Command;
using Clio.Tests.Extensions;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests
{
	[TestFixture]
	internal class EnvironmentManagerTest : BaseClioModuleTests {

		protected override MockFileSystem CreateFs() {
			var mockFS = base.CreateFs();
			mockFS.MockExamplesFolder("deployments-manifest");
			mockFS.AddFile("\\MyAppHub\\cliogate\\master\\cliogate_master_2.1.1.zip", "testbody");
			mockFS.AddFile("\\MyAppHub\\cliogate-netcore\\master\\cliogate-netcore_master_2.0.2.zip", "testbody");
			mockFS.AddFile("\\MyAppHub\\cliogate-netcore\\master___\\cliogate-netcore_master____2.3.4.zip", "testbody");
			mockFS.AddFile("\\MyAppHub\\cliogate-netcore\\master_\\cliogate-netcore_master__2.4.6.zip", "testbody");
			return mockFS;

		}

		[TestCase("easy-creatio-config.yaml", 3)]
		[TestCase("full-creatio-config.yaml", 2)]
		public void GetApplicationsFrommanifest_if_applicationExists(string fileName, int appCount) {
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{fileName}";
			var applications = environmentManager.GetApplicationsFromManifest(manifestFilePath);
			Assert.AreEqual(appCount, applications.Count);
		}

		[TestCase(0, "CrtCustomer360", "1.5.2", "easy-creatio-config.yaml")]
		[TestCase(1, "CrtCaseManagment", "1.0.2", "easy-creatio-config.yaml")]
		[TestCase(0, "CrtCustomer360", "1.5.2", "full-creatio-config.yaml")]
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
			var applicationsFromAppHub = environmentManager.FindApplicationsInAppHub(manifestFilePath);
			Assert.AreEqual(2, applicationsFromAppHub.Count);
		}

		public static IEnumerable<TestCaseData> FindApplicationsFromManifestTestCases {
			get
			{
				yield return new TestCaseData("easy-creatio-config-with-branches.yaml",
					new []{ "\\MyAppHub\\cliogate\\master\\cliogate_master_2.1.1.zip",
						"\\MyAppHub\\cliogate-netcore\\master\\cliogate-netcore_master_2.0.2.zip",
						"\\MyAppHub\\cliogate-netcore\\master___\\cliogate-netcore_master____2.3.4.zip",
						"\\MyAppHub\\cliogate-netcore\\master_\\cliogate-netcore_master__2.4.6.zip"
					});
			}
		}

		[TestCaseSource(nameof(FindApplicationsFromManifestTestCases))]
		public void FindApplicationsFromManifest_InAppHub_WithCorrectBranch(string manifestFileName, string[] zipFilePathes) {
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";
			var applicationsFromAppHub = environmentManager.FindApplicationsInAppHub(manifestFilePath);
			Assert.AreEqual(3, applicationsFromAppHub.Count);
			foreach (var app in applicationsFromAppHub) {
				zipFilePathes.Contains(app.ZipFileName).Should().BeTrue($"App zip fil name{app.ZipFileName} should exists.");
			}
		}

		[TestCase("easy-creatio-config.yaml", "CrtCustomer360", "//tscrm.com/dfs-ts/MyAppHub/CrtCustomer360/1.5.2/CrtCustomer360_1.5.2.zip")]
		[TestCase("easy-creatio-config.yaml", "CrtCaseManagment", "//tscrm.com/dfs-ts/MyAppHub/CrtCaseManagment/1.0.2/CrtCaseManagment_1.0.2.zip")]
		public void FindAppHubPath_In_FromManifest(string manifestFileName, string appName, string path) {
			string resultPath = path.Replace('/', Path.DirectorySeparatorChar);
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";
			var app = environmentManager.FindApplicationsInAppHub(manifestFilePath).Where(s => s.Name == appName).FirstOrDefault();
			Assert.AreEqual(resultPath, app.ZipFileName);
		}

		[TestCase("easy-creatio-config.yaml", "CrtCustomer360", "//tscrm.com/dfs-ts/MyAppHub/Customer360/1.5.2/Customer360_1.5.2.zip")]
		public void FindAppHubPath_In_FromManifest_ByAliases(string manifestFileName, string appName, string path) {
			string resultPath = path.Replace('/', Path.DirectorySeparatorChar);
			_fileSystem.MockFile(resultPath);
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";
			var app = environmentManager.FindApplicationsInAppHub(manifestFilePath).Where(s => s.Name == appName).FirstOrDefault();
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
		public void ParsesYamlAndReturnsStructure(string manifestFileName, int count) {
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
						{"Supervisor", true},
						{"System administrators", false},
						{"Developer", true},
						{"2nd-line support", true},
					}
				},

				new Feature {
					Code = "Feature3",
					Value = false
				}
			];
			features.Should().BeEquivalentTo(expected);	
		}


		[TestCase("setting-creatio-config.yaml", 7)]
		public void GetSettingsFromManifest(string manifestFileName, int count) {
			 //Arrange
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";

			//Act
			IEnumerable<CreatioManifestSetting> settings = environmentManager.GetSettingsFromManifest(manifestFilePath);
			//Assert
			settings.Count().Should().Be(count);
			
			List<CreatioManifestSetting> expected = [
            				new CreatioManifestSetting {
            					Code = "IntSysSettingsATF",
            					Value = "10"
            				},
							new CreatioManifestSetting {
            					Code = "FloatSysSettingsATF",
            					Value = "0.5"
            				},
            
							new CreatioManifestSetting {
            					Code = "StringSettingsATF",
            					Value = "ATF"
            				},
            
							new CreatioManifestSetting {
            					Code = "DateTimeSettingsATF",
            					Value = "2021-01-01T00:00:00"
            				},
            
							new CreatioManifestSetting {
            					Code = "GuidSettingsATF",
            					Value = "00000000-0000-0000-0000-000000000001"
            				},
            
							new CreatioManifestSetting {
            					Code = "LookupSettingsATF",
            					Value = "TextLookupValue"
            				},
            				new CreatioManifestSetting {
            					Code = "BooleanSettingsATF",
            					Value = "false",
            					UserValues = new Dictionary<string, string>() {
            						{"Supervisor", "true"},
            						{"System administrators", "false"},
            						{"Developer", "true"},
            						{"2nd-line support", "true"},
            					}
            				}
            			];
			settings.Should().BeEquivalentTo(expected);
			
		}
		
		
		[TestCase("setting-creatio-config-broken.yaml", 7)]
		public void GetSettingsFromManifest_Throws_When_YAML_ValueNull(string manifestFileName, int count) {
			 //Arrange
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";

			//Act + Assert
			Action act = ()=>environmentManager.GetSettingsFromManifest(manifestFilePath);
			act.Should().Throw<Exception>("null values should throw")
				.WithMessage("*Setting value cannot be null for: [IntSysSettingsATF]");
		}
		
		[TestCase("setting-creatio-config-broken.yaml", 7)]
		public void GetSettingsFromManifest_Throws_When_YAML_CodeNull(string manifestFileName, int count) {
			 //Arrange
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";

			//Act + Assert
			Action act = ()=>environmentManager.GetSettingsFromManifest(manifestFilePath);
			act.Should().Throw<Exception>("null values should throw")
				.WithMessage("Setting code cannot be null or empty. Found invalid values on lines *");
		}

		[TestCase("web-services-creatio.yaml", 2)]
		public void GetWebServicesFromManifest(string manifestFileName, int count) {
			//Arrange
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";

			//Act
			IEnumerable<CreatioManifestWebService> webservices = environmentManager.GetWebServicesFromManifest(manifestFilePath);
			//Assert
			webservices.Count().Should().Be(count);
			List<CreatioManifestWebService> expected = [
				new CreatioManifestWebService {
					Name = "WebService1",
					Url = "https://preprod.creatio.com/0/ServiceModel/EntityDataService.svc"
				},
				new CreatioManifestWebService {
					Name = "WebService2",
					Url = "https://preprod.creatio.com/0/ServiceModel/EntityDataService.svc"
				}
			];
			webservices.Should().BeEquivalentTo(expected);
		}

		[TestCase("sections-without-items-creatio.yaml")]
		public void GetWebServicesFromManifest_WhenExistsSectionButNotExistsItems(string manifestFileName) {
			//Arrange
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFilePath = $"C:\\{manifestFileName}";

			//Act
			var webservices = environmentManager.GetWebServicesFromManifest(manifestFilePath);
			var features = environmentManager.GetFeaturesFromManifest(manifestFilePath);
			var settings = environmentManager.GetSettingsFromManifest(manifestFilePath);
			//Assert
			webservices.Count().Should().Be(0);
			features.Count().Should().Be(0);
			settings.Count().Should().Be(0);
		}

		[Test]
		public void GetEnvironmnetPackagesManifest() {
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFileName = $"C:\\creatio-config-package.yaml";
			var expectedPackagesCount = 2;
			List<CreatioManifestPackage> packages = environmentManager.GetPackagesGromManifest(manifestFileName);
			Assert.AreEqual(expectedPackagesCount, packages.Count);
			List<CreatioManifestPackage> expected = [
				new CreatioManifestPackage {
					Name = "Base",
					Hash = "1234567890"
				},
				new CreatioManifestPackage {
					Name = "UI",
					Hash = "0987654321"
				}
			];
			packages.Should().BeEquivalentTo(expected);
		}

		[Test]
		public void GetEnvironmnetEmptyPackagesManifest() {
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var manifestFileName = $"C:\\creatio-config-empty-package.yaml";
			var expectedPackagesCount = 0;
			List<CreatioManifestPackage> packages = environmentManager.GetPackagesGromManifest(manifestFileName);
			Assert.AreEqual(expectedPackagesCount, packages.Count);
		}

		[Test]
		public void SaveEnvironmnetPackagesManifest() {
			string environmentUrl = "https://preprod.atf.com";
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var expectedManifestFileName = $"C:\\creatio-config-package.yaml";
			var actualManifestFileName = $"C:\\actual-creatio-config-package.yaml";
			var expectedPackagesCount = 2;
			List<CreatioManifestPackage> environmnetPackages = [
				new CreatioManifestPackage {
					Name = "Base",
					Hash = "1234567890"
				},
				new CreatioManifestPackage {
					Name = "UI",
					Hash = "0987654321"
				}
			];
			var environmentManifest = new EnvironmentManifest() {
				EnvironmentSettings = new EnvironmentSettings() {
					Uri = environmentUrl,
					AuthAppUri = null
				},
				Packages = environmnetPackages
			};
			environmentManager.SaveManifestToFile(actualManifestFileName, environmentManifest);
			var expectedFile = _fileSystem.File.ReadAllText(expectedManifestFileName);
			var actualFile = _fileSystem.File.ReadAllText(actualManifestFileName);
			Assert.AreEqual(expectedFile, actualFile);
		}

		[Test]
		public void SaveEnvironmnetPackagesInReversOrderManifest() {
			string environmentUrl = "https://preprod.atf.com";
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var expectedManifestFileName = $"C:\\creatio-config-package.yaml";
			var actualManifestFileName = $"C:\\actual-creatio-config-package.yaml";
			var expectedPackagesCount = 2;
			List<CreatioManifestPackage> environmnetPackages = [
				new CreatioManifestPackage {
					Name = "UI",
					Hash = "0987654321"
				},
				new CreatioManifestPackage {
					Name = "Base",
					Hash = "1234567890"
				}
			];
			var environmentManifest = new EnvironmentManifest() {
				EnvironmentSettings = new EnvironmentSettings() {
					Uri = environmentUrl,
					AuthAppUri = null
				},
				Packages = environmnetPackages
			};
			environmentManager.SaveManifestToFile(actualManifestFileName, environmentManifest);
			var expectedFile = _fileSystem.File.ReadAllText(expectedManifestFileName);
			var actualFile = _fileSystem.File.ReadAllText(actualManifestFileName);
			Assert.AreEqual(expectedFile, actualFile);
		}

		[Test]
		public void ThrowException_If_SaveEnvironmnetManifest_in_ExistingFile() {
			var existingManifestFilePath = $"C:\\creatio-config-package.yaml";
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var environmentManifest = new EnvironmentManifest();
			Action act = () => environmentManager.SaveManifestToFile(existingManifestFilePath, environmentManifest);
			act.Should().Throw<Exception>().WithMessage($"Manifest file already exists: {existingManifestFilePath}");	
		}

		[Test]
		public void RewriteExistingManifest_If_SaveEnvironmnetManifest_in_ExistingFile() {
			var existingManifestFilePath = $"C:\\creatio-config-package.yaml";
			var environmentManager = _container.Resolve<IEnvironmentManager>();
			var environmentManifest = new EnvironmentManifest();
			Action act = () => environmentManager.SaveManifestToFile(existingManifestFilePath, environmentManifest, true);
			act.Should().NotThrow<Exception>();
		}
	}
}

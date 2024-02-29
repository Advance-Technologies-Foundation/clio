using Autofac;
using Clio.Workspaces;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Terrasoft.Common.Json;

namespace Clio.Tests
{
	[TestFixture]
	internal class WorkspaceTest
	{
		private EnvironmentSettings GetTestEnvironmentSettings() {
			var envSettingsJson = @"{
				  ""Uri"": ""https://forrester-lcap-demo-dev.creatio.com/"",
				  ""Login"": ""Supervisor"",
				  ""Password"": ""Supervisor1"",
				  ""Maintainer"": """",
				  ""IsNetCore"": false,
				  ""ClientId"": """",
				  ""ClientSecret"": """",
				  ""WorkspacePathes"": """",
				  ""AuthAppUri"": ""https://forrester-lcap-demo-dev-is.creatio.com/connect/token/"",
				  ""SimpleloginUri"": ""https://forrester-lcap-demo-dev.creatio.com/0/Shell/?simplelogin=true"",
				  ""Safe"": false,
				  ""DeveloperModeEnabled"": false
			}";
			return Json.Deserialize<EnvironmentSettings>(envSettingsJson);
		}

		private IWorkspace GetTestWorkspace(EnvironmentSettings envSettings) {
			var bindingModule = new BindingsModule();
			var diContainer = bindingModule.Register(envSettings);
			var workspace = diContainer.Resolve<IWorkspace>();
			return workspace;
		}

		[Test]
		public void PublishWorkspaceTest() {
			// Arrange
			var envSettings = GetTestEnvironmentSettings();
			var workspace = GetTestWorkspace(envSettings);
			// Act
			string appStorePath = @"C:\Temp\clioAppStore";
			string appName = "iframe-sample";
			string appVersion = "1.0.0";
			string fileName = $"{appName}_{appVersion}.zip";
			string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..");
			string exampleWorkspacePath = Path.Combine(originClioSourcePath, "Examples","workspaces",appName);
			var releaseFileName = workspace.PublishToFolder(exampleWorkspacePath, appStorePath, appName, appVersion);
			// Assert
			var expectedFileName = Path.Combine(appStorePath,appName,appVersion, fileName);
			Assert.AreEqual(expectedFileName, releaseFileName);
			var versionFileExist = File.Exists(expectedFileName);
			Assert.AreEqual(true, versionFileExist);
		}	
	}
}

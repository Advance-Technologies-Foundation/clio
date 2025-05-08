using System;
using System.IO;
using System.Text.Json;
using Autofac;
using Clio.Workspaces;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
internal class WorkspaceTest
{
    private BindingsModule _bindingModule;
    private IContainer _diContainer;

    private EnvironmentSettings GetTestEnvironmentSettings()
    {
        string envSettingsJson = @"{
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
        return JsonSerializer.Deserialize<EnvironmentSettings>(envSettingsJson);
    }

    private IWorkspace GetTestWorkspace(EnvironmentSettings envSettings)
    {
        _bindingModule = new BindingsModule();
        _diContainer = _bindingModule.Register(envSettings);
        IWorkspace workspace = _diContainer.Resolve<IWorkspace>();
        return workspace;
    }

    [Test]
    public void PublishWorkspaceTest()
    {
        // Arrange
        string appStorePath = @"C:\Temp\clioAppStore";
        string appName = "iframe-sample";
        string appVersion = "1.0.0";
        string fileName = $"{appName}_{appVersion}.zip";
        string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
        string expectedFileName = Path.Combine(appStorePath, appName, appVersion, fileName);
        string exampleWorkspacePath = Path.Combine(originClioSourcePath, "Examples", "workspaces", appName);
        try
        {
            // Act
            EnvironmentSettings envSettings = GetTestEnvironmentSettings();
            IWorkspace workspace = GetTestWorkspace(envSettings);
            string releaseFileName = workspace.PublishToFolder(exampleWorkspacePath, appStorePath, appName, appVersion);
            // Assert
            expectedFileName.Should().Be(releaseFileName);
            bool versionFileExist = File.Exists(expectedFileName);
            versionFileExist.Should().BeTrue();
            new FileInfo(expectedFileName).Length.Should().BeGreaterThan(80000);
        }
        finally
        {
            if (File.Exists(expectedFileName))
            {
                File.Delete(expectedFileName);
            }
        }
    }

    [TestCase("trunk")]
    [TestCase("master")]
    [TestCase("feature/rnd-2035")]
    [TestCase("bugf%i_:{}x/rnd-2035")]
    [TestCase("feature/rnd-2035")]
    public void PublishWorkspaceWithBranchTest(string branch)
    {
        // Arrange
        string appStorePath = @"C:\Temp\clioAppStore";
        string appName = "iframe-sample";
        string appVersion = "1.0.0";
        string expectedFileName =
            Clio.Workspaces.Workspace.GetSanitizeFileNameFromString($"{appName}_{branch}_{appVersion}.zip");
        string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
        string exampleWorkspacePath = Path.Combine(originClioSourcePath, "Examples", "workspaces", appName);
        string branchFolderName = Clio.Workspaces.Workspace.GetSanitizeFileNameFromString(branch);
        // app_store_path\app_name\bramch\file_name
        string expectedFilePath = Path.Combine(appStorePath, appName, branchFolderName, expectedFileName);
        try
        {
            // Act
            EnvironmentSettings envSettings = GetTestEnvironmentSettings();
            IWorkspace workspace = GetTestWorkspace(envSettings);
            string releaseFileName =
                workspace.PublishToFolder(exampleWorkspacePath, appStorePath, appName, appVersion, branch);
            // Assert
            expectedFilePath.Should().Be(releaseFileName);
            bool versionFileExist = File.Exists(expectedFilePath);
            versionFileExist.Should().BeTrue();
            new FileInfo(expectedFilePath).Length.Should().BeGreaterThan(80000);
        }
        finally
        {
            if (File.Exists(expectedFilePath))
            {
                File.Delete(expectedFilePath);
            }
        }
    }
}

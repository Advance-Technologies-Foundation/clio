using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Json;
using Clio.Command.ApplicationCommand;
using Clio.ComposableApplication;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ApplicationCommand;

// TODO - Extract manager aseertion to ComposibleApp tests
internal class SetApplicationVersionCommandTest : BaseCommandTests<SetApplicationVersionOption>
{
    private static readonly string MockPackageFolderPath = Path.Combine("C:", "MockPackageFolder");

    private static readonly string MockPackageAppDescriptorPath =
        Path.Combine(MockPackageFolderPath, "Files", "app-descriptor.json");

    private static readonly string MockWorspacePath = Path.Combine("C:", "MockWorkspaceFolder");

    private static readonly string MockWorkspaceAppPackageFolderPath =
        Path.Combine(MockWorspacePath, "packages", "IFrameSample");

    private static readonly string MockWorkspaceAppDescriptorPath =
        Path.Combine(MockWorkspaceAppPackageFolderPath, "Files", "app-descriptor.json");

    private MockFileSystem _fileSystem;

    private static MockFileSystem CreateFs(string filePath, string packagePath)
    {
        string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
        string appDescriptorExamplesDescriptorPath =
            Path.Combine(originClioSourcePath, "Examples", "AppDescriptors", filePath);
        string mockAppDescriptorFilePath = Path.Combine(packagePath, "Files", "app-descriptor.json");
        return new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { mockAppDescriptorFilePath, new MockFileData(File.ReadAllText(appDescriptorExamplesDescriptorPath)) }
        });
    }

    private static MockFileSystem CreateFs(Dictionary<string, string> appDescriptors)
    {
        string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
        MockFileSystem mockFileSystem = new();
        foreach (KeyValuePair<string, string> appDescriptor in appDescriptors)
        {
            string appDescriptorExamplesDescriptorPath =
                Path.Combine(originClioSourcePath, "Examples", "AppDescriptors", appDescriptor.Value);
            string mockAppDescriptorJsonPath = Path.Combine(MockWorspacePath, "packages", appDescriptor.Key, "Files",
                "app-descriptor.json");
            mockFileSystem.AddFile(
                mockAppDescriptorJsonPath,
                new MockFileData(File.ReadAllText(appDescriptorExamplesDescriptorPath)));
        }

        return mockFileSystem;
    }

    [TestCase("app-descriptor_v.json")]
    [TestCase("app-descriptor_wv.json")]
    [TestCase("app-descriptor_dv.json")]
    public void SetVersion_WhenWorkspaceContainsOneApplication(string descriptorPath)
    {
        _fileSystem = CreateFs(descriptorPath, MockWorkspaceAppPackageFolderPath);
        string expectedVersion = "8.1.1";
        ComposableApplicationManager composableApplicationManager = new(_fileSystem, null, null, null);
        SetApplicationVersionCommand command = new(composableApplicationManager);
        string worspaceFolderPath = MockWorspacePath;
        command.Execute(new SetApplicationVersionOption
        {
            Version = expectedVersion, WorspaceFolderPath = worspaceFolderPath
        });
        JsonValue? objectJson = JsonObject.Parse(_fileSystem.File.ReadAllText(MockWorkspaceAppDescriptorPath));
        string actualVersion = objectJson["Version"];
        _fileSystem.FileExists(MockWorkspaceAppDescriptorPath).Should().BeTrue();
        expectedVersion.Should().Be(actualVersion);
        _fileSystem.File.ReadAllLines(MockWorkspaceAppDescriptorPath).Length.Should().BeGreaterThan(20);
    }

    [Test]
    public void SetVersion_ThrowException_WhenWorkspaceContainsMoreThanOneApplication()
    {
        Dictionary<string, string> appDescriptions = new()
        {
            { "Package1", "app1-app-descriptor.json" }, { "Package2", "app2-app-descriptor.json" }
        };
        _fileSystem = CreateFs(appDescriptions);
        ComposableApplicationManager composableApplicationManager = new(_fileSystem, null, null, null);
        SetApplicationVersionCommand command = new(composableApplicationManager);
        string expectedVersion = "8.1.1";
        string worspaceFolderPath = MockWorspacePath;
        Exception? exception = Assert.Throws<Exception>(() =>
            command.Execute(new SetApplicationVersionOption
            {
                Version = expectedVersion, WorspaceFolderPath = worspaceFolderPath
            }));
        exception.Message.Contains("Package1").Should().BeTrue();
        exception.Message.Contains("Package2").Should().BeTrue();
    }

    [Test]
    public void SetVersion_ThrowExceptionWhenAplicationExtendedAndPackageNotDefined()
    {
        Dictionary<string, string> appDescriptions = new()
        {
            { "Package1", "app1-app-descriptor.json" }, { "Package2", "app1-ext-app-descriptor.json" }
        };
        _fileSystem = CreateFs(appDescriptions);
        string expectedVersion = "8.1.1";
        ComposableApplicationManager composableApplicationManager = new(_fileSystem, null, null, null);
        SetApplicationVersionCommand command = new(composableApplicationManager);
        string worspaceFolderPath = MockWorspacePath;
        Exception? exception = Assert.Throws<Exception>(() =>
            command.Execute(new SetApplicationVersionOption
            {
                Version = expectedVersion, WorspaceFolderPath = worspaceFolderPath
            }));
        exception.Message.Contains("Package1").Should().BeTrue();
        exception.Message.Contains("Package2").Should().BeTrue();
    }

    [Test]
    public void SetVersion_WhenAplicationExtendedAndPackageDefined()
    {
        Dictionary<string, string> appDescriptions = [];
        string extendPackageName = "Package2";
        appDescriptions.Add("Package1", "app1-app-descriptor.json");
        appDescriptions.Add(extendPackageName, "app1-ext-app-descriptor.json");
        _fileSystem = CreateFs(appDescriptions);
        string expectedVersion = "8.1.1";
        ComposableApplicationManager composableApplicationManager = new(_fileSystem, null, null, null);
        SetApplicationVersionCommand command = new(composableApplicationManager);
        string worspaceFolderPath = MockWorspacePath;
        command.Execute(new SetApplicationVersionOption
        {
            Version = expectedVersion, WorspaceFolderPath = worspaceFolderPath, PackageName = extendPackageName
        });
    }

    [TestCase("app-descriptor_v.json")]
    [TestCase("app-descriptor_wv.json")]
    [TestCase("app-descriptor_dv.json")]
    public void SetVersion_WhenSetAppFolderPathForOneApplication(string descriptorPath)
    {
        _fileSystem = CreateFs(descriptorPath, MockPackageFolderPath);
        string expectedVersion = "8.1.1";
        ComposableApplicationManager composableApplicationManager = new(_fileSystem, null, null, null);
        SetApplicationVersionCommand command = new(composableApplicationManager);
        command.Execute(new SetApplicationVersionOption
        {
            Version = expectedVersion, PackageFolderPath = MockPackageFolderPath
        });
        JsonValue? objectJson = JsonObject.Parse(_fileSystem.File.ReadAllText(MockPackageAppDescriptorPath));
        string actualVersion = objectJson["Version"];
        _fileSystem.FileExists(MockPackageAppDescriptorPath).Should().BeTrue();
        expectedVersion.Should().Be(actualVersion);
        _fileSystem.File.ReadAllLines(MockPackageAppDescriptorPath).Length.Should().BeGreaterThan(20);
    }
}

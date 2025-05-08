using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Autofac;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command;

public class SchemaBuilderTestFixture : BaseClioModuleTests
{
    private const string PackagePath = "T:\\TestPackage";
    private const string SchemaName = "MyService";
    private const string SchemaType = "source-code";

    private void AssertOnDescriptor(string descriptorPath)
    {
        string dContent = fileSystem.File.ReadAllText(descriptorPath);
        JObject obj = JObject.Parse(dContent);
        string schemaUid = (string)obj.SelectToken("$.Descriptor.UId");
        Guid.TryParse(schemaUid, out _).Should().BeTrue();

        string name = (string)obj.SelectToken("$.Descriptor.Name");
        name.Should().Be(SchemaName);

        DateTime modifiedOnUtc = (DateTime)obj.SelectToken("$.Descriptor.ModifiedOnUtc");
        modifiedOnUtc.Should().BeAfter(DateTime.MinValue);
    }

    private void AssertOnMetaData(string metadataPath)
    {
        string dContent = fileSystem.File.ReadAllText(metadataPath);
        JObject obj = JObject.Parse(dContent);

        Guid uid = (Guid)obj.SelectToken("$.MetaData.Schema.UId");
        uid.Should().NotBe(Guid.Empty);

        string sCHEMA_NAME = (string)obj.SelectToken("$.MetaData.Schema.A2");
        sCHEMA_NAME.Should().Be(SchemaName);

        IPackageInfoProvider pif = container.Resolve<IPackageInfoProvider>();
        PackageInfo packageInfo = pif.GetPackageInfo(PackagePath);
        Guid pACKAGE_UID = (Guid)obj.SelectToken("$.MetaData.Schema.A5");
        pACKAGE_UID.Should().Be(packageInfo.Descriptor.UId);

        Guid pACKAGE_UID_2 = (Guid)obj.SelectToken("$.MetaData.Schema.B6");
        pACKAGE_UID_2.Should().Be(packageInfo.Descriptor.UId);

        Guid hD1 = (Guid)obj.SelectToken("$.MetaData.Schema.HD1");
        hD1.Should().Be(Guid.Parse("50E3ACC0-26FC-4237-A095-849A1D534BD3"));
    }

    private void AssertOnSchema(string schemaPath)
    {
        IPackageInfoProvider pif = container.Resolve<IPackageInfoProvider>();
        PackageInfo packageInfo = pif.GetPackageInfo(PackagePath);
        string content = fileSystem.File.ReadAllText(schemaPath);
        string expectedNameSpace = $"namespace {packageInfo.Descriptor.Maintainer}.{packageInfo.Descriptor.Name}";
        content.Should().Contain(expectedNameSpace);
        string expectedClassName = $"public class {SchemaName}"; // public class [SCHEMA_NAME]
        content.Should().Contain(expectedClassName);
    }

    public override void Setup()
    {
        base.Setup();
        string tplFileContent = File.ReadAllText("tpl/schemas-template/source-code/Resources/resource.en-US.xml.tpl");
        fileSystem.AddFile(
            "E:\\Clio\\tpl\\schemas-template\\source-code\\Resources\\resource.en-US.xml.tpl",
            new MockFileData(tplFileContent));

        string csFileContent = File.ReadAllText("tpl/schemas-template/source-code/Schema/[SCHEMA_NAME].cs.tpl");
        fileSystem.AddFile(
            "E:\\Clio\\tpl\\schemas-template\\source-code\\Schema\\[SCHEMA_NAME].cs.tpl",
            new MockFileData(csFileContent));

        string descriptorFileContent = File.ReadAllText("tpl/schemas-template/source-code/Schema/descriptor.json.tpl");
        fileSystem.AddFile(
            "E:\\Clio\\tpl\\schemas-template\\source-code\\Schema\\descriptor.json.tpl",
            new MockFileData(descriptorFileContent));

        string metadataFileContent = File.ReadAllText("tpl/schemas-template/source-code/Schema/metadata.json.tpl");
        fileSystem.AddFile(
            "E:\\Clio\\tpl\\schemas-template\\source-code\\Schema\\metadata.json.tpl",
            new MockFileData(metadataFileContent));

        string propertiesFileContent = File.ReadAllText("tpl/schemas-template/source-code/Schema/properties.json.tpl");
        fileSystem.AddFile(
            "E:\\Clio\\tpl\\schemas-template\\source-code\\Schema\\properties.json.tpl",
            new MockFileData(propertiesFileContent));

        string pkgDescriptorFileContent = File.ReadAllText("Examples/package/descriptor.json");
        fileSystem.AddFile(
            "T:\\TestPackage\\descriptor.json",
            new MockFileData(pkgDescriptorFileContent));

        WorkingDirectoriesProvider._executingDirectory = "E:\\Clio";
    }

    [TearDown]
    public void TearDown() => WorkingDirectoriesProvider._executingDirectory = null;

    [Test]
    public void AddSchema_CopiesMetadataFiles_AndAdjustsContent()
    {
        // Arrange
        fileSystem.AddDirectory(PackagePath);
        ISchemaBuilder sut = container.Resolve<ISchemaBuilder>();

        // Act
        sut.AddSchema(SchemaType, SchemaName, PackagePath);

        // Assert
        string metadataFolderPath = Path.Combine(PackagePath, "Schemas", SchemaName);
        fileSystem.Directory.Exists(metadataFolderPath).Should().BeTrue();
        string[] files = fileSystem.Directory.GetFiles(metadataFolderPath);

        files.Should().HaveCount(4);
        files.Should().Contain(f => f.EndsWith("descriptor.json"));
        files.Should().Contain(f => f.EndsWith("metadata.json"));
        files.Should().Contain(f => f.EndsWith("properties.json"));
        files.Should().Contain(f => f.EndsWith($"{SchemaName}.cs"));

        foreach (string filePath in files)
        {
            string fileContent = fileSystem.File.ReadAllText(filePath);
            sut.SupportedMacroKeys.ForEach(macro => fileContent.Should().NotContain(macro));

            if (filePath.EndsWith("descriptor.json"))
            {
                AssertOnDescriptor(filePath);
            }

            if (filePath.EndsWith("metadata.json"))
            {
                AssertOnMetaData(filePath);
            }

            if (filePath.EndsWith(".cs"))
            {
                AssertOnSchema(filePath);
            }
        }
    }

    [Test]
    public void AddSchema_CopiesResourceFiles_AndAdjustsContent()
    {
        // Arrange
        fileSystem.AddDirectory(PackagePath);
        ISchemaBuilder sut = container.Resolve<ISchemaBuilder>();

        // Act
        sut.AddSchema(SchemaType, SchemaName, PackagePath);

        // Assert
        string enResourceFilePath
            = Path.Combine(PackagePath, "Resources", $"{SchemaName}.SourceCode", "resource.en-US.xml");
        fileSystem.FileExists(enResourceFilePath).Should().BeTrue();
        fileSystem.File.ReadAllText(enResourceFilePath).Should().NotContain("[SCHEMA_NAME]");
        fileSystem.File.ReadAllText(enResourceFilePath).Should()
            .Contain($"<Item Name=\"Caption\" Value=\"{SchemaName}\" />");
    }

    [Test]
    public void AddSchema_Creates_FileContent()
    {
        // Arrange
        fileSystem.AddDirectory(PackagePath);
        ISchemaBuilder sut = container.Resolve<ISchemaBuilder>();

        // Act
        sut.AddSchema(SchemaType, SchemaName, PackagePath);

        // Assert
        string resourcesSchemaFolderPath = Path.Combine(PackagePath, "Resources", $"{SchemaName}.SourceCode");
        fileSystem.Directory.Exists(resourcesSchemaFolderPath).Should().BeTrue();

        string enResourceFilePath = Path.Combine(resourcesSchemaFolderPath, "resource.en-US.xml");
        fileSystem.FileExists(enResourceFilePath).Should().BeTrue();

        string metadataSchemaFolderPath = Path.Combine(PackagePath, "Schemas", SchemaName);
        fileSystem.Directory.Exists(metadataSchemaFolderPath).Should().BeTrue();
    }

    [Test]
    public void AddSchema_Throws_When_DirAlreadyExists()
    {
        // Arrange
        fileSystem.AddDirectory(PackagePath);

        string resourcesSchemaFolderPath = Path.Combine(PackagePath, "Resources", $"{SchemaName}.SourceCode");
        fileSystem.AddDirectory(resourcesSchemaFolderPath);

        string metadataSchemaFolderPath = Path.Combine(PackagePath, "Schemas", SchemaName);
        fileSystem.AddDirectory(metadataSchemaFolderPath);
        ISchemaBuilder sut = container.Resolve<ISchemaBuilder>();

        // Act
        Action act = () => sut.AddSchema(SchemaType, SchemaName, PackagePath);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}

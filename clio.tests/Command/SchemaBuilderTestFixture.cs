using System;
using System.Collections.Generic;
using Autofac;
using Clio.Common;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Tests.Extensions;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;


public class SchemaBuilderTestFixture : BaseClioModuleTests
{

	private const string PackagePath = "T:\\TestPackage";
	private const string SchemaType = "WebService";
	private const string SchemaName = "MyService";

	public override void Setup(){
		base.Setup();
		string tplFileContent = File.ReadAllText("tpl/schemas-template/source-code/Resources/resource.en-US.xml.tpl");
		FileSystem.AddFile("E:\\Clio\\tpl\\schemas-template\\source-code\\Resources\\resource.en-US.xml.tpl", 
			new MockFileData(tplFileContent));
		
		string csFileContent = File.ReadAllText("tpl/schemas-template/source-code/Schema/[SCHEMA_NAME].cs.tpl");
		FileSystem.AddFile("E:\\Clio\\tpl\\schemas-template\\source-code\\Schema\\[SCHEMA_NAME].cs.tpl", 
			new MockFileData(csFileContent));
		
		string descriptorFileContent = File.ReadAllText("tpl/schemas-template/source-code/Schema/descriptor.json.tpl");
		FileSystem.AddFile("E:\\Clio\\tpl\\schemas-template\\source-code\\Schema\\descriptor.json.tpl", 
			new MockFileData(descriptorFileContent));
		
		string metadataFileContent = File.ReadAllText("tpl/schemas-template/source-code/Schema/metadata.json.tpl");
		FileSystem.AddFile("E:\\Clio\\tpl\\schemas-template\\source-code\\Schema\\metadata.json.tpl", 
			new MockFileData(metadataFileContent));
		
		string propertiesFileContent = File.ReadAllText("tpl/schemas-template/source-code/Schema/properties.json.tpl");
		FileSystem.AddFile("E:\\Clio\\tpl\\schemas-template\\source-code\\Schema\\properties.json.tpl", 
			new MockFileData(propertiesFileContent));
		
		WorkingDirectoriesProvider._executingDirectory= "E:\\Clio";

	}

	[Test]
	public void AddSchema_Creates_FileContent(){

		//Arrange
		FileSystem.AddDirectory(PackagePath);
		ISchemaBuilder sut = Container.Resolve<ISchemaBuilder>();

		//Act
		sut.AddSchema(SchemaType,SchemaName,PackagePath);

		//Assert
		string resourcesSchemaFolderPath =Path.Combine(PackagePath, "Resources", $"{SchemaName}.SourceCode");
		FileSystem.Directory.Exists(resourcesSchemaFolderPath).Should().BeTrue();
		
		var enResourceFilePath = Path.Combine(resourcesSchemaFolderPath, "resource.en-US.xml");
		FileSystem.FileExists(enResourceFilePath).Should().BeTrue();
		
		string metadataSchemaFolderPath =Path.Combine(PackagePath, "Schemas", SchemaName);
		FileSystem.Directory.Exists(metadataSchemaFolderPath).Should().BeTrue();
		
	}
	
	[Test]
	public void AddSchema_Throws_When_DirAlreadyExists(){

		//Arrange
		FileSystem.AddDirectory(PackagePath);
		
		string resourcesSchemaFolderPath =Path.Combine(PackagePath, "Resources", $"{SchemaName}.SourceCode");
		FileSystem.AddDirectory(resourcesSchemaFolderPath);
		
		string metadataSchemaFolderPath =Path.Combine(PackagePath, "Schemas", SchemaName);
		FileSystem.AddDirectory(metadataSchemaFolderPath);
		ISchemaBuilder sut = Container.Resolve<ISchemaBuilder>();
		
		//Act
		Action act = ()=> sut.AddSchema(SchemaType,SchemaName,PackagePath);

		//Assert
		act.Should().Throw<ArgumentException>();
	}

	
	[Test]
	public void AddSchema_CopiesResourceFiles_AndAdjustsContent(){

		//Arrange
		FileSystem.AddDirectory(PackagePath);
		ISchemaBuilder sut = Container.Resolve<ISchemaBuilder>();
		
		//Act
		sut.AddSchema(SchemaType,SchemaName,PackagePath);

		//Assert
		string enResourceFilePath =Path.Combine(PackagePath, "Resources", $"{SchemaName}.SourceCode", "resource.en-US.xml");
		FileSystem.FileExists(enResourceFilePath).Should().BeTrue();
		FileSystem.File.ReadAllText(enResourceFilePath).Should().NotContain("[SCHEMA_NAME]");
		FileSystem.File.ReadAllText(enResourceFilePath).Should().Contain($"<Item Name=\"Caption\" Value=\"{SchemaName}\" />");

	}
	
	[Test]
	public void AddSchema_CopiesMetadataFiles_AndAdjustsContent(){

		//Arrange
		FileSystem.AddDirectory(PackagePath);
		ISchemaBuilder sut = Container.Resolve<ISchemaBuilder>();
		
		//Act
		sut.AddSchema(SchemaType,SchemaName,PackagePath);

		//Assert
		string metadataFolderPath =Path.Combine(PackagePath, "Schemas", SchemaName);
		FileSystem.Directory.Exists(metadataFolderPath).Should().BeTrue();
		var files = FileSystem.Directory.GetFiles(metadataFolderPath);
		
		files.Should().HaveCount(4);
		files.Should().Contain(f=>f.EndsWith("descriptor.json"));
		files.Should().Contain(f=>f.EndsWith("metadata.json"));
		files.Should().Contain(f=>f.EndsWith("properties.json"));
		files.Should().Contain(f=>f.EndsWith($"{SchemaName}.cs"));

		foreach (string filePath in files) {
			var fileContent = FileSystem.File.ReadAllText(filePath);
			sut.SupportedMacroKeys.ForEach(macro => fileContent.Should().NotContain(macro));
		}

	}
}
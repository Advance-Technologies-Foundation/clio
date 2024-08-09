using System;
using System.Collections.Generic;
using Autofac;
using Clio.Common;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Tests.Extensions;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command;


public class SchemaBuilderTestFixture : BaseClioModuleTests
{

	private const string PackagePath = "T:\\TestPackage";
	private const string SchemaType = "source-code";
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
		
		string pkgDescriptorFileContent = File.ReadAllText("Examples/package/descriptor.json");
		FileSystem.AddFile("T:\\TestPackage\\descriptor.json", 
			new MockFileData(pkgDescriptorFileContent));
		
		
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
			
			
			if(filePath.EndsWith("descriptor.json")){
				AssertOnDescriptor(filePath);
			}
			if(filePath.EndsWith("metadata.json")){
				AssertOnMetaData(filePath);
			}
			if(filePath.EndsWith(".cs")){
				AssertOnSchema(filePath);
			}
			
		}

	}
	
	private void AssertOnDescriptor(string descriptorPath){
		var dContent = FileSystem.File.ReadAllText(descriptorPath);
		var obj = JObject.Parse(dContent);
		var schemaUid = (string)obj.SelectToken("$.Descriptor.UId");
		Guid.TryParse(schemaUid, out _).Should().BeTrue();
		
		var name = (string)obj.SelectToken("$.Descriptor.Name");
		name.Should().Be(SchemaName);
		
		var modifiedOnUtc = (DateTime)obj.SelectToken("$.Descriptor.ModifiedOnUtc");
		modifiedOnUtc.Should().BeAfter(DateTime.MinValue);
		
		
	}
	
	private void AssertOnMetaData(string metadataPath){
		var dContent = FileSystem.File.ReadAllText(metadataPath);
		var obj = JObject.Parse(dContent);
		
		var uid = (Guid)obj.SelectToken("$.MetaData.Schema.UId");
		uid.Should().NotBe(Guid.Empty);
		
		
		var SCHEMA_NAME = (string)obj.SelectToken("$.MetaData.Schema.A2");
		SCHEMA_NAME.Should().Be(SchemaName);
		
		var pif = Container.Resolve<IPackageInfoProvider>();
		var packageInfo  = pif.GetPackageInfo(PackagePath);
		var PACKAGE_UID = (Guid)obj.SelectToken("$.MetaData.Schema.A5");
		PACKAGE_UID.Should().Be(packageInfo.Descriptor.UId);
		
		
		var PACKAGE_UID_2 = (Guid)obj.SelectToken("$.MetaData.Schema.B6");
		PACKAGE_UID_2.Should().Be(packageInfo.Descriptor.UId);
		
		var HD1 = (Guid)obj.SelectToken("$.MetaData.Schema.HD1");
		HD1.Should().Be(Guid.Parse("50E3ACC0-26FC-4237-A095-849A1D534BD3"));
	}
	
	private void AssertOnSchema(string schemaPath){
		
		var pif = Container.Resolve<IPackageInfoProvider>();
		var packageInfo  = pif.GetPackageInfo(PackagePath);
		var content = FileSystem.File.ReadAllText(schemaPath);
		var expectedNameSpace = $"namespace {packageInfo.Descriptor.Maintainer}.{packageInfo.Descriptor.Name}";
		content.Should().Contain(expectedNameSpace);
		string expectedClassName = $"public class {SchemaName}"; //public class [SCHEMA_NAME]
		content.Should().Contain(expectedClassName);
	}
	
}
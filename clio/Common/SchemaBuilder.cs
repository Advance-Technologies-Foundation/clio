using System;
using System.Collections.Generic;
using System.IO;

namespace Clio.Common;

public interface ISchemaBuilder
{
	List<string> SupportedMacroKeys { get; }
	void AddSchema(string schemaType, string schemaName, string packagePath);

}

public class SchemaBuilder: ISchemaBuilder
{

	private readonly IFileSystem _fileSystem;
	private readonly ITemplateProvider _templateProvider;
	
	public List<string> SupportedMacroKeys { get; } = new List<string> {
		"[SCHEMA_NAME]",
		"[MAINTAINER]",
		"[PACKAGE_NAME]",
		"[SCHEMA_UID]",
		"[DATETIME_NOW_TICK]",
		"[PACKAGE_UID]",
	};
	

	public SchemaBuilder(IFileSystem fileSystem, ITemplateProvider templateProvider){
		_fileSystem = fileSystem;
		_templateProvider = templateProvider;
	}

	

	public void AddSchema(string schemaType, string schemaName, string packagePath){
		
		string resourcesDir = Path.Combine(packagePath, "Resources",  $"{schemaName}.SourceCode");
		_fileSystem.CreateDirectory(resourcesDir, true);
		
		string schemaDir = Path.Combine(packagePath, "Schemas", schemaName); 
		_fileSystem.CreateDirectory(schemaDir, true);
		
		const string templateFolderName = "source-code";
		
		string relativeTemplateResourceFolderPath = Path.Combine("schemas-template", templateFolderName,"Resources");
		Dictionary<string, string> macrosValues = new() {
			{"[SCHEMA_NAME]",schemaName},
			{"[MAINTAINER]",schemaName},
			{"[PACKAGE_NAME]",schemaName},
			{"[SCHEMA_UID]",schemaName},
			{"[DATETIME_NOW_TICK]",schemaName},
			{"[PACKAGE_UID]",schemaName},
		};
		_templateProvider.CopyTemplateFolder(relativeTemplateResourceFolderPath, resourcesDir, macrosValues);
		
		string relativeTemplateSchemaFolderPath = Path.Combine("schemas-template", templateFolderName,"Schema");
		_templateProvider.CopyTemplateFolder(relativeTemplateSchemaFolderPath, schemaDir, macrosValues);
		
		
	}

}
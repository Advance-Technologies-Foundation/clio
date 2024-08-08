using System;
using System.Collections.Generic;
using System.IO;

namespace Clio.Common;

public interface ISchemaBuilder
{

	void AddSchema(string schemaType, string schemaName, string packagePath);

}

public class SchemaBuilder: ISchemaBuilder
{

	private readonly IFileSystem _fileSystem;
	private readonly ITemplateProvider _templateProvider;

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
		string relativeTemplateFolderPath = Path.Combine("schemas-template", templateFolderName,"Resources");
		Dictionary<string, string> macrosValues = new Dictionary<string, string>() {
			{"[SCHEMA_NAME]",schemaName}
		};
		_templateProvider.CopyTemplateFolder(relativeTemplateFolderPath, resourcesDir, macrosValues);
		
		
	}

}
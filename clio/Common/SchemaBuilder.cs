using System;
using System.Collections.Generic;
using System.IO;
using Clio.Package;

namespace Clio.Common;

public interface ISchemaBuilder
{

	#region Properties: Public

	List<string> SupportedMacroKeys { get; }

	#endregion

	#region Methods: Public

	void AddSchema(string schemaType, string schemaName, string packagePath);

	#endregion

}

public class SchemaBuilder : ISchemaBuilder
{

	#region Fields: Private

	private readonly IFileSystem _fileSystem;
	private readonly ITemplateProvider _templateProvider;
	private readonly IPackageInfoProvider _packageInfoProvider;

	#endregion

	#region Constructors: Public

	public SchemaBuilder(IFileSystem fileSystem, ITemplateProvider templateProvider,
		IPackageInfoProvider packageInfoProvider){
		_fileSystem = fileSystem;
		_templateProvider = templateProvider;
		_packageInfoProvider = packageInfoProvider;
	}

	#endregion

	#region Properties: Public

	public List<string> SupportedMacroKeys { get; } = new() {
		"[SCHEMA_NAME]",
		"[MAINTAINER]",
		"[PACKAGE_NAME]",
		"[SCHEMA_UID]",
		"[DATETIME_NOW_TICK]",
		"[PACKAGE_UID]"
	};

	#endregion

	#region Methods: Public

	public void AddSchema(string schemaType, string schemaName, string packagePath){
		if (schemaType != "source-code") {
			throw new NotImplementedException(
				$"Schema type '{schemaType}' is not supported, only source-code is supported");
		}

		string resourcesDir = Path.Combine(packagePath, "Resources", $"{schemaName}.SourceCode");
		_fileSystem.CreateDirectory(resourcesDir, true);

		string schemaDir = Path.Combine(packagePath, "Schemas", schemaName);
		_fileSystem.CreateDirectory(schemaDir, true);

		string relativeTemplateResourceFolderPath = Path.Combine("schemas-template", schemaType, "Resources");

		PackageInfo pkgInfo = _packageInfoProvider.GetPackageInfo(packagePath);
		string maintainer = string.IsNullOrEmpty(pkgInfo.Descriptor.Maintainer)
			? "Customer"
			: pkgInfo.Descriptor.Maintainer;

		string modifiedOnUtc = PackageDescriptor.ConvertToModifiedOnUtc(DateTime.UtcNow);

		Dictionary<string, string> macrosValues = new() {
			{"[SCHEMA_NAME]", schemaName}, //User input
			{"[MAINTAINER]", maintainer}, //package maintainer otherwise Customer
			{"[PACKAGE_NAME]", pkgInfo.Descriptor.Name}, //package name or from path
			{"[SCHEMA_UID]", Guid.NewGuid().ToString()}, //Guid.NewGuid()
			{"[DATETIME_NOW_TICK]", modifiedOnUtc}, //DateTime.Now.Ticks
			{"[PACKAGE_UID]", pkgInfo.Descriptor.UId.ToString()} // UID from package descriptor
		};
		_templateProvider.CopyTemplateFolder(relativeTemplateResourceFolderPath, resourcesDir, macrosValues);

		string relativeTemplateSchemaFolderPath = Path.Combine("schemas-template", schemaType, "Schema");
		_templateProvider.CopyTemplateFolder(relativeTemplateSchemaFolderPath, schemaDir, macrosValues);
	}

	#endregion

}
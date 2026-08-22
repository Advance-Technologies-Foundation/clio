using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Package;

namespace Clio.Common;

public interface ISchemaBuilder
{

	#region Properties: Public

	List<string> SupportedMacroKeys { get; }

	#endregion

	#region Methods: Public

	/// <summary>Adds a schema to a package.</summary>
	/// <param name="schemaType">Schema type.</param>
	/// <param name="schemaName">Schema name.</param>
	/// <param name="packagePath">Package directory.</param>
	/// <param name="sourceCodeOptions">Optional source-code schema customization.</param>
	void AddSchema(string schemaType, string schemaName, string packagePath,
		SourceCodeSchemaOptions sourceCodeOptions = null);

	#endregion

}

/// <summary>
/// Data used to customize a generated source-code schema without introducing behavior or dependencies.
/// </summary>
/// <param name="Namespace">C# namespace for the generated class.</param>
/// <param name="ClassDocumentation">XML documentation placed above the generated class.</param>
/// <param name="LocalizableStrings">Initial resource item names and default-culture values.</param>
public sealed record SourceCodeSchemaOptions(string Namespace, string ClassDocumentation,
	IReadOnlyDictionary<string, string> LocalizableStrings);

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
		"[PACKAGE_UID]",
		"[NAMESPACE]",
		"[CLASS_DOCUMENTATION]",
		"[RESOURCE_ITEMS]"
	};

	#endregion

	#region Methods: Public

	public void AddSchema(string schemaType, string schemaName, string packagePath,
		SourceCodeSchemaOptions sourceCodeOptions = null){
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
		string schemaNamespace = $"{maintainer}.{pkgInfo.Descriptor.Name}";
		if (sourceCodeOptions != null) {
			schemaNamespace = sourceCodeOptions.Namespace;
		}
		string classDocumentation = BuildClassDocumentation(sourceCodeOptions?.ClassDocumentation);
		string resourceItems = BuildResourceItems(sourceCodeOptions?.LocalizableStrings);

		Dictionary<string, string> macrosValues = new() {
			{"[SCHEMA_NAME]", schemaName}, //User input
			{"[MAINTAINER]", maintainer}, //package maintainer otherwise Customer
			{"[PACKAGE_NAME]", pkgInfo.Descriptor.Name}, //package name or from path
			{"[SCHEMA_UID]", Guid.NewGuid().ToString()}, //Guid.NewGuid()
			{"[DATETIME_NOW_TICK]", modifiedOnUtc}, //DateTime.Now.Ticks
			{"[PACKAGE_UID]", pkgInfo.Descriptor.UId.ToString()}, // UID from package descriptor
			{"[NAMESPACE]", schemaNamespace},
			{"[CLASS_DOCUMENTATION]", classDocumentation},
			{"[RESOURCE_ITEMS]", resourceItems}
		};
		_templateProvider.CopyTemplateFolder(relativeTemplateResourceFolderPath, resourcesDir, macrosValues);

		string relativeTemplateSchemaFolderPath = Path.Combine("schemas-template", schemaType, "Schema");
		_templateProvider.CopyTemplateFolder(relativeTemplateSchemaFolderPath, schemaDir, macrosValues);
	}

	private static string BuildClassDocumentation(string documentation) {
		if (string.IsNullOrWhiteSpace(documentation)) {
			return string.Empty;
		}
		string normalizedDocumentation = documentation.Replace("\r\n", "\n").Replace('\r', '\n');
		string[] documentationLines = normalizedDocumentation.Split('\n');
		string documentationBody = string.Join(Environment.NewLine, documentationLines.Select(line =>
			$"\t/// {System.Security.SecurityElement.Escape(line)}"));
		string classDocumentation = $"\t/// <summary>{Environment.NewLine}{documentationBody}" +
			$"{Environment.NewLine}\t/// </summary>{Environment.NewLine}";
		return classDocumentation;
	}

	private static string BuildResourceItems(IReadOnlyDictionary<string, string> values) {
		if (values is null || values.Count == 0) {
			return string.Empty;
		}
		string resourceItems = string.Join(Environment.NewLine, values.Select(value =>
			$"\t\t\t<Item Name=\"{System.Security.SecurityElement.Escape(value.Key)}\" " +
			$"Value=\"{System.Security.SecurityElement.Escape(value.Value)}\" />"));
		return resourceItems;
	}

	#endregion

}

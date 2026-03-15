using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Options for the <c>create-data-binding</c> command.
/// </summary>
[Verb("create-data-binding", HelpText = "Create or regenerate a package data binding")]
public class CreateDataBindingOptions : EnvironmentOptions {
	[Option("environment", Required = false, HelpText = "Environment name")]
	public string? EnvironmentAlias {
		get => Environment;
		set => Environment = value;
	}

	[Option("package", Required = true, HelpText = "Target package name")]
	public string PackageName { get; set; } = string.Empty;

	[Option("schema", Required = true, HelpText = "Entity schema name")]
	public string SchemaName { get; set; } = string.Empty;

	[Option("binding-name", Required = false, HelpText = "Binding folder name")]
	public string? BindingName { get; set; }

	[Option("install-type", Required = false, Default = 0, HelpText = "Descriptor install type")]
	public int InstallType { get; set; }

	[Option("values", Required = false, HelpText = "Row values as JSON object keyed by column name")]
	public string? ValuesJson { get; set; }

	[Option("localizations", Required = false, HelpText = "Localized values as JSON object keyed by culture and column name")]
	public string? LocalizationsJson { get; set; }

	[Option("workspace-path", Required = false, HelpText = "Workspace root path. Defaults to the current workspace")]
	public string? WorkspacePath { get; set; }
}

/// <summary>
/// Options for the <c>add-data-binding-row</c> command.
/// </summary>
[Verb("add-data-binding-row", HelpText = "Add or replace a row in a package data binding")]
public class AddDataBindingRowOptions {
	[Option("package", Required = true, HelpText = "Target package name")]
	public string PackageName { get; set; } = string.Empty;

	[Option("binding-name", Required = true, HelpText = "Binding folder name")]
	public string BindingName { get; set; } = string.Empty;

	[Option("values", Required = true, HelpText = "Row values as JSON object keyed by column name")]
	public string ValuesJson { get; set; } = string.Empty;

	[Option("localizations", Required = false, HelpText = "Localized values as JSON object keyed by culture and column name")]
	public string? LocalizationsJson { get; set; }

	[Option("workspace-path", Required = false, HelpText = "Workspace root path. Defaults to the current workspace")]
	public string? WorkspacePath { get; set; }
}

/// <summary>
/// Options for the <c>remove-data-binding-row</c> command.
/// </summary>
[Verb("remove-data-binding-row", HelpText = "Remove a row from a package data binding")]
public class RemoveDataBindingRowOptions {
	[Option("package", Required = true, HelpText = "Target package name")]
	public string PackageName { get; set; } = string.Empty;

	[Option("binding-name", Required = true, HelpText = "Binding folder name")]
	public string BindingName { get; set; } = string.Empty;

	[Option("key-value", Required = true, HelpText = "Primary-key value of the row to delete")]
	public string KeyValue { get; set; } = string.Empty;

	[Option("workspace-path", Required = false, HelpText = "Workspace root path. Defaults to the current workspace")]
	public string? WorkspacePath { get; set; }
}

/// <summary>
/// Creates or regenerates package data-binding files from a resolved schema.
/// </summary>
public class CreateDataBindingCommand(IDataBindingService dataBindingService, ILogger logger)
	: Command<CreateDataBindingOptions> {
	public override int Execute(CreateDataBindingOptions options) {
		try {
			dataBindingService.CreateBinding(options);
			logger.WriteInfo("Done");
			return 0;
		}
		catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>
/// Adds or replaces a row inside an existing package data binding.
/// </summary>
public class AddDataBindingRowCommand(IDataBindingService dataBindingService, ILogger logger)
	: Command<AddDataBindingRowOptions> {
	public override int Execute(AddDataBindingRowOptions options) {
		try {
			dataBindingService.AddOrUpdateRow(options);
			logger.WriteInfo("Done");
			return 0;
		}
		catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>
/// Removes a row from an existing package data binding.
/// </summary>
public class RemoveDataBindingRowCommand(IDataBindingService dataBindingService, ILogger logger)
	: Command<RemoveDataBindingRowOptions> {
	public override int Execute(RemoveDataBindingRowOptions options) {
		try {
			dataBindingService.RemoveRow(options);
			logger.WriteInfo("Done");
			return 0;
		}
		catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>
/// Shared data-binding service used by the CLI commands and MCP tools.
/// </summary>
public interface IDataBindingService {
	/// <summary>
	/// Creates or regenerates a binding folder from the requested schema.
	/// </summary>
	void CreateBinding(CreateDataBindingOptions options);

	/// <summary>
	/// Adds or replaces a row inside an existing binding.
	/// </summary>
	void AddOrUpdateRow(AddDataBindingRowOptions options);

	/// <summary>
	/// Removes a row from an existing binding.
	/// </summary>
	void RemoveRow(RemoveDataBindingRowOptions options);
}

internal interface IDataBindingSchemaClient {
	DataBindingSchema Fetch(string schemaName);
}

internal interface IDataBindingSchemaResolver {
	DataBindingSchema Resolve(string schemaName);
}

/// <summary>
/// Provides built-in offline data-binding schema templates for stable Creatio entities.
/// </summary>
public interface IDataBindingTemplateCatalog {
	/// <summary>
	/// Determines whether a built-in template exists for the requested schema.
	/// </summary>
	/// <param name="schemaName">The entity schema name.</param>
	/// <returns><c>true</c> when a built-in template exists; otherwise, <c>false</c>.</returns>
	bool HasTemplate(string schemaName);
}

internal interface IDataBindingTemplateSchemaCatalog : IDataBindingTemplateCatalog {
	bool TryGetTemplate(string schemaName, out DataBindingSchema schema);
}

internal interface IDataBindingSerializer {
	string SerializeDescriptor(DataBindingDescriptorFile descriptor);

	string SerializePackageData(DataBindingPackageDataFile dataFile);
}

internal interface IDataBindingValueConverter {
	object? ConvertValue(JsonNode? valueNode, Guid dataTypeUId, string columnName, bool allowEmptyString);

	string NormalizeKeyValue(object? value, Guid dataTypeUId);

	bool IsStringLike(Guid dataTypeUId);
}

internal sealed class DataBindingService(
	IDataBindingSchemaResolver schemaResolver,
	IDataBindingTemplateCatalog templateCatalog,
	IDataBindingSerializer serializer,
	IDataBindingValueConverter valueConverter,
	IWorkspacePathBuilder workspacePathBuilder,
	IFileSystem fileSystem) : IDataBindingService {
	private const string DataFolderName = "Data";
	private const string LocalizationFolderName = "Localization";
	private const string DescriptorFileName = "descriptor.json";
	private const string DataFileName = "data.json";
	private const string FilterFileName = "filter.json";

	public void CreateBinding(CreateDataBindingOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ValidateCreateOptions(options);

		string workspaceRoot = ResolveWorkspaceRoot(options.WorkspacePath);
		string packagePath = ResolvePackagePath(workspaceRoot, options.PackageName);
		DataBindingSchema schema = schemaResolver.Resolve(options.SchemaName);

		string bindingName = string.IsNullOrWhiteSpace(options.BindingName)
			? options.SchemaName
			: options.BindingName.Trim();
		string bindingDirectoryPath = fileSystem.CombinePaths(packagePath, DataFolderName, bindingName);

		DataBindingDescriptorFile? existingDescriptor = TryReadDescriptor(bindingDirectoryPath);
		if (existingDescriptor is not null &&
			!string.Equals(existingDescriptor.Descriptor.Schema.Name, schema.Name, StringComparison.Ordinal)) {
			throw new InvalidOperationException(
				$"Binding '{bindingName}' already exists for schema '{existingDescriptor.Descriptor.Schema.Name}'.");
		}

		fileSystem.CreateDirectoryIfNotExists(fileSystem.CombinePaths(packagePath, DataFolderName));
		fileSystem.CreateDirectoryIfNotExists(bindingDirectoryPath);

		Dictionary<string, JsonNode?>? values = ParseColumnObject(options.ValuesJson, "values", required: false);
		Dictionary<string, Dictionary<string, JsonNode?>>? localizations =
			ParseLocalizations(options.LocalizationsJson);

		bool isTemplate = values is null;
		List<DataBindingColumnDefinition> descriptorColumns = BuildDescriptorColumns(
			schema,
			values?.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
			includeAllColumns: isTemplate);
		DataBindingDescriptorFile descriptor = BuildDescriptor(
			existingDescriptor?.Descriptor.UId,
			bindingName,
			options.InstallType,
			schema,
			descriptorColumns);

		DataBindingPackageDataFile dataFile = isTemplate
			? BuildTemplateData(descriptorColumns)
			: BuildDataFile(schema, descriptorColumns, values!, allowEmptyPrimaryKey: false, autoGeneratePrimaryKey: true);

		fileSystem.WriteAllTextToFile(fileSystem.CombinePaths(bindingDirectoryPath, DescriptorFileName),
			serializer.SerializeDescriptor(descriptor));
		fileSystem.WriteAllTextToFile(fileSystem.CombinePaths(bindingDirectoryPath, DataFileName),
			serializer.SerializePackageData(dataFile));
		fileSystem.WriteAllTextToFile(fileSystem.CombinePaths(bindingDirectoryPath, FilterFileName), string.Empty);

		string localizationDirectoryPath = fileSystem.CombinePaths(bindingDirectoryPath, LocalizationFolderName);
		fileSystem.DeleteDirectoryIfExists(localizationDirectoryPath);
		fileSystem.CreateDirectoryIfNotExists(localizationDirectoryPath);
		WriteLocalizationFiles(
			localizationDirectoryPath,
			schema,
			dataFile,
			localizations,
			createTemplateDefault: isTemplate,
			descriptorColumns);
	}

	public void AddOrUpdateRow(AddDataBindingRowOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ValidateAddOptions(options);

		string workspaceRoot = ResolveWorkspaceRoot(options.WorkspacePath);
		string bindingDirectoryPath = ResolveBindingDirectory(workspaceRoot, options.PackageName, options.BindingName);
		DataBindingDescriptorFile descriptor = ReadDescriptor(bindingDirectoryPath);
		DataBindingPackageDataFile dataFile = ReadDataFile(bindingDirectoryPath);

		Dictionary<string, JsonNode?> values = ParseColumnObject(options.ValuesJson, "values", required: true)!;
		Dictionary<string, Dictionary<string, JsonNode?>>? localizations =
			ParseLocalizations(options.LocalizationsJson);

		DataBindingRuntimeDescriptor runtimeDescriptor = DataBindingRuntimeDescriptor.FromDescriptor(descriptor, valueConverter);
		DataBindingRow newRow = BuildRow(runtimeDescriptor, values, allowEmptyPrimaryKey: false, autoGeneratePrimaryKey: true);
		string key = GetPrimaryKey(newRow, runtimeDescriptor);
		int existingIndex = FindRowIndex(dataFile.PackageData, runtimeDescriptor, key);
		if (existingIndex >= 0) {
			dataFile.PackageData[existingIndex] = newRow;
		}
		else {
			dataFile.PackageData.Add(newRow);
		}

		fileSystem.WriteAllTextToFile(fileSystem.CombinePaths(bindingDirectoryPath, DataFileName),
			serializer.SerializePackageData(dataFile));
		UpdateLocalizationFilesForRow(bindingDirectoryPath, runtimeDescriptor, key, localizations);
	}

	public void RemoveRow(RemoveDataBindingRowOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ValidateRemoveOptions(options);

		string workspaceRoot = ResolveWorkspaceRoot(options.WorkspacePath);
		string bindingDirectoryPath = ResolveBindingDirectory(workspaceRoot, options.PackageName, options.BindingName);
		DataBindingDescriptorFile descriptor = ReadDescriptor(bindingDirectoryPath);
		DataBindingPackageDataFile dataFile = ReadDataFile(bindingDirectoryPath);
		DataBindingRuntimeDescriptor runtimeDescriptor = DataBindingRuntimeDescriptor.FromDescriptor(descriptor, valueConverter);
		string normalizedKeyValue = valueConverter.NormalizeKeyValue(
			valueConverter.ConvertValue(JsonValue.Create(options.KeyValue), runtimeDescriptor.PrimaryColumn.DataTypeValueUId,
				runtimeDescriptor.PrimaryColumn.ColumnName, allowEmptyString: false),
			runtimeDescriptor.PrimaryColumn.DataTypeValueUId);
		int rowIndex = FindRowIndex(dataFile.PackageData, runtimeDescriptor, normalizedKeyValue);
		if (rowIndex < 0) {
			throw new InvalidOperationException(
				$"Row with key '{options.KeyValue}' was not found in binding '{options.BindingName}'.");
		}

		dataFile.PackageData.RemoveAt(rowIndex);
		fileSystem.WriteAllTextToFile(fileSystem.CombinePaths(bindingDirectoryPath, DataFileName),
			serializer.SerializePackageData(dataFile));
		RemoveLocalizationRows(bindingDirectoryPath, runtimeDescriptor, normalizedKeyValue);
	}

	private void ValidateCreateOptions(CreateDataBindingOptions options) {
		bool requiresEnvironment = !templateCatalog.HasTemplate(options.SchemaName);
		if (requiresEnvironment &&
			string.IsNullOrWhiteSpace(options.Environment) &&
			string.IsNullOrWhiteSpace(options.Uri)) {
			throw new InvalidOperationException("create-data-binding requires --environment or --uri.");
		}
		if (string.IsNullOrWhiteSpace(options.PackageName)) {
			throw new InvalidOperationException("--package is required.");
		}
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			throw new InvalidOperationException("--schema is required.");
		}
		if (options.InstallType is < 0 or > 3) {
			throw new InvalidOperationException("--install-type must be between 0 and 3.");
		}
	}

	private static void ValidateAddOptions(AddDataBindingRowOptions options) {
		if (string.IsNullOrWhiteSpace(options.PackageName)) {
			throw new InvalidOperationException("--package is required.");
		}
		if (string.IsNullOrWhiteSpace(options.BindingName)) {
			throw new InvalidOperationException("--binding-name is required.");
		}
		if (string.IsNullOrWhiteSpace(options.ValuesJson)) {
			throw new InvalidOperationException("--values is required.");
		}
	}

	private static void ValidateRemoveOptions(RemoveDataBindingRowOptions options) {
		if (string.IsNullOrWhiteSpace(options.PackageName)) {
			throw new InvalidOperationException("--package is required.");
		}
		if (string.IsNullOrWhiteSpace(options.BindingName)) {
			throw new InvalidOperationException("--binding-name is required.");
		}
		if (string.IsNullOrWhiteSpace(options.KeyValue)) {
			throw new InvalidOperationException("--key-value is required.");
		}
	}

	private string ResolveWorkspaceRoot(string? explicitWorkspacePath) {
		string rootPath = string.IsNullOrWhiteSpace(explicitWorkspacePath)
			? workspacePathBuilder.RootPath
			: fileSystem.GetFullPath(explicitWorkspacePath);
		if (!fileSystem.ExistsDirectory(rootPath)) {
			throw new InvalidOperationException($"Workspace path not found: {rootPath}");
		}

		string workspaceSettingsPath = fileSystem.CombinePaths(rootPath, ".clio", "workspaceSettings.json");
		if (!fileSystem.ExistsFile(workspaceSettingsPath)) {
			throw new InvalidOperationException(
				$"Workspace root was not detected at '{rootPath}'. Run the command from a workspace or supply --workspace-path.");
		}

		return rootPath;
	}

	private string ResolvePackagePath(string workspaceRoot, string packageName) {
		string packagePath = fileSystem.CombinePaths(workspaceRoot, "packages", packageName);
		if (!fileSystem.ExistsDirectory(packagePath)) {
			throw new InvalidOperationException($"Package directory not found: {packagePath}");
		}

		return packagePath;
	}

	private string ResolveBindingDirectory(string workspaceRoot, string packageName, string bindingName) {
		string packagePath = ResolvePackagePath(workspaceRoot, packageName);
		string bindingDirectoryPath = fileSystem.CombinePaths(packagePath, DataFolderName, bindingName);
		if (!fileSystem.ExistsDirectory(bindingDirectoryPath)) {
			throw new InvalidOperationException($"Binding directory not found: {bindingDirectoryPath}");
		}

		return bindingDirectoryPath;
	}

	private DataBindingDescriptorFile? TryReadDescriptor(string bindingDirectoryPath) {
		string descriptorPath = fileSystem.CombinePaths(bindingDirectoryPath, DescriptorFileName);
		if (!fileSystem.ExistsFile(descriptorPath)) {
			return null;
		}

		return JsonSerializer.Deserialize<DataBindingDescriptorFile>(fileSystem.ReadAllText(descriptorPath),
			DataBindingJson.Options);
	}

	private DataBindingDescriptorFile ReadDescriptor(string bindingDirectoryPath) {
		DataBindingDescriptorFile? descriptor = TryReadDescriptor(bindingDirectoryPath);
		if (descriptor is null) {
			throw new InvalidOperationException($"Binding descriptor not found in '{bindingDirectoryPath}'.");
		}

		return descriptor;
	}

	private DataBindingPackageDataFile ReadDataFile(string bindingDirectoryPath) {
		string dataPath = fileSystem.CombinePaths(bindingDirectoryPath, DataFileName);
		if (!fileSystem.ExistsFile(dataPath)) {
			throw new InvalidOperationException($"Binding data file not found in '{bindingDirectoryPath}'.");
		}

		DataBindingPackageDataFile? dataFile = JsonSerializer.Deserialize<DataBindingPackageDataFile>(
			fileSystem.ReadAllText(dataPath),
			DataBindingJson.Options);
		return dataFile ?? new DataBindingPackageDataFile();
	}

	private static Dictionary<string, JsonNode?>? ParseColumnObject(string? json, string argumentName, bool required) {
		if (string.IsNullOrWhiteSpace(json)) {
			if (required) {
				throw new InvalidOperationException($"--{argumentName} must contain a JSON object.");
			}

			return null;
		}

		JsonNode? node;
		try {
			node = JsonNode.Parse(json);
		}
		catch (JsonException exception) {
			throw new InvalidOperationException($"--{argumentName} must contain valid JSON. {exception.Message}");
		}

		if (node is not JsonObject jsonObject) {
			throw new InvalidOperationException($"--{argumentName} must be a JSON object keyed by column name.");
		}

		return jsonObject.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
	}

	private static Dictionary<string, Dictionary<string, JsonNode?>>? ParseLocalizations(string? json) {
		if (string.IsNullOrWhiteSpace(json)) {
			return null;
		}

		JsonNode? node;
		try {
			node = JsonNode.Parse(json);
		}
		catch (JsonException exception) {
			throw new InvalidOperationException($"--localizations must contain valid JSON. {exception.Message}");
		}

		if (node is not JsonObject culturesObject) {
			throw new InvalidOperationException("--localizations must be a JSON object keyed by culture.");
		}

		Dictionary<string, Dictionary<string, JsonNode?>> result = new(StringComparer.OrdinalIgnoreCase);
		foreach ((string culture, JsonNode? cultureNode) in culturesObject) {
			if (cultureNode is not JsonObject valuesObject) {
				throw new InvalidOperationException(
					$"--localizations entry '{culture}' must be a JSON object keyed by column name.");
			}

			result[culture] = valuesObject.ToDictionary(item => item.Key, item => item.Value,
				StringComparer.OrdinalIgnoreCase);
		}

		return result;
	}

	private static List<DataBindingColumnDefinition> BuildDescriptorColumns(
		DataBindingSchema schema,
		HashSet<string>? selectedColumns,
		bool includeAllColumns) {
		IEnumerable<DataBindingSchemaColumn> columns = includeAllColumns
			? schema.Columns
			: schema.Columns.Where(column =>
				selectedColumns!.Contains(column.Name) ||
				string.Equals(column.UId.ToString(), schema.PrimaryColumnUId.ToString(), StringComparison.OrdinalIgnoreCase));

		List<DataBindingColumnDefinition> descriptorColumns = columns
			.Select(column => new DataBindingColumnDefinition {
				ColumnUId = column.UId,
				IsForceUpdate = false,
				IsKey = column.UId == schema.PrimaryColumnUId,
				ColumnName = column.Name,
				DataTypeValueUId = DataValueTypeMap.FromRuntimeValueType(column.DataValueType)
			})
			.OrderBy(column => column.ColumnName, StringComparer.Ordinal)
			.ToList();

		if (!descriptorColumns.Any(column => column.IsKey)) {
			throw new InvalidOperationException($"Schema '{schema.Name}' does not expose a primary column.");
		}

		return descriptorColumns;
	}

	private static DataBindingDescriptorFile BuildDescriptor(
		Guid? existingUid,
		string bindingName,
		int installType,
		DataBindingSchema schema,
		List<DataBindingColumnDefinition> descriptorColumns) {
		return new DataBindingDescriptorFile {
			Descriptor = new DataBindingDescriptor {
				UId = existingUid ?? Guid.NewGuid(),
				Name = bindingName,
				ModifiedOnUtc = $"/Date({DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()})/",
				InstallType = installType,
				Schema = new DataBindingSchemaRef {
					UId = schema.UId,
					Name = schema.Name
				},
				Columns = descriptorColumns
			}
		};
	}

	private static DataBindingPackageDataFile BuildTemplateData(
		IReadOnlyCollection<DataBindingColumnDefinition> descriptorColumns) {
		List<DataBindingRowValue> row = descriptorColumns.Select(column => new DataBindingRowValue {
			SchemaColumnUId = column.ColumnUId,
			Value = string.Empty
		}).ToList();
		return new DataBindingPackageDataFile {
			PackageData = [new DataBindingRow { Row = row }]
		};
	}

	private DataBindingPackageDataFile BuildDataFile(
		DataBindingSchema schema,
		IReadOnlyCollection<DataBindingColumnDefinition> descriptorColumns,
		Dictionary<string, JsonNode?> values,
		bool allowEmptyPrimaryKey,
		bool autoGeneratePrimaryKey) {
		DataBindingRuntimeDescriptor descriptor =
			DataBindingRuntimeDescriptor.FromSchema(schema, descriptorColumns, valueConverter);
		return new DataBindingPackageDataFile {
			PackageData = [BuildRow(descriptor, values, allowEmptyPrimaryKey, autoGeneratePrimaryKey)]
		};
	}

	private DataBindingRow BuildRow(
		DataBindingRuntimeDescriptor descriptor,
		Dictionary<string, JsonNode?> values,
		bool allowEmptyPrimaryKey,
		bool autoGeneratePrimaryKey = false) {
		Dictionary<string, JsonNode?> resolvedValues = EnsurePrimaryKeyValue(descriptor, values, autoGeneratePrimaryKey);
		List<DataBindingRowValue> row = [];
		foreach ((string columnName, JsonNode? node) in resolvedValues.OrderBy(item => item.Key, StringComparer.Ordinal)) {
			if (!descriptor.ColumnsByName.TryGetValue(columnName, out DataBindingColumnDefinition? column)) {
				throw new InvalidOperationException($"Column '{columnName}' is not part of binding '{descriptor.Name}'.");
			}

			object? converted = valueConverter.ConvertValue(
				node,
				column.DataTypeValueUId,
				column.ColumnName,
				allowEmptyString: allowEmptyPrimaryKey || !column.IsKey);
			row.Add(new DataBindingRowValue {
				SchemaColumnUId = column.ColumnUId,
				Value = converted
			});
		}

		DataBindingColumnDefinition primaryColumn = descriptor.PrimaryColumn;
		if (row.All(value => value.SchemaColumnUId != primaryColumn.ColumnUId)) {
			throw new InvalidOperationException(
				$"Column '{primaryColumn.ColumnName}' is required because it is the binding primary key.");
		}

		return new DataBindingRow { Row = row };
	}

	private static Dictionary<string, JsonNode?> EnsurePrimaryKeyValue(
		DataBindingRuntimeDescriptor descriptor,
		Dictionary<string, JsonNode?> values,
		bool autoGeneratePrimaryKey) {
		if (TryGetExplicitPrimaryKeyValue(values, descriptor.PrimaryColumn.ColumnName, out JsonNode? primaryKeyValue) &&
			!IsMissingPrimaryKeyValue(primaryKeyValue)) {
			return values;
		}

		if (!autoGeneratePrimaryKey) {
			return values;
		}

		if (DataValueTypeMap.Resolve(descriptor.PrimaryColumn.DataTypeValueUId) != typeof(Guid)) {
			throw new InvalidOperationException(
				$"Column '{descriptor.PrimaryColumn.ColumnName}' is required because it is the binding primary key.");
		}

		Dictionary<string, JsonNode?> valuesWithPrimaryKey = new(values, StringComparer.OrdinalIgnoreCase) {
			[descriptor.PrimaryColumn.ColumnName] = JsonValue.Create(Guid.NewGuid().ToString())
		};
		return valuesWithPrimaryKey;
	}

	private static bool TryGetExplicitPrimaryKeyValue(
		Dictionary<string, JsonNode?> values,
		string primaryColumnName,
		out JsonNode? primaryKeyValue) {
		foreach ((string columnName, JsonNode? node) in values) {
			if (string.Equals(columnName, primaryColumnName, StringComparison.OrdinalIgnoreCase)) {
				primaryKeyValue = node;
				return true;
			}
		}

		primaryKeyValue = null;
		return false;
	}

	private static bool IsMissingPrimaryKeyValue(JsonNode? primaryKeyValue) {
		if (primaryKeyValue is null) {
			return true;
		}

		if (primaryKeyValue is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string? stringValue)) {
			return string.IsNullOrWhiteSpace(stringValue);
		}

		return false;
	}

	private string GetPrimaryKey(DataBindingRow row, DataBindingRuntimeDescriptor descriptor) {
		DataBindingRowValue? keyValue =
			row.Row.SingleOrDefault(value => value.SchemaColumnUId == descriptor.PrimaryColumn.ColumnUId);
		if (keyValue is null) {
			throw new InvalidOperationException(
				$"Row does not contain the primary key column '{descriptor.PrimaryColumn.ColumnName}'.");
		}

		return valueConverter.NormalizeKeyValue(keyValue.Value, descriptor.PrimaryColumn.DataTypeValueUId);
	}

	private int FindRowIndex(IList<DataBindingRow> rows, DataBindingRuntimeDescriptor descriptor, string normalizedKey) {
		for (int index = 0; index < rows.Count; index++) {
			string rowKey = GetPrimaryKey(rows[index], descriptor);
			if (string.Equals(rowKey, normalizedKey, StringComparison.Ordinal)) {
				return index;
			}
		}

		return -1;
	}

	private void WriteLocalizationFiles(
		string localizationDirectoryPath,
		DataBindingSchema schema,
		DataBindingPackageDataFile dataFile,
		Dictionary<string, Dictionary<string, JsonNode?>>? localizations,
		bool createTemplateDefault,
		IReadOnlyCollection<DataBindingColumnDefinition> descriptorColumns) {
		DataBindingRuntimeDescriptor descriptor =
			DataBindingRuntimeDescriptor.FromSchema(schema, descriptorColumns, valueConverter);
		DataBindingRow sourceRow = dataFile.PackageData.Single();
		DataBindingRow? defaultTemplateRow = createTemplateDefault
			? BuildLocalizationRowTemplate(descriptor, sourceRow)
			: null;

		if (defaultTemplateRow is not null) {
			DataBindingPackageDataFile defaultFile = new() { PackageData = [defaultTemplateRow] };
			fileSystem.WriteAllTextToFile(
				fileSystem.CombinePaths(localizationDirectoryPath, "data.en-US.json"),
				serializer.SerializePackageData(defaultFile));
		}

		if (localizations is null) {
			return;
		}

		string primaryKey = GetPrimaryKey(sourceRow, descriptor);
		foreach ((string culture, Dictionary<string, JsonNode?> cultureValues) in localizations) {
			DataBindingRow localizedRow = BuildLocalizationRow(descriptor, primaryKey, cultureValues);
			DataBindingPackageDataFile localizedFile = new() { PackageData = [localizedRow] };
			fileSystem.WriteAllTextToFile(
				fileSystem.CombinePaths(localizationDirectoryPath, $"data.{culture}.json"),
				serializer.SerializePackageData(localizedFile));
		}
	}

	private void UpdateLocalizationFilesForRow(
		string bindingDirectoryPath,
		DataBindingRuntimeDescriptor descriptor,
		string normalizedKey,
		Dictionary<string, Dictionary<string, JsonNode?>>? localizations) {
		string localizationDirectoryPath = fileSystem.CombinePaths(bindingDirectoryPath, LocalizationFolderName);
		if (!fileSystem.ExistsDirectory(localizationDirectoryPath)) {
			if (localizations is null) {
				return;
			}

			fileSystem.CreateDirectoryIfNotExists(localizationDirectoryPath);
		}

		if (localizations is null) {
			return;
		}

		foreach ((string culture, Dictionary<string, JsonNode?> cultureValues) in localizations) {
			string culturePath = fileSystem.CombinePaths(localizationDirectoryPath, $"data.{culture}.json");
			DataBindingPackageDataFile localizationFile = fileSystem.ExistsFile(culturePath)
				? JsonSerializer.Deserialize<DataBindingPackageDataFile>(fileSystem.ReadAllText(culturePath), DataBindingJson.Options) ?? new DataBindingPackageDataFile()
				: new DataBindingPackageDataFile();
			DataBindingRow localizedRow = BuildLocalizationRow(descriptor, normalizedKey, cultureValues);
			int existingIndex = FindRowIndex(localizationFile.PackageData, descriptor, normalizedKey);
			if (existingIndex >= 0) {
				localizationFile.PackageData[existingIndex] = localizedRow;
			}
			else {
				localizationFile.PackageData.Add(localizedRow);
			}

			fileSystem.WriteAllTextToFile(culturePath, serializer.SerializePackageData(localizationFile));
		}
	}

	private void RemoveLocalizationRows(string bindingDirectoryPath, DataBindingRuntimeDescriptor descriptor, string normalizedKeyValue) {
		string localizationDirectoryPath = fileSystem.CombinePaths(bindingDirectoryPath, LocalizationFolderName);
		if (!fileSystem.ExistsDirectory(localizationDirectoryPath)) {
			return;
		}

		foreach (string filePath in fileSystem.GetFiles(localizationDirectoryPath, "data.*.json", SearchOption.TopDirectoryOnly)) {
			DataBindingPackageDataFile? localizationFile = JsonSerializer.Deserialize<DataBindingPackageDataFile>(
				fileSystem.ReadAllText(filePath),
				DataBindingJson.Options);
			if (localizationFile is null) {
				continue;
			}

			int existingIndex = FindRowIndex(localizationFile.PackageData, descriptor, normalizedKeyValue);
			if (existingIndex >= 0) {
				localizationFile.PackageData.RemoveAt(existingIndex);
				fileSystem.WriteAllTextToFile(filePath, serializer.SerializePackageData(localizationFile));
			}
		}
	}

	private DataBindingRow BuildLocalizationRow(
		DataBindingRuntimeDescriptor descriptor,
		string normalizedKey,
		Dictionary<string, JsonNode?> values) {
		List<DataBindingRowValue> row = [
			new DataBindingRowValue {
				SchemaColumnUId = descriptor.PrimaryColumn.ColumnUId,
				ColumnName = descriptor.PrimaryColumn.ColumnName,
				Value = RestoreKeyValue(normalizedKey, descriptor.PrimaryColumn)
			}
		];

		foreach ((string columnName, JsonNode? node) in values.OrderBy(item => item.Key, StringComparer.Ordinal)) {
			if (!descriptor.ColumnsByName.TryGetValue(columnName, out DataBindingColumnDefinition? column)) {
				throw new InvalidOperationException($"Localization column '{columnName}' is not part of binding '{descriptor.Name}'.");
			}

			if (!valueConverter.IsStringLike(column.DataTypeValueUId)) {
				throw new InvalidOperationException(
					$"Localization column '{columnName}' must be a string-compatible data type.");
			}

			row.Add(new DataBindingRowValue {
				SchemaColumnUId = column.ColumnUId,
				ColumnName = column.ColumnName,
				Value = valueConverter.ConvertValue(node, column.DataTypeValueUId, column.ColumnName, allowEmptyString: true)
			});
		}

		return new DataBindingRow { Row = row };
	}

	private DataBindingRow BuildLocalizationRowTemplate(DataBindingRuntimeDescriptor descriptor, DataBindingRow sourceRow) {
		List<DataBindingRowValue> row = [];
		string normalizedKey = GetPrimaryKey(sourceRow, descriptor);
		row.Add(new DataBindingRowValue {
			SchemaColumnUId = descriptor.PrimaryColumn.ColumnUId,
			ColumnName = descriptor.PrimaryColumn.ColumnName,
			Value = RestoreKeyValue(normalizedKey, descriptor.PrimaryColumn)
		});

		foreach (DataBindingColumnDefinition column in descriptor.Columns
			         .Where(column => !column.IsKey && valueConverter.IsStringLike(column.DataTypeValueUId))
			         .OrderBy(column => column.ColumnName, StringComparer.Ordinal)) {
			row.Add(new DataBindingRowValue {
				SchemaColumnUId = column.ColumnUId,
				ColumnName = column.ColumnName,
				Value = string.Empty
			});
		}

		return new DataBindingRow { Row = row };
	}

	private object RestoreKeyValue(string normalizedKey, DataBindingColumnDefinition primaryColumn) {
		if (string.IsNullOrWhiteSpace(normalizedKey)) {
			return string.Empty;
		}

		return valueConverter.ConvertValue(JsonValue.Create(normalizedKey), primaryColumn.DataTypeValueUId,
			primaryColumn.ColumnName, allowEmptyString: false)
			?? normalizedKey;
	}
}

internal sealed class DataBindingSchemaClient(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder)
	: IDataBindingSchemaClient {
	public DataBindingSchema Fetch(string schemaName) {
		if (string.IsNullOrWhiteSpace(schemaName)) {
			throw new InvalidOperationException("Schema name is required.");
		}

		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest);
		string response = applicationClient.ExecutePostRequest(url, $$"""{"Name":"{{schemaName}}"}""");
		DataBindingRuntimeSchemaResponse? schemaResponse =
			JsonSerializer.Deserialize<DataBindingRuntimeSchemaResponse>(response, DataBindingJson.Options);
		if (schemaResponse?.Schema?.Columns?.Items is null || !schemaResponse.Success) {
			throw new InvalidOperationException($"Runtime schema '{schemaName}' was not returned by Creatio.");
		}

		List<DataBindingSchemaColumn> columns = schemaResponse.Schema.Columns.Items.Values
			.Select(column => new DataBindingSchemaColumn(
				column.UId,
				column.Name,
				column.DataValueType,
				column.ReferenceSchemaName))
			.ToList();
		if (schemaResponse.Schema.PrimaryColumnUId == Guid.Empty) {
			throw new InvalidOperationException($"Schema '{schemaName}' does not expose a primary column.");
		}

		return new DataBindingSchema(
			schemaResponse.Schema.UId,
			schemaResponse.Schema.Name,
			schemaResponse.Schema.PrimaryColumnUId,
			columns);
	}
}

internal sealed class DataBindingSchemaResolver(
	IDataBindingTemplateSchemaCatalog templateCatalog,
	IDataBindingSchemaClient schemaClient) : IDataBindingSchemaResolver {
	public DataBindingSchema Resolve(string schemaName) {
		if (templateCatalog.TryGetTemplate(schemaName, out DataBindingSchema schema)) {
			return schema;
		}

		return schemaClient.Fetch(schemaName);
	}
}

internal sealed class DataBindingTemplateCatalog : IDataBindingTemplateSchemaCatalog {
	private static readonly IReadOnlyDictionary<string, DataBindingSchema> Templates =
		new Dictionary<string, DataBindingSchema>(StringComparer.OrdinalIgnoreCase) {
			["SysSettings"] = new(
				new Guid("27aeadd6-d508-4572-8061-5b55b667c902"),
				"SysSettings",
				new Guid("ae0e45ca-c495-4fe7-a39d-3ab7278e1617"),
				[
					new DataBindingSchemaColumn(
						new Guid("13aad544-ec30-4e76-a373-f0cff3202e24"),
						"Code",
						27,
						null),
					new DataBindingSchemaColumn(
						new Guid("64fadca1-ab7c-4471-9d25-68599acc729b"),
						"IsSSPAvailable",
						12,
						null),
					new DataBindingSchemaColumn(
						new Guid("736c30a7-c0ec-4fa9-b034-2552b319b633"),
						"Name",
						28,
						null),
					new DataBindingSchemaColumn(
						new Guid("764cd95a-59b3-4060-b17f-2797d5c76aaa"),
						"IsPersonal",
						12,
						null),
					new DataBindingSchemaColumn(
						new Guid("9e53fd7c-dde4-4502-a64c-b9e34148108b"),
						"Description",
						29,
						null),
					new DataBindingSchemaColumn(
						new Guid("ae0e45ca-c495-4fe7-a39d-3ab7278e1617"),
						"Id",
						0,
						null),
					new DataBindingSchemaColumn(
						new Guid("b2280617-35f9-4006-8634-c557a2e121c2"),
						"ReferenceSchemaUId",
						0,
						"SysSchema"),
					new DataBindingSchemaColumn(
						new Guid("eb971f1a-dd41-4668-99aa-1f2a6b61a1b9"),
						"IsCacheable",
						12,
						null),
					new DataBindingSchemaColumn(
						new Guid("f7960a8a-1fd4-41d2-997a-fd78ea60075f"),
						"ValueTypeName",
						28,
						null)
				])
		};

	public bool HasTemplate(string schemaName) {
		return !string.IsNullOrWhiteSpace(schemaName) && Templates.ContainsKey(schemaName);
	}

	public bool TryGetTemplate(string schemaName, out DataBindingSchema schema) {
		if (string.IsNullOrWhiteSpace(schemaName)) {
			schema = default!;
			return false;
		}

		return Templates.TryGetValue(schemaName, out schema!);
	}
}

internal sealed class DataBindingSerializer : IDataBindingSerializer {
	public string SerializeDescriptor(DataBindingDescriptorFile descriptor) {
		return JsonSerializer.Serialize(descriptor, DataBindingJson.Options);
	}

	public string SerializePackageData(DataBindingPackageDataFile dataFile) {
		return JsonSerializer.Serialize(dataFile, DataBindingJson.Options);
	}
}

internal sealed class DataBindingValueConverter : IDataBindingValueConverter {
	public object? ConvertValue(JsonNode? valueNode, Guid dataTypeUId, string columnName, bool allowEmptyString) {
		if (valueNode is null) {
			return null;
		}

		Type targetType = DataValueTypeMap.Resolve(dataTypeUId);
		if (valueNode is JsonValue jsonValue) {
			if (targetType == typeof(bool)) {
				if (jsonValue.TryGetValue<bool>(out bool boolValue)) {
					return boolValue;
				}

				if (jsonValue.TryGetValue<string>(out string? boolText) &&
					bool.TryParse(boolText, out bool parsedBool)) {
					return parsedBool;
				}
			}
			else if (targetType == typeof(int)) {
				if (jsonValue.TryGetValue<int>(out int intValue)) {
					return intValue;
				}

				if (jsonValue.TryGetValue<string>(out string? intText) &&
					int.TryParse(intText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt)) {
					return parsedInt;
				}
			}
			else if (targetType == typeof(float)) {
				if (jsonValue.TryGetValue<double>(out double doubleValue)) {
					return Convert.ToSingle(doubleValue, CultureInfo.InvariantCulture);
				}

				if (jsonValue.TryGetValue<string>(out string? floatText) &&
					float.TryParse(floatText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedFloat)) {
					return parsedFloat;
				}
			}
			else if (targetType == typeof(decimal)) {
				if (jsonValue.TryGetValue<decimal>(out decimal decimalValue)) {
					return decimalValue;
				}

				if (jsonValue.TryGetValue<string>(out string? decimalText) &&
					decimal.TryParse(decimalText, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsedDecimal)) {
					return parsedDecimal;
				}
			}
			else if (targetType == typeof(Guid)) {
				if (jsonValue.TryGetValue<Guid>(out Guid guidValue)) {
					return guidValue.ToString();
				}

				if (jsonValue.TryGetValue<string>(out string? guidText)) {
					if (string.Equals(guidText, "null", StringComparison.OrdinalIgnoreCase)) {
						return "null";
					}

					if (allowEmptyString && string.IsNullOrWhiteSpace(guidText)) {
						return string.Empty;
					}

					if (Guid.TryParse(guidText, out Guid parsedGuid)) {
						return parsedGuid.ToString();
					}
				}
			}
			else if (targetType == typeof(DateTime)) {
				if (jsonValue.TryGetValue<DateTime>(out DateTime dateTimeValue)) {
					return dateTimeValue.ToString("O", CultureInfo.InvariantCulture);
				}

				if (jsonValue.TryGetValue<string>(out string? dateText) &&
					DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
						out DateTime parsedDateTime)) {
					return parsedDateTime.ToString("O", CultureInfo.InvariantCulture);
				}
			}
			else if (jsonValue.TryGetValue<string>(out string? stringValue)) {
				if (!allowEmptyString && string.IsNullOrWhiteSpace(stringValue)) {
					throw new InvalidOperationException($"Column '{columnName}' cannot be empty.");
				}

				return stringValue;
			}
		}

		if (targetType == typeof(string)) {
			string stringValue = valueNode.ToJsonString().Trim('"');
			if (!allowEmptyString && string.IsNullOrWhiteSpace(stringValue)) {
				throw new InvalidOperationException($"Column '{columnName}' cannot be empty.");
			}

			return stringValue;
		}

		throw new InvalidOperationException(
			$"Column '{columnName}' value '{valueNode}' is not valid for data type '{targetType.Name}'.");
	}

	public string NormalizeKeyValue(object? value, Guid dataTypeUId) {
		if (value is null) {
			return string.Empty;
		}

		Type targetType = DataValueTypeMap.Resolve(dataTypeUId);
		if (targetType == typeof(Guid) && string.IsNullOrWhiteSpace(value.ToString())) {
			return string.Empty;
		}

		return targetType == typeof(Guid)
			? Guid.Parse(value.ToString()!).ToString()
			: Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
	}

	public bool IsStringLike(Guid dataTypeUId) {
		return DataValueTypeMap.Resolve(dataTypeUId) == typeof(string);
	}
}

internal sealed record DataBindingSchema(Guid UId, string Name, Guid PrimaryColumnUId, IReadOnlyList<DataBindingSchemaColumn> Columns);

internal sealed record DataBindingSchemaColumn(Guid UId, string Name, int DataValueType, string? ReferenceSchemaName);

internal sealed class DataBindingRuntimeDescriptor {
	public required string Name { get; init; }

	public required IReadOnlyList<DataBindingColumnDefinition> Columns { get; init; }

	public required Dictionary<string, DataBindingColumnDefinition> ColumnsByName { get; init; }

	public required DataBindingColumnDefinition PrimaryColumn { get; init; }

	public static DataBindingRuntimeDescriptor FromSchema(
		DataBindingSchema schema,
		IReadOnlyCollection<DataBindingColumnDefinition> columns,
		IDataBindingValueConverter _valueConverter) {
		List<DataBindingColumnDefinition> columnList = columns.ToList();
		DataBindingColumnDefinition primaryColumn = columnList.Single(column => column.IsKey);
		return new DataBindingRuntimeDescriptor {
			Name = schema.Name,
			Columns = columnList,
			ColumnsByName = columnList.ToDictionary(column => column.ColumnName, column => column,
				StringComparer.OrdinalIgnoreCase),
			PrimaryColumn = primaryColumn
		};
	}

	public static DataBindingRuntimeDescriptor FromDescriptor(
		DataBindingDescriptorFile descriptor,
		IDataBindingValueConverter _valueConverter) {
		List<DataBindingColumnDefinition> columnList = descriptor.Descriptor.Columns;
		DataBindingColumnDefinition primaryColumn = columnList.Single(column => column.IsKey);
		return new DataBindingRuntimeDescriptor {
			Name = descriptor.Descriptor.Name,
			Columns = columnList,
			ColumnsByName = columnList.ToDictionary(column => column.ColumnName, column => column,
				StringComparer.OrdinalIgnoreCase),
			PrimaryColumn = primaryColumn
		};
	}
}

internal static class DataBindingJson {
	internal static readonly JsonSerializerOptions Options = new() {
		PropertyNamingPolicy = null,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = true
	};
}

internal sealed class DataBindingRuntimeSchemaResponse {
	[JsonPropertyName("schema")]
	public DataBindingRuntimeSchemaPayload? Schema { get; set; }

	[JsonPropertyName("success")]
	public bool Success { get; set; }
}

internal sealed class DataBindingRuntimeSchemaPayload {
	[JsonPropertyName("columns")]
	public DataBindingRuntimeSchemaColumns? Columns { get; set; }

	[JsonPropertyName("primaryColumnUId")]
	public Guid PrimaryColumnUId { get; set; }

	[JsonPropertyName("uId")]
	public Guid UId { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;
}

internal sealed class DataBindingRuntimeSchemaColumns {
	[JsonPropertyName("Items")]
	public Dictionary<Guid, DataBindingRuntimeSchemaColumnPayload> Items { get; set; } = new();
}

internal sealed class DataBindingRuntimeSchemaColumnPayload {
	[JsonPropertyName("uId")]
	public Guid UId { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("dataValueType")]
	public int DataValueType { get; set; }

	[JsonPropertyName("referenceSchemaName")]
	public string? ReferenceSchemaName { get; set; }
}

internal sealed class DataBindingDescriptorFile {
	[JsonPropertyName("Descriptor")]
	public DataBindingDescriptor Descriptor { get; set; } = new();
}

internal sealed class DataBindingDescriptor {
	[JsonPropertyName("UId")]
	public Guid UId { get; set; }

	[JsonPropertyName("Name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("ModifiedOnUtc")]
	public string ModifiedOnUtc { get; set; } = string.Empty;

	[JsonPropertyName("InstallType")]
	public int InstallType { get; set; }

	[JsonPropertyName("Schema")]
	public DataBindingSchemaRef Schema { get; set; } = new();

	[JsonPropertyName("Columns")]
	public List<DataBindingColumnDefinition> Columns { get; set; } = [];
}

internal sealed class DataBindingSchemaRef {
	[JsonPropertyName("UId")]
	public Guid UId { get; set; }

	[JsonPropertyName("Name")]
	public string Name { get; set; } = string.Empty;
}

internal sealed class DataBindingColumnDefinition {
	[JsonPropertyName("ColumnUId")]
	public Guid ColumnUId { get; set; }

	[JsonPropertyName("IsForceUpdate")]
	public bool IsForceUpdate { get; set; }

	[JsonPropertyName("IsKey")]
	public bool IsKey { get; set; }

	[JsonPropertyName("ColumnName")]
	public string ColumnName { get; set; } = string.Empty;

	[JsonPropertyName("DataTypeValueUId")]
	public Guid DataTypeValueUId { get; set; }
}

internal sealed class DataBindingPackageDataFile {
	[JsonPropertyName("PackageData")]
	public List<DataBindingRow> PackageData { get; set; } = [];
}

internal sealed class DataBindingRow {
	[JsonPropertyName("Row")]
	public List<DataBindingRowValue> Row { get; set; } = [];
}

internal sealed class DataBindingRowValue {
	[JsonPropertyName("SchemaColumnUId")]
	public Guid SchemaColumnUId { get; set; }

	[JsonPropertyName("ColumnName")]
	public string? ColumnName { get; set; }

	[JsonPropertyName("Value")]
	public object? Value { get; set; }
}

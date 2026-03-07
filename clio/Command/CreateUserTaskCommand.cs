using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Package;
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Defines CLI options for creating a workspace-scoped process user task schema.
/// </summary>
[Verb("add-user-task", HelpText = "Create a new ProcessUserTask schema in a package")]
public class CreateUserTaskOptions : RemoteCommandOptions {

	/// <summary>
	/// Gets or sets the user task schema code.
	/// </summary>
	[Value(0, MetaName = "Code", Required = true, HelpText = "User task code (schema/class name)")]
	public string Code { get; set; }

	/// <summary>
	/// Gets or sets the workspace package name.
	/// </summary>
	[Option("package", Required = true, HelpText = "Package name")]
	public string Package { get; set; }

	/// <summary>
	/// Gets or sets the default localized title.
	/// </summary>
	[Option('t', "title", Required = true, HelpText = "Default localized title")]
	public string Title { get; set; }

	/// <summary>
	/// Gets or sets the default localized description.
	/// </summary>
	[Option('d', "description", Required = false, HelpText = "Default localized description")]
	public string Description { get; set; }

	/// <summary>
	/// Gets or sets the culture for default title and description values.
	/// </summary>
	[Option("culture", Required = false, Default = "en-US",
		HelpText = "Culture for --title and --description values")]
	public string Culture { get; set; }

	/// <summary>
	/// Gets or sets additional localized titles.
	/// </summary>
	[Option("title-localization", Required = false, Separator = ';',
		HelpText = "Additional title localization in <culture>=<value> format")]
	public IEnumerable<string> TitleLocalizations { get; set; }

	/// <summary>
	/// Gets or sets additional localized descriptions.
	/// </summary>
	[Option("description-localization", Required = false, Separator = ';',
		HelpText = "Additional description localization in <culture>=<value> format")]
	public IEnumerable<string> DescriptionLocalizations { get; set; }

	/// <summary>
	/// Gets or sets repeatable parameter definitions for the created user task.
	/// </summary>
	[Option("parameter", Required = false, Separator = '|',
		HelpText = "Parameter definition in 'code=<name>;title=<caption>;type=<type>[;direction=<In|Out|Variable|0|1|2>][;required=true][;resulting=true][;serializable=true][;copyValue=true][;lazyLoad=true][;containsPerformerId=true]' format. Separate multiple values with '|'")]
	public IEnumerable<string> Parameters { get; set; }

	/// <summary>
	/// Gets or sets the workspace root override used by MCP tools.
	/// </summary>
	[Option("workspace-path", Required = false, Hidden = true,
		HelpText = "Workspace path override. Intended for MCP usage.")]
	public string WorkspacePath { get; set; }
}

/// <summary>
/// Creates a new process user task schema in a workspace package and builds that package.
/// </summary>
public class CreateUserTaskCommand : RemoteCommand<CreateUserTaskOptions> {

	private const string ProcessUserTaskSchemaManagerName = "ProcessUserTaskSchemaManager";
	private const string DefaultColor = "#839DC3";

	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IJsonConverter _jsonConverter;
	private readonly IFileSystem _fileSystem;
	private readonly IFileDesignModePackages _fileDesignModePackages;
	private readonly IUserTaskMetadataDirectionApplier _userTaskMetadataDirectionApplier;

	public CreateUserTaskCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder, IWorkspacePathBuilder workspacePathBuilder,
		IJsonConverter jsonConverter, IFileSystem fileSystem, IFileDesignModePackages fileDesignModePackages,
		IUserTaskMetadataDirectionApplier userTaskMetadataDirectionApplier)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
		_workspacePathBuilder = workspacePathBuilder;
		_jsonConverter = jsonConverter;
		_fileSystem = fileSystem;
		_fileDesignModePackages = fileDesignModePackages;
		_userTaskMetadataDirectionApplier = userTaskMetadataDirectionApplier;
	}

	protected override void ExecuteRemoteCommand(CreateUserTaskOptions options) {
		ConfigureWorkspace(options);
		PackageDescriptor package = ResolveWorkspacePackage(options.Package);
		Guid packageUId = package.UId;
		Dictionary<string, int> explicitParameterDirections = UserTaskSchemaSupport
			.ExtractExplicitDirections(options.Parameters);

		string createNewSchemaUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.CreateUserTaskSchema);
		string createRequestBody = JsonSerializer.Serialize(new CreateSchemaRequestDto {
			PackageUId = packageUId
		});
		string createResponseJson = ApplicationClient.ExecutePostRequest(createNewSchemaUrl, createRequestBody,
			RequestTimeout, RetryCount, DelaySec);

		DesignerResponse<ProcessUserTaskDesignSchemaDto> createResponse = UserTaskSchemaSupport
			.Deserialize<DesignerResponse<ProcessUserTaskDesignSchemaDto>>(createResponseJson, "CreateNewSchema");
		UserTaskSchemaSupport.EnsureResponseSucceeded(createResponse, "CreateNewSchema");

		ProcessUserTaskDesignSchemaDto schema = createResponse.Schema
			?? throw new InvalidOperationException("CreateNewSchema did not return a schema payload.");

		ApplyRequestedValues(schema, options, package);

		string saveSchemaUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveUserTaskSchema);
		string saveRequestBody = JsonSerializer.Serialize(schema);
		string saveResponseJson = ApplicationClient.ExecutePostRequest(saveSchemaUrl, saveRequestBody, RequestTimeout,
			RetryCount, DelaySec);

		SaveDesignItemDesignerResponse saveResponse = UserTaskSchemaSupport
			.Deserialize<SaveDesignItemDesignerResponse>(saveResponseJson, "SaveSchema");
		UserTaskSchemaSupport.EnsureSaveSucceeded(saveResponse);
		Logger.WriteInfo($"Created user task schema '{schema.Name}' ({saveResponse.SchemaUId}).");
		BuildPackage(package.Name);
		ApplyParameterDirectionMetadataIfNeeded(package.Name, schema.Name, explicitParameterDirections);
	}

	private void ConfigureWorkspace(CreateUserTaskOptions options) {
		if (!string.IsNullOrWhiteSpace(options.WorkspacePath)) {
			_workspacePathBuilder.RootPath = options.WorkspacePath.Trim();
		}
	}

	private void BuildPackage(string packageName) {
		string buildPackageUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.BuildPackage);
		string buildRequestBody = JsonSerializer.Serialize(new BuildPackageRequestDto {
			PackageName = packageName
		});
		Logger.WriteInfo($"Building package '{packageName}'...");
		ApplicationClient.ExecutePostRequest(buildPackageUrl, buildRequestBody, RequestTimeout, RetryCount, DelaySec);
	}

	private void ApplyParameterDirectionMetadataIfNeeded(string packageName, string schemaName,
		IReadOnlyDictionary<string, int> directionsByParameterName) {
		if (directionsByParameterName is null || directionsByParameterName.Count == 0) {
			return;
		}

		Logger.WriteInfo($"Applying direction metadata for {directionsByParameterName.Count} parameter(s) on '{schemaName}'...");
		_userTaskMetadataDirectionApplier.ApplyDirections(packageName, schemaName, directionsByParameterName);
		Logger.WriteInfo("Loading workspace packages to database to apply direction metadata changes...");
		_fileDesignModePackages.LoadPackagesToDb();
		BuildPackage(packageName);
	}

	private static void ApplyRequestedValues(ProcessUserTaskDesignSchemaDto schema, CreateUserTaskOptions options,
		PackageDescriptor package) {
		schema.Name = options.Code.Trim();
		schema.Caption = UserTaskSchemaSupport.BuildLocalizableValues(options.Culture, options.Title, options.TitleLocalizations,
			allowEmptyPrimaryValue: false);
		schema.Description = UserTaskSchemaSupport.BuildLocalizableValues(options.Culture, options.Description,
			options.DescriptionLocalizations, allowEmptyPrimaryValue: true);
		schema.Parameters = UserTaskSchemaSupport.BuildParameters(options.Culture, options.Parameters);
		schema.LocalizableStrings ??= [];
		schema.OptionalProperties ??= [];
		schema.Body ??= string.Empty;
		schema.Color = string.IsNullOrWhiteSpace(schema.Color) ? DefaultColor : schema.Color;
		schema.IsPartial = false;
		schema.IsUserTask = true;
		schema.EnableCustomEventHandlers = false;
		schema.SerializeToDB = true;
		schema.UseFullHierarchy = false;
		schema.IsUserLevelSchema = false;
		schema.IsFullHierarchyDesignSchema = false;
		schema.ForceSave = false;
		schema.Package ??= new WorkspacePackageDto();
		schema.Package.Name = package.Name;
		schema.Package.UId = package.UId;
		schema.Package.Type = (int)package.Type;
		schema.SmallSvgImage ??= new ProcessSchemaImageItemDto();
		schema.LargeSvgImage ??= new ProcessSchemaImageItemDto();
		schema.TitleSvgImage ??= new ProcessSchemaImageItemDto();
		schema.DcmSmallSvgImage ??= new ProcessSchemaImageItemDto();
		schema.MetaData = BuildMetaData(schema.UId, schema.Name, package.UId, schema.Color);
	}

	private PackageDescriptor ResolveWorkspacePackage(string packageName) {
		if (!_workspacePathBuilder.IsWorkspace) {
			throw new InvalidOperationException(
				"Current directory is not a workspace. Please run this command from a workspace directory.");
		}

		string packagePath = _workspacePathBuilder.BuildPackagePath(packageName);
		string packageDescriptorPath = PackageUtilities.BuildPackageDescriptorPath(packagePath);
		if (!_fileSystem.ExistsDirectory(packagePath) || !_fileSystem.ExistsFile(packageDescriptorPath)) {
			throw new InvalidOperationException(
				$"Package '{packageName}' is not part of the current workspace.");
		}

		PackageDescriptorDto packageDescriptorDto =
			_jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(packageDescriptorPath);
		PackageDescriptor package = packageDescriptorDto?.Descriptor;
		if (package is null) {
			throw new InvalidOperationException(
				$"Package '{packageName}' has an invalid descriptor at '{packageDescriptorPath}'.");
		}

		return package;
	}


	private static string BuildMetaData(Guid schemaUId, string schemaName, Guid packageUId, string color) {
		var metadata = new MetaDataEnvelope {
			MetaData = new MetaDataContainer {
				Schema = new MetaDataSchema {
					ManagerName = ProcessUserTaskSchemaManagerName,
					UId = schemaUId,
					Name = schemaName,
					PackageUId = packageUId,
					Color = color
				}
			}
		};
		return JsonSerializer.Serialize(metadata);
	}

}

internal sealed class CreateSchemaRequestDto {
	[JsonPropertyName("packageUId")]
	public Guid PackageUId { get; set; }

	[JsonPropertyName("cultures")]
	public List<string> Cultures { get; set; }
}

internal sealed class BuildPackageRequestDto {
	[JsonPropertyName("packageName")]
	public string PackageName { get; set; }
}

internal sealed class GetSchemaRequestDto {
	[JsonPropertyName("schemaUId")]
	public Guid SchemaUId { get; set; }

	[JsonPropertyName("useFullHierarchy")]
	public bool UseFullHierarchy { get; set; }

	[JsonPropertyName("cultures")]
	public List<string> Cultures { get; set; }
}

internal sealed class DesignerResponse<T> : BaseResponse {
	[JsonPropertyName("schema")]
	public T Schema { get; set; }
}

internal sealed class SaveDesignItemDesignerResponse : BaseResponse {
	[JsonPropertyName("schemaUid")]
	public Guid SchemaUId { get; set; }

	[JsonPropertyName("validationErrors")]
	public List<ValidationErrorResponse> ValidationErrors { get; set; }
}

internal sealed class ValidationErrorResponse {
	[JsonPropertyName("source")]
	public string Source { get; set; }

	[JsonPropertyName("reference")]
	public string Reference { get; set; }

	[JsonPropertyName("package")]
	public string Package { get; set; }
}

internal sealed class ProcessUserTaskDesignSchemaDto {
	[JsonPropertyName("uId")]
	public Guid UId { get; set; }

	[JsonPropertyName("id")]
	public Guid Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("body")]
	public string Body { get; set; }

	[JsonPropertyName("metaData")]
	public string MetaData { get; set; }

	[JsonPropertyName("caption")]
	public List<LocalizableStringDto> Caption { get; set; }

	[JsonPropertyName("description")]
	public List<LocalizableStringDto> Description { get; set; }

	[JsonPropertyName("parameters")]
	public List<UserTaskParameterDto> Parameters { get; set; }

	[JsonPropertyName("localizableStrings")]
	public List<object> LocalizableStrings { get; set; }

	[JsonPropertyName("addonTypes")]
	public List<string> AddonTypes { get; set; }

	[JsonPropertyName("optionalProperties")]
	public List<KeyValueObjectPairDto> OptionalProperties { get; set; }

	[JsonPropertyName("dependencies")]
	public object Dependencies { get; set; }

	[JsonPropertyName("isReadOnly")]
	public bool IsReadOnly { get; set; }

	[JsonPropertyName("useFullHierarchy")]
	public bool UseFullHierarchy { get; set; }

	[JsonPropertyName("forceSave")]
	public bool ForceSave { get; set; }

	[JsonPropertyName("userLevelSchema")]
	public bool IsUserLevelSchema { get; set; }

	[JsonPropertyName("isFullHierarchyDesignSchema")]
	public bool IsFullHierarchyDesignSchema { get; set; }

	[JsonPropertyName("partial")]
	public bool IsPartial { get; set; }

	[JsonPropertyName("userTask")]
	public bool IsUserTask { get; set; }

	[JsonPropertyName("customEventHandler")]
	public bool EnableCustomEventHandlers { get; set; }

	[JsonPropertyName("editPageSchemaUId")]
	public Guid EditPageSchemaUId { get; set; }

	[JsonPropertyName("dcmEditPageSchemaUId")]
	public Guid DcmEditPageSchemaUId { get; set; }

	[JsonPropertyName("smallSvgImage")]
	public ProcessSchemaImageItemDto SmallSvgImage { get; set; }

	[JsonPropertyName("largeSvgImage")]
	public ProcessSchemaImageItemDto LargeSvgImage { get; set; }

	[JsonPropertyName("titleSvgImage")]
	public ProcessSchemaImageItemDto TitleSvgImage { get; set; }

	[JsonPropertyName("dcmSmallSvgImage")]
	public ProcessSchemaImageItemDto DcmSmallSvgImage { get; set; }

	[JsonPropertyName("color")]
	public string Color { get; set; }

	[JsonPropertyName("serializeToDB")]
	public bool SerializeToDB { get; set; }

	[JsonPropertyName("package")]
	public WorkspacePackageDto Package { get; set; }
}

internal sealed class LocalizableStringDto {
	[JsonPropertyName("cultureName")]
	public string CultureName { get; set; }

	[JsonPropertyName("value")]
	public string Value { get; set; }
}

internal sealed class UserTaskParameterDto {
	[JsonPropertyName("uId")]
	public Guid UId { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("caption")]
	public List<LocalizableStringDto> Caption { get; set; }

	[JsonPropertyName("itemProperties")]
	public List<UserTaskParameterDto> ItemProperties { get; set; }

	[JsonPropertyName("type")]
	public int Type { get; set; }

	[JsonPropertyName("value")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Value { get; set; }

	[JsonPropertyName("lookup")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Guid? ReferenceSchemaUId { get; set; }

	[JsonPropertyName("schema")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ReferenceSchemaName { get; set; }

	[JsonPropertyName("required")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? Required { get; set; }

	[JsonPropertyName("resulting")]
	public bool Resulting { get; set; }

	[JsonPropertyName("containsPerformerId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? ContainsPerformerId { get; set; }

	[JsonPropertyName("lazyLoad")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? LazyLoad { get; set; }

	[JsonPropertyName("serializable")]
	public bool Serializable { get; set; }

	[JsonPropertyName("copyValue")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? CopyValue { get; set; }

	[JsonPropertyName("direction")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Direction { get; set; }

	[JsonPropertyName("icon")]
	public string Icon { get; set; }
}

internal sealed class KeyValueObjectPairDto {
	[JsonPropertyName("key")]
	public string Key { get; set; }

	[JsonPropertyName("value")]
	public object Value { get; set; }
}

internal sealed class WorkspacePackageDto {
	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("uId")]
	public Guid UId { get; set; }

	[JsonPropertyName("type")]
	public int Type { get; set; }
}

internal sealed class ProcessSchemaImageItemDto {
	[JsonPropertyName("_isChanged")]
	public bool IsChanged { get; set; }

	[JsonPropertyName("imageContent")]
	public string ImageContent { get; set; }
}

internal sealed class MetaDataEnvelope {
	[JsonPropertyName("metaData")]
	public MetaDataContainer MetaData { get; set; }
}

internal sealed class MetaDataContainer {
	[JsonPropertyName("schema")]
	public MetaDataSchema Schema { get; set; }
}

internal sealed class MetaDataSchema {
	[JsonPropertyName("managerName")]
	public string ManagerName { get; set; }

	[JsonPropertyName("uId")]
	public Guid UId { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("methods")]
	public List<object> Methods { get; set; } = [];

	[JsonPropertyName("localizableStrings")]
	public List<object> LocalizableStrings { get; set; } = [];

	[JsonPropertyName("usings")]
	public List<object> Usings { get; set; } = [];

	[JsonPropertyName("packageUId")]
	public Guid PackageUId { get; set; }

	[JsonPropertyName("parameters")]
	public List<object> Parameters { get; set; } = [];

	[JsonPropertyName("color")]
	public string Color { get; set; }
}

internal sealed record UserTaskParameterTypeDefinition(int TypeId, string Icon);

internal static class UserTaskSchemaSupport {
	private static readonly string[] SupportedParameterTypeNames = [
		"Boolean",
		"Date",
		"DateTime",
		"Float",
		"Guid",
		"Integer",
		"Money",
		"Text",
		"Time"
	];

	private static readonly Dictionary<string, UserTaskParameterTypeDefinition> SupportedParameterTypes =
		new(StringComparer.OrdinalIgnoreCase) {
			["Boolean"] = new(12, "data-type-boolean-icon.svg"),
			["Bool"] = new(12, "data-type-boolean-icon.svg"),
			["Date"] = new(8, "data-type-date-icon.svg"),
			["DateTime"] = new(7, "data-type-datetime-icon.svg"),
			["Float"] = new(5, "data-type-float1-icon.svg"),
			["Double"] = new(5, "data-type-float1-icon.svg"),
			["Guid"] = new(10, "data-type-guid-icon.svg"),
			["Integer"] = new(4, "data-type-integer-icon.svg"),
			["Int"] = new(4, "data-type-integer-icon.svg"),
			["Money"] = new(6, "data-type-currency-icon.svg"),
			["Decimal"] = new(6, "data-type-currency-icon.svg"),
			["Text"] = new(1, "data-type-text-icon.svg"),
			["String"] = new(1, "data-type-text-icon.svg"),
			["Time"] = new(9, "data-type-time-icon.svg")
		};

	private static readonly string[] SupportedDirectionNames = [
		"In",
		"Out",
		"Variable"
	];

	private static readonly Dictionary<string, int> SupportedDirections =
		new(StringComparer.OrdinalIgnoreCase) {
			["0"] = 0,
			["In"] = 0,
			["1"] = 1,
			["Out"] = 1,
			["2"] = 2,
			["Variable"] = 2
		};

	private static readonly JsonSerializerOptions SerializerOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	internal static List<UserTaskParameterDto> BuildParameters(string culture, IEnumerable<string> parameterDefinitions) {
		var parameters = new List<UserTaskParameterDto>();
		var parameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string parameterDefinition in parameterDefinitions ?? []) {
			UserTaskParameterDto parameter = ParseParameter(culture, parameterDefinition);
			if (!parameterNames.Add(parameter.Name)) {
				throw new InvalidOperationException(
					$"Parameter '{parameter.Name}' is defined more than once. Parameter names must be unique.");
			}

			parameters.Add(parameter);
		}

		return parameters;
	}

	internal static Dictionary<string, int> ExtractExplicitDirections(IEnumerable<string> parameterDefinitions) {
		var directions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (string parameterDefinition in parameterDefinitions ?? []) {
			Dictionary<string, string> values = ParseParameterDefinitionValues(parameterDefinition);
			if (!values.TryGetValue("direction", out string directionValue) || string.IsNullOrWhiteSpace(directionValue)) {
				continue;
			}

			string parameterName = GetRequiredValue(values, "code", parameterDefinition).Trim();
			int direction = ParseDirection(directionValue, parameterDefinition);
			if (!directions.TryAdd(parameterName, direction)) {
				throw new InvalidOperationException(
					$"Parameter '{parameterName}' is defined more than once with an explicit direction.");
			}
		}

		return directions;
	}

	internal static List<LocalizableStringDto> BuildLocalizableValues(string culture, string primaryValue,
		IEnumerable<string> additionalValues, bool allowEmptyPrimaryValue) {
		var valuesByCulture = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		if (primaryValue is not null && (allowEmptyPrimaryValue || !string.IsNullOrWhiteSpace(primaryValue))) {
			valuesByCulture[culture] = primaryValue;
		}

		foreach ((string parsedCulture, string parsedValue) in (additionalValues ?? []).Select(ParseLocalization)) {
			valuesByCulture[parsedCulture] = parsedValue;
		}

		return valuesByCulture
			.Select(value => new LocalizableStringDto {
				CultureName = value.Key,
				Value = value.Value
			})
			.ToList();
	}

	internal static (string Culture, string Value) ParseLocalization(string localization) {
		if (string.IsNullOrWhiteSpace(localization)) {
			throw new ArgumentException("Localization value cannot be empty.", nameof(localization));
		}

		int separatorIndex = localization.IndexOf('=');
		if (separatorIndex < 0) {
			separatorIndex = localization.IndexOf(':');
		}

		if (separatorIndex <= 0) {
			throw new ArgumentException(
				$"Localization '{localization}' must be in '<culture>=<value>' format.",
				nameof(localization));
		}

		string culture = localization[..separatorIndex].Trim();
		string value = localization[(separatorIndex + 1)..].Trim();

		if (string.IsNullOrWhiteSpace(culture)) {
			throw new ArgumentException(
				$"Localization '{localization}' must contain a culture name.",
				nameof(localization));
		}

		return (culture, value);
	}

	internal static T Deserialize<T>(string json, string operation) {
		T result = JsonSerializer.Deserialize<T>(json, SerializerOptions);
		return result ?? throw new InvalidOperationException($"{operation} returned an empty response.");
	}

	internal static void EnsureResponseSucceeded(BaseResponse response, string operation) {
		if (response.Success) {
			return;
		}

		string message = string.IsNullOrWhiteSpace(response.ErrorInfo?.Message)
			? $"{operation} failed."
			: response.ErrorInfo.Message;
		throw new InvalidOperationException(message);
	}

	internal static void EnsureSaveSucceeded(SaveDesignItemDesignerResponse response) {
		if (response.Success) {
			return;
		}

		if (response.ValidationErrors?.Count > 0) {
			string validationErrors = string.Join("; ", response.ValidationErrors.Select(error =>
				$"{error.Source} -> {error.Reference} ({error.Package})"));
			throw new InvalidOperationException($"SaveSchema failed validation: {validationErrors}");
		}

		EnsureResponseSucceeded(response, "SaveSchema");
	}

	private static UserTaskParameterDto ParseParameter(string culture, string parameterDefinition) {
		Dictionary<string, string> values = ParseParameterDefinitionValues(parameterDefinition);

		string name = GetRequiredValue(values, "code", parameterDefinition);
		string title = GetRequiredValue(values, "title", parameterDefinition);
		string typeName = GetRequiredValue(values, "type", parameterDefinition);
		UserTaskParameterTypeDefinition parameterType = ResolveParameterType(typeName);

		return new UserTaskParameterDto {
			UId = Guid.NewGuid(),
			Name = name.Trim(),
			Caption = [
				new LocalizableStringDto {
					CultureName = culture,
					Value = title
				}
			],
			ItemProperties = [],
			Type = parameterType.TypeId,
			Required = ParseOptionalBoolean(values, "required", parameterDefinition),
			Resulting = ParseOptionalBoolean(values, "resulting", parameterDefinition) ?? true,
			ContainsPerformerId = ParseOptionalBoolean(values, "containsPerformerId", parameterDefinition),
			LazyLoad = ParseOptionalBoolean(values, "lazyLoad", parameterDefinition),
			Serializable = ParseOptionalBoolean(values, "serializable", parameterDefinition) ?? true,
			CopyValue = ParseOptionalBoolean(values, "copyValue", parameterDefinition),
			Direction = ParseOptionalDirection(values, parameterDefinition) ?? 2,
			Icon = parameterType.Icon
		};
	}

	private static Dictionary<string, string> ParseParameterDefinitionValues(string parameterDefinition) {
		if (string.IsNullOrWhiteSpace(parameterDefinition)) {
			throw new InvalidOperationException("Parameter definition cannot be empty.");
		}

		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string part in parameterDefinition.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			int separatorIndex = part.IndexOf('=');
			if (separatorIndex < 0) {
				separatorIndex = part.IndexOf(':');
			}

			if (separatorIndex <= 0) {
				throw new InvalidOperationException(
					$"Parameter definition '{parameterDefinition}' must use '<key>=<value>' pairs separated by ';'.");
			}

			string key = part[..separatorIndex].Trim();
			string value = part[(separatorIndex + 1)..].Trim();
			if (string.IsNullOrWhiteSpace(key)) {
				throw new InvalidOperationException(
					$"Parameter definition '{parameterDefinition}' contains an empty key.");
			}

			values[key] = value;
		}

		return values;
	}

	private static string GetRequiredValue(Dictionary<string, string> values, string key, string parameterDefinition) {
		if (!values.TryGetValue(key, out string value) || string.IsNullOrWhiteSpace(value)) {
			throw new InvalidOperationException(
				$"Parameter definition '{parameterDefinition}' must include a non-empty '{key}' value.");
		}

		return value;
	}

	private static bool? ParseOptionalBoolean(Dictionary<string, string> values, string key, string parameterDefinition) {
		if (!values.TryGetValue(key, out string value)) {
			return null;
		}

		if (bool.TryParse(value, out bool parsedValue)) {
			return parsedValue;
		}

		throw new InvalidOperationException(
			$"Parameter definition '{parameterDefinition}' has invalid boolean value '{value}' for '{key}'.");
	}

	private static int? ParseOptionalDirection(Dictionary<string, string> values, string parameterDefinition) {
		if (!values.TryGetValue("direction", out string value) || string.IsNullOrWhiteSpace(value)) {
			return null;
		}

		return ParseDirection(value, parameterDefinition);
	}

	internal static int ParseDirection(string value, string context) {
		string normalizedValue = value?.Trim();
		if (!string.IsNullOrWhiteSpace(normalizedValue) && SupportedDirections.TryGetValue(normalizedValue, out int direction)) {
			return direction;
		}

		throw new InvalidOperationException(
			$"Parameter definition '{context}' has invalid direction '{value}'. Supported values: {string.Join(", ", SupportedDirectionNames)} or 0, 1, 2.");
	}

	private static UserTaskParameterTypeDefinition ResolveParameterType(string typeName) {
		if (SupportedParameterTypes.TryGetValue(typeName.Trim(), out UserTaskParameterTypeDefinition parameterType)) {
			return parameterType;
		}

		throw new InvalidOperationException(
			$"Unsupported parameter type '{typeName}'. Supported types: {string.Join(", ", SupportedParameterTypeNames)}.");
	}
}

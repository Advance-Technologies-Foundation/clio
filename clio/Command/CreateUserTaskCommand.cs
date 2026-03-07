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

[Verb("add-user-task", HelpText = "Create a new ProcessUserTask schema in a package")]
public class CreateUserTaskOptions : RemoteCommandOptions {

	[Value(0, MetaName = "Code", Required = true, HelpText = "User task code (schema/class name)")]
	public string Code { get; set; }

	[Option("package", Required = true, HelpText = "Package name")]
	public string Package { get; set; }

	[Option('t', "title", Required = true, HelpText = "Default localized title")]
	public string Title { get; set; }

	[Option('d', "description", Required = false, HelpText = "Default localized description")]
	public string Description { get; set; }

	[Option("culture", Required = false, Default = "en-US",
		HelpText = "Culture for --title and --description values")]
	public string Culture { get; set; }

	[Option("title-localization", Required = false, Separator = ';',
		HelpText = "Additional title localization in <culture>=<value> format")]
	public IEnumerable<string> TitleLocalizations { get; set; }

	[Option("description-localization", Required = false, Separator = ';',
		HelpText = "Additional description localization in <culture>=<value> format")]
	public IEnumerable<string> DescriptionLocalizations { get; set; }
}

public class CreateUserTaskCommand : RemoteCommand<CreateUserTaskOptions> {

	private const string CreateNewSchemaServicePath =
		"ServiceModel/ProcessUserTaskSchemaDesignerService.svc/CreateNewSchema";
	private const string SaveSchemaServicePath =
		"ServiceModel/ProcessUserTaskSchemaDesignerService.svc/SaveSchema";
	private const string ProcessUserTaskSchemaManagerName = "ProcessUserTaskSchemaManager";
	private const string DefaultColor = "#839DC3";

	private static readonly JsonSerializerOptions SerializerOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IJsonConverter _jsonConverter;
	private readonly IFileSystem _fileSystem;

	public CreateUserTaskCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder, IWorkspacePathBuilder workspacePathBuilder,
		IJsonConverter jsonConverter, IFileSystem fileSystem)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
		_workspacePathBuilder = workspacePathBuilder;
		_jsonConverter = jsonConverter;
		_fileSystem = fileSystem;
	}

	protected override void ExecuteRemoteCommand(CreateUserTaskOptions options) {
		PackageDescriptor package = ResolveWorkspacePackage(options.Package);
		Guid packageUId = package.UId;

		string createNewSchemaUrl = _serviceUrlBuilder.Build(CreateNewSchemaServicePath);
		string createRequestBody = JsonSerializer.Serialize(new CreateSchemaRequestDto {
			PackageUId = packageUId
		});
		string createResponseJson = ApplicationClient.ExecutePostRequest(createNewSchemaUrl, createRequestBody,
			RequestTimeout, RetryCount, DelaySec);

		DesignerResponse<ProcessUserTaskDesignSchemaDto> createResponse =
			Deserialize<DesignerResponse<ProcessUserTaskDesignSchemaDto>>(createResponseJson, "CreateNewSchema");
		EnsureResponseSucceeded(createResponse, "CreateNewSchema");

		ProcessUserTaskDesignSchemaDto schema = createResponse.Schema
			?? throw new InvalidOperationException("CreateNewSchema did not return a schema payload.");

		ApplyRequestedValues(schema, options, package);

		string saveSchemaUrl = _serviceUrlBuilder.Build(SaveSchemaServicePath);
		string saveRequestBody = JsonSerializer.Serialize(schema);
		string saveResponseJson = ApplicationClient.ExecutePostRequest(saveSchemaUrl, saveRequestBody, RequestTimeout,
			RetryCount, DelaySec);

		SaveDesignItemDesignerResponse saveResponse =
			Deserialize<SaveDesignItemDesignerResponse>(saveResponseJson, "SaveSchema");
		EnsureSaveSucceeded(saveResponse);
		Logger.WriteInfo($"Created user task schema '{schema.Name}' ({saveResponse.SchemaUId}).");
		BuildPackage(package.Name);
	}

	private void BuildPackage(string packageName) {
		string buildPackageUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.BuildPackage);
		string buildRequestBody = JsonSerializer.Serialize(new BuildPackageRequestDto {
			PackageName = packageName
		});
		Logger.WriteInfo($"Building package '{packageName}'...");
		ApplicationClient.ExecutePostRequest(buildPackageUrl, buildRequestBody, RequestTimeout, RetryCount, DelaySec);
	}

	private static void ApplyRequestedValues(ProcessUserTaskDesignSchemaDto schema, CreateUserTaskOptions options,
		PackageDescriptor package) {
		schema.Name = options.Code.Trim();
		schema.Caption = BuildLocalizableValues(options.Culture, options.Title, options.TitleLocalizations,
			allowEmptyPrimaryValue: false);
		schema.Description = BuildLocalizableValues(options.Culture, options.Description,
			options.DescriptionLocalizations, allowEmptyPrimaryValue: true);
		schema.Parameters ??= [];
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

	private static List<LocalizableStringDto> BuildLocalizableValues(string culture, string primaryValue,
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

	private static (string Culture, string Value) ParseLocalization(string localization) {
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

	private static T Deserialize<T>(string json, string operation) {
		T result = JsonSerializer.Deserialize<T>(json, SerializerOptions);
		return result ?? throw new InvalidOperationException($"{operation} returned an empty response.");
	}

	private static void EnsureResponseSucceeded(BaseResponse response, string operation) {
		if (response.Success) {
			return;
		}

		string message = string.IsNullOrWhiteSpace(response.ErrorInfo?.Message)
			? $"{operation} failed."
			: response.ErrorInfo.Message;
		throw new InvalidOperationException(message);
	}

	private static void EnsureSaveSucceeded(SaveDesignItemDesignerResponse response) {
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
	public List<object> Parameters { get; set; }

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

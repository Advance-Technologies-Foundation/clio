using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// CLI options for deleting a section from an existing installed application.
/// </summary>
[Verb("delete-app-section", HelpText = "Delete a section from an existing installed application")]
public sealed class DeleteAppSectionOptions : EnvironmentOptions {
	[Option("application-code", Required = true, HelpText = "Installed application code")]
	public string ApplicationCode { get; set; } = string.Empty;

	[Option("section-code", Required = true, HelpText = "Section code inside the installed application")]
	public string SectionCode { get; set; } = string.Empty;

	[Option("delete-entity-schema", Required = false, Default = false,
		HelpText = "When set, also deletes the entity schema. WARNING: destructive and irreversible.")]
	public bool DeleteEntitySchema { get; set; }
}

/// <summary>
/// Deletes a section from an existing installed application.
/// </summary>
public interface IApplicationSectionDeleteService {
	/// <summary>
	/// Deletes a section from an existing installed application in the specified environment.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">Section delete request payload.</param>
	/// <returns>Structured result with deleted section metadata.</returns>
	ApplicationSectionDeleteResult DeleteSection(string environmentName, ApplicationSectionDeleteRequest request);
}

/// <summary>
/// Default ApplicationSection DataService-backed implementation for existing-app section deletion.
/// </summary>
public sealed class ApplicationSectionDeleteService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder,
	IApplicationInfoService applicationInfoService,
	ILogger logger)
	: IApplicationSectionDeleteService {
	private const string ApplicationSectionSchemaName = "ApplicationSection";
	private const string SysModuleSchemaName = "SysModule";
	private const string SysModuleLczSchemaName = "SysModuleLcz";
	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public ApplicationSectionDeleteResult DeleteSection(string environmentName, ApplicationSectionDeleteRequest request) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		ArgumentNullException.ThrowIfNull(request);
		ValidateRequest(request);
		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				$"Environment with key '{environmentName}' not found. Check your clio configuration.");
		if (string.IsNullOrWhiteSpace(environmentSettings.Uri)) {
			throw new InvalidOperationException(
				$"Environment with key '{environmentName}' not found. Check your clio configuration.");
		}

		IApplicationClient client = applicationClientFactory.CreateEnvironmentClient(environmentSettings);
		logger.WriteInfo($"Resolving application '{request.ApplicationCode}'...");
		InstalledAppSummary appSummary = applicationInfoService.FindApplicationId(environmentName, request.ApplicationCode);
		string applicationId = appSummary.Id;
		logger.WriteInfo($"Application found: {appSummary.Name} ({appSummary.Code})");
		logger.WriteInfo($"Looking up section '{request.SectionCode}'...");
		ApplicationSectionRecord sectionRecord = GetSectionRecord(
			client,
			environmentSettings,
			applicationId,
			request.SectionCode);
		logger.WriteInfo($"Deleting section '{sectionRecord.Code}' ({sectionRecord.Id})...");
		string deleteUrl = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Delete, environmentSettings);
		DeleteSection(client, deleteUrl, environmentSettings, sectionRecord, request.DeleteEntitySchema);

		return new ApplicationSectionDeleteResult(
			null,
			null,
			applicationId,
			appSummary.Name,
			appSummary.Code,
			appSummary.Version,
			MapSection(sectionRecord));
	}

	private ApplicationSectionRecord GetSectionRecord(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string applicationId,
		string sectionCode) {
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select, environmentSettings),
			JsonSerializer.Serialize(BuildSectionSelectQuery(applicationId), JsonOptions));
		ApplicationSectionSelectQueryResponseDto response =
			JsonSerializer.Deserialize<ApplicationSectionSelectQueryResponseDto>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("ApplicationSection select query returned an empty response.");
		if (!response.Success) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "ApplicationSection select query failed.");
		}

		return response.Rows
				.Find(row => string.Equals(row.Code, sectionCode, StringComparison.OrdinalIgnoreCase))
			?? throw new InvalidOperationException(
				$"Section '{sectionCode}' was not found in application '{applicationId}'.");
	}

	private static object BuildSectionSelectQuery(string applicationId) =>
		SelectQueryHelper.BuildSelectQuery(
			ApplicationSectionSchemaName,
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id"),
				new SelectQueryHelper.SelectQueryColumnDefinition("ApplicationId", "ApplicationId"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Caption", "Caption"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Code", "Code"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Description", "Description"),
				new SelectQueryHelper.SelectQueryColumnDefinition("EntitySchemaName", "EntitySchemaName"),
				new SelectQueryHelper.SelectQueryColumnDefinition("PackageId", "PackageId"),
				new SelectQueryHelper.SelectQueryColumnDefinition("SectionSchemaUId", "SectionSchemaUId"),
				new SelectQueryHelper.SelectQueryColumnDefinition("CardSchemaUId", "CardSchemaUId"),
				new SelectQueryHelper.SelectQueryColumnDefinition("SysModuleEntityId", "SysModuleEntityId"),
				new SelectQueryHelper.SelectQueryColumnDefinition("LogoId", "LogoId"),
				new SelectQueryHelper.SelectQueryColumnDefinition("IconBackground", "IconBackground"),
				new SelectQueryHelper.SelectQueryColumnDefinition("ClientTypeId", "ClientTypeId")
			],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition(
					"ApplicationId",
					applicationId,
					SelectQueryHelper.GuidDataValueType)
			]);

	private void DeleteSection(IApplicationClient client, string deleteUrl, EnvironmentSettings environmentSettings, ApplicationSectionRecord section, bool deleteEntitySchema) {
		logger.WriteInfo("Loading section schemas from workspace...");
		List<WorkspaceSchemaItemDto> sectionSchemas = LoadSectionSchemas(client, environmentSettings, section);

		logger.WriteInfo("Deleting SysModuleInWorkplace records...");
		ExecuteDeleteQuery(client, deleteUrl, BuildSysModuleInWorkplaceDeleteQuery(section.Id));
		logger.WriteInfo("Clearing section localizations (SysModuleLcz)...");
		TryExecuteDeleteQuery(client, deleteUrl, BuildLczDeleteQueryBody(section.Id));

		string deleteSchemaUrl = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeleteWorkspaceItem, environmentSettings);
		foreach (WorkspaceSchemaItemDto schema in sectionSchemas) {
			if (!deleteEntitySchema && string.Equals(schema.Name, section.Code, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			logger.WriteInfo($"Deleting schema '{schema.Name}'...");
			TryDeleteWorkspaceSchema(client, deleteSchemaUrl, schema);
		}

		if (!string.IsNullOrWhiteSpace(section.SysModuleEntityId)) {
			logger.WriteInfo("Deleting SysModuleEntity record...");
			ExecuteDeleteQuery(client, deleteUrl, BuildSysModuleEntityDeleteQuery(section.SysModuleEntityId));
		}

		logger.WriteInfo("Deleting SysModule record...");
		ExecuteDeleteQuery(client, deleteUrl, BuildDeleteQueryBody(section.Id));
	}

	private List<WorkspaceSchemaItemDto> LoadSectionSchemas(IApplicationClient client, EnvironmentSettings environmentSettings, ApplicationSectionRecord section) {
		string getItemsUrl = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems, environmentSettings);
		string responseBody = client.ExecutePostRequest(getItemsUrl, string.Empty);
		WorkspaceItemsCollectionDto collection = JsonSerializer.Deserialize<WorkspaceItemsCollectionDto>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("GetWorkspaceItems returned an empty response.");
		return (collection.Items ?? [])
			.Where(item => !string.IsNullOrWhiteSpace(item.Name)
				&& item.Name.StartsWith(section.Code, StringComparison.OrdinalIgnoreCase))
			.ToList();
	}

	private void TryDeleteWorkspaceSchema(IApplicationClient client, string deleteUrl, WorkspaceSchemaItemDto schema) {
		try {
			string requestBody = JsonSerializer.Serialize(new[] { schema }, JsonOptions);
			string responseBody = client.ExecutePostRequest(deleteUrl, requestBody);
			DeleteQueryResponseDto response = JsonSerializer.Deserialize<DeleteQueryResponseDto>(responseBody, JsonOptions)
				?? throw new InvalidOperationException("Delete returned an empty response.");
			if (!response.Success) {
				logger.WriteInfo($"Warning: failed to delete schema '{schema.Name}': {response.ErrorInfo?.Message ?? "Unknown error"}");
			}
		} catch (Exception ex) {
			logger.WriteInfo($"Warning: failed to delete schema '{schema.Name}': {ex.Message}");
		}
	}

	private void ExecuteDeleteQuery(IApplicationClient client, string deleteUrl, string requestBody) {
		string responseBody = client.ExecutePostRequest(deleteUrl, requestBody);
		DeleteQueryResponseDto response = JsonSerializer.Deserialize<DeleteQueryResponseDto>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("DeleteQuery returned an empty response.");
		if (!response.Success) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "DeleteQuery failed.");
		}
	}

	private void TryExecuteDeleteQuery(IApplicationClient client, string deleteUrl, string requestBody) {
		try {
			ExecuteDeleteQuery(client, deleteUrl, requestBody);
		} catch (Exception ex) {
			logger.WriteInfo($"Warning: non-critical delete step failed and will be skipped: {ex.Message}");
		}
	}

	private static string BuildSysModuleInWorkplaceDeleteQuery(string sectionId) =>
		$$"""
		{
		  "__type":"Terrasoft.Nui.ServiceModel.DataContract.DeleteQuery",
		  "rootSchemaName":"SysModuleInWorkplace",
		  "filters":{
		    "isEnabled":true,
		    "filterType":6,
		    "logicalOperation":0,
		    "trimDateTimeParameterToDate":false,
		    "items":{
		      "primaryFilter":{
		        "filterType":1,
		        "comparisonType":3,
		        "isEnabled":true,
		        "trimDateTimeParameterToDate":false,
		        "leftExpression":{
		          "expressionType":0,
		          "columnPath":"SysModule"
		        },
		        "rightExpression":{
		          "expressionType":2,
		          "parameter":{
		            "dataValueType":0,
		            "value":"{{sectionId}}"
		          }
		        }
		      }
		    }
		  }
		}
		""";

	private static string BuildLczDeleteQueryBody(string sectionId) =>
		$$"""
		{
		  "__type":"Terrasoft.Nui.ServiceModel.DataContract.DeleteQuery",
		  "rootSchemaName":"{{SysModuleLczSchemaName}}",
		  "filters":{
		    "isEnabled":true,
		    "filterType":6,
		    "logicalOperation":0,
		    "trimDateTimeParameterToDate":false,
		    "items":{
		      "primaryFilter":{
		        "filterType":1,
		        "comparisonType":3,
		        "isEnabled":true,
		        "trimDateTimeParameterToDate":false,
		        "leftExpression":{
		          "expressionType":0,
		          "columnPath":"RecordId"
		        },
		        "rightExpression":{
		          "expressionType":2,
		          "parameter":{
		            "dataValueType":0,
		            "value":"{{sectionId}}"
		          }
		        }
		      }
		    }
		  }
		}
		""";

	private static string BuildSysModuleEntityDeleteQuery(string sysModuleEntityId) =>
		$$"""
		{
		  "__type":"Terrasoft.Nui.ServiceModel.DataContract.DeleteQuery",
		  "rootSchemaName":"SysModuleEntity",
		  "filters":{
		    "isEnabled":true,
		    "filterType":6,
		    "logicalOperation":0,
		    "trimDateTimeParameterToDate":false,
		    "items":{
		      "primaryFilter":{
		        "filterType":1,
		        "comparisonType":3,
		        "isEnabled":true,
		        "trimDateTimeParameterToDate":false,
		        "leftExpression":{
		          "expressionType":0,
		          "columnPath":"Id"
		        },
		        "rightExpression":{
		          "expressionType":2,
		          "parameter":{
		            "dataValueType":0,
		            "value":"{{sysModuleEntityId}}"
		          }
		        }
		      }
		    }
		  }
		}
		""";

	private static string BuildDeleteQueryBody(string sectionId) =>
		$$"""
		{
		  "__type":"Terrasoft.Nui.ServiceModel.DataContract.DeleteQuery",
		  "rootSchemaName":"{{SysModuleSchemaName}}",
		  "filters":{
		    "isEnabled":true,
		    "filterType":6,
		    "logicalOperation":0,
		    "trimDateTimeParameterToDate":false,
		    "items":{
		      "primaryFilter":{
		        "filterType":1,
		        "comparisonType":3,
		        "isEnabled":true,
		        "trimDateTimeParameterToDate":false,
		        "leftExpression":{
		          "expressionType":0,
		          "columnPath":"Id"
		        },
		        "rightExpression":{
		          "expressionType":2,
		          "parameter":{
		            "dataValueType":0,
		            "value":"{{sectionId}}"
		          }
		        }
		      }
		    }
		  }
		}
		""";

	private static ApplicationSectionInfoResult MapSection(ApplicationSectionRecord record) =>
		new(
			record.Id,
			record.Code,
			record.Caption ?? string.Empty,
			record.Description,
			record.EntitySchemaName,
			record.PackageId,
			record.SectionSchemaUId,
			record.LogoId,
			record.IconBackground,
			record.ClientTypeId);

	private static void ValidateRequest(ApplicationSectionDeleteRequest request) {
		if (string.IsNullOrWhiteSpace(request.ApplicationCode)) {
			throw new ArgumentException("application-code is required.");
		}

		if (string.IsNullOrWhiteSpace(request.SectionCode)) {
			throw new ArgumentException("section-code is required.");
		}
	}

	private sealed class ApplicationSectionSelectQueryResponseDto : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public System.Collections.Generic.List<ApplicationSectionRecord> Rows { get; set; } = [];
	}

	private sealed class DeleteQueryResponseDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoDto? ErrorInfo { get; set; }
	}

	private sealed class ErrorInfoDto {
		[JsonPropertyName("message")]
		public string? Message { get; set; }
	}

	private sealed class WorkspaceItemsCollectionDto {
		[JsonPropertyName("items")]
		public System.Collections.Generic.List<WorkspaceSchemaItemDto>? Items { get; set; }
	}

	private sealed class WorkspaceSchemaItemDto {
		[JsonPropertyName("id")]
		public Guid Id { get; set; }

		[JsonPropertyName("uId")]
		public Guid UId { get; set; }

		[JsonPropertyName("name")]
		public string? Name { get; set; }

		[JsonPropertyName("title")]
		public string? Title { get; set; }

		[JsonPropertyName("packageUId")]
		public Guid PackageUId { get; set; }

		[JsonPropertyName("packageName")]
		public string? PackageName { get; set; }

		[JsonPropertyName("type")]
		public int Type { get; set; }

		[JsonPropertyName("modifiedOn")]
		public string? ModifiedOn { get; set; }

		[JsonPropertyName("isChanged")]
		public bool IsChanged { get; set; }

		[JsonPropertyName("isLocked")]
		public bool IsLocked { get; set; }

		[JsonPropertyName("isReadOnly")]
		public bool IsReadOnly { get; set; }
	}
}

/// <summary>
/// Deletes a section from an existing installed application and prints the structured result to the logger.
/// </summary>
public sealed class DeleteAppSectionCommand(
	IApplicationSectionDeleteService applicationSectionDeleteService,
	ILogger logger)
	: Command<DeleteAppSectionOptions> {
	/// <inheritdoc />
	public override int Execute(DeleteAppSectionOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}

			ApplicationSectionDeleteResult result = applicationSectionDeleteService.DeleteSection(
				options.Environment,
				new ApplicationSectionDeleteRequest(
					options.ApplicationCode,
					options.SectionCode,
					options.DeleteEntitySchema));
			logger.WriteInfo(JsonSerializer.Serialize(result));
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>
/// Request payload for existing-app section deletion.
/// </summary>
/// <param name="ApplicationCode">Installed application code.</param>
/// <param name="SectionCode">Section code inside the installed application.</param>
/// <param name="DeleteEntitySchema">When true, also deletes the entity schema record. WARNING: irreversible.</param>
public sealed record ApplicationSectionDeleteRequest(
	string ApplicationCode,
	string SectionCode,
	bool DeleteEntitySchema = false);

/// <summary>
/// Structured result for existing-app section deletion.
/// </summary>
/// <param name="PackageUId">Primary package identifier.</param>
/// <param name="PackageName">Primary package name.</param>
/// <param name="ApplicationId">Installed application identifier.</param>
/// <param name="ApplicationName">Installed application display name.</param>
/// <param name="ApplicationCode">Installed application code.</param>
/// <param name="ApplicationVersion">Installed application version.</param>
/// <param name="DeletedSection">Deleted section metadata.</param>
public sealed record ApplicationSectionDeleteResult(
	string? PackageUId,
	string? PackageName,
	string ApplicationId,
	string ApplicationName,
	string ApplicationCode,
	string? ApplicationVersion,
	ApplicationSectionInfoResult DeletedSection);

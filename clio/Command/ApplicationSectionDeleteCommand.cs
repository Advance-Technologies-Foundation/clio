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
	/// <summary>Gets or sets the installed application code.</summary>
	[Option("application-code", Required = true, HelpText = "Installed application code")]
	public string ApplicationCode { get; set; } = string.Empty;

	/// <summary>Gets or sets the section code.</summary>
	[Option("section-code", Required = true, HelpText = "Section code inside the installed application")]
	public string SectionCode { get; set; } = string.Empty;

	/// <summary>Gets or sets explicit permission to delete the declared entity schema.</summary>
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

	/// <summary>
	/// Deletes a section from an existing installed application against an already-resolved
	/// environment. Behaves identically to <see cref="DeleteSection(string, ApplicationSectionDeleteRequest)"/>
	/// except it never consults <see cref="Clio.UserEnvironment.ISettingsRepository"/> — the caller
	/// supplies the settings directly (e.g. an MCP passthrough tenant resolved from request headers) —
	/// and the nested application lookup uses the settings-based overload of
	/// <see cref="IApplicationInfoService"/>, never the name-based one (ENG-93347, ADR OQ-01 c1 rule).
	/// </summary>
	/// <param name="environmentSettings">The already-resolved environment settings; must not be <c>null</c>.</param>
	/// <param name="request">Section delete request payload.</param>
	/// <returns>Structured result with deleted section metadata.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="environmentSettings"/> is <c>null</c>.</exception>
	ApplicationSectionDeleteResult DeleteSection(EnvironmentSettings environmentSettings, ApplicationSectionDeleteRequest request);
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
	private const int EntitySchemaItemType = 3;
	private const int ClientUnitSchemaItemType = 4;
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
				EnvironmentNotFoundError.Build(environmentName, settingsRepository));
		if (string.IsNullOrWhiteSpace(environmentSettings.Uri)) {
			throw new InvalidOperationException(
				EnvironmentNotFoundError.Build(environmentName, settingsRepository));
		}

		// The name-based path keeps its pre-change nested call byte-for-byte (name-based application
		// lookup) so stdio / registered-environment behavior is untouched (ENG-93347 AC-05).
		return DeleteSectionCore(
			environmentSettings,
			request,
			() => applicationInfoService.FindApplicationId(environmentName, request.ApplicationCode));
	}

	/// <inheritdoc />
	public ApplicationSectionDeleteResult DeleteSection(EnvironmentSettings environmentSettings, ApplicationSectionDeleteRequest request) {
		ArgumentNullException.ThrowIfNull(environmentSettings);
		ArgumentNullException.ThrowIfNull(request);
		ValidateRequest(request);

		// ADR OQ-01 (c1) rule: a settings-based overload never calls a name-based overload or
		// ISettingsRepository — the nested application lookup goes through the Story-2
		// settings-based overload.
		return DeleteSectionCore(
			environmentSettings,
			request,
			() => applicationInfoService.FindApplicationId(environmentSettings, request.ApplicationCode));
	}

	private ApplicationSectionDeleteResult DeleteSectionCore(
		EnvironmentSettings environmentSettings,
		ApplicationSectionDeleteRequest request,
		Func<InstalledAppSummary> findApplication) {
		using IOwnedApplicationClient client = applicationClientFactory.CreateOwnedEnvironmentClient(environmentSettings);
		logger.WriteInfo($"Resolving application '{request.ApplicationCode}'...");
		InstalledAppSummary appSummary = findApplication();
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
		List<SectionReferenceDto> sections = LoadSectionReferences(client, environmentSettings);
		SectionReferenceDto target = sections.SingleOrDefault(row => string.Equals(row.Id, section.Id, StringComparison.OrdinalIgnoreCase))
			?? throw new InvalidOperationException("Cannot resolve the persisted section. No artifacts were deleted.");
		section = section with {
			SectionSchemaUId = target.SectionSchemaUId, CardSchemaUId = target.CardSchemaUId,
			SysModuleEntityId = target.SysModuleEntityId
		};
		logger.WriteInfo("Loading section schemas from workspace...");
		List<WorkspaceSchemaItemDto> sectionSchemas = LoadSectionSchemas(client, environmentSettings, section, deleteEntitySchema);
		HashSet<Guid> registeredEditPages = LoadRegisteredEditPages(client, environmentSettings);
		List<SectionReferenceDto> otherSections = sections.Where(row => !string.Equals(row.Id, section.Id, StringComparison.OrdinalIgnoreCase)).ToList();
		bool sharedEntityBinding = !string.IsNullOrWhiteSpace(section.SysModuleEntityId) && otherSections.Any(other =>
			string.Equals(other.SysModuleEntityId, section.SysModuleEntityId, StringComparison.OrdinalIgnoreCase));
		if (deleteEntitySchema && (sharedEntityBinding || sectionSchemas.Any(schema =>
			schema.Type == EntitySchemaItemType && otherSections.Any(other => MatchesUId(other.EntitySchemaUId, schema.UId))))) {
			throw new InvalidOperationException("The entity is used by another section. No artifacts were deleted.");
		}
		sectionSchemas.RemoveAll(schema => schema.Type == ClientUnitSchemaItemType &&
			((!deleteEntitySchema && MatchesUId(section.CardSchemaUId, schema.UId)) || registeredEditPages.Contains(schema.UId) || otherSections.Any(other =>
				MatchesUId(other.SectionSchemaUId, schema.UId) || MatchesUId(other.CardSchemaUId, schema.UId))));
		logger.WriteInfo($"Schemas selected for deletion: {string.Join(", ", sectionSchemas.Select(schema => $"{schema.Name} ({schema.UId})"))}");

		logger.WriteInfo("Deleting SysModuleInWorkplace records...");
		ExecuteDeleteQuery(client, deleteUrl, BuildSysModuleInWorkplaceDeleteQuery(section.Id));
		logger.WriteInfo("Clearing section localizations (SysModuleLcz)...");
		TryExecuteDeleteQuery(client, deleteUrl, BuildLczDeleteQueryBody(section.Id));

		string deleteSchemaUrl = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeleteWorkspaceItem, environmentSettings);
		foreach (WorkspaceSchemaItemDto schema in sectionSchemas) {
			logger.WriteInfo($"Deleting schema '{schema.Name}'...");
			DeleteWorkspaceSchema(client, deleteSchemaUrl, schema);
			logger.WriteInfo($"Deleted schema '{schema.Name}' ({schema.UId}).");
		}

		logger.WriteInfo("Deleting ApplicationSection record...");
		TryExecuteDeleteQuery(client, deleteUrl, BuildApplicationSectionDeleteQuery(section.Id));

		logger.WriteInfo("Deleting SysModule record...");
		ExecuteDeleteQuery(client, deleteUrl, BuildDeleteQueryBody(section.Id));
		if (!sharedEntityBinding && !string.IsNullOrWhiteSpace(section.SysModuleEntityId)) {
			logger.WriteInfo("Deleting SysModuleEntity record...");
			ExecuteDeleteQuery(client, deleteUrl, BuildSysModuleEntityDeleteQuery(section.SysModuleEntityId));
		}
	}

	private static bool MatchesUId(string? value, Guid uId) => Guid.TryParse(value, out Guid parsed) && parsed == uId;

	private HashSet<Guid> LoadRegisteredEditPages(IApplicationClient client, EnvironmentSettings settings) {
		object query = SelectQueryHelper.BuildSelectQuery("SysModuleEdit",
			[new("CardSchemaUId", "CardSchemaUId"), new("MiniPageSchemaUId", "SectionSchemaUId"),
				new("SearchRowSchemaUId", "SearchRowSchemaUId")], []);
		string body = client.ExecutePostRequest(serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select, settings),
			JsonSerializer.Serialize(query, JsonOptions));
		SectionReferencesResponseDto response = JsonSerializer.Deserialize<SectionReferencesResponseDto>(body, JsonOptions)
			?? throw new InvalidOperationException("Cannot check registered edit pages.");
		if (!response.Success || response.Rows is null || response.Rows.Count >= 10000) {
			throw new InvalidOperationException("Cannot check registered edit pages. No artifacts were deleted.");
		}
		// Edit-page registrations are independent of section ownership, including details
		// and mini pages. Retain their schemas even when this section uses the same page.
		return response.Rows.SelectMany(row => new[] { row.CardSchemaUId, row.SectionSchemaUId, row.SearchRowSchemaUId })
			.Where(value => Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty)
			.Select(Guid.Parse).ToHashSet();
	}

	private List<SectionReferenceDto> LoadSectionReferences(IApplicationClient client, EnvironmentSettings settings) {
		object query = SelectQueryHelper.BuildSelectQuery(SysModuleSchemaName,
			[
				new("Id", "Id"), new("SectionSchemaUId", "SectionSchemaUId"),
				new("CardSchemaUId", "CardSchemaUId"), new("SysModuleEntity.Id", "SysModuleEntityId"),
				new("SysModuleEntity.SysEntitySchemaUId", "EntitySchemaUId")
			], []);
		string body = client.ExecutePostRequest(serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select, settings),
			JsonSerializer.Serialize(query, JsonOptions));
		SectionReferencesResponseDto response = JsonSerializer.Deserialize<SectionReferencesResponseDto>(body, JsonOptions)
			?? throw new InvalidOperationException("Cannot check shared section artifacts.");
		if (!response.Success || response.Rows is null || response.Rows.Count >= 10000) {
			throw new InvalidOperationException("Cannot check shared section artifacts. No artifacts were deleted.");
		}
		return response.Rows;
	}

	private List<WorkspaceSchemaItemDto> LoadSectionSchemas(IApplicationClient client, EnvironmentSettings environmentSettings, ApplicationSectionRecord section, bool deleteEntitySchema) {
		string getItemsUrl = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems, environmentSettings);
		string responseBody = client.ExecutePostRequest(getItemsUrl, string.Empty);
		WorkspaceItemsCollectionDto collection = JsonSerializer.Deserialize<WorkspaceItemsCollectionDto>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("GetWorkspaceItems returned an empty response.");
		if (collection.Items is null || collection.Success == false) {
			throw new InvalidOperationException("Cannot determine section schemas: GetWorkspaceItems failed or omitted its items.");
		}
		// Names (including conventional suffixes) are not ownership. Only the section's
		// declared page identities are eligible; retain auxiliary schemas with no explicit link.
		List<WorkspaceSchemaItemDto> selected = [];
		foreach (string? declaredUId in new[] { section.SectionSchemaUId, section.CardSchemaUId }) {
			if (string.IsNullOrWhiteSpace(declaredUId)) {
				continue;
			}
			if (!Guid.TryParse(declaredUId, out Guid uId)) {
				throw new InvalidOperationException($"Invalid declared page identity '{declaredUId}'. No artifacts were deleted.");
			}
			if (uId == Guid.Empty) {
				continue;
			}
			List<WorkspaceSchemaItemDto> matches = collection.Items.Where(item => item.UId == uId).ToList();
			if (matches.Count > 1 || matches.Any(item => item.Type != ClientUnitSchemaItemType)) {
				throw new InvalidOperationException($"Ambiguous or non-page schema '{uId}'. No artifacts were deleted.");
			}
			if (matches.Count == 1 && selected.All(item => item.UId != uId)) {
				selected.Add(matches[0]);
			}
		}
		if (deleteEntitySchema) {
			List<WorkspaceSchemaItemDto> entities = collection.Items.Where(item =>
				item.Type == EntitySchemaItemType && !string.IsNullOrWhiteSpace(section.EntitySchemaName)
				&& string.Equals(item.Name, section.EntitySchemaName, StringComparison.OrdinalIgnoreCase)).ToList();
			if (entities.Count != 1 || entities[0].UId == Guid.Empty) {
				throw new InvalidOperationException($"Cannot uniquely resolve entity '{section.EntitySchemaName}'. No artifacts were deleted.");
			}
			selected.Add(entities[0]);
		}
		return selected;
	}

	private static void DeleteWorkspaceSchema(IApplicationClient client, string deleteUrl, WorkspaceSchemaItemDto schema) {
		string requestBody = JsonSerializer.Serialize(new[] { schema }, JsonOptions);
		string responseBody = client.ExecutePostRequest(deleteUrl, requestBody);
		DeleteQueryResponseDto response = JsonSerializer.Deserialize<DeleteQueryResponseDto>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("Delete returned an empty response.");
		if (!response.Success) {
			throw new InvalidOperationException($"Failed to delete schema '{schema.Name}': {response.ErrorInfo?.Message ?? "Unknown error"}. Deletion may be partial; inspect the environment before retrying.");
		}
	}

	private static void ExecuteDeleteQuery(IApplicationClient client, string deleteUrl, string requestBody) {
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

	private static string BuildApplicationSectionDeleteQuery(string sectionId) =>
		$$"""
		{
		  "__type":"Terrasoft.Nui.ServiceModel.DataContract.DeleteQuery",
		  "rootSchemaName":"{{ApplicationSectionSchemaName}}",
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
		[JsonPropertyName("success")]
		public bool? Success { get; set; }

		[JsonPropertyName("items")]
		public System.Collections.Generic.List<WorkspaceSchemaItemDto>? Items { get; set; }
	}

	private sealed class SectionReferencesResponseDto {
		public bool Success { get; set; }
		public List<SectionReferenceDto>? Rows { get; set; }
	}

	private sealed record SectionReferenceDto(string Id, string? SectionSchemaUId, string? CardSchemaUId,
		string? SysModuleEntityId, string? EntitySchemaUId, string? SearchRowSchemaUId);

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

using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Package;

namespace Clio.Command.EntitySchemaDesigner;

internal interface IRemoteEntitySchemaDesignerClient
{
	DesignerResponse<EntityDesignSchemaDto> CreateNewSchema(CreateEntitySchemaRequestDto request, RemoteCommandOptions options);
	AvailableEntitySchemasResponse GetAvailableParentSchemas(GetAvailableSchemasRequestDto request, RemoteCommandOptions options);
	AvailableEntitySchemasResponse GetAvailableReferenceSchemas(GetAvailableSchemasRequestDto request, RemoteCommandOptions options);
	DesignerResponse<EntityDesignSchemaDto> AssignParentSchema(
		AssignParentSchemaRequestDto<EntityDesignSchemaDto> request,
		RemoteCommandOptions options);
	BoolResponse CheckUniqueSchemaName(string managerName, string schemaName, Guid excludeUId, RemoteCommandOptions options);
	DesignerResponse<EntityDesignSchemaDto> GetSchemaDesignItem(GetSchemaDesignItemRequestDto request, RemoteCommandOptions options);
	DesignerResponse<EntityDesignSchemaDto>? TryGetSchemaDesignItem(GetSchemaDesignItemRequestDto request, RemoteCommandOptions options);
	SaveDesignItemDesignerResponse SaveSchema(EntityDesignSchemaDto schema, RemoteCommandOptions options);
	BaseResponse SaveSchemaDbStructure(Guid schemaUId, RemoteCommandOptions options);

	/// <summary>
	/// Publishes pending configuration changes so saved entity schemas become visible to designer
	/// surfaces (lookup pickers, sys-setting reference schema lists). Mirrors the platform designer UI:
	/// sends a <c>SchemaDesignerRequest</c> with <c>buildWorkspace</c> and <c>buildChangedConfiguration</c>
	/// flags, and the server picks the publication strategy for its runtime generation — a full workspace
	/// build on legacy instances or an incremental configuration build plus an
	/// <c>EntitySchemaManager</c> refresh on modern ones.
	/// </summary>
	BaseResponse PublishConfigurationChanges(RemoteCommandOptions options);

	/// <summary>
	/// Requests a rebuild of the OData entities assembly so a freshly published schema becomes reachable
	/// over OData (<c>/0/odata/&lt;Entity&gt;</c>) without a manual full compile. Mirrors the Freedom UI
	/// "Save and Publish", which POSTs <c>WorkspaceExplorerService.svc/RunODataBuild</c>. The build runs
	/// asynchronously, so OData access appears shortly after publish rather than synchronously.
	/// </summary>
	BaseResponse RunODataBuild(RemoteCommandOptions options);
	RuntimeEntitySchemaResponse GetRuntimeEntitySchema(Guid schemaUId, RemoteCommandOptions options);
	IReadOnlyList<SystemValueLookupValueDto> GetSystemValues(Guid dataValueTypeUId, RemoteCommandOptions options);
	IReadOnlyList<SysSettingsSelectQueryRowDto> GetSysSettingsByValueTypeName(
		string valueTypeName,
		RemoteCommandOptions options);

	/// <summary>
	/// Checks whether a record with the given identifier exists in the referenced entity schema, used to
	/// validate a lookup <c>Const</c> default before it is persisted. Returns
	/// <see cref="LookupRecordExistence.Unknown"/> when the check cannot be performed (for example the
	/// current user has no read access to the referenced entity), so an unverifiable check never blocks a write.
	/// </summary>
	/// <param name="schemaName">Referenced entity schema name to query.</param>
	/// <param name="recordId">Record identifier to look up.</param>
	/// <param name="options">Remote command options identifying the target environment.</param>
	/// <returns>Whether the record exists, was not found, or could not be verified.</returns>
	LookupRecordExistence CheckRecordExists(string schemaName, Guid recordId, RemoteCommandOptions options);
}

internal sealed class RemoteEntitySchemaDesignerClient : IRemoteEntitySchemaDesignerClient
{
	private readonly IApplicationClient _applicationClient;
	private readonly IJsonConverter _jsonConverter;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private const string DesignerServicePath = "ServiceModel/EntitySchemaDesignerService.svc";
	private const string WorkspaceExplorerServicePath = "ServiceModel/WorkspaceExplorerService.svc";

	// Publishing triggers a server-side configuration build on legacy instances (BuildWorkspace),
	// which is a compile-class operation. Use the same long timeout as compile-configuration
	// so a slow-but-successful build is not mistaken for a failure.
	internal static readonly int PublishConfigurationTimeoutMs = (int)TimeSpan.FromMinutes(60).TotalMilliseconds;

	public RemoteEntitySchemaDesignerClient(IApplicationClient applicationClient, IJsonConverter jsonConverter,
		IServiceUrlBuilder serviceUrlBuilder) {
		_applicationClient = applicationClient;
		_jsonConverter = jsonConverter;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	public DesignerResponse<EntityDesignSchemaDto> CreateNewSchema(CreateEntitySchemaRequestDto request,
		RemoteCommandOptions options) {
		return Post<CreateEntitySchemaRequestDto, DesignerResponse<EntityDesignSchemaDto>>("CreateNewSchema", request,
			options);
	}

	public AvailableEntitySchemasResponse GetAvailableParentSchemas(GetAvailableSchemasRequestDto request,
		RemoteCommandOptions options) {
		return Post<GetAvailableSchemasRequestDto, AvailableEntitySchemasResponse>("GetAvailableParentSchemas",
			request, options);
	}

	public AvailableEntitySchemasResponse GetAvailableReferenceSchemas(GetAvailableSchemasRequestDto request,
		RemoteCommandOptions options) {
		return Post<GetAvailableSchemasRequestDto, AvailableEntitySchemasResponse>("GetAvailableReferenceSchemas",
			request, options);
	}

	public DesignerResponse<EntityDesignSchemaDto> AssignParentSchema(
		AssignParentSchemaRequestDto<EntityDesignSchemaDto> request,
		RemoteCommandOptions options) {
		return Post<AssignParentSchemaRequestDto<EntityDesignSchemaDto>, DesignerResponse<EntityDesignSchemaDto>>(
			"AssignParentSchema", request, options);
	}

	public BoolResponse CheckUniqueSchemaName(string managerName, string schemaName, Guid excludeUId,
		RemoteCommandOptions options) {
		return Post<object, BoolResponse>("CheckUniqueSchemaName", new {
			managerName,
			schemaName,
			excludeUId
		}, options);
	}

	public DesignerResponse<EntityDesignSchemaDto> GetSchemaDesignItem(GetSchemaDesignItemRequestDto request,
		RemoteCommandOptions options) {
		return Post<GetSchemaDesignItemRequestDto, DesignerResponse<EntityDesignSchemaDto>>("GetSchemaDesignItem",
			request, options);
	}

	public DesignerResponse<EntityDesignSchemaDto>? TryGetSchemaDesignItem(GetSchemaDesignItemRequestDto request,
		RemoteCommandOptions options) {
		string url = BuildDesignerMethodUrl("GetSchemaDesignItem");
		return TryPostToUrl<GetSchemaDesignItemRequestDto, DesignerResponse<EntityDesignSchemaDto>>(url, request,
			options, "GetSchemaDesignItem");
	}

	public SaveDesignItemDesignerResponse SaveSchema(EntityDesignSchemaDto schema, RemoteCommandOptions options) {
		return Post<EntityDesignSchemaDto, SaveDesignItemDesignerResponse>("SaveSchema", schema, options);
	}

	public BaseResponse SaveSchemaDbStructure(Guid schemaUId, RemoteCommandOptions options) {
		return PostToUrl<SchemaDesignerRequestDto, BaseResponse>(
			_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SchemaDesignerRequest),
			new SchemaDesignerRequestDto {
				SaveSchemaDbStructure = [schemaUId]
			},
			options,
			"SaveSchemaDbStructure");
	}

	public BaseResponse PublishConfigurationChanges(RemoteCommandOptions options) {
		// Build POST is non-idempotent: retrying a timed-out build may stack concurrent full compiles.
		// One attempt, no retries (maxAttempts: 1), regardless of the command-level defaults. The value is
		// the total attempt count (minimum 1), so 1 issues exactly one request with no retry.
		return PostToUrl<SchemaDesignerRequestDto, BaseResponse>(
			_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SchemaDesignerRequest),
			new SchemaDesignerRequestDto {
				BuildWorkspace = true,
				BuildChangedConfiguration = true
			},
			PublishConfigurationTimeoutMs,
			maxAttempts: 1,
			options.RetryDelay,
			"PublishConfigurationChanges");
	}

	public BaseResponse RunODataBuild(RemoteCommandOptions options) {
		// Starts the OData entities rebuild as a background task and returns immediately. Triggering the build
		// is non-idempotent (a retry may stack concurrent OData builds), so issue exactly one attempt with no
		// retry (maxAttempts: 1), matching PublishConfigurationChanges.
		string url = $"{_serviceUrlBuilder.Build(WorkspaceExplorerServicePath)}/RunODataBuild";
		// RunODataBuild takes no parameters; the server accepts an empty JSON body, so an empty object ("{}") is posted.
		return PostToUrl<object, BaseResponse>(url, new object(), options.TimeOut, maxAttempts: 1, options.RetryDelay,
			"RunODataBuild");
	}

	public RuntimeEntitySchemaResponse GetRuntimeEntitySchema(Guid schemaUId, RemoteCommandOptions options) {
		return PostToUrl<RuntimeEntitySchemaRequestDto, RuntimeEntitySchemaResponse>(
			_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest),
			new RuntimeEntitySchemaRequestDto {
				UId = schemaUId
			},
			options,
			"GetRuntimeEntitySchema");
	}

	public IReadOnlyList<SystemValueLookupValueDto> GetSystemValues(Guid dataValueTypeUId, RemoteCommandOptions options) {
		SystemValuesResponse response = Post<object, SystemValuesResponse>(
			"GetSystemValues",
			new {
				dataValueTypeUId
			},
			options);
		return response.Items ?? [];
	}

	public IReadOnlyList<SysSettingsSelectQueryRowDto> GetSysSettingsByValueTypeName(
		string valueTypeName,
		RemoteCommandOptions options) {
		object query = SelectQueryHelper.BuildSelectQuery(
			"SysSettings",
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Code", "Code"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Name", "Name"),
				new SelectQueryHelper.SelectQueryColumnDefinition("ValueTypeName", "ValueTypeName")
			],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition(
					"ValueTypeName",
					valueTypeName,
					SelectQueryHelper.TextDataValueType)
			]);
		SysSettingsSelectQueryResponse response = PostToUrl<object, SysSettingsSelectQueryResponse>(
			_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select),
			query,
			options,
			"SelectQuery(SysSettings)");
		return response.Rows ?? [];
	}

	public LookupRecordExistence CheckRecordExists(string schemaName, Guid recordId, RemoteCommandOptions options) {
		if (string.IsNullOrWhiteSpace(schemaName) || recordId == Guid.Empty) {
			return LookupRecordExistence.Unknown;
		}
		object query = SelectQueryHelper.BuildSelectQuery(
			schemaName,
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition(
					"Id",
					recordId.ToString("D"),
					SelectQueryHelper.GuidDataValueType)
			],
			rowCount: 1);
		try {
			RecordIdSelectQueryResponse response = PostToUrl<object, RecordIdSelectQueryResponse>(
				_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select),
				query,
				options,
				$"SelectQuery({schemaName})");
			return (response.Rows?.Length ?? 0) > 0 ? LookupRecordExistence.Exists : LookupRecordExistence.NotFound;
		} catch (Exception ex) when (ex is InvalidOperationException
				or System.Net.Http.HttpRequestException
				or System.Net.WebException
				or System.Threading.Tasks.TaskCanceledException
				or Newtonsoft.Json.JsonException) {
			// Cannot verify existence (security denial on the referenced entity, or a transport/timeout/parse
			// fault): degrade to Unknown so a previously-working write is never blocked on a check that could
			// not be performed (LookupRecordExistence.Unknown contract).
			return LookupRecordExistence.Unknown;
		}
	}

	private TResponse Post<TRequest, TResponse>(string methodName, TRequest request, RemoteCommandOptions options)
		where TRequest : class
		where TResponse : BaseResponse {
		string url = BuildDesignerMethodUrl(methodName);
		return PostToUrl<TRequest, TResponse>(url, request, options, methodName);
	}

	private TResponse PostToUrl<TRequest, TResponse>(string url, TRequest request, RemoteCommandOptions options,
		string methodName)
		where TRequest : class
		where TResponse : BaseResponse {
		return PostToUrl<TRequest, TResponse>(url, request, options.TimeOut, options.MaxAttempts, options.RetryDelay,
			methodName);
	}

	private TResponse PostToUrl<TRequest, TResponse>(string url, TRequest request, int timeoutMs, int maxAttempts,
		int retryDelay, string methodName)
		where TRequest : class
		where TResponse : BaseResponse {
		string requestBody = request == null ? "{}" : _jsonConverter.SerializeObject(request);
		string rawResponse = _applicationClient.ExecutePostRequest(url, requestBody, timeoutMs, maxAttempts, retryDelay);
		TResponse response = DeserializeResponse<TResponse>(methodName, rawResponse);
		return EnsureSuccess(response, methodName);
	}

	private TResponse? TryPostToUrl<TRequest, TResponse>(string url, TRequest request, RemoteCommandOptions options,
		string methodName)
		where TRequest : class
		where TResponse : BaseResponse {
		string requestBody = request == null ? "{}" : _jsonConverter.SerializeObject(request);
		string rawResponse = _applicationClient.ExecutePostRequest(url, requestBody, options.TimeOut, options.MaxAttempts,
			options.RetryDelay);
		if (IsHtmlResponse(rawResponse)) {
			return null;
		}
		TResponse response = DeserializeResponse<TResponse>(methodName, rawResponse);
		return EnsureSuccess(response, methodName);
	}

	private TResponse DeserializeResponse<TResponse>(string methodName, string rawResponse)
		where TResponse : BaseResponse {
		try {
			return _jsonConverter.DeserializeObject<TResponse>(rawResponse);
		} catch (Exception rawException) {
			if (IsHtmlResponse(rawResponse)) {
				throw new InvalidOperationException(
					$"{methodName} returned an HTML error page instead of JSON. " +
					"The Creatio server encountered an unhandled error. Two common causes, check them in this order: " +
					"(1) the target package is MISSING A DEPENDENCY on the package/app that owns the upper layer of " +
					"the object you are extending (for example extending the Opportunity layer without depending on " +
					"CrtLeadOppMgmtApp) — add the owning package with the add-package-dependency command/tool, then " +
					"retry the operation; " +
					"(2) a stale database table left by a previously deleted package — use find-entity-schema to check " +
					"whether the schema was partially created before retrying. " +
					"Do NOT write into the owning (managed) package and do NOT fall back to raw SQL/OData/DataService. " +
					"MCP agents: read get-guidance name=package-dependencies for the full recovery path.",
					rawException);
			}
			string correctedJson = _jsonConverter.CorrectJson(rawResponse);
			try {
				return _jsonConverter.DeserializeObject<TResponse>(correctedJson);
			} catch (Exception correctedException) {
				throw new InvalidOperationException(
					$"{methodName} returned invalid JSON. Raw error: {rawException.Message}. " +
					$"Corrected error: {correctedException.Message}. Response: {Truncate(rawResponse, 1000)}",
					correctedException);
			}
		}
	}

	private static bool IsHtmlResponse(string rawResponse) {
		if (string.IsNullOrEmpty(rawResponse)) {
			return false;
		}
		string trimmed = rawResponse.TrimStart();
		return trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
			|| trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
			|| trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
	}

	private string BuildDesignerMethodUrl(string methodName) {
		string baseUrl = _serviceUrlBuilder.Build(DesignerServicePath);
		return $"{baseUrl}/{methodName}";
	}

	private static TResponse EnsureSuccess<TResponse>(TResponse response, string methodName)
		where TResponse : BaseResponse {
		if (response == null) {
			throw new InvalidOperationException($"{methodName} returned an empty response.");
		}

		if (!response.Success) {
			throw new InvalidOperationException(
				string.IsNullOrWhiteSpace(response.ErrorInfo?.Message)
					? $"{methodName} failed."
					: response.ErrorInfo.Message);
		}

		return response;
	}

	private static string Truncate(string value, int maxLength) {
		return string.IsNullOrEmpty(value) || value.Length <= maxLength
			? value
			: value[..maxLength];
	}
}

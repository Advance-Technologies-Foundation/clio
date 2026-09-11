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

	/// <summary>
	/// Reports whether the background OData entities build started by <see cref="RunODataBuild"/> is still
	/// running, so a caller can hold a publish back instead of colliding with it.
	/// </summary>
	/// <remarks>
	/// Returns <see langword="null"/> when the server does not expose the method at all - it answers with an
	/// HTML error page rather than JSON, the same shape <c>TryGetSchemaDesignItem</c> already treats as
	/// "this server cannot answer that". Callers must read <see langword="null"/> as "unknown", never as
	/// "not running": on such a stand the collision this check exists to prevent is simply undetectable.
	/// </remarks>
	/// <param name="options">Remote command options identifying the target environment.</param>
	/// <returns>Whether a build is running, or <see langword="null"/> when the server has no such method.</returns>
	bool? TryGetIsODataBuildRunning(RemoteCommandOptions options);
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
		return PostToUrl<GetSchemaDesignItemRequestDto, DesignerResponse<EntityDesignSchemaDto>>(
			_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetEntitySchemaDesignItem),
			request,
			options,
			"GetSchemaDesignItem");
	}

	public DesignerResponse<EntityDesignSchemaDto>? TryGetSchemaDesignItem(GetSchemaDesignItemRequestDto request,
		RemoteCommandOptions options) {
		string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetEntitySchemaDesignItem);
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

	public bool? TryGetIsODataBuildRunning(RemoteCommandOptions options) {
		// Same URL shape as RunODataBuild, and the same single attempt (maxAttempts: 1, overriding the
		// command-level default of 3): this is a status read whose answer is stale the moment it arrives, so a
		// retry buys nothing a later poll does not, and retrying a faulted poll would only delay the publish.
		string url = $"{_serviceUrlBuilder.Build(WorkspaceExplorerServicePath)}/IsODataBuildRunning";
		BoolResponse? response = TryPostToUrl<object, BoolResponse>(url, new object(), options,
			"IsODataBuildRunning", maxAttempts: 1);
		return response?.Value;
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
		TResponse response = DeserializeResponse<TResponse>(methodName, url, rawResponse);
		return EnsureSuccess(response, methodName);
	}

	// maxAttempts: null keeps the command-level default; pass an explicit value for a non-idempotent or
	// throwaway request that must not be retried.
	private TResponse? TryPostToUrl<TRequest, TResponse>(string url, TRequest request, RemoteCommandOptions options,
		string methodName, int? maxAttempts = null)
		where TRequest : class
		where TResponse : BaseResponse {
		string requestBody = request == null ? "{}" : _jsonConverter.SerializeObject(request);
		string rawResponse = _applicationClient.ExecutePostRequest(url, requestBody, options.TimeOut,
			maxAttempts ?? options.MaxAttempts, options.RetryDelay);
		// The session check runs BEFORE the markup gate on purpose. A login page is markup, so without this
		// the caller would receive null - the same value that means "this server cannot answer that" - and
		// RemoteEntitySchemaColumnManager.LoadSchema turns that null into a package-dependency mutation. An
		// authentication failure must never be able to rewrite a package's dependency list.
		ThrowIfSessionExpired(methodName, url, rawResponse);
		if (ServiceResponseJsonGuard.LooksLikeMarkup(rawResponse)) {
			return null;
		}
		TResponse response = DeserializeResponse<TResponse>(methodName, url, rawResponse);
		return EnsureSuccess(response, methodName);
	}

	/// <summary>
	/// Deserializes a designer service body into <typeparamref name="TResponse"/>, converting every
	/// non-JSON shape into a classified <see cref="NonJsonServiceResponseException"/>.
	/// <para>
	/// Four shapes are told apart, because the recovery for each is different and a message that conflates
	/// them sends the caller down the wrong path: an expired session (the sign-in response), a markup body,
	/// an empty body, and a body that is neither markup nor parseable JSON. No branch echoes an HTML body and
	/// no branch states a cause the call has no evidence for - naming what the request observed is the whole
	/// contract here (issue #722).
	/// </para>
	/// </summary>
	/// <typeparam name="TResponse">Designer response contract to deserialize into.</typeparam>
	/// <param name="methodName">Designer method the body came from, used to open the error message.</param>
	/// <param name="url">Endpoint the body came from, included so the caller can tell which request failed.</param>
	/// <param name="rawResponse">Raw response body as returned by the application client.</param>
	/// <returns>The deserialized designer response.</returns>
	private TResponse DeserializeResponse<TResponse>(string methodName, string url, string rawResponse)
		where TResponse : BaseResponse {
		// Checked against the RAW body, before any parse attempt: IsSessionExpiredResponse also recognises the
		// JSON 401 fault envelope, which deserializes cleanly into TResponse and would otherwise reach the
		// caller as the generic "<method> failed." from EnsureSuccess.
		ThrowIfSessionExpired(methodName, url, rawResponse);
		if (string.IsNullOrWhiteSpace(rawResponse)) {
			throw new NonJsonServiceResponseException(
				ServiceResponseJsonGuard.BuildEmptyBodyMessage(methodName, url));
		}
		try {
			return _jsonConverter.DeserializeObject<TResponse>(rawResponse);
		} catch (Exception rawException) {
			if (ServiceResponseJsonGuard.LooksLikeMarkup(rawResponse)) {
				// NonJsonServiceResponseException (not a plain InvalidOperationException): its message is marked
				// authoritative, so the MCP boundary surfaces this classified text instead of unwrapping to the
				// raw parser message of the inner exception (ENG-93365).
				throw new NonJsonServiceResponseException(BuildMarkupResponseMessage(methodName, url), rawException);
			}
			string correctedJson = _jsonConverter.CorrectJson(rawResponse);
			try {
				return _jsonConverter.DeserializeObject<TResponse>(correctedJson);
			} catch (Exception correctedException) {
				// Authoritative for the same reason as the markup branch above. The message is built by the
				// shared guard rather than locally: it redacts the parser text and caps the body preview at 200
				// characters, which the previous local Truncate(rawResponse, 1000) did not - that path copied up
				// to a kilobyte of unredacted response body straight into an agent transcript.
				throw new NonJsonServiceResponseException(
					ServiceResponseJsonGuard.BuildNonJsonMessage(methodName, url, rawResponse, correctedException),
					correctedException);
			}
		}
	}

	/// <summary>
	/// Throws when the body is Creatio's answer to an unauthenticated request - the rendered sign-in page or
	/// the JSON 401 fault envelope - so an authentication failure is never reported as, or acted on as,
	/// anything else.
	/// </summary>
	/// <param name="methodName">Designer method the body came from.</param>
	/// <param name="url">Endpoint the body came from.</param>
	/// <param name="rawResponse">Raw response body.</param>
	/// <exception cref="NonJsonServiceResponseException">The body is a session-expired response.</exception>
	private static void ThrowIfSessionExpired(string methodName, string url, string rawResponse) {
		if (!ReauthExecutor.IsSessionExpiredResponse(rawResponse)) {
			return;
		}
		// Same recovery wording as the generic ExecutePostRequest<T> overload in CreatioClientAdapter, which
		// the string-typed overload this client uses does not apply.
		throw new SessionExpiredServiceResponseException(
			$"{methodName} was answered with the Creatio sign-in response instead of JSON (URL: {url}). " +
			"The session expired and the automatic re-authentication did not restore it. Verify the " +
			"environment credentials (for example 'clio reg-web-app --check-login') and retry. " +
			"This response says nothing about the requested schema or package, so do not read it as a " +
			"missing dependency, a missing schema, or a server defect. " +
			"The response body is omitted because a sign-in page can carry session tokens.");
	}

	/// <summary>
	/// Builds the message for a designer body that is markup rather than JSON. It states only what was
	/// observed: the method, the endpoint, and that the body was withheld.
	/// </summary>
	/// <remarks>
	/// It deliberately asserts NO cause. The predecessor of this text named two - a missing package
	/// dependency and "a stale database table left by a previously deleted package" - on the strength of a
	/// purely syntactic "the body starts with &lt;" test, and nothing in this class can distinguish them or
	/// produce evidence for either. The missing-dependency diagnosis, WITH the packages that were actually
	/// found, is added one level up by <c>RemoteEntitySchemaColumnManager.LoadSchema</c>, which has run the
	/// lookup that supports it (issue #722).
	/// </remarks>
	/// <param name="methodName">Designer method the body came from.</param>
	/// <param name="url">Endpoint the body came from.</param>
	/// <returns>The error message to surface.</returns>
	private static string BuildMarkupResponseMessage(string methodName, string url) =>
		$"{methodName} answered with an HTML/XML page instead of JSON (URL: {url}). " +
		"The Creatio server did not produce a service response for this request. " +
		"The response body is omitted from this message because an error or sign-in page can carry session " +
		"tokens. clio has not established WHY the server answered this way and states no cause - check the " +
		"Creatio server log for this endpoint.";

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
}

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
	RuntimeEntitySchemaResponse GetRuntimeEntitySchema(Guid schemaUId, RemoteCommandOptions options);
	IReadOnlyList<SystemValueLookupValueDto> GetSystemValues(Guid dataValueTypeUId, RemoteCommandOptions options);
	IReadOnlyList<SysSettingsSelectQueryRowDto> GetSysSettingsByValueTypeName(
		string valueTypeName,
		RemoteCommandOptions options);
}

internal sealed class RemoteEntitySchemaDesignerClient : IRemoteEntitySchemaDesignerClient
{
	private readonly IApplicationClient _applicationClient;
	private readonly IJsonConverter _jsonConverter;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private const string DesignerServicePath = "ServiceModel/EntitySchemaDesignerService.svc";

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
		string requestBody = request == null ? "{}" : _jsonConverter.SerializeObject(request);
		string rawResponse = _applicationClient.ExecutePostRequest(url, requestBody, options.TimeOut, options.RetryCount,
			options.RetryDelay);
		TResponse response = DeserializeResponse<TResponse>(methodName, rawResponse);
		return EnsureSuccess(response, methodName);
	}

	private TResponse? TryPostToUrl<TRequest, TResponse>(string url, TRequest request, RemoteCommandOptions options,
		string methodName)
		where TRequest : class
		where TResponse : BaseResponse {
		string requestBody = request == null ? "{}" : _jsonConverter.SerializeObject(request);
		string rawResponse = _applicationClient.ExecutePostRequest(url, requestBody, options.TimeOut, options.RetryCount,
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
					$"The Creatio server encountered an unhandled error, possibly due to a stale database table from a previously deleted package. " +
					$"Use find-entity-schema to check whether the schema was partially created before retrying.",
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

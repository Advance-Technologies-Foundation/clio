using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Package;

namespace Clio.Command.EntitySchemaDesigner;

public interface IRemoteEntitySchemaCreator
{
	void Create(CreateEntitySchemaOptions options);
}

internal sealed class RemoteEntitySchemaCreator : IRemoteEntitySchemaCreator
{
	private readonly IApplicationClient _applicationClient;
	private readonly IApplicationPackageListProvider _applicationPackageListProvider;
	private readonly IJsonConverter _jsonConverter;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	private const string EntitySchemaManagerName = "EntitySchemaManager";
	private const string ServiceBasePath = "/ServiceModel/EntitySchemaDesignerService.svc/";
	private const string DefaultCultureName = "en-US";

	private static readonly HashSet<string> _textLikeTypes = new(StringComparer.OrdinalIgnoreCase) {
		"text"
	};

	private static readonly Dictionary<string, int> _supportedDataValueTypes =
		new(StringComparer.OrdinalIgnoreCase) {
			["guid"] = 0,
			["text"] = 1,
			["integer"] = 4,
			["datetime"] = 7,
			["lookup"] = 10,
			["boolean"] = 12,
		};

	public RemoteEntitySchemaCreator(
		IApplicationClient applicationClient,
		IApplicationPackageListProvider applicationPackageListProvider,
		IJsonConverter jsonConverter,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger)
	{
		_applicationClient = applicationClient;
		_applicationPackageListProvider = applicationPackageListProvider;
		_jsonConverter = jsonConverter;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	public void Create(CreateEntitySchemaOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		var package = ResolvePackage(options.Package);
		var parsedColumns = ParseColumns(options.Columns).ToList();
		var createResponse = Post<CreateEntitySchemaRequestDto, DesignerResponse<EntityDesignSchemaDto>>(
			"CreateNewSchema",
			new CreateEntitySchemaRequestDto {
				PackageUId = package.Descriptor.UId,
				ExtendParent = options.ExtendParent
			},
			options);
		var schema = createResponse.Schema ?? throw new InvalidOperationException("CreateNewSchema returned no schema.");
		schema.Package ??= new WorkspacePackageDto();
		schema.Package.UId = package.Descriptor.UId;
		schema.Package.Name = package.Descriptor.Name;
		if (!CheckUniqueSchemaName(options.SchemaName, schema.UId, options)) {
			throw new InvalidOperationException($"Schema '{options.SchemaName}' already exists.");
		}
		if (!string.IsNullOrWhiteSpace(options.ParentSchemaName)) {
			schema = AssignParentSchema(schema, options.ParentSchemaName, package.Descriptor.UId, options);
		}
		ApplySchemaMetadata(schema, options, parsedColumns, package);
		var saveResponse = Post<EntityDesignSchemaDto, SaveDesignItemDesignerResponse>("SaveSchema", schema, options);
		EnsureSuccess(saveResponse, "SaveSchema");
		_logger.WriteInfo($"Entity schema '{options.SchemaName}' created in package '{options.Package}'.");
	}

	private PackageInfo ResolvePackage(string packageName)
	{
		var package = _applicationPackageListProvider
			.GetPackages()
			.FirstOrDefault(p => string.Equals(p.Descriptor.Name, packageName, StringComparison.OrdinalIgnoreCase));
		return package ?? throw new InvalidOperationException($"Package '{packageName}' was not found.");
	}

	private IEnumerable<ParsedColumn> ParseColumns(IEnumerable<string> columnSpecs)
	{
		if (columnSpecs == null) {
			yield break;
		}
		foreach (var columnSpec in columnSpecs.Where(spec => !string.IsNullOrWhiteSpace(spec))) {
			var parts = columnSpec.Split(':');
			if (parts.Length < 2 || parts.Length > 4) {
				throw new InvalidOperationException(
					$"Column '{columnSpec}' has invalid format. Expected <name>:<type>[:<title>[:<refSchema>]].");
			}
			var name = parts[0].Trim();
			var type = parts[1].Trim();
			if (string.IsNullOrWhiteSpace(name) || !_supportedDataValueTypes.ContainsKey(type)) {
				throw new InvalidOperationException(
					$"Column '{columnSpec}' has unsupported values. Supported types: {string.Join(", ", _supportedDataValueTypes.Keys.OrderBy(k => k))}.");
			}
			var title = parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2].Trim() : name;
			var referenceSchemaName = parts.Length == 4 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3].Trim() : null;
			if (string.Equals(type, "lookup", StringComparison.OrdinalIgnoreCase) &&
				string.IsNullOrWhiteSpace(referenceSchemaName)) {
				throw new InvalidOperationException(
					$"Lookup column '{columnSpec}' must specify a reference schema name.");
			}
			yield return new ParsedColumn(name, type, title, referenceSchemaName);
		}
	}

	private bool CheckUniqueSchemaName(string schemaName, Guid excludeUId, CreateEntitySchemaOptions options)
	{
		var response = Post<object, BoolResponse>("CheckUniqueSchemaName", new {
			managerName = EntitySchemaManagerName,
			schemaName,
			excludeUId
		}, options);
		EnsureSuccess(response, "CheckUniqueSchemaName");
		return response.Value;
	}

	private EntityDesignSchemaDto AssignParentSchema(
		EntityDesignSchemaDto schema,
		string parentSchemaName,
		Guid packageUId,
		CreateEntitySchemaOptions options)
	{
		var parentResponse = Post<GetAvailableSchemasRequestDto, AvailableEntitySchemasResponse>(
			"GetAvailableParentSchemas",
			new GetAvailableSchemasRequestDto {
				PackageUId = packageUId,
				UseFullHierarchy = false
			},
			options);
		var parent = parentResponse.Items?.FirstOrDefault(item =>
			string.Equals(item.Name, parentSchemaName, StringComparison.OrdinalIgnoreCase));
		if (parent == null) {
			throw new InvalidOperationException(
				$"Parent schema '{parentSchemaName}' is not available for package '{schema.Package?.Name ?? packageUId.ToString()}'.");
		}
		var response = Post<AssignParentSchemaRequestDto<EntityDesignSchemaDto>, DesignerResponse<EntityDesignSchemaDto>>(
			"AssignParentSchema",
			new AssignParentSchemaRequestDto<EntityDesignSchemaDto> {
				DesignSchema = schema,
				ParentSchemaUId = parent.UId,
				UseFullHierarchy = false
			},
			options);
		return response.Schema ?? throw new InvalidOperationException("AssignParentSchema returned no schema.");
	}

	private void ApplySchemaMetadata(
		EntityDesignSchemaDto schema,
		CreateEntitySchemaOptions options,
		IReadOnlyCollection<ParsedColumn> parsedColumns,
		PackageInfo package)
	{
		var cultureName = CultureInfo.CurrentCulture.Name;
		schema.Name = options.SchemaName;
		schema.Caption = [
			new LocalizableStringDto {
				CultureName = string.IsNullOrWhiteSpace(cultureName) ? DefaultCultureName : cultureName,
				Value = options.Title
			}
		];
		schema.Package ??= new WorkspacePackageDto();
		schema.Package.UId = package.Descriptor.UId;
		schema.Package.Name = package.Descriptor.Name;
		schema.Columns ??= [];
		schema.Indexes ??= [];
		schema.InheritedColumns ??= [];

		var referenceSchemas = parsedColumns.Any(c => c.IsLookup)
			? GetReferenceSchemas(package.Descriptor.UId, options)
			: new Dictionary<string, ManagerItemDto>(StringComparer.OrdinalIgnoreCase);
		var columns = schema.Columns.ToList();
		foreach (var parsedColumn in parsedColumns) {
			columns.Add(CreateColumn(parsedColumn, referenceSchemas, cultureName));
		}
		if (!schema.ParentSchema.HasValue() && columns.All(column => !column.IsGuidType())) {
			columns.Insert(0, CreateColumn(new ParsedColumn("Id", "guid", "Id", null), referenceSchemas, cultureName));
		}
		schema.Columns = columns;
		schema.PrimaryColumn = columns.FirstOrDefault(column => column.IsGuidType()) ?? schema.PrimaryColumn;
		if (schema.PrimaryDisplayColumn == null) {
			schema.PrimaryDisplayColumn = columns.FirstOrDefault(column => column.IsTextType());
		}
	}

	private Dictionary<string, ManagerItemDto> GetReferenceSchemas(Guid packageUId, CreateEntitySchemaOptions options)
	{
		var response = Post<GetAvailableSchemasRequestDto, AvailableEntitySchemasResponse>(
			"GetAvailableReferenceSchemas",
			new GetAvailableSchemasRequestDto {
				PackageUId = packageUId,
				UseFullHierarchy = false,
				AllowVirtual = false
			},
			options);
		return response.Items?
			.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
			?? new Dictionary<string, ManagerItemDto>(StringComparer.OrdinalIgnoreCase);
	}

	private EntitySchemaColumnDto CreateColumn(
		ParsedColumn parsedColumn,
		IReadOnlyDictionary<string, ManagerItemDto> referenceSchemas,
		string cultureName)
	{
		var column = new EntitySchemaColumnDto {
			UId = Guid.NewGuid(),
			Name = parsedColumn.Name,
			DataValueType = _supportedDataValueTypes[parsedColumn.Type],
			Caption = [
				new LocalizableStringDto {
					CultureName = string.IsNullOrWhiteSpace(cultureName) ? DefaultCultureName : cultureName,
					Value = parsedColumn.Title
				}
			]
		};
		if (parsedColumn.IsLookup) {
			if (!referenceSchemas.TryGetValue(parsedColumn.ReferenceSchemaName!, out var referenceSchema)) {
				throw new InvalidOperationException(
					$"Reference schema '{parsedColumn.ReferenceSchemaName}' was not found for lookup column '{parsedColumn.Name}'.");
			}
			column.ReferenceSchema = new EntityDesignSchemaDto {
				UId = referenceSchema.UId,
				Name = referenceSchema.Name,
				Caption = [
					new LocalizableStringDto {
						CultureName = string.IsNullOrWhiteSpace(cultureName) ? DefaultCultureName : cultureName,
						Value = referenceSchema.Caption
					}
				]
			};
		}
		return column;
	}

	private TResponse Post<TRequest, TResponse>(string methodName, TRequest request, CreateEntitySchemaOptions options)
		where TResponse : BaseResponse
	{
		var url = _serviceUrlBuilder.Build(ServiceBasePath + methodName);
		var requestBody = request == null ? "{}" : _jsonConverter.SerializeObject(request);
		var rawResponse = _applicationClient.ExecutePostRequest(
			url,
			requestBody,
			options.TimeOut,
			options.RetryCount,
			options.RetryDelay);
		var correctedJson = _jsonConverter.CorrectJson(rawResponse);
		TResponse response;
		try {
			response = _jsonConverter.DeserializeObject<TResponse>(correctedJson);
		} catch (Exception exception) {
			throw new InvalidOperationException(
				$"{methodName} returned invalid JSON: {Truncate(rawResponse, 1000)}",
				exception);
		}
		return EnsureSuccess(response, methodName);
	}

	private static TResponse EnsureSuccess<TResponse>(TResponse response, string methodName)
		where TResponse : BaseResponse
	{
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

	private sealed record ParsedColumn(string Name, string Type, string Title, string? ReferenceSchemaName)
	{
		public bool IsLookup => string.Equals(Type, "lookup", StringComparison.OrdinalIgnoreCase);
	}

	private static string Truncate(string value, int maxLength) =>
		string.IsNullOrEmpty(value) || value.Length <= maxLength
			? value
			: value[..maxLength];
}

internal static class EntitySchemaDesignerExtensions
{
	public static bool HasValue(this EntityDesignSchemaDto? schema) =>
		schema != null && schema.UId != Guid.Empty;

	public static bool IsGuidType(this EntitySchemaColumnDto column) => column.DataValueType == 0;

	public static bool IsTextType(this EntitySchemaColumnDto column) =>
		column.DataValueType == 1;
}

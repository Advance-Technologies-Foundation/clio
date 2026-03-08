using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Package;

namespace Clio.Command.EntitySchemaDesigner;

public interface IRemoteEntitySchemaCreator{
	#region Methods: Public

	void Create(CreateEntitySchemaOptions options);

	#endregion
}

internal sealed class RemoteEntitySchemaCreator : IRemoteEntitySchemaCreator{
	#region Constants: Private

	private const string DefaultCultureName = "en-US";

	private const string EntitySchemaManagerName = "EntitySchemaManager";
	private const string ServiceBasePath = "/ServiceModel/EntitySchemaDesignerService.svc/";

	#endregion

	#region Fields: Private

	private readonly IApplicationClient _applicationClient;
	private readonly IApplicationPackageListProvider _applicationPackageListProvider;
	private readonly IJsonConverter _jsonConverter;
	private readonly ILogger _logger;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	#endregion

	#region Class: Nested

	private sealed record ParsedColumn(string Name, string Type, string Title, string? ReferenceSchemaName){
		public bool IsLookup => string.Equals(Type, "lookup", StringComparison.OrdinalIgnoreCase);
	}

	#endregion

	private static readonly Dictionary<string, int> SupportedDataValueTypes =
		new(StringComparer.OrdinalIgnoreCase) {
			["guid"] = 0,
			["text"] = 1,
			["integer"] = 4,
			["datetime"] = 7,
			["lookup"] = 10,
			["boolean"] = 12
		};

	#region Constructors: Public

	public RemoteEntitySchemaCreator(
		IApplicationClient applicationClient,
		IApplicationPackageListProvider applicationPackageListProvider,
		IJsonConverter jsonConverter,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_applicationPackageListProvider = applicationPackageListProvider;
		_jsonConverter = jsonConverter;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	#endregion

	#region Methods: Private

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

	private void ApplySchemaMetadata(
		EntityDesignSchemaDto schema,
		CreateEntitySchemaOptions options,
		IReadOnlyCollection<ParsedColumn> parsedColumns,
		PackageInfo package) {
		string cultureName = CultureInfo.CurrentCulture.Name;
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

		Dictionary<string, ManagerItemDto> referenceSchemas = parsedColumns.Any(c => c.IsLookup)
			? GetReferenceSchemas(package.Descriptor.UId, options)
			: new Dictionary<string, ManagerItemDto>(StringComparer.OrdinalIgnoreCase);
		List<EntitySchemaColumnDto> columns = schema.Columns.ToList();
		foreach (ParsedColumn parsedColumn in parsedColumns) {
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

	private EntityDesignSchemaDto AssignParentSchema(
		EntityDesignSchemaDto schema,
		string parentSchemaName,
		Guid packageUId,
		CreateEntitySchemaOptions options) {
		AvailableEntitySchemasResponse parentResponse
			= Post<GetAvailableSchemasRequestDto, AvailableEntitySchemasResponse>(
				"GetAvailableParentSchemas",
				new GetAvailableSchemasRequestDto {
					PackageUId = packageUId,
					UseFullHierarchy = false
				},
				options);
		ManagerItemDto parent = parentResponse.Items?.FirstOrDefault(item =>
			string.Equals(item.Name, parentSchemaName, StringComparison.OrdinalIgnoreCase));
		if (parent == null) {
			throw new InvalidOperationException(
				$"Parent schema '{parentSchemaName}' is not available for package '{schema.Package?.Name ?? packageUId.ToString()}'.");
		}

		DesignerResponse<EntityDesignSchemaDto> response
			= Post<AssignParentSchemaRequestDto<EntityDesignSchemaDto>, DesignerResponse<EntityDesignSchemaDto>>(
				"AssignParentSchema",
				new AssignParentSchemaRequestDto<EntityDesignSchemaDto> {
					DesignSchema = schema,
					ParentSchemaUId = parent.UId,
					UseFullHierarchy = false
				},
				options);
		return response.Schema ?? throw new InvalidOperationException("AssignParentSchema returned no schema.");
	}

	private bool CheckUniqueSchemaName(string schemaName, Guid excludeUId, CreateEntitySchemaOptions options) {
		BoolResponse response = Post<object, BoolResponse>("CheckUniqueSchemaName", new {
			managerName = EntitySchemaManagerName,
			schemaName,
			excludeUId
		}, options);
		EnsureSuccess(response, "CheckUniqueSchemaName");
		return response.Value;
	}

	private EntitySchemaColumnDto CreateColumn(
		ParsedColumn parsedColumn,
		IReadOnlyDictionary<string, ManagerItemDto> referenceSchemas,
		string cultureName) {
		EntitySchemaColumnDto column = new() {
			UId = Guid.NewGuid(),
			Name = parsedColumn.Name,
			DataValueType = SupportedDataValueTypes[parsedColumn.Type],
			Caption = [
				new LocalizableStringDto {
					CultureName = string.IsNullOrWhiteSpace(cultureName) ? DefaultCultureName : cultureName,
					Value = parsedColumn.Title
				}
			]
		};
		if (parsedColumn.IsLookup) {
			if (!referenceSchemas.TryGetValue(parsedColumn.ReferenceSchemaName!, out ManagerItemDto referenceSchema)) {
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

	private Dictionary<string, ManagerItemDto> GetReferenceSchemas(Guid packageUId, CreateEntitySchemaOptions options) {
		AvailableEntitySchemasResponse response = Post<GetAvailableSchemasRequestDto, AvailableEntitySchemasResponse>(
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

	private IEnumerable<ParsedColumn> ParseColumns(IEnumerable<string> columnSpecs) {
		if (columnSpecs == null) {
			yield break;
		}

		foreach (string columnSpec in columnSpecs.Where(spec => !string.IsNullOrWhiteSpace(spec))) {
			string[] parts = columnSpec.Split(':');
			if (parts.Length < 2 || parts.Length > 4) {
				throw new InvalidOperationException(
					$"Column '{columnSpec}' has invalid format. Expected <name>:<type>[:<title>[:<refSchema>]].");
			}

			string name = parts[0].Trim();
			string type = parts[1].Trim();
			if (string.IsNullOrWhiteSpace(name) || !SupportedDataValueTypes.ContainsKey(type)) {
				throw new InvalidOperationException(
					$"Column '{columnSpec}' has unsupported values. Supported types: {string.Join(", ", SupportedDataValueTypes.Keys.OrderBy(k => k))}.");
			}

			string title = parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2].Trim() : name;
			string referenceSchemaName
				= parts.Length == 4 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3].Trim() : null;
			if (string.Equals(type, "lookup", StringComparison.OrdinalIgnoreCase) &&
				string.IsNullOrWhiteSpace(referenceSchemaName)) {
				throw new InvalidOperationException(
					$"Lookup column '{columnSpec}' must specify a reference schema name.");
			}

			yield return new ParsedColumn(name, type, title, referenceSchemaName);
		}
	}

	private TResponse Post<TRequest, TResponse>(string methodName, TRequest request, CreateEntitySchemaOptions options)
		where TRequest : class
		where TResponse : BaseResponse {
		string url = _serviceUrlBuilder.Build(ServiceBasePath + methodName);
		string requestBody = request == null ? "{}" : _jsonConverter.SerializeObject(request);
		string rawResponse = _applicationClient.ExecutePostRequest(
			url,
			requestBody,
			options.TimeOut,
			options.RetryCount,
			options.RetryDelay);
		string correctedJson = _jsonConverter.CorrectJson(rawResponse);
		TResponse response;
		try {
			response = _jsonConverter.DeserializeObject<TResponse>(correctedJson);
		}
		catch (Exception exception) {
			throw new InvalidOperationException(
				$"{methodName} returned invalid JSON: {Truncate(rawResponse, 1000)}",
				exception);
		}

		return EnsureSuccess(response, methodName);
	}

	private PackageInfo ResolvePackage(string packageName) {
		PackageInfo package = _applicationPackageListProvider
							  .GetPackages()
							  .FirstOrDefault(p =>
								  string.Equals(p.Descriptor.Name, packageName, StringComparison.OrdinalIgnoreCase));
		return package ?? throw new InvalidOperationException($"Package '{packageName}' was not found.");
	}

	#endregion

	#region Methods: Public

	public void Create(CreateEntitySchemaOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		PackageInfo package = ResolvePackage(options.Package);
		List<ParsedColumn> parsedColumns = ParseColumns(options.Columns).ToList();
		DesignerResponse<EntityDesignSchemaDto> createResponse
			= Post<CreateEntitySchemaRequestDto, DesignerResponse<EntityDesignSchemaDto>>(
				"CreateNewSchema",
				new CreateEntitySchemaRequestDto {
					PackageUId = package.Descriptor.UId,
					ExtendParent = options.ExtendParent
				},
				options);
		EntityDesignSchemaDto schema = createResponse.Schema ??
									   throw new InvalidOperationException("CreateNewSchema returned no schema.");
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
		SaveDesignItemDesignerResponse saveResponse
			= Post<EntityDesignSchemaDto, SaveDesignItemDesignerResponse>("SaveSchema", schema, options);
		EnsureSuccess(saveResponse, "SaveSchema");
		_logger.WriteInfo($"Entity schema '{options.SchemaName}' created in package '{options.Package}'.");
	}

	#endregion
}

internal static class EntitySchemaDesignerExtensions{
	#region Methods: Public

	public static bool HasValue(this EntityDesignSchemaDto? schema) {
		return schema != null && schema.UId != Guid.Empty;
	}

	public static bool IsGuidType(this EntitySchemaColumnDto column) {
		return column.DataValueType == 0;
	}

	public static bool IsTextType(this EntitySchemaColumnDto column) {
		return column.DataValueType == 1;
	}

	#endregion
}

using System;
using System.Collections.Generic;
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
	#region Fields: Private

	private readonly IApplicationPackageListProvider _applicationPackageListProvider;
	private readonly IRemoteEntitySchemaDesignerClient _entitySchemaDesignerClient;
	private readonly ILogger _logger;

	#endregion

	#region Class: Nested

	private sealed record ParsedColumn(string Name, string Type, string Title, string? ReferenceSchemaName){
		public bool IsLookup => string.Equals(Type, "lookup", StringComparison.OrdinalIgnoreCase);
	}

	#endregion

	#region Constructors: Public

	public RemoteEntitySchemaCreator(
		IApplicationPackageListProvider applicationPackageListProvider,
		IRemoteEntitySchemaDesignerClient entitySchemaDesignerClient,
		ILogger logger) {
		_applicationPackageListProvider = applicationPackageListProvider;
		_entitySchemaDesignerClient = entitySchemaDesignerClient;
		_logger = logger;
	}

	#endregion

	#region Methods: Private

	private void ApplySchemaMetadata(
		EntityDesignSchemaDto schema,
		CreateEntitySchemaOptions options,
		IReadOnlyCollection<ParsedColumn> parsedColumns,
		PackageInfo package) {
		string cultureName = EntitySchemaDesignerSupport.GetCurrentCultureName();
		schema.Name = options.SchemaName;
		schema.Caption = [
			new LocalizableStringDto {
				CultureName = cultureName,
				Value = options.Title
			}
		];
		EntitySchemaDesignerSupport.EnsurePackageAssigned(schema, package);
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
		schema.PrimaryDisplayColumn ??= columns.FirstOrDefault(column => column.IsTextType());
	}

	private EntityDesignSchemaDto AssignParentSchema(
		EntityDesignSchemaDto schema,
		string parentSchemaName,
		Guid packageUId,
		CreateEntitySchemaOptions options) {
		AvailableEntitySchemasResponse parentResponse = _entitySchemaDesignerClient.GetAvailableParentSchemas(
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

		DesignerResponse<EntityDesignSchemaDto> response = _entitySchemaDesignerClient.AssignParentSchema(
			new AssignParentSchemaRequestDto<EntityDesignSchemaDto> {
				DesignSchema = schema,
				ParentSchemaUId = parent.UId,
				UseFullHierarchy = false
			},
			options);
		return response.Schema ?? throw new InvalidOperationException("AssignParentSchema returned no schema.");
	}

	private bool CheckUniqueSchemaName(string schemaName, Guid excludeUId, CreateEntitySchemaOptions options) {
		BoolResponse response = _entitySchemaDesignerClient.CheckUniqueSchemaName(
			EntitySchemaDesignerSupport.EntitySchemaManagerName,
			schemaName,
			excludeUId,
			options);
		return response.Value;
	}

	private EntitySchemaColumnDto CreateColumn(
		ParsedColumn parsedColumn,
		IReadOnlyDictionary<string, ManagerItemDto> referenceSchemas,
		string cultureName) {
		EntitySchemaColumnDto column = new() {
			UId = Guid.NewGuid(),
			Name = parsedColumn.Name,
			DataValueType = EntitySchemaDesignerSupport.SupportedDataValueTypes[parsedColumn.Type],
			Caption = [
				new LocalizableStringDto {
					CultureName = cultureName,
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
						CultureName = cultureName,
						Value = referenceSchema.Caption
					}
				]
			};
		}

		return column;
	}

	private Dictionary<string, ManagerItemDto> GetReferenceSchemas(Guid packageUId, CreateEntitySchemaOptions options) {
		AvailableEntitySchemasResponse response = _entitySchemaDesignerClient.GetAvailableReferenceSchemas(
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
			yield return ParseColumn(columnSpec);
		}
	}

	private static string GetLookupReferenceSchemaName(string[] parts) {
		return parts.Length == 4 && !string.IsNullOrWhiteSpace(parts[3])
			? parts[3].Trim()
			: null;
	}

	private static string GetSupportedTypesList() {
		return EntitySchemaDesignerSupport.GetSupportedTypesList();
	}

	private static string GetTitle(string[] parts, string name) {
		return parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2])
			? parts[2].Trim()
			: name;
	}

	private static void ValidateColumnFormat(string columnSpec, string[] parts) {
		if (parts.Length is < 2 or > 4) {
			throw new InvalidOperationException(
				$"Column '{columnSpec}' has invalid format. Expected <name>:<type>[:<title>[:<refSchema>]].");
		}
	}

	private static void ValidateLookupReferenceSchema(string columnSpec, string type, string referenceSchemaName) {
		bool isLookup = string.Equals(type, "lookup", StringComparison.OrdinalIgnoreCase);
		if (isLookup && string.IsNullOrWhiteSpace(referenceSchemaName)) {
			throw new InvalidOperationException(
				$"Lookup column '{columnSpec}' must specify a reference schema name.");
		}

		if (!isLookup && !string.IsNullOrWhiteSpace(referenceSchemaName)) {
			throw new InvalidOperationException(
				$"Column '{columnSpec}' can specify a reference schema name only for lookup columns.");
		}
	}

	private static void ValidateSupportedColumnValues(string columnSpec, string name, string type) {
		if (string.IsNullOrWhiteSpace(name)
			|| !EntitySchemaDesignerSupport.SupportedDataValueTypes.ContainsKey(type)) {
			throw new InvalidOperationException(
				$"Column '{columnSpec}' has unsupported values. Supported types: {GetSupportedTypesList()}.");
		}
	}

	private ParsedColumn ParseColumn(string columnSpec) {
		string[] parts = columnSpec.Split(':');
		ValidateColumnFormat(columnSpec, parts);

		string name = parts[0].Trim();
		string type = parts[1].Trim();
		ValidateSupportedColumnValues(columnSpec, name, type);

		string title = GetTitle(parts, name);
		string referenceSchemaName = GetLookupReferenceSchemaName(parts);
		ValidateLookupReferenceSchema(columnSpec, type, referenceSchemaName);

		return new ParsedColumn(name, type, title, referenceSchemaName);
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
		DesignerResponse<EntityDesignSchemaDto> createResponse = _entitySchemaDesignerClient.CreateNewSchema(
			new CreateEntitySchemaRequestDto {
				PackageUId = package.Descriptor.UId,
				ExtendParent = options.ExtendParent
			},
			options);
		EntityDesignSchemaDto schema = createResponse.Schema ??
									   throw new InvalidOperationException("CreateNewSchema returned no schema.");
		EntitySchemaDesignerSupport.EnsurePackageAssigned(schema, package);
		if (!CheckUniqueSchemaName(options.SchemaName, schema.UId, options)) {
			throw new InvalidOperationException($"Schema '{options.SchemaName}' already exists.");
		}

		if (!string.IsNullOrWhiteSpace(options.ParentSchemaName)) {
			schema = AssignParentSchema(schema, options.ParentSchemaName, package.Descriptor.UId, options);
		}

		ApplySchemaMetadata(schema, options, parsedColumns, package);
		_entitySchemaDesignerClient.SaveSchema(schema, options);
		_logger.WriteInfo($"Entity schema '{options.SchemaName}' created in package '{options.Package}'.");
	}

	#endregion
}

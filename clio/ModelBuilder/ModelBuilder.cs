using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using Clio.Dto;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.ModelBuilder;

internal interface IModelBuilder{
	#region Methods: Public

	void GetModels(AddItemOptions opts);

	#endregion
}

internal class ModelBuilder : IModelBuilder{
	#region Fields: Private

	private readonly IApplicationClient _applicationClient;
	private readonly Dictionary<string, List<DetailConnection>> _detailConnectionsByMasterSchema = new();
	private readonly Dictionary<string, Schema> _schemas = new();
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private AddItemOptions _opts;

	#endregion

	#region Constructors: Public

	public ModelBuilder(IApplicationClient applicationClient,
		IWorkingDirectoriesProvider workingDirectoriesProvider, IServiceUrlBuilder serviceUrlBuilder) {
		_applicationClient = applicationClient;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	#endregion

	#region Properties: Private

	private string EntitySchemaManagerRequestUrl =>
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.EntitySchemaManagerRequest);

	private string RuntimeEntitySchemaRequestUrl =>
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest);

	#endregion

	#region Methods: Private

	private string CreateClassFileText(KeyValuePair<string, Schema> schema) {
		List<DetailConnection> detailConnections = GetDetailConnections(schema.Key);
		StringBuilder sb = new();
		sb.AppendLine("#pragma warning disable CS8618, // Non-nullable field is uninitialized.")
		  .AppendLine()
		  .AppendLine("using ATF.Repository;")
		  .AppendLine("using ATF.Repository.Attributes;")
		  .AppendLine("using System.Diagnostics.CodeAnalysis;")
		  .AppendLine()
		  .Append("namespace ").Append(_opts.Namespace).Append(" {").AppendLine();

		if (!string.IsNullOrEmpty(schema.Value.Description)) {
			List<string> commentLines = schema.Value.Description.Split("\n").ToList();

			sb.Append('\t').AppendLine("/// <summary>");
			commentLines.ForEach(l => sb.Append('\t').Append("/// ").AppendLine(l));
			sb.Append('\t').AppendLine("/// </summary>");
		}

		sb.Append('\t').AppendLine("[ExcludeFromCodeCoverage]");
		sb.Append('\t').Append("[Schema(\"").Append(schema.Value.Name).AppendLine("\")]")
		  .Append('\t').Append("public class ").Append(schema.Value.Name).AppendLine(": BaseModel {")
		  .AppendLine();
		
		sb.AppendLine("\t\t#region Schema Columns").AppendLine();
		
		foreach (KeyValuePair<string, Column> column in schema.Value.Columns) {
			if (column.Key == "Id") {
				continue;
			}

			if (!string.IsNullOrEmpty(column.Value.Description)) {
				sb
					.Append("\t\t").AppendLine("/// <summary>")
					.Append("\t\t").Append("/// ").AppendLine(column.Value.Description)
					.Append("\t\t").AppendLine("/// </summary>");
			}

			if (column.Value.DataValueType == 13) {
				continue; //byte[] properties are not supported in the current implementation of ATF Repository.
			}
			sb.Append("\t\t").Append("[SchemaProperty(\"").Append(column.Value.Name).AppendLine("\")]");
			if (string.IsNullOrEmpty(column.Value.ReferenceSchemaName)) {

				//Some models have ColumnName "IsNew" which is an already define property in the BaseModel.
				if (string.Equals(column.Value.Name, "IsNew", StringComparison.Ordinal)) {
					sb.Append("\t\t").Append("public new ").Append(GetTypeFromDataValueType(column.Value.DataValueType))
					  .Append(' ').Append(column.Value.Name).AppendLine(" { get; set; }");
				}
				else {
					sb.Append("\t\t").Append("public ").Append(GetTypeFromDataValueType(column.Value.DataValueType))
					  .Append(' ').Append(column.Value.Name).AppendLine(" { get; set; }");
				}
				
			}
			else {
				sb.Append("\t\t").Append("public ").Append(GetTypeFromDataValueType(column.Value.DataValueType))
				  .Append(' ').Append(column.Value.Name).Append("Id").AppendLine(" { get; set; }");
				sb.AppendLine();

				if (!string.IsNullOrEmpty(column.Value.Description)) {
					sb.Append("\t\t").Append("/// <inheritdoc cref=\"").Append(column.Value.Name).Append("Id")
					  .AppendLine("\"/>");
				}

				sb.Append("\t\t").Append("[LookupProperty(\"").Append(column.Value.Name).AppendLine("\")]");
				sb.Append("\t\t").Append("public virtual ").Append(column.Value.ReferenceSchemaName).Append(' ')
				  .Append(column.Value.Name).AppendLine(" { get; set; }");
			}

			sb.AppendLine();
		}

		sb.AppendLine("\t\t#endregion").AppendLine();

		if (detailConnections.Count > 0) {
			sb.AppendLine("\t\t#region Details");
			sb.AppendLine();

			foreach (DetailConnection detailConnection in detailConnections) {
				
				sb.Append("\t\t").AppendLine("/// <summary>")
							   .Append("\t\t").Append("/// ").AppendLine($"Collection of {detailConnection.DetailSchemaName} by column {detailConnection.DetailSchemaPropertyName}")
							   .Append("\t\t").AppendLine("/// <remarks>")
							   .Append("\t\t").AppendLine($"/// <see cref=\"global::{_opts.Namespace}.{detailConnection.DetailSchemaName}\">See more about {detailConnection.DetailSchemaName} model</see>")
							   .Append("\t\t").AppendLine("/// </remarks>")
							   .Append("\t\t").AppendLine("/// </summary>");
				sb.Append("\t\t[DetailProperty(nameof(global::").Append(_opts.Namespace).Append('.')
					.Append(detailConnection.DetailSchemaName).Append('.')
					.Append(detailConnection.DetailSchemaPropertyName).AppendLine("))]");
				sb.Append("\t\tpublic virtual List<").Append(detailConnection.DetailSchemaName).Append("> ")
					.Append(detailConnection.DetailPropertyName).AppendLine(" { get; set; }");
				sb.AppendLine();
			}
			sb.AppendLine("\t\t#endregion");
		}

		sb.AppendLine("\t}");
		sb.AppendLine("}");
		sb.AppendLine("#pragma warning restore CS8618 // Non-nullable field is uninitialized.");
		return sb.ToString();
	}

	private void GetEntitySchemasAsync() {
		string responseJson = _applicationClient.ExecutePostRequest(EntitySchemaManagerRequestUrl, string.Empty);

		JsonSerializerSettings settings = new() {
			NullValueHandling = NullValueHandling.Ignore
		};

		EntitySchemaResponse col = JsonConvert.DeserializeObject<EntitySchemaResponse>(responseJson, settings);
		foreach (EntitySchema item in col.Collection.Where(item => !_schemas.ContainsKey(item.Name))) {
			_schemas.Add(item.Name, new Schema {
				Name = item.Name
			});
		}
	}

	private void GetRuntimeEntitySchema(KeyValuePair<string, Schema> schema) {
		string definition
			= _applicationClient.ExecutePostRequest(RuntimeEntitySchemaRequestUrl,
				$"{{\"Name\" : \"{schema.Key}\"}}");

		JToken jt = JToken.Parse(definition);
		JToken items = jt.SelectToken("$.schema.columns.Items");

		if (items == null) {
			Console.WriteLine($"Schema {schema.Key} not found");
			return;
		}

		schema.Value.Description = jt.SelectToken($"$.schema.description.{_opts.Culture}")?.ToString();
		Dictionary<Guid, JObject> columns = items.ToObject<Dictionary<Guid, JObject>>();

		foreach (KeyValuePair<Guid, JObject> item in columns) {
			Column col = new() {
				Name = (string)item.Value.GetValue("name"),
				DataValueType = (int)item.Value.GetValue("dataValueType")
			};
			if (item.Value.TryGetValue("referenceSchemaName", out JToken value)) {
				col.ReferenceSchemaName = (string)value;
			}

			col.Description = item.Value.SelectToken($"$.description.{_opts.Culture}")?.ToString();
			schema.Value.Columns.Add(col.Name, col);
		}
	}

	private static string GetTypeFromDataValueType(int dataValueType) {
		return dataValueType switch {
				   0 => nameof(Guid),
				   1 => "string",
				   4 => "int",
				   5 => nameof(Single),
				   6 => "decimal",
				   7 => nameof(DateTime),
				   8 => nameof(DateTime),
				   9 => nameof(DateTime),
				   10 => nameof(Guid),
				   11 => nameof(Guid),
				   12 => "bool",
				   13 => "byte[]",

				   //case 14: return nameof(byte[]); what is IMAGE
				   //CUSTOM_OBJECT	15
				   //IMAGELOOKUP	16
				   //COLLECTION	17
				   //COLOR	18
				   //LOCALIZABLE_STRING	19
				   //ENTITY	20
				   //ENTITY_COLLECTION	21
				   //ENTITY_COLUMN_MAPPING_COLLECTION	22
				   23 => nameof(String),
				   24 => nameof(String),

				   //case 25: return nameof(String); FILE	25
				   //MAPPING	26
				   27 => "string",
				   28 => "string",
				   29 => "string",
				   30 => "string",
				   31 => "decimal",
				   32 => "decimal",
				   33 => "decimal",
				   34 => "decimal",

				   //LOCALIZABLE_PARAMETER_VALUES_LIST	35
				   //METADATA_TEXT	36
				   //STAGE_INDICATOR	37
				   //OBJECT_LIST	38
				   //COMPOSITE_OBJECT_LIST	39
				   40 => "decimal",

				   //FILE_LOCATOR	41
				   42 => "string",
				   var _ => "string"
			   };
	}

	private void BuildDetailConnections() {
		_detailConnectionsByMasterSchema.Clear();

		foreach (Schema detailSchema in _schemas.Values) {
			foreach (Column lookupColumn in detailSchema.Columns.Values.Where(column =>
				         !string.IsNullOrWhiteSpace(column.ReferenceSchemaName))) {
				if (lookupColumn.Name == "Id") {
					continue;
				}

				string masterSchemaName = lookupColumn.ReferenceSchemaName;
				if (!_schemas.ContainsKey(masterSchemaName)) {
					continue;
				}

				string detailSchemaPropertyName = $"{lookupColumn.Name}Id";
				string detailPropertyName = $"CollectionOf{detailSchema.Name}By{lookupColumn.Name}";

				if (_detailConnectionsByMasterSchema.TryGetValue(masterSchemaName, out List<DetailConnection> existingConnections)) {
					if (existingConnections.Any(connection =>
						    connection.DetailSchemaName == detailSchema.Name
						    && connection.DetailSchemaPropertyName == detailSchemaPropertyName)) {
						continue;
					}
				}

				DetailConnection detailConnection = new() {
					DetailSchemaName = detailSchema.Name,
					DetailSchemaPropertyName = detailSchemaPropertyName,
					DetailPropertyName = detailPropertyName
				};

				if (!_detailConnectionsByMasterSchema.TryGetValue(masterSchemaName, out List<DetailConnection> value)) {
                    value = [];
                    _detailConnectionsByMasterSchema.Add(masterSchemaName, value);
				}

                value.Add(detailConnection);
			}
		}
	}

	private List<DetailConnection> GetDetailConnections(string schemaName) {
		return _detailConnectionsByMasterSchema.TryGetValue(schemaName, out List<DetailConnection> detailConnections)
			? detailConnections.OrderBy(connection => connection.DetailPropertyName).ToList()
			: [];
	}

	#endregion

	#region Methods: Public

	public void GetModels(AddItemOptions opts) {
		_opts = opts;
		GetEntitySchemasAsync();

		Parallel.ForEach(_schemas, new ParallelOptions { MaxDegreeOfParallelism = 4 },
			GetRuntimeEntitySchema);
		BuildDetailConnections();
		if (string.IsNullOrWhiteSpace(_opts.DestinationPath)) {
			_opts.DestinationPath = _workingDirectoriesProvider.CurrentDirectory;
		}

		Console.WriteLine($"Models will be generated in directory: {_opts.DestinationPath}");
		DirectoryInfo di = new(_opts.DestinationPath);
		if (!di.Exists) {
			di.Create();
		}

		int i = 0;
		foreach (KeyValuePair<string, Schema> schema in _schemas) {
			string filePath = Path.Combine(_opts.DestinationPath, schema.Key + ".cs");
			File.WriteAllText(filePath, CreateClassFileText(schema));
			Console.Write($"Generated: {++i} models from {_schemas.Count}\r");
		}

		Console.WriteLine();
	}

	#endregion
}

public class DetailConnection{
	#region Properties: Public

	public string DetailSchemaName { get; set; }
	public string DetailSchemaPropertyName { get; set; }
	public string DetailPropertyName { get; set; }

	#endregion
}

public class Column{
	#region Properties: Public

	public string Name { get; set; }
	public int DataValueType { get; set; }
	public string ReferenceSchemaName { get; set; }
	public string Description { get; set; }

	#endregion
}

public class Schema{
	#region Constructors: Public

	public Schema() {
		Columns = new Dictionary<string, Column>();
	}

	#endregion

	#region Properties: Public

	public string Name { get; set; }
	public string Description { get; set; }
	public Dictionary<string, Column> Columns { get; set; }

	#endregion
}

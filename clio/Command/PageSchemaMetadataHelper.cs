namespace Clio.Command {
	using Clio.Common;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	internal static class PageSchemaMetadataHelper {
		private const string ExpressionTypeKey = "expressionType";
		private const string ColumnPathKey = "columnPath";

		internal static (JToken row, string error) QuerySysSchemaRow(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			string schemaName,
			params (string alias, string path)[] columns) {
			var columnsItems = new JObject();
			foreach ((string alias, string path) in columns) {
				columnsItems[alias] = new JObject {
					["expression"] = new JObject {
						[ExpressionTypeKey] = 0,
						[ColumnPathKey] = path
					}
				};
			}
			var query = new JObject {
				["rootSchemaName"] = "SysSchema",
				["operationType"] = 0,
				["filters"] = new JObject {
					["filterType"] = 6,
					["logicalOperation"] = 0,
					["isEnabled"] = true,
					["trimDateTimeParameterToDate"] = false,
					["items"] = new JObject {
						["filter0"] = new JObject {
							["filterType"] = 1,
							["comparisonType"] = 3,
							["isEnabled"] = true,
							["trimDateTimeParameterToDate"] = false,
							["leftExpression"] = new JObject {[ExpressionTypeKey] = 0, [ColumnPathKey] = "Name"},
							["rightExpression"] = new JObject {[ExpressionTypeKey] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = schemaName}}
						},
						["filter1"] = new JObject {
							["filterType"] = 1,
							["comparisonType"] = 3,
							["isEnabled"] = true,
							["trimDateTimeParameterToDate"] = false,
							["leftExpression"] = new JObject {[ExpressionTypeKey] = 0, [ColumnPathKey] = "ManagerName"},
							["rightExpression"] = new JObject {[ExpressionTypeKey] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = "ClientUnitSchemaManager"}}
						}
					}
				},
				["columns"] = new JObject { ["items"] = columnsItems },
				["rowCount"] = 1
			};
			string dataServiceUrl = serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery");
			string metadataJson = applicationClient.ExecutePostRequest(dataServiceUrl, query.ToString(Formatting.None));
			var metadataResponse = JObject.Parse(metadataJson);
			if (!(metadataResponse["success"]?.Value<bool>() ?? false)) {
				return (null, "Failed to query schema metadata");
			}
			var rows = metadataResponse["rows"] as JArray ?? new JArray();
			if (rows.Count == 0) {
				return (null, $"Schema '{schemaName}' not found");
			}
			return (rows[0], null);
		}
	}
}

namespace Clio.Command {
using System;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Verb("page-get", HelpText = "Get Freedom UI page schema body")]
public class PageGetOptions : EnvironmentOptions {
[Option("schema-name", Required = true, HelpText = "Page schema name")]
public string SchemaName { get; set; }
}

public class PageGetCommand : Command<PageGetOptions> {
private readonly IApplicationClient _applicationClient;
private readonly IServiceUrlBuilder _serviceUrlBuilder;
private readonly ILogger _logger;

public PageGetCommand(
IApplicationClient applicationClient,
IServiceUrlBuilder serviceUrlBuilder,
ILogger logger) {
_applicationClient = applicationClient;
_serviceUrlBuilder = serviceUrlBuilder;
_logger = logger;
}

public override int Execute(PageGetOptions options) {
try {
var selectQuery = new JObject {
["rootSchemaName"] = "VwSysClientUnitSchema",
["operationType"] = 0,
["filters"] = new JObject {
["filterType"] = 6,
["items"] = new JObject {
["Name"] = new JObject {
["filterType"] = 1,
["comparisonType"] = 3,
["leftExpression"] = new JObject {["expressionType"] = 0, ["columnPath"] = "Name"},
["rightExpression"] = new JObject {["expressionType"] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = options.SchemaName}}
},
["ManagerName"] = new JObject {
["filterType"] = 1,
["comparisonType"] = 3,
["leftExpression"] = new JObject {["expressionType"] = 0, ["columnPath"] = "ManagerName"},
["rightExpression"] = new JObject {["expressionType"] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = "ClientUnitSchemaManager"}}
}
}
},
["columns"] = new JObject {
["items"] = new JObject {
["Name"] = new JObject(),
["UId"] = new JObject(),
["SysPackageId"] = new JObject(),
["Body"] = new JObject()
}
},
["rowCount"] = 1
};

string url = _serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery");
string responseJson = _applicationClient.ExecutePostRequest(url, selectQuery.ToString(Formatting.None));
var response = JObject.Parse(responseJson);

if (!(response["success"]?.Value<bool>() ?? false)) {
_logger.WriteError("Query failed");
return 1;
}

var rows = response["rows"] as JArray ?? new JArray();
if (rows.Count == 0) {
_logger.WriteError($"Schema '{options.SchemaName}' not found");
return 1;
}

var row = rows[0];
_logger.WriteInfo($"Schema: {row["Name"]}");
_logger.WriteInfo($"UId: {row["UId"]}");
_logger.WriteInfo($"Body length: {row["Body"]?.ToString().Length ?? 0} chars");
return 0;
}
catch (Exception ex) {
_logger.WriteError(ex.ToString());
return 1;
}
}
}
}

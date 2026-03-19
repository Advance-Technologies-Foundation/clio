namespace Clio.Command {
using System;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Verb("page-update", HelpText = "Update Freedom UI page schema body")]
public class PageUpdateOptions : EnvironmentOptions {
[Option("schema-name", Required = true, HelpText = "Page schema name")]
public string SchemaName { get; set; }

[Option("body", Required = true, HelpText = "New JSON body content")]
public string Body { get; set; }

[Option("dry-run", Required = false, HelpText = "Validate only, don't save")]
public bool DryRun { get; set; }
}

public class PageUpdateCommand : Command<PageUpdateOptions> {
private readonly IApplicationClient _applicationClient;
private readonly IServiceUrlBuilder _serviceUrlBuilder;
private readonly ILogger _logger;

public PageUpdateCommand(
IApplicationClient applicationClient,
IServiceUrlBuilder serviceUrlBuilder,
ILogger logger) {
_applicationClient = applicationClient;
_serviceUrlBuilder = serviceUrlBuilder;
_logger = logger;
}

public override int Execute(PageUpdateOptions options) {
try {
JToken.Parse(options.Body);

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
}
}
},
["columns"] = new JObject {["items"] = new JObject {["UId"] = new JObject()}},
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

if (options.DryRun) {
_logger.WriteInfo($"DRY RUN: Body validated. No changes saved.");
return 0;
}

var updateQuery = new JObject {
["__type"] = "Terrasoft.Nui.ServiceModel.DataContract.UpdateQuery",
["rootSchemaName"] = "VwSysClientUnitSchema",
["filters"] = new JObject {
["filterType"] = 1,
["comparisonType"] = 3,
["leftExpression"] = new JObject {["expressionType"] = 0, ["columnPath"] = "UId"},
["rightExpression"] = new JObject {["expressionType"] = 2, ["parameter"] = new JObject {["dataValueType"] = 0, ["value"] = rows[0]["UId"]}}
},
["columnValues"] = new JObject {
["items"] = new JObject {
["Body"] = new JObject {
["expressionType"] = 2,
["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = options.Body}
}
}
}
};

responseJson = _applicationClient.ExecutePostRequest(url, updateQuery.ToString(Formatting.None));
response = JObject.Parse(responseJson);

if (!(response["success"]?.Value<bool>() ?? false)) {
_logger.WriteError("Update failed");
return 1;
}

_logger.WriteInfo($"Updated successfully");
return 0;
}
catch (Exception ex) {
_logger.WriteError(ex.ToString());
return 1;
}
}
}
}

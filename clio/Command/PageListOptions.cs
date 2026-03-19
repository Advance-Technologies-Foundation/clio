namespace Clio.Command {
using System;
using System.Collections.Generic;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Verb("page-list", HelpText = "List Freedom UI pages")]
public class PageListOptions : EnvironmentOptions {
[Option("package-name", Required = false, HelpText = "Filter by package name")]
public string PackageName { get; set; }

[Option("search-pattern", Required = false, HelpText = "Filter by schema name (contains)")]
public string SearchPattern { get; set; }

[Option("limit", Required = false, Default = 50, HelpText = "Maximum number of results")]
public int Limit { get; set; }
}

public class PageListCommand : Command<PageListOptions> {
private readonly IApplicationClient _applicationClient;
private readonly IServiceUrlBuilder _serviceUrlBuilder;
private readonly ILogger _logger;

public PageListCommand(
IApplicationClient applicationClient,
IServiceUrlBuilder serviceUrlBuilder,
ILogger logger) {
_applicationClient = applicationClient;
_serviceUrlBuilder = serviceUrlBuilder;
_logger = logger;
}

public override int Execute(PageListOptions options) {
try {
var filters = new JObject {
["filterType"] = 6,
["items"] = new JObject {
["ManagerName"] = new JObject {
["filterType"] = 1,
["comparisonType"] = 3,
["leftExpression"] = new JObject {["expressionType"] = 0, ["columnPath"] = "ManagerName"},
["rightExpression"] = new JObject {["expressionType"] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = "ClientUnitSchemaManager"}}
}
}
};

if (!string.IsNullOrWhiteSpace(options.PackageName)) {
filters["items"]["PackageName"] = new JObject {
["filterType"] = 1,
["comparisonType"] = 3,
["leftExpression"] = new JObject {["expressionType"] = 0, ["columnPath"] = "PackageName"},
["rightExpression"] = new JObject {["expressionType"] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = options.PackageName}}
};
}

if (!string.IsNullOrWhiteSpace(options.SearchPattern)) {
filters["items"]["Name"] = new JObject {
["filterType"] = 1,
["comparisonType"] = 11,
["leftExpression"] = new JObject {["expressionType"] = 0, ["columnPath"] = "Name"},
["rightExpression"] = new JObject {["expressionType"] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = options.SearchPattern}}
};
}

var selectQuery = new JObject {
["rootSchemaName"] = "VwSysSchemaInfo",
["operationType"] = 0,
["filters"] = filters,
["columns"] = new JObject {
["items"] = new JObject {
["Name"] = new JObject(),
["UId"] = new JObject(),
["PackageName"] = new JObject()
}
},
["rowCount"] = options.Limit
};

string url = _serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery");
string responseJson = _applicationClient.ExecutePostRequest(url, selectQuery.ToString(Formatting.None));
var response = JObject.Parse(responseJson);

if (!(response["success"]?.Value<bool>() ?? false)) {
_logger.WriteError("Query failed");
return 1;
}

var rows = response["rows"] as JArray ?? new JArray();
_logger.WriteInfo($"Found {rows.Count} page(s)");
foreach (var row in rows) {
_logger.WriteInfo($"  - {row["Name"]} ({row["PackageName"]})");
}
return 0;
}
catch (Exception ex) {
_logger.WriteError(ex.ToString());
return 1;
}
}
}
}

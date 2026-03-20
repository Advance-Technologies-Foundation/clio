namespace Clio.Command {
using System;
using System.Collections.Generic;
using System.Linq;
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

private static readonly string[] PAGE_MARKERS = {
"SCHEMA_DEPS",
"SCHEMA_ARGS",
"SCHEMA_VIEW_CONFIG_DIFF",
"SCHEMA_HANDLERS",
"SCHEMA_CONVERTERS",
"SCHEMA_VALIDATORS"
};

private static readonly string[] PAGE_FORM_MARKERS = {
"SCHEMA_VIEW_MODEL_CONFIG",
"SCHEMA_MODEL_CONFIG"
};

private static readonly string[] PAGE_LIST_MARKERS = {
"SCHEMA_VIEW_MODEL_CONFIG_DIFF",
"SCHEMA_MODEL_CONFIG_DIFF"
};

public PageUpdateCommand(
IApplicationClient applicationClient,
IServiceUrlBuilder serviceUrlBuilder,
ILogger logger) {
_applicationClient = applicationClient;
_serviceUrlBuilder = serviceUrlBuilder;
_logger = logger;
}

private int GetMarkerOccurrences(string body, string marker) {
if (string.IsNullOrEmpty(body)) return 0;
string searchPattern = $"/* Start:{marker}";
int count = 0;
int index = 0;
while ((index = body.IndexOf(searchPattern, index, StringComparison.Ordinal)) != -1) {
count++;
index += searchPattern.Length;
}
return count;
}

private List<string> GetMissingPageMarkers(string body) {
var missingMarkers = PAGE_MARKERS.Where(m => GetMarkerOccurrences(body, m) != 2).ToList();
bool hasFormMarkers = PAGE_FORM_MARKERS.All(m => GetMarkerOccurrences(body, m) == 2);
bool hasListMarkers = PAGE_LIST_MARKERS.All(m => GetMarkerOccurrences(body, m) == 2);
if (!hasFormMarkers && !hasListMarkers) {
missingMarkers.AddRange(PAGE_FORM_MARKERS);
missingMarkers.AddRange(PAGE_LIST_MARKERS);
}
return missingMarkers;
}

public override int Execute(PageUpdateOptions options) {
try {
if (string.IsNullOrWhiteSpace(options.SchemaName)) {
var errorResponse = new PageUpdateResponse {
Success = false,
Error = "schemaName is required"
};
_logger.WriteInfo(JsonConvert.SerializeObject(errorResponse));
return 1;
}

if (string.IsNullOrWhiteSpace(options.Body)) {
var errorResponse = new PageUpdateResponse {
Success = false,
Error = "body is required and must not be empty"
};
_logger.WriteInfo(JsonConvert.SerializeObject(errorResponse));
return 1;
}

var missingMarkers = GetMissingPageMarkers(options.Body);
if (missingMarkers.Count > 0) {
var errorResponse = new PageUpdateResponse {
Success = false,
Error = $"Body is missing required marker pairs: {string.Join("; ", missingMarkers)}"
};
_logger.WriteInfo(JsonConvert.SerializeObject(errorResponse));
return 1;
}

// Step 1: Get schema metadata from SysSchema
var metadataQuery = new JObject {
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
["leftExpression"] = new JObject {["expressionType"] = 0, ["columnPath"] = "Name"},
["rightExpression"] = new JObject {["expressionType"] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = options.SchemaName}}
},
["filter1"] = new JObject {
["filterType"] = 1,
["comparisonType"] = 3,
["isEnabled"] = true,
["trimDateTimeParameterToDate"] = false,
["leftExpression"] = new JObject {["expressionType"] = 0, ["columnPath"] = "ManagerName"},
["rightExpression"] = new JObject {["expressionType"] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = "ClientUnitSchemaManager"}}
}
}
},
["columns"] = new JObject {
["items"] = new JObject {
["UId"] = new JObject {
["expression"] = new JObject {
["expressionType"] = 0,
["columnPath"] = "UId"
}
}
}
},
["rowCount"] = 1
};

string dataServiceUrl = _serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery");
string metadataJson = _applicationClient.ExecutePostRequest(dataServiceUrl, metadataQuery.ToString(Formatting.None));
var metadataResponse = JObject.Parse(metadataJson);

if (!(metadataResponse["success"]?.Value<bool>() ?? false)) {
var errorResponse = new PageUpdateResponse {
Success = false,
Error = "Failed to query schema metadata"
};
_logger.WriteInfo(JsonConvert.SerializeObject(errorResponse));
return 1;
}

var rows = metadataResponse["rows"] as JArray ?? new JArray();
if (rows.Count == 0) {
var errorResponse = new PageUpdateResponse {
Success = false,
Error = $"Schema '{options.SchemaName}' not found"
};
_logger.WriteInfo(JsonConvert.SerializeObject(errorResponse));
return 1;
}

string schemaUId = rows[0]["UId"]?.ToString();

if (options.DryRun) {
var dryRunResponse = new PageUpdateResponse {
Success = true,
SchemaName = options.SchemaName,
BodyLength = options.Body.Length,
DryRun = true
};
_logger.WriteInfo(JsonConvert.SerializeObject(dryRunResponse));
return 0;
}

// Step 2: Get full schema from ClientUnitSchemaDesignerService
var getSchemaRequest = new JObject {
["schemaUId"] = schemaUId,
["useFullHierarchy"] = false
};

string designerUrl = _serviceUrlBuilder.Build("/0/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
string getSchemaJson = _applicationClient.ExecutePostRequest(designerUrl, getSchemaRequest.ToString(Formatting.None));
var getSchemaResponse = JObject.Parse(getSchemaJson);

if (!(getSchemaResponse["success"]?.Value<bool>() ?? false) || getSchemaResponse["schema"] == null) {
var errorResponse = new PageUpdateResponse {
Success = false,
Error = $"Failed to load schema '{options.SchemaName}'"
};
_logger.WriteInfo(JsonConvert.SerializeObject(errorResponse));
return 1;
}

// Step 3: Modify body and save
var schemaToSave = getSchemaResponse["schema"] as JObject;
schemaToSave["body"] = options.Body;

string saveUrl = _serviceUrlBuilder.Build("/0/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema");
string saveJson = _applicationClient.ExecutePostRequest(saveUrl, schemaToSave.ToString(Formatting.None));
var saveResponse = JObject.Parse(saveJson);

if (!(saveResponse["success"]?.Value<bool>() ?? false)) {
string errorMessage = "Failed to save page schema";
var validationErrors = saveResponse["validationErrors"] as JArray;
if (validationErrors != null && validationErrors.Count > 0) {
var messages = validationErrors
.Select(e => e["message"]?.ToString() ?? e["caption"]?.ToString())
.Where(m => !string.IsNullOrWhiteSpace(m));
errorMessage = string.Join("; ", messages);
}
var addonsErrors = saveResponse["addonsErrors"] as JArray;
if (addonsErrors != null && addonsErrors.Count > 0) {
errorMessage = string.Join("; ", addonsErrors.Select(e => e.ToString()));
}
var errorResponse = new PageUpdateResponse {
Success = false,
Error = errorMessage
};
_logger.WriteInfo(JsonConvert.SerializeObject(errorResponse));
return 1;
}

var successResponse = new PageUpdateResponse {
Success = true,
SchemaName = options.SchemaName,
BodyLength = options.Body.Length,
DryRun = false
};
_logger.WriteInfo(JsonConvert.SerializeObject(successResponse));
return 0;
}
catch (Exception ex) {
var errorResponse = new PageUpdateResponse {
Success = false,
Error = ex.Message
};
_logger.WriteInfo(JsonConvert.SerializeObject(errorResponse));
return 1;
}
}
}
}

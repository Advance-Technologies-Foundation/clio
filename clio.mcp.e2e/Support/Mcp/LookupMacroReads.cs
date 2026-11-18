using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Package;
using NUnit.Framework;
using static Clio.Mcp.E2E.Support.Mcp.ProcessDesignerStoredReads;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Reads a process-designer e2e needs to state what a <c>[#Lookup…#]</c> macro is stored as: the ROOT schema UId of an
/// object (the segment the designer stores) and one record id of it, both read from the stand at arrange time.
/// </summary>
internal static class LookupMacroReads {

	/// <summary>
	/// The schema UId of an object's ROOT registry row - the form the designer stores in a lookup macro. The registry
	/// view also lists one row per package extending the schema, each with its own UId; ExtendParent picks the root.
	/// </summary>
	internal static async Task<string> RootSchemaUIdAsync(ProcessDesignerArrangeContext context, string schemaName) {
		JsonElement query = JsonSerializer.Deserialize<JsonElement>($$"""
			{ "rootSchemaName": "VwSysEntitySchemaInWorkspace", "operationType": 0, "allColumns": false, "rowCount": 1,
			  "columns": { "items": { "UId": { "expression": { "expressionType": 0, "columnPath": "UId" } } } },
			  "filters": { "filterType": 6, "logicalOperation": 0, "items": {
			    "name": { "filterType": 1, "comparisonType": 3,
			      "leftExpression": { "expressionType": 0, "columnPath": "Name" },
			      "rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 1, "value": "{{schemaName}}" } } },
			    "root": { "filterType": 1, "comparisonType": 3,
			      "leftExpression": { "expressionType": 0, "columnPath": "ExtendParent" },
			      "rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 12, "value": false } } } } } }
			""");
		JsonElement rows = await SelectInlineAsync(context, query, $"the root registry row of {schemaName}");
		if (rows.GetArrayLength() == 0) {
			Assert.Ignore($"Object {schemaName} not found on '{context.EnvironmentName}'.");
		}
		return rows[0].GetProperty("UId").GetString()!.ToLowerInvariant();
	}

	/// <summary>The id of one record of an object; ignores the test when the object has none.</summary>
	internal static async Task<string> AnyRecordIdAsync(ProcessDesignerArrangeContext context, string schemaName) {
		JsonElement rows = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery(schemaName,
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")], [], rowCount: 1), $"a {schemaName} record");
		if (rows.GetArrayLength() == 0) {
			Assert.Ignore($"No {schemaName} record on '{context.EnvironmentName}' to use as a fixed lookup value.");
		}
		return rows.EnumerateArray().First().GetProperty("Id").GetString()!.ToLowerInvariant();
	}

}

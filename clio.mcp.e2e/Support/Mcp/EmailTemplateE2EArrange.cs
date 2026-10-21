using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// An email template a template-mode Send email test creates for itself and removes afterwards, so the test depends
/// on nothing a product ships.
/// </summary>
/// <remarks>
/// The stock templates these tests used to name - 'Case closure notification' and 'Case feedback request
/// notification', the second authored against Case - come with the Service packages, which a Studio stand does not
/// carry. The template is written through execute-dataservice-batch with an explicit record id, which is what the
/// delete needs.
/// </remarks>
internal static class EmailTemplateE2EArrange {

	/// <summary>
	/// The <c>MessageTemplateType</c> row "Email template", the only type the Send email element's picker offers.
	/// Bound data of the platform with the same id on every environment; CrtProcessBuilder resolves a template name
	/// only among rows of this type.
	/// </summary>
	private static readonly Guid EmailTemplateTypeId = new("74ff0cee-6593-482f-a62f-6dde6e17ab5e");

	/// <summary>
	/// Creates an email template with a unique name, authored against <paramref name="objectSchemaName"/> when one is
	/// given, and asserts that the write completed. A write that failed or whose outcome is unknown is removed first,
	/// because the caller's finally is not armed until this returns.
	/// </summary>
	/// <param name="session">The MCP session the test already holds.</param>
	/// <param name="environmentName">The stand to write to.</param>
	/// <param name="objectSchemaName">The object the template's macros resolve against, or <c>null</c> for none.</param>
	/// <param name="cancellationToken">The test's token.</param>
	/// <returns>The created template.</returns>
	internal static async Task<E2EEmailTemplate> CreateAsync(McpServerSession session, string environmentName,
			string? objectSchemaName, CancellationToken cancellationToken) {
		E2EEmailTemplate template = new(Guid.NewGuid(), $"Clio e2e template {Guid.NewGuid():N}");
		Dictionary<string, object?> values = new() {
			["Name"] = Value(1, template.Name),
			["Subject"] = Value(1, "Clio e2e template"),
			["TemplateType"] = Value(10, EmailTemplateTypeId)
		};
		if (objectSchemaName is not null) {
			// EmailTemplate.Object is a lookup to VwSysSchemaInfo, whose key is the schema UId - the value the
			// package reads back as the template's object.
			values["Object"] = Value(10,
				await ReadEntitySchemaUIdAsync(session, environmentName, objectSchemaName, cancellationToken));
		}
		DataServiceBatchResult result;
		try {
			result = await RunBatchAsync(session, environmentName, new Dictionary<string, object?> {
				["operation"] = "insert", ["schema-name"] = "EmailTemplate", ["record-id"] = template.Id,
				["values"] = values
			}, cancellationToken);
		} catch {
			// An insert whose answer never arrived may still have been written. The delete is by the id this call
			// generated, so it is harmless when nothing was.
			await DeleteAsync(session, environmentName, template);
			throw;
		}
		if (!result.Success) {
			// An "unknown" outcome can still mean the row exists; the caller's finally is not armed yet.
			await DeleteAsync(session, environmentName, template);
		}
		result.Success.Should().BeTrue(
			because: "the test binds a template of its own, so the arrange has to write one; the write said: {0}",
			ItemErrors(result));
		return template;
	}

	/// <summary>
	/// Removes a template <see cref="CreateAsync"/> wrote. Called from finally, so a failed delete is reported, not
	/// thrown: an exception here would replace the assertion failure the test was already reporting.
	/// </summary>
	/// <param name="session">The MCP session the test holds.</param>
	/// <param name="environmentName">The stand the template was written to.</param>
	/// <param name="template">The template to remove.</param>
	internal static async Task DeleteAsync(McpServerSession session, string environmentName,
			E2EEmailTemplate template) {
		using CancellationTokenSource cleanup = new(TimeSpan.FromMinutes(2));
		try {
			DataServiceBatchResult result = await RunBatchAsync(session, environmentName,
				new Dictionary<string, object?> {
					["operation"] = "delete", ["schema-name"] = "EmailTemplate", ["record-id"] = template.Id
				}, cleanup.Token);
			if (!result.Success) {
				await TestContext.Error.WriteLineAsync(
					$"Could not delete the email template '{template.Name}': {ItemErrors(result)}");
			}
		} catch (Exception exception) {
			await TestContext.Error.WriteLineAsync(
				$"Could not delete the email template '{template.Name}'; it stays on the stand: {exception.Message}");
		}
	}

	private static async Task<DataServiceBatchResult> RunBatchAsync(McpServerSession session, string environmentName,
			Dictionary<string, object?> operation, CancellationToken cancellationToken) {
		CallToolResult call = await session.CallToolAsync(DataServiceBatchTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["operations"] = new[] { operation }
				}
			}, cancellationToken);
		return EntitySchemaStructuredResultParser.Extract<DataServiceBatchResult>(call);
	}

	// The root registry row of an entity schema: ExtendParent false skips the rows of the packages that extend it.
	private static async Task<Guid> ReadEntitySchemaUIdAsync(McpServerSession session, string environmentName,
			string schemaName, CancellationToken cancellationToken) {
		schemaName.Should().MatchRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$",
			because: "the name is written into the query text, so only a schema identifier may reach it");
		CallToolResult call = await session.CallToolAsync(ExecuteEsqTool.ToolName, new Dictionary<string, object?> {
			["args"] = new Dictionary<string, object?> {
				["environment-name"] = environmentName,
				["query"] = JsonSerializer.Deserialize<JsonElement>($$"""
					{ "rootSchemaName": "VwSysEntitySchemaInWorkspace", "operationType": 0, "allColumns": false,
					  "columns": { "items": {
					    "UId": { "expression": { "expressionType": 0, "columnPath": "UId" } } } },
					  "filters": { "filterType": 6, "logicalOperation": 0, "items": {
					    "name": { "filterType": 1, "comparisonType": 3,
					      "leftExpression": { "expressionType": 0, "columnPath": "Name" },
					      "rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 1, "value": "{{schemaName}}" } } },
					    "root": { "filterType": 1, "comparisonType": 3,
					      "leftExpression": { "expressionType": 0, "columnPath": "ExtendParent" },
					      "rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 12, "value": false } } } } } }
					""")
			}
		}, cancellationToken);
		ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(call);
		response.Success.Should().BeTrue(
			because: "arrange: the registry view must be readable to find the UId of {0}: {1}", schemaName,
			response.Error);
		JsonElement[] rows = response.Rows!.Value.EnumerateArray().ToArray();
		rows.Should().ContainSingle(because: "exactly one root registry row is named {0}; the view returned {1}",
			schemaName, response.Rows.Value.GetRawText());
		return Guid.Parse(rows[0].GetProperty("UId").GetString()!);
	}

	private static Dictionary<string, object?> Value(int dataValueType, object value) => new() {
		["data-value-type"] = dataValueType,
		["value"] = value
	};

	private static string ItemErrors(DataServiceBatchResult result) =>
		string.Join("; ", (result.Items ?? []).Select(item => $"{item.State}: {item.Error}"));

}

/// <summary>An email template a test created: its record id, for the delete, and its unique name, for the descriptor.</summary>
internal sealed record E2EEmailTemplate(Guid Id, string Name);

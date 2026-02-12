using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.McpServer;

internal sealed class McpToolRegistry {
	private readonly Dictionary<string, McpTool> _tools;

	public McpToolRegistry(ClioFacade facade, JsonSerializerOptions jsonOptions) {
		_tools = new(StringComparer.OrdinalIgnoreCase) {
			["env.list"] = new McpTool(
				"env.list",
				"List configured Creatio environments from clio appsettings.",
				JsonSchema.Object(
					("includeSecrets", JsonSchema.Boolean(false, "Include sensitive credentials in output."))
				),
				args => Task.FromResult(facade.ListEnvironments(args))
			),
			["env.get"] = new McpTool(
				"env.get",
				"Get one environment by name.",
				JsonSchema.Object(
					required: new[] { "name" },
					properties: new[] {
						("name", JsonSchema.String("Environment name.")),
						("includeSecrets", JsonSchema.Boolean(false, "Include sensitive credentials in output."))
					}
				),
				args => Task.FromResult(facade.GetEnvironment(args))
			),
			["env.set_active"] = new McpTool(
				"env.set_active",
				"Set active environment in clio settings.",
				JsonSchema.Object(
					required: new[] { "name" },
					properties: new[] {
						("name", JsonSchema.String("Environment name."))
					}
				),
				args => Task.FromResult(facade.SetActiveEnvironment(args))
			),
			["env.upsert"] = new McpTool(
				"env.upsert",
				"Create or update clio environment settings.",
				JsonSchema.Object(
					required: new[] { "name" },
					properties: new[] {
						("name", JsonSchema.String("Environment name.")),
						("uri", JsonSchema.String("Creatio base URI.")),
						("login", JsonSchema.String("User login.")),
						("password", JsonSchema.String("User password.")),
						("isNetCore", JsonSchema.Boolean(null, "Use NetCore endpoint mode.")),
						("maintainer", JsonSchema.String("Maintainer value.")),
						("clientId", JsonSchema.String("OAuth client id.")),
						("clientSecret", JsonSchema.String("OAuth client secret.")),
						("authAppUri", JsonSchema.String("OAuth token endpoint.")),
						("workspacePathes", JsonSchema.String("Workspace paths.")),
						("environmentPath", JsonSchema.String("Path to local app root.")),
						("setActive", JsonSchema.Boolean(false, "Set this environment active after save."))
					}
				),
				args => Task.FromResult(facade.UpsertEnvironment(args))
			),
			["creatio.ping"] = new McpTool(
				"creatio.ping",
				"Ping a Creatio environment.",
				JsonSchema.Object(
					properties: ClioSchemaBuilder.CommonEnvironmentProperties(includeEndpoint: true)
				),
				args => Task.FromResult(facade.Ping(args))
			),
			["creatio.get_info"] = new McpTool(
				"creatio.get_info",
				"Get Creatio system info (requires cliogate on target instance).",
				JsonSchema.Object(
					properties: ClioSchemaBuilder.CommonEnvironmentProperties()
				),
				args => Task.FromResult(facade.GetInfo(args))
			),
			["creatio.call_service"] = new McpTool(
				"creatio.call_service",
				"Call arbitrary Creatio service route with optional JSON body.",
				JsonSchema.Object(
					required: new[] { "servicePath" },
					properties: ClioSchemaBuilder.CommonEnvironmentProperties(extra: new[] {
						("servicePath", JsonSchema.String("Service path like /0/ServiceModel/EntityDataService.svc.")),
						("httpMethod", JsonSchema.String("HTTP method: GET, POST or DELETE.")),
						("requestBody", JsonSchema.String("JSON payload string.")),
						("variables", JsonSchema.Array(JsonSchema.String("Template variable in key=value format."), "Variables for request templates."))
					})
				),
				args => Task.FromResult(facade.CallService(args))
			)
		};
	}

	public IReadOnlyList<object> ListTools() {
		return _tools.Values
			.Select(tool => new {
				name = tool.Name,
				description = tool.Description,
				inputSchema = tool.InputSchema
			})
			.Cast<object>()
			.ToList();
	}

	public async Task<ToolCallResult> CallToolAsync(string name, JsonElement args) {
		if (!_tools.TryGetValue(name, out McpTool? tool)) {
			return new ToolCallResult(true, $"Tool '{name}' is not registered.", new {
				tool = name,
				status = "error"
			});
		}

		ToolExecutionResult result = await tool.Handler(args);
		return new ToolCallResult(result.IsError, result.Message, result.Payload);
	}
}

internal sealed record McpTool(
	string Name,
	string Description,
	JsonObject InputSchema,
	Func<JsonElement, Task<ToolExecutionResult>> Handler);

internal readonly record struct ToolExecutionResult(bool IsError, string Message, object Payload);

internal static class JsonSchema {
	public static JsonObject Object((string Name, JsonObject Node) property) {
		return Object(new[] { property });
	}

	public static JsonObject Object((string Name, JsonObject Node)[]? properties = null,
		string[]? required = null,
		string? description = null) {
		JsonObject props = new();
		if (properties is not null) {
			foreach ((string name, JsonObject node) in properties) {
				props[name] = node;
			}
		}

		JsonObject result = new() {
			["type"] = "object",
			["properties"] = props
		};
		if (required is { Length: > 0 }) {
			JsonArray requiredArray = new(required.Select(x => JsonValue.Create(x)).ToArray<JsonNode?>());
			result["required"] = requiredArray;
		}
		if (!string.IsNullOrWhiteSpace(description)) {
			result["description"] = description;
		}
		return result;
	}

	public static JsonObject String(string description) {
		return new JsonObject {
			["type"] = "string",
			["description"] = description
		};
	}

	public static JsonObject Boolean(bool? defaultValue, string description) {
		JsonObject result = new() {
			["type"] = "boolean",
			["description"] = description
		};
		if (defaultValue.HasValue) {
			result["default"] = defaultValue.Value;
		}
		return result;
	}

	public static JsonObject Array(JsonObject itemSchema, string description) {
		return new JsonObject {
			["type"] = "array",
			["description"] = description,
			["items"] = itemSchema
		};
	}
}

internal static class ClioSchemaBuilder {
	public static (string Name, JsonObject Node)[] CommonEnvironmentProperties(bool includeEndpoint = false,
		(string Name, JsonObject Node)[]? extra = null) {
		List<(string Name, JsonObject Node)> properties = new() {
			("environment", JsonSchema.String("Saved clio environment name.")),
			("uri", JsonSchema.String("Creatio URI, overrides environment URI.")),
			("login", JsonSchema.String("Login, overrides environment login.")),
			("password", JsonSchema.String("Password, overrides environment password.")),
			("isNetCore", JsonSchema.Boolean(null, "Use net core mode.")),
			("clientId", JsonSchema.String("OAuth client id.")),
			("clientSecret", JsonSchema.String("OAuth client secret.")),
			("authAppUri", JsonSchema.String("OAuth token URI."))
		};

		if (includeEndpoint) {
			properties.Add(("endpoint", JsonSchema.String("Endpoint to ping. Default /ping.")));
		}

		if (extra is not null) {
			properties.AddRange(extra);
		}

		return properties.ToArray();
	}
}

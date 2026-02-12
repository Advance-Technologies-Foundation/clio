using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace Clio.McpServer;

[McpServerToolType]
public static class ClioMcpTools {
	[McpServerTool(Name = "env.list"), Description("List configured Creatio environments from clio appsettings.")]
	public static object EnvList(
		ClioFacade facade,
		[Description("Include sensitive credentials in output.")] bool includeSecrets = false) {
		return ToToolResponse(facade.ListEnvironments(Args(new {
			includeSecrets
		})));
	}

	[McpServerTool(Name = "env.get"), Description("Get one environment by name.")]
	public static object EnvGet(
		ClioFacade facade,
		[Description("Environment name.")] string name,
		[Description("Include sensitive credentials in output.")] bool includeSecrets = false) {
		return ToToolResponse(facade.GetEnvironment(Args(new {
			name,
			includeSecrets
		})));
	}

	[McpServerTool(Name = "env.set_active"), Description("Set active environment in clio settings.")]
	public static object EnvSetActive(
		ClioFacade facade,
		[Description("Environment name.")] string name) {
		return ToToolResponse(facade.SetActiveEnvironment(Args(new {
			name
		})));
	}

	[McpServerTool(Name = "env.upsert"), Description("Create or update clio environment settings.")]
	public static object EnvUpsert(
		ClioFacade facade,
		[Description("Environment name.")] string name,
		string? uri = null,
		string? login = null,
		string? password = null,
		bool? isNetCore = null,
		string? maintainer = null,
		string? clientId = null,
		string? clientSecret = null,
		string? authAppUri = null,
		string? workspacePathes = null,
		string? environmentPath = null,
		bool setActive = false) {
		return ToToolResponse(facade.UpsertEnvironment(Args(new {
			name,
			uri,
			login,
			password,
			isNetCore,
			maintainer,
			clientId,
			clientSecret,
			authAppUri,
			workspacePathes,
			environmentPath,
			setActive
		})));
	}

	[McpServerTool(Name = "creatio.ping"), Description("Ping a Creatio environment.")]
	public static object Ping(
		ClioFacade facade,
		string? environment = null,
		string? uri = null,
		string? login = null,
		string? password = null,
		bool? isNetCore = null,
		string? clientId = null,
		string? clientSecret = null,
		string? authAppUri = null,
		string? endpoint = null) {
		return ToToolResponse(facade.Ping(Args(new {
			environment,
			uri,
			login,
			password,
			isNetCore,
			clientId,
			clientSecret,
			authAppUri,
			endpoint
		})));
	}

	[McpServerTool(Name = "creatio.get_info"), Description("Get Creatio system info (requires cliogate on target instance).")]
	public static object GetInfo(
		ClioFacade facade,
		string? environment = null,
		string? uri = null,
		string? login = null,
		string? password = null,
		bool? isNetCore = null,
		string? clientId = null,
		string? clientSecret = null,
		string? authAppUri = null) {
		return ToToolResponse(facade.GetInfo(Args(new {
			environment,
			uri,
			login,
			password,
			isNetCore,
			clientId,
			clientSecret,
			authAppUri
		})));
	}

	[McpServerTool(Name = "creatio.call_service"), Description("Call arbitrary Creatio service route with optional JSON body.")]
	public static object CallService(
		ClioFacade facade,
		[Description("Service path like /0/ServiceModel/EntityDataService.svc.")] string servicePath,
		string? environment = null,
		string? uri = null,
		string? login = null,
		string? password = null,
		bool? isNetCore = null,
		string? clientId = null,
		string? clientSecret = null,
		string? authAppUri = null,
		string? httpMethod = null,
		string? requestBody = null,
		string[]? variables = null) {
		return ToToolResponse(facade.CallService(Args(new {
			servicePath,
			environment,
			uri,
			login,
			password,
			isNetCore,
			clientId,
			clientSecret,
			authAppUri,
			httpMethod,
			requestBody,
			variables
		})));
	}

	private static JsonElement Args(object args) {
		return JsonSerializer.SerializeToElement(args);
	}

	private static object ToToolResponse(ToolExecutionResult result) {
		return new {
			isError = result.IsError,
			message = result.Message,
			payload = result.Payload
		};
	}
}

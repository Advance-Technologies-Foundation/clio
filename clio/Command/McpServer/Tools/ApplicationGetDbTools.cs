using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.UserEnvironment;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public class ApplicationGetInfoDbTool : BaseMcpBackendTool<ApplicationGetInfoDbOptions>
{
	internal const string ToolName = "application-get-info-db";

	public ApplicationGetInfoDbTool(IMcpHttpClientFactory mcpClientFactory, ILogger logger)
		: base(mcpClientFactory, logger)
	{
	}

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Gets application information from Creatio through backend MCP. Returns application context with packages and entities.")]
	public CommandExecutionResult GetApplicationInfo(
		[Description("Application info parameters")]
		[Required]
		ApplicationGetInfoDbArgs args)
	{
		var arguments = new Dictionary<string, object>();

		if (!string.IsNullOrEmpty(args.AppId))
		{
			arguments["appId"] = args.AppId;
		}

		if (!string.IsNullOrEmpty(args.AppCode))
		{
			arguments["appCode"] = args.AppCode;
		}

		var options = new ApplicationGetInfoDbOptions
		{
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteMcpTool(options, "application.get_info", arguments);
	}
}

[McpServerToolType]
public class ApplicationGetListDbTool : BaseMcpBackendTool<ApplicationGetListDbOptions>
{
	internal const string ToolName = "application-get-list-db";

	public ApplicationGetListDbTool(IMcpHttpClientFactory mcpClientFactory, ILogger logger)
		: base(mcpClientFactory, logger)
	{
	}

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Gets list of all applications from Creatio through backend MCP.")]
	public CommandExecutionResult GetApplicationList(
		[Description("Application list parameters")]
		[Required]
		ApplicationGetListDbArgs args)
	{
		var arguments = new Dictionary<string, object>();

		var options = new ApplicationGetListDbOptions
		{
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteMcpTool(options, "application.get_list", arguments);
	}
}

public class ApplicationGetInfoDbOptions : EnvironmentOptions
{
}

public class ApplicationGetListDbOptions : EnvironmentOptions
{
}

public class ApplicationGetInfoDbArgs
{
	[JsonPropertyName("appId")]
	[Description("Application ID (GUID). Provide either appId or appCode")]
	public string AppId { get; set; }

	[JsonPropertyName("appCode")]
	[Description("Application code. Provide either appId or appCode")]
	public string AppCode { get; set; }

	[JsonPropertyName("environmentName")]
	[Description("Creatio environment name from clio configuration")]
	public string EnvironmentName { get; set; }

	[JsonPropertyName("uri")]
	[Description("Creatio instance URL (alternative to environmentName)")]
	public string Uri { get; set; }

	[JsonPropertyName("login")]
	[Description("Creatio login (required when using uri)")]
	public string Login { get; set; }

	[JsonPropertyName("password")]
	[Description("Creatio password (required when using uri)")]
	public string Password { get; set; }
}

public class ApplicationGetListDbArgs
{
	[JsonPropertyName("environmentName")]
	[Description("Creatio environment name from clio configuration")]
	public string EnvironmentName { get; set; }

	[JsonPropertyName("uri")]
	[Description("Creatio instance URL (alternative to environmentName)")]
	public string Uri { get; set; }

	[JsonPropertyName("login")]
	[Description("Creatio login (required when using uri)")]
	public string Login { get; set; }

	[JsonPropertyName("password")]
	[Description("Creatio password (required when using uri)")]
	public string Password { get; set; }
}

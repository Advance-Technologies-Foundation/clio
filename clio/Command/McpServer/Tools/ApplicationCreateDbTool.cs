using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.UserEnvironment;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public class ApplicationCreateDbTool : BaseMcpBackendTool<ApplicationCreateDbOptions>
{
	internal const string ToolName = "application-create-db";

	public ApplicationCreateDbTool(IMcpHttpClientFactory mcpClientFactory, ILogger logger)
		: base(mcpClientFactory, logger)
	{
	}

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Creates an application in Creatio through backend MCP (DB-first approach). Persists all artifacts directly to the database.")]
	public CommandExecutionResult CreateApplication(
		[Description("Application creation parameters")]
		[Required]
		ApplicationCreateDbArgs args)
	{
		var arguments = new Dictionary<string, object>
		{
			["name"] = args.Name,
			["code"] = args.Code,
			["templateCode"] = args.TemplateCode,
			["iconBackground"] = args.IconBackground
		};

		if (!string.IsNullOrEmpty(args.IconId))
		{
			arguments["iconId"] = args.IconId;
		}

		if (!string.IsNullOrEmpty(args.Description))
		{
			arguments["description"] = args.Description;
		}

		if (!string.IsNullOrEmpty(args.ClientTypeId))
		{
			arguments["clientTypeId"] = args.ClientTypeId;
		}

		if (!string.IsNullOrEmpty(args.OptionalTemplateDataJson))
		{
			arguments["optionalTemplateDataJson"] = args.OptionalTemplateDataJson;
		}

		var options = new ApplicationCreateDbOptions
		{
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteMcpTool(options, "application.create", arguments);
	}
}

public class ApplicationCreateDbOptions : EnvironmentOptions
{
}

public class ApplicationCreateDbArgs
{
	[JsonPropertyName("name")]
	[Description("Application name")]
	[Required]
	public string Name { get; set; }

	[JsonPropertyName("code")]
	[Description("Application code")]
	[Required]
	public string Code { get; set; }

	[JsonPropertyName("templateCode")]
	[Description("Template code (e.g., 'AppFreedomUI')")]
	[Required]
	public string TemplateCode { get; set; }

	[JsonPropertyName("iconBackground")]
	[Description("Application icon background color (e.g., '#FF5733')")]
	[Required]
	public string IconBackground { get; set; }

	[JsonPropertyName("iconId")]
	[Description("Application icon GUID. If omitted, a random icon will be selected")]
	public string IconId { get; set; }

	[JsonPropertyName("description")]
	[Description("Application description")]
	public string Description { get; set; }

	[JsonPropertyName("clientTypeId")]
	[Description("Client type GUID. Defaults to web client type")]
	public string ClientTypeId { get; set; }

	[JsonPropertyName("optionalTemplateDataJson")]
	[Description("Optional template data as JSON string")]
	public string OptionalTemplateDataJson { get; set; }

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

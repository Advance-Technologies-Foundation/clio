using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.UserEnvironment;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public class PageGetDbTool : BaseMcpBackendTool<PageGetDbOptions>
{
	internal const string ToolName = "page-get-db";
	private readonly IToolCommandResolver _commandResolver;

	public PageGetDbTool(IMcpHttpClientFactory mcpClientFactory, ILogger logger, IToolCommandResolver commandResolver)
		: base(mcpClientFactory, logger)
	{
		_commandResolver = commandResolver;
	}

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Gets Freedom UI page schema from Creatio through backend MCP.")]
	public CommandExecutionResult GetPage(
		[Description("Get page parameters")]
		[Required]
		PageGetDbArgs args)
	{
		var arguments = new Dictionary<string, object>
		{
			["pageName"] = args.PageName
		};

		if (!string.IsNullOrEmpty(args.PackageUId))
		{
			arguments["packageUId"] = args.PackageUId;
		}

		var options = new PageGetDbOptions
		{
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteMcpToolWithEnvironment(options, "page.get", arguments, _commandResolver);
	}
}

[McpServerToolType]
public class PageUpdateDbTool : BaseMcpBackendTool<PageUpdateDbOptions>
{
	internal const string ToolName = "page-update-db";
	private readonly IToolCommandResolver _commandResolver;

	public PageUpdateDbTool(IMcpHttpClientFactory mcpClientFactory, ILogger logger, IToolCommandResolver commandResolver)
		: base(mcpClientFactory, logger)
	{
		_commandResolver = commandResolver;
	}

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Updates Freedom UI page schema in Creatio through backend MCP (DB-first approach).")]
	public CommandExecutionResult UpdatePage(
		[Description("Update page parameters")]
		[Required]
		PageUpdateDbArgs args)
	{
		var arguments = new Dictionary<string, object>
		{
			["pageName"] = args.PageName,
			["packageUId"] = args.PackageUId,
			["schemaJson"] = args.SchemaJson
		};

		var options = new PageUpdateDbOptions
		{
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteMcpToolWithEnvironment(options, "page.update", arguments, _commandResolver);
	}
}

[McpServerToolType]
public class PageListDbTool : BaseMcpBackendTool<PageListDbOptions>
{
	internal const string ToolName = "page-list-db";
	private readonly IToolCommandResolver _commandResolver;

	public PageListDbTool(IMcpHttpClientFactory mcpClientFactory, ILogger logger, IToolCommandResolver commandResolver)
		: base(mcpClientFactory, logger)
	{
		_commandResolver = commandResolver;
	}

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Gets list of all Freedom UI pages from Creatio through backend MCP.")]
	public CommandExecutionResult ListPages(
		[Description("List pages parameters")]
		[Required]
		PageListDbArgs args)
	{
		var arguments = new Dictionary<string, object>();

		var options = new PageListDbOptions
		{
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteMcpToolWithEnvironment(options, "page.list", arguments, _commandResolver);
	}
}

public class PageGetDbOptions : EnvironmentOptions
{
}

public class PageUpdateDbOptions : EnvironmentOptions
{
}

public class PageListDbOptions : EnvironmentOptions
{
}

public class PageGetDbArgs
{
	[JsonPropertyName("pageName")]
	[Description("Page name to retrieve")]
	[Required]
	public string PageName { get; set; }

	[JsonPropertyName("packageUId")]
	[Description("Package GUID (optional)")]
	public string PackageUId { get; set; }

	[JsonPropertyName("environmentName")]
	[Description("Creatio environment name")]
	public string EnvironmentName { get; set; }

	[JsonPropertyName("uri")]
	[Description("Creatio instance URL")]
	public string Uri { get; set; }

	[JsonPropertyName("login")]
	[Description("Creatio login")]
	public string Login { get; set; }

	[JsonPropertyName("password")]
	[Description("Creatio password")]
	public string Password { get; set; }
}

public class PageUpdateDbArgs
{
	[JsonPropertyName("pageName")]
	[Description("Page name to update")]
	[Required]
	public string PageName { get; set; }

	[JsonPropertyName("packageUId")]
	[Description("Package GUID")]
	[Required]
	public string PackageUId { get; set; }

	[JsonPropertyName("schemaJson")]
	[Description("Updated page schema as JSON")]
	[Required]
	public string SchemaJson { get; set; }

	[JsonPropertyName("environmentName")]
	[Description("Creatio environment name")]
	public string EnvironmentName { get; set; }

	[JsonPropertyName("uri")]
	[Description("Creatio instance URL")]
	public string Uri { get; set; }

	[JsonPropertyName("login")]
	[Description("Creatio login")]
	public string Login { get; set; }

	[JsonPropertyName("password")]
	[Description("Creatio password")]
	public string Password { get; set; }
}

public class PageListDbArgs
{
	[JsonPropertyName("environmentName")]
	[Description("Creatio environment name")]
	public string EnvironmentName { get; set; }

	[JsonPropertyName("uri")]
	[Description("Creatio instance URL")]
	public string Uri { get; set; }

	[JsonPropertyName("login")]
	[Description("Creatio login")]
	public string Login { get; set; }

	[JsonPropertyName("password")]
	[Description("Creatio password")]
	public string Password { get; set; }
}

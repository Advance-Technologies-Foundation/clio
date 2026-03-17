using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.UserEnvironment;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public class BindingCreateDbTool : BaseMcpBackendTool<BindingCreateDbOptions>
{
	internal const string ToolName = "binding-create-db";
	private readonly IToolCommandResolver _commandResolver;

	public BindingCreateDbTool(IMcpHttpClientFactory mcpClientFactory, ILogger logger, IToolCommandResolver commandResolver)
		: base(mcpClientFactory, logger)
	{
		_commandResolver = commandResolver;
	}

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Creates a data binding in Creatio through backend MCP (DB-first approach). Persists binding data directly to the database.")]
	public CommandExecutionResult CreateBinding(
		[Description("Binding creation parameters")]
		[Required]
		BindingCreateDbArgs args)
	{
		var arguments = new Dictionary<string, object>
		{
			["packageUId"] = args.PackageUId,
			["schemaName"] = args.SchemaName,
			["bindingName"] = args.BindingName,
			["rowsJson"] = args.RowsJson
		};

		var options = new BindingCreateDbOptions
		{
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteMcpToolWithEnvironment(options, "binding.create", arguments, _commandResolver);
	}
}

[McpServerToolType]
public class BindingGetColumnsDbTool : BaseMcpBackendTool<BindingGetColumnsDbOptions>
{
	internal const string ToolName = "binding-get-columns-db";
	private readonly IToolCommandResolver _commandResolver;

	public BindingGetColumnsDbTool(IMcpHttpClientFactory mcpClientFactory, ILogger logger, IToolCommandResolver commandResolver)
		: base(mcpClientFactory, logger)
	{
		_commandResolver = commandResolver;
	}

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Gets available columns for creating a data binding through backend MCP.")]
	public CommandExecutionResult GetColumns(
		[Description("Get columns parameters")]
		[Required]
		BindingGetColumnsDbArgs args)
	{
		var arguments = new Dictionary<string, object>
		{
			["schemaName"] = args.SchemaName
		};

		var options = new BindingGetColumnsDbOptions
		{
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteMcpToolWithEnvironment(options, "binding.get_columns", arguments, _commandResolver);
	}
}

public class BindingCreateDbOptions : EnvironmentOptions
{
}

public class BindingGetColumnsDbOptions : EnvironmentOptions
{
}

public class BindingCreateDbArgs
{
	[JsonPropertyName("packageUId")]
	[Description("Package GUID")]
	[Required]
	public string PackageUId { get; set; }

	[JsonPropertyName("schemaName")]
	[Description("Schema name for the binding")]
	[Required]
	public string SchemaName { get; set; }

	[JsonPropertyName("bindingName")]
	[Description("Name for the binding folder (e.g., SysModule_UsrTodoTask)")]
	[Required]
	public string BindingName { get; set; }

	[JsonPropertyName("rowsJson")]
	[Description("JSON array of binding rows")]
	[Required]
	public string RowsJson { get; set; }

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

public class BindingGetColumnsDbArgs
{
	[JsonPropertyName("schemaName")]
	[Description("Schema name to get columns for")]
	[Required]
	public string SchemaName { get; set; }

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

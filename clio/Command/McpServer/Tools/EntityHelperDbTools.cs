using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.UserEnvironment;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public class EntityCheckNameDbTool : BaseMcpBackendTool<EntityCheckNameDbOptions>
{
	internal const string ToolName = "entity-check-name-db";
	private readonly IToolCommandResolver _commandResolver;

	public EntityCheckNameDbTool(IMcpHttpClientFactory mcpClientFactory, ILogger logger, IToolCommandResolver commandResolver)
		: base(mcpClientFactory, logger)
	{
		_commandResolver = commandResolver;
	}

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Checks if entity schema name is already taken in Creatio through backend MCP.")]
	public CommandExecutionResult CheckName(
		[Description("Check name parameters")]
		[Required]
		EntityCheckNameDbArgs args)
	{
		var arguments = new Dictionary<string, object>
		{
			["name"] = args.Name
		};

		var options = new EntityCheckNameDbOptions
		{
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteMcpToolWithEnvironment(options, "entity.check_name", arguments, _commandResolver);
	}
}

[McpServerToolType]
public class EntityListPackagesDbTool : BaseMcpBackendTool<EntityListPackagesDbOptions>
{
	internal const string ToolName = "entity-list-packages-db";
	private readonly IToolCommandResolver _commandResolver;

	public EntityListPackagesDbTool(IMcpHttpClientFactory mcpClientFactory, ILogger logger, IToolCommandResolver commandResolver)
		: base(mcpClientFactory, logger)
	{
		_commandResolver = commandResolver;
	}

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Gets list of packages available for entity schema operations through backend MCP.")]
	public CommandExecutionResult ListPackages(
		[Description("List packages parameters")]
		[Required]
		EntityListPackagesDbArgs args)
	{
		var arguments = new Dictionary<string, object>();

		var options = new EntityListPackagesDbOptions
		{
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteMcpToolWithEnvironment(options, "entity.list_parents", arguments, _commandResolver);
	}
}

[McpServerToolType]
public class EntityGetSchemaDbTool : BaseMcpBackendTool<EntityGetSchemaDbOptions>
{
	internal const string ToolName = "entity-get-schema-db";
	private readonly IToolCommandResolver _commandResolver;

	public EntityGetSchemaDbTool(IMcpHttpClientFactory mcpClientFactory, ILogger logger, IToolCommandResolver commandResolver)
		: base(mcpClientFactory, logger)
	{
		_commandResolver = commandResolver;
	}

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Gets detailed entity schema information from Creatio through backend MCP.")]
	public CommandExecutionResult GetSchema(
		[Description("Get schema parameters")]
		[Required]
		EntityGetSchemaDbArgs args)
	{
		var arguments = new Dictionary<string, object>
		{
			["name"] = args.SchemaName
		};

		if (!string.IsNullOrEmpty(args.PackageUId))
		{
			arguments["packageUId"] = args.PackageUId;
		}

		var options = new EntityGetSchemaDbOptions
		{
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteMcpToolWithEnvironment(options, "entity.get_schema_info", arguments, _commandResolver);
	}
}

public class EntityCheckNameDbOptions : EnvironmentOptions
{
}

public class EntityListPackagesDbOptions : EnvironmentOptions
{
}

public class EntityGetSchemaDbOptions : EnvironmentOptions
{
}

public class EntityCheckNameDbArgs
{
	[JsonPropertyName("name")]
	[Description("Schema name to check")]
	[Required]
	public string Name { get; set; }

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

public class EntityListPackagesDbArgs
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

public class EntityGetSchemaDbArgs
{
	[JsonPropertyName("schemaName")]
	[Description("Schema name to retrieve")]
	[Required]
	public string SchemaName { get; set; }

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

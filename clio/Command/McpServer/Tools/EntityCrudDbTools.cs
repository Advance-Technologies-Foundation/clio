using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.UserEnvironment;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public class EntityCreateDbTool : BaseMcpBackendTool<EntityCreateDbOptions>
{
	internal const string ToolName = "entity-create-db";

	public EntityCreateDbTool(IMcpHttpClientFactory mcpClientFactory, ILogger logger)
		: base(mcpClientFactory, logger)
	{
	}

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Creates an entity schema in Creatio through backend MCP (DB-first approach). Persists schema directly to the database.")]
	public CommandExecutionResult CreateEntity(
		[Description("Entity creation parameters")]
		[Required]
		EntityCreateDbArgs args)
	{
		var arguments = new Dictionary<string, object>
		{
			["packageUId"] = args.PackageUId,
			["name"] = args.Name,
			["caption"] = args.Caption
		};

		if (!string.IsNullOrEmpty(args.ParentSchemaName))
		{
			arguments["parentSchemaName"] = args.ParentSchemaName;
		}

		if (!string.IsNullOrEmpty(args.ColumnsJson))
		{
			arguments["columnsJson"] = args.ColumnsJson;
		}

		var options = new EntityCreateDbOptions
		{
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteMcpTool(options, "entity.create", arguments);
	}
}

[McpServerToolType]
public class EntityCreateLookupDbTool : BaseMcpBackendTool<EntityCreateLookupDbOptions>
{
	internal const string ToolName = "entity-create-lookup-db";

	public EntityCreateLookupDbTool(IMcpHttpClientFactory mcpClientFactory, ILogger logger)
		: base(mcpClientFactory, logger)
	{
	}

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Creates a lookup schema in Creatio through backend MCP (DB-first approach).")]
	public CommandExecutionResult CreateLookup(
		[Description("Lookup creation parameters")]
		[Required]
		EntityCreateLookupDbArgs args)
	{
		var arguments = new Dictionary<string, object>
		{
			["packageUId"] = args.PackageUId,
			["name"] = args.Name,
			["caption"] = args.Caption
		};

		var options = new EntityCreateLookupDbOptions
		{
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteMcpTool(options, "entity.create_lookup", arguments);
	}
}

[McpServerToolType]
public class EntityUpdateDbTool : BaseMcpBackendTool<EntityUpdateDbOptions>
{
	internal const string ToolName = "entity-update-db";

	public EntityUpdateDbTool(IMcpHttpClientFactory mcpClientFactory, ILogger logger)
		: base(mcpClientFactory, logger)
	{
	}

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Updates an existing entity schema in Creatio through backend MCP. Supports ADD, UPDATE, DELETE column operations.")]
	public CommandExecutionResult UpdateEntity(
		[Description("Entity update parameters")]
		[Required]
		EntityUpdateDbArgs args)
	{
		var arguments = new Dictionary<string, object>
		{
			["schemaName"] = args.SchemaName,
			["packageUId"] = args.PackageUId,
			["columnsJson"] = args.ColumnsJson
		};

		var options = new EntityUpdateDbOptions
		{
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		return ExecuteMcpTool(options, "entity.update", arguments);
	}
}

public class EntityCreateDbOptions : EnvironmentOptions
{
}

public class EntityCreateLookupDbOptions : EnvironmentOptions
{
}

public class EntityUpdateDbOptions : EnvironmentOptions
{
}

public class EntityCreateDbArgs
{
	[JsonPropertyName("packageUId")]
	[Description("Package GUID")]
	[Required]
	public string PackageUId { get; set; }

	[JsonPropertyName("name")]
	[Description("Entity name (Usr prefix, PascalCase)")]
	[Required]
	public string Name { get; set; }

	[JsonPropertyName("caption")]
	[Description("Human-readable caption")]
	[Required]
	public string Caption { get; set; }

	[JsonPropertyName("parentSchemaName")]
	[Description("Parent entity name (default: BaseEntity)")]
	public string ParentSchemaName { get; set; }

	[JsonPropertyName("columnsJson")]
	[Description("JSON array of column definitions")]
	public string ColumnsJson { get; set; }

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

public class EntityCreateLookupDbArgs
{
	[JsonPropertyName("packageUId")]
	[Description("Package GUID")]
	[Required]
	public string PackageUId { get; set; }

	[JsonPropertyName("name")]
	[Description("Lookup name (Usr prefix, PascalCase)")]
	[Required]
	public string Name { get; set; }

	[JsonPropertyName("caption")]
	[Description("Human-readable caption")]
	[Required]
	public string Caption { get; set; }

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

public class EntityUpdateDbArgs
{
	[JsonPropertyName("schemaName")]
	[Description("Schema name to update")]
	[Required]
	public string SchemaName { get; set; }

	[JsonPropertyName("packageUId")]
	[Description("Package GUID")]
	[Required]
	public string PackageUId { get; set; }

	[JsonPropertyName("columnsJson")]
	[Description("JSON array of column operations (ADD/UPDATE/DELETE)")]
	[Required]
	public string ColumnsJson { get; set; }

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

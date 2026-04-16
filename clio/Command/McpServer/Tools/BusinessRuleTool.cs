using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.BusinessRules;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for entity-level Freedom UI business-rule creation.
/// </summary>
[McpServerToolType]
public sealed class CreateEntityBusinessRuleTool(
	CreateEntityBusinessRuleCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreateEntityBusinessRuleOptions>(command, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for entity business-rule creation.
	/// </summary>
	internal const string BusinessRuleCreateToolName = "create-entity-business-rule";

	/// <summary>
	/// Creates an entity-level Freedom UI business rule in the requested package and entity schema.
	/// </summary>
	[McpServerTool(Name = BusinessRuleCreateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates an entity-level Freedom UI business rule.")]
	public CommandExecutionResult BusinessRuleCreate(
		[Description("Creatio environment name.")]
		[Required]
		string environmentName,
		[Description("Target package name on the Creatio environment.")]
		[Required]
		string packageName,
		[Description("Target entity schema name.")]
		[Required]
		string entitySchemaName,
		[Description("Structured entity business-rule definition.")]
		[Required]
		BusinessRule rule) {
		CreateEntityBusinessRuleOptions options = new () {
			EnvironmentName = environmentName,
			PackageName = packageName,
			EntitySchemaName = entitySchemaName,
			Rule = rule
		};
		return InternalExecute<CreateEntityBusinessRuleCommand>(options);
	}
}

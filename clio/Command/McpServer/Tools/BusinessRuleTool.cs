using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.BusinessRules;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class CreateEntityBusinessRuleTool(
	CreateEntityBusinessRuleCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreateEntityBusinessRuleOptions>(command, logger, commandResolver) {
	
	internal const string BusinessRuleCreateToolName = "create-entity-business-rule";
	
	[McpServerTool(Name = BusinessRuleCreateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates an entity-level Freedom UI business rule, including Set values actions from constants.")]
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
		[Description("Structured entity business-rule definition. Set values supports Const values for text, number, boolean, Date, DateTime, and Time targets.")]
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

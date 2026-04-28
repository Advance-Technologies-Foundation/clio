using System;
using Clio.Command.BusinessRules;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Contains the environment-scoped arguments required to create an entity business rule.
/// </summary>
public sealed class CreateEntityBusinessRuleOptions : EnvironmentNameOptions {
	public string PackageName { get; set; } = string.Empty;
	
	public string EntitySchemaName { get; set; } = string.Empty;
	
	public BusinessRule Rule { get; set; } = null!;
}

/// <summary>
/// Creates an entity-level Freedom UI business rule through the business-rule service.
/// </summary>
public sealed class CreateEntityBusinessRuleCommand(
	IBusinessRuleService businessRuleService,
	ILogger logger)
	: Command<CreateEntityBusinessRuleOptions> {
	
	public override int Execute(CreateEntityBusinessRuleOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		try {
			Validate(options);
			BusinessRuleCreateResult result = businessRuleService.Create(new BusinessRuleCreateRequest(
				options.PackageName,
				options.EntitySchemaName,
				options.Rule));
			logger.WriteInfo($"Rule name: {result.RuleName}");
			logger.WriteInfo("Done");
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	private static void Validate(CreateEntityBusinessRuleOptions options) {
		if (string.IsNullOrWhiteSpace(options.Environment)) {
			throw new ArgumentException("environment-name is required.");
		}

		if (string.IsNullOrWhiteSpace(options.PackageName)) {
			throw new ArgumentException("package-name is required.");
		}

		if (string.IsNullOrWhiteSpace(options.EntitySchemaName)) {
			throw new ArgumentException("entity-schema-name is required.");
		}

		if (options.Rule is null) {
			throw new ArgumentException("rule is required.");
		}
	}
}

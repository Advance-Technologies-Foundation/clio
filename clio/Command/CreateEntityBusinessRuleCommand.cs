using System;
using Clio.Command.BusinessRules;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Environment-scoped options for entity-level business-rule creation.
/// </summary>
public sealed class CreateEntityBusinessRuleOptions : EnvironmentNameOptions {
	/// <summary>
	/// Gets or sets the target package name on the Creatio environment.
	/// </summary>
	public string PackageName { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the target entity schema name.
	/// </summary>
	public string EntitySchemaName { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the structured business-rule definition.
	/// </summary>
	public BusinessRule Rule { get; set; } = null!;
}

/// <summary>
/// Creates an entity-level Freedom UI business rule on the requested environment.
/// </summary>
public sealed class CreateEntityBusinessRuleCommand(
	IBusinessRuleService businessRuleService,
	ILogger logger)
	: Command<CreateEntityBusinessRuleOptions> {

	/// <summary>
	/// Executes the business-rule creation flow and returns the generated rule metadata.
	/// </summary>
	/// <param name="options">Environment-scoped business-rule options.</param>
	/// <returns>Structured information about the created business rule.</returns>
	public BusinessRuleCreateResult Create(CreateEntityBusinessRuleOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		Validate(options);
		return businessRuleService.Create(
			options.Environment,
			new BusinessRuleCreateRequest(
				options.PackageName,
				options.EntitySchemaName,
				options.Rule));
	}

	/// <inheritdoc />
	public override int Execute(CreateEntityBusinessRuleOptions options) {
		try {
			_ = Create(options);
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

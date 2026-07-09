using System;
using Clio.Command.BusinessRules;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Contains the environment-scoped arguments required to create a page business rule.
/// </summary>
public sealed class CreatePageBusinessRuleOptions : EnvironmentNameOptions {
	/// <summary>
	/// Gets or sets the package where the page BusinessRule add-on is saved.
	/// </summary>
	public string PackageName { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the Freedom UI page schema name.
	/// </summary>
	public string PageSchemaName { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the structured page business-rule definition.
	/// </summary>
	public BusinessRule Rule { get; set; } = null!;
}

/// <summary>
/// Creates a page-level Freedom UI business rule through the page business-rule service.
/// </summary>
public sealed class CreatePageBusinessRuleCommand(
	IPageBusinessRuleService pageBusinessRuleService,
	ILogger logger)
	: Command<CreatePageBusinessRuleOptions> {

	/// <inheritdoc />
	public override int Execute(CreatePageBusinessRuleOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		try {
			Validate(options);
			BusinessRuleCreateResult result = pageBusinessRuleService.Create(new BusinessRuleCreateRequest(
				options.PackageName,
				options.PageSchemaName,
				options.Rule));
			logger.WriteInfo($"Rule name: {result.RuleName}");
			logger.WriteInfo("Done");
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	private static void Validate(CreatePageBusinessRuleOptions options) {
		if (string.IsNullOrWhiteSpace(options.Environment)) {
			throw new ArgumentException("environment-name is required.");
		}

		if (string.IsNullOrWhiteSpace(options.PackageName)) {
			throw new ArgumentException("package-name is required.");
		}

		if (string.IsNullOrWhiteSpace(options.PageSchemaName)) {
			throw new ArgumentException("page-schema-name is required.");
		}

		if (options.Rule is null) {
			throw new ArgumentException("rule is required.");
		}
	}
}

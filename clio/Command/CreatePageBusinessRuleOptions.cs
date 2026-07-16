using Clio.Command.BusinessRules;

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

using Clio.Command.BusinessRules;

namespace Clio.Command;

/// <summary>
/// Contains the environment-scoped arguments required to create an entity business rule.
/// </summary>
public sealed class CreateEntityBusinessRuleOptions : EnvironmentNameOptions {
	public string PackageName { get; set; } = string.Empty;

	public string EntitySchemaName { get; set; } = string.Empty;

	public BusinessRule Rule { get; set; } = null!;
}

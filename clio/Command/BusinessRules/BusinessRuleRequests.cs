using System.Collections.Generic;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Describes the package, schema, and single business-rule definition to create. The schema is an entity
/// or page schema depending on which service handles the request.
/// </summary>
public sealed record BusinessRuleCreateRequest(
	string PackageName,
	string SchemaName,
	BusinessRule Rule
);

/// <summary>
/// Describes the package, schema, and business-rule definitions to create or update in one batch (one
/// add-on round-trip). The schema is an entity or page schema depending on which service handles it.
/// </summary>
public sealed record BusinessRulesBatchRequest(
	string PackageName,
	string SchemaName,
	IReadOnlyList<BusinessRule> Rules
);

/// <summary>
/// Describes the package and schema whose business rules are read.
/// </summary>
public sealed record BusinessRulesReadRequest(
	string PackageName,
	string SchemaName
);

/// <summary>
/// Describes the package, schema, and internal rule names to delete in one batch.
/// </summary>
public sealed record BusinessRulesDeleteRequest(
	string PackageName,
	string SchemaName,
	IReadOnlyList<string> RuleNames
);

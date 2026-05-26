using System;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;

namespace Clio.Command.BusinessRules.Filters.Schema;

/// <summary>
/// Bundles the schema provider and lookup-value resolver required for apply-static-filter handling.
/// </summary>
internal sealed record StaticFilterContext(IFilterSchemaProvider SchemaProvider, ILookupValueResolver LookupResolver);

internal interface IStaticFilterContextFactory {
	StaticFilterContext Create(Guid packageUId, EntityDesignSchemaDto rootSchema);
}

internal sealed class StaticFilterContextFactory(
	IEntityBusinessRuleSchemaProvider entitySchemaProvider,
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder)
	: IStaticFilterContextFactory {

	public StaticFilterContext Create(Guid packageUId, EntityDesignSchemaDto rootSchema) {
		FilterSchemaProvider schemaProvider = new(entitySchemaProvider);
		schemaProvider.Initialize(packageUId, rootSchema);
		LookupValueResolver lookupResolver = new(schemaProvider, applicationClient, serviceUrlBuilder);
		return new StaticFilterContext(schemaProvider, lookupResolver);
	}
}

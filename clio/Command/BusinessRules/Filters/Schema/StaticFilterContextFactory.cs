using System;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;

namespace Clio.Command.BusinessRules.Filters.Schema;

/// <summary>
/// Bundles the schema provider, lookup-value resolver and filter probe required for apply-static-filter handling.
/// </summary>
internal sealed record StaticFilterContext(
	IFilterSchemaProvider SchemaProvider,
	ILookupValueResolver LookupResolver,
	IStaticFilterProbe Probe);

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
		return new StaticFilterContext(schemaProvider, lookupResolver,
			new StaticFilterProbe(applicationClient, serviceUrlBuilder));
	}
}

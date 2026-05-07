using System;
using System.Collections.Generic;
using Clio.Command.EntitySchemaDesigner;

namespace Clio.Command.BusinessRules;

internal interface IEntityBusinessRuleAttributeProvider {
	EntityBusinessRuleAttributeContext GetAttributes(string entitySchemaName, Guid packageUId);
}

internal sealed record EntityBusinessRuleAttributeContext(
	EntityDesignSchemaDto EntitySchema,
	IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> Attributes);

internal sealed class EntityBusinessRuleAttributeProvider(
	IEntityBusinessRuleSchemaProvider schemaProvider)
	: IEntityBusinessRuleAttributeProvider {

	public EntityBusinessRuleAttributeContext GetAttributes(string entitySchemaName, Guid packageUId) {
		EntityDesignSchemaDto entitySchema = schemaProvider.GetSchema(entitySchemaName, packageUId);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = BusinessRuleHelpers.BuildColumnMap(entitySchema);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributes =
			BusinessRuleHelpers.BuildAttributeDescriptorMap(columnMap);
		return new EntityBusinessRuleAttributeContext(entitySchema, attributes);
	}
}

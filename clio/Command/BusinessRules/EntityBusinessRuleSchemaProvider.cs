using System;
using Clio.Command.EntitySchemaDesigner;

namespace Clio.Command.BusinessRules;

internal interface IEntityBusinessRuleSchemaProvider {
	EntityDesignSchemaDto GetSchema(string entitySchemaName, Guid packageUId);
}

internal sealed class EntityBusinessRuleSchemaProvider(
	IRemoteEntitySchemaDesignerClient entitySchemaDesignerClient)
	: IEntityBusinessRuleSchemaProvider {

	public EntityDesignSchemaDto GetSchema(string entitySchemaName, Guid packageUId) {
		Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> response =
			entitySchemaDesignerClient.GetSchemaDesignItem(
				new GetSchemaDesignItemRequestDto {
					Name = entitySchemaName.Trim(),
					PackageUId = packageUId,
					UseFullHierarchy = true,
					Cultures = [EntitySchemaDesignerSupport.DefaultCultureName]
				},
				new RemoteCommandOptions());
		return response.Schema
			?? throw new InvalidOperationException($"Entity schema '{entitySchemaName}' was not returned.");
	}
}

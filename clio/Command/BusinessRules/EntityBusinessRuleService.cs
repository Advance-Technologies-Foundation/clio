using System;
using System.Collections.Generic;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.BusinessRules.Filters;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Creates entity business rules by appending add-on metadata to the target entity schema.
/// </summary>
public interface IEntityBusinessRuleService {
	/// <summary>
	/// Creates a new business rule in the target package and entity schema.
	/// </summary>
	/// <param name="request">Business-rule creation input.</param>
	/// <returns>Generated metadata about the created rule.</returns>
	BusinessRuleCreateResult Create(EntityBusinessRuleCreateRequest request);
}

/// <summary>
/// Describes the package, entity schema, and business-rule definition to create.
/// </summary>
public sealed record EntityBusinessRuleCreateRequest(
	string PackageName,
	string EntitySchemaName,
	BusinessRule Rule
);

internal sealed class EntityBusinessRuleService(
	IBusinessRulePackageResolver packageResolver,
	IEntityBusinessRuleAttributeProvider attributeProvider,
	IBusinessRuleAddonService businessRuleAddonService,
	IEsqFilterConverterClient esqConverterClient)
	: IEntityBusinessRuleService {

	public BusinessRuleCreateResult Create(EntityBusinessRuleCreateRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		ValidateCreateRequest(request);

		Guid packageUId = packageResolver.ResolveUId(request.PackageName);
		EntityBusinessRuleAttributeContext attributeContext = attributeProvider.GetAttributes(
			request.EntitySchemaName,
			packageUId);
		BusinessRuleValidator.Validate(request.Rule, attributeContext.Attributes);

		BusinessRuleMetadataDto createdRule = BusinessRuleMetadataConverter.ToMetadata(
			attributeContext.Attributes,
			request.Rule,
			esqConverterClient);
		return businessRuleAddonService.AppendRule(
			BuildAddonSchemaRequest(attributeContext.EntitySchema, packageUId),
			request.Rule,
			createdRule);
	}

	private static void ValidateCreateRequest(EntityBusinessRuleCreateRequest request) {
		if (string.IsNullOrWhiteSpace(request.PackageName)) {
			throw new ArgumentException("package-name is required.");
		}

		if (string.IsNullOrWhiteSpace(request.EntitySchemaName)) {
			throw new ArgumentException("entity-schema-name is required.");
		}

		if (request.Rule is null) {
			throw new ArgumentException("rule is required.");
		}
	}

	private static AddonGetRequestDto BuildAddonSchemaRequest(
		EntityDesignSchemaDto entitySchema,
		Guid packageUId) {
		return new AddonGetRequestDto {
			AddonName = BusinessRuleAddonName,
			TargetSchemaUId = entitySchema.UId,
			TargetParentSchemaUId = entitySchema.ParentSchema?.UId ?? Guid.Empty,
			TargetPackageUId = packageUId,
			TargetSchemaManagerName = EntitySchemaManagerName,
			UseFullHierarchy = true
		};
	}

}

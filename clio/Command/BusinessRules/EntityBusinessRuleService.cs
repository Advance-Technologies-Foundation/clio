using System;
using System.Collections.Generic;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.BusinessRules.Filters.Schema;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
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
	IEntityBusinessRuleSchemaProvider entitySchemaProvider,
	IBusinessRuleAddonService businessRuleAddonService,
	IBusinessRuleFormulaValidationService formulaValidationService,
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder)
	: IEntityBusinessRuleService {

	public BusinessRuleCreateResult Create(EntityBusinessRuleCreateRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		ValidateCreateRequest(request);

		Guid packageUId = packageResolver.ResolveUId(request.PackageName);
		EntityBusinessRuleAttributeContext attributeContext = attributeProvider.GetAttributes(
			request.EntitySchemaName,
			packageUId);

		FilterSchemaProvider? filterSchemaProvider = null;
		LookupValueResolver? lookupValueResolver = null;
		if (RequiresStaticFilterScope(request.Rule)) {
			filterSchemaProvider = new FilterSchemaProvider(entitySchemaProvider);
			filterSchemaProvider.Initialize(packageUId, attributeContext.EntitySchema);
			lookupValueResolver = new LookupValueResolver(filterSchemaProvider, applicationClient, serviceUrlBuilder);
		}

		BusinessRuleValidator.ValidateEntity(request.Rule, attributeContext.Attributes, filterSchemaProvider);
		ValidateFormulas(attributeContext.EntitySchema.Name, attributeContext.Attributes, request.Rule);

		IReadOnlyList<BusinessRuleMetadataDto> createdRules = BusinessRuleMetadataConverter.ToEntityMetadata(
			attributeContext.Attributes,
			request.Rule,
			attributeContext.EntitySchema.Name,
			filterSchemaProvider,
			lookupValueResolver);
		return businessRuleAddonService.AppendRule(
			BuildAddonSchemaRequest(attributeContext.EntitySchema, packageUId),
			request.Rule,
			createdRules);
	}

	private static bool RequiresStaticFilterScope(BusinessRule rule) {
		if (rule?.Actions is null) {
			return false;
		}

		foreach (BusinessRuleAction action in rule.Actions) {
			if (string.Equals(action?.ActionType, ApplyStaticFilterActionTypeName, StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
		}

		return false;
	}

	private void ValidateFormulas(
		string entitySchemaName,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule) {
		foreach (BusinessRuleFormulaValidationContext context
			in BusinessRuleFormulaBuilder.BuildValidationContexts(entitySchemaName, attributeMap, rule)) {
			formulaValidationService.Validate(context);
		}
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

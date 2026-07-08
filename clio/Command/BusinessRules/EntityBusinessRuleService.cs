using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.BusinessRules.Filters.Schema;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;
using Clio.Command.BusinessRules.Converters;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Creates entity business rules by appending add-on metadata to the target entity schema.
/// </summary>
public interface IEntityBusinessRuleService {
	/// <summary>
	/// Creates a single business rule in the target package and entity schema.
	/// </summary>
	/// <remarks>
	/// This single-rule overload backs the <c>create-entity-business-rule</c> CLI command; the MCP tool
	/// uses the batch <see cref="Create(EntityBusinessRulesBatchRequest)"/> overload exclusively (a
	/// single MCP rule is sent as a one-element batch).
	/// </remarks>
	/// <param name="request">Business-rule creation input.</param>
	/// <returns>Generated metadata about the created rule.</returns>
	BusinessRuleCreateResult Create(EntityBusinessRuleCreateRequest request);

	/// <summary>
	/// Creates multiple business rules on the same package and entity schema in a single
	/// add-on round-trip (one <c>SaveSchema</c> + cache reset + configuration rebuild for the whole
	/// batch). Per-rule validation/conversion failures are isolated and reported; the remaining rules
	/// are still saved.
	/// </summary>
	/// <param name="request">Batch business-rule creation input.</param>
	/// <returns>Per-rule outcomes, one entry per input rule, in input order.</returns>
	IReadOnlyList<BusinessRuleBatchItemResult> Create(EntityBusinessRulesBatchRequest request);

	IReadOnlyList<BusinessRule> Read(EntityBusinessRulesReadRequest request);

	IReadOnlyList<BusinessRuleBatchItemResult> Update(EntityBusinessRulesBatchRequest request);

	IReadOnlyList<BusinessRuleBatchItemResult> Delete(EntityBusinessRulesDeleteRequest request);
}

/// <summary>
/// Describes the package, entity schema, and business-rule definition to create.
/// </summary>
public sealed record EntityBusinessRuleCreateRequest(
	string PackageName,
	string EntitySchemaName,
	BusinessRule Rule
);

/// <summary>
/// Describes the package, entity schema, and business-rule definitions to create in one batch.
/// </summary>
public sealed record EntityBusinessRulesBatchRequest(
	string PackageName,
	string EntitySchemaName,
	IReadOnlyList<BusinessRule> Rules
);

public sealed record EntityBusinessRulesReadRequest(
	string PackageName,
	string EntitySchemaName
);

public sealed record EntityBusinessRulesDeleteRequest(
	string PackageName,
	string EntitySchemaName,
	IReadOnlyList<string> RuleNames
);

internal sealed class EntityBusinessRuleService(
	IBusinessRulePackageResolver packageResolver,
	IEntityBusinessRuleAttributeProvider attributeProvider,
	IBusinessRuleAddonService businessRuleAddonService,
	IBusinessRuleFormulaValidationService formulaValidationService,
	IBusinessRuleValidator businessRuleValidator,
	IStaticFilterContextFactory staticFilterContextFactory)
	: IEntityBusinessRuleService {

	public BusinessRuleCreateResult Create(EntityBusinessRuleCreateRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		ValidateCreateRequest(request);

		Guid packageUId = packageResolver.ResolveUId(request.PackageName);
		EntityBusinessRuleAttributeContext attributeContext = attributeProvider.GetAttributes(
			request.EntitySchemaName,
			packageUId);

		BusinessRule rule = BusinessRuleHelpers.StripBlockUIds(request.Rule);
		StaticFilterContext? staticFilterContext = RequiresStaticFilterScope(rule)
			? staticFilterContextFactory.Create(packageUId, attributeContext.EntitySchema)
			: null;

		businessRuleValidator.ValidateEntity(rule, attributeContext.Attributes, staticFilterContext?.SchemaProvider);
		ValidateFormulas(attributeContext.EntitySchema.Name, attributeContext.Attributes, rule);

		IReadOnlyList<BusinessRuleMetadataDto> createdRules = SimpleToFullBusinessRuleConverter.ToEntityMetadata(
			attributeContext.Attributes,
			rule,
			attributeContext.EntitySchema.Name,
			staticFilterContext?.SchemaProvider,
			staticFilterContext?.LookupResolver);
		return businessRuleAddonService.AppendRule(
			BuildAddonSchemaRequest(attributeContext.EntitySchema, packageUId),
			rule,
			createdRules);
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> Create(EntityBusinessRulesBatchRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		BusinessRuleBatchValidation.RequireBatchFields(
			request.PackageName, request.EntitySchemaName, "entity-schema-name", request.Rules);

		Guid packageUId = packageResolver.ResolveUId(request.PackageName);
		EntityBusinessRuleAttributeContext attributeContext = attributeProvider.GetAttributes(
			request.EntitySchemaName,
			packageUId);

		var results = new BusinessRuleBatchItemResult[request.Rules.Count];
		var pending = new List<(int Index, string Caption, string RuleName)>();
		var toAppend = new List<BusinessRuleMetadataDto>();

		// One static-filter context is shared across the whole batch: its schema/lookup caches then
		// accumulate across rules instead of being rebuilt (and re-fetched) for every static-filter rule.
		StaticFilterContext? batchStaticFilterContext = null;

		for (int index = 0; index < request.Rules.Count; index++) {
			BusinessRule rule = request.Rules[index];
			string caption = rule?.Caption ?? string.Empty;
			try {
				ArgumentNullException.ThrowIfNull(rule);
				rule = BusinessRuleHelpers.StripBlockUIds(rule);
				StaticFilterContext? staticFilterContext = RequiresStaticFilterScope(rule)
					? batchStaticFilterContext ??= staticFilterContextFactory.Create(packageUId, attributeContext.EntitySchema)
					: null;

				businessRuleValidator.ValidateEntity(rule, attributeContext.Attributes, staticFilterContext?.SchemaProvider);
				ValidateFormulas(attributeContext.EntitySchema.Name, attributeContext.Attributes, rule);

				IReadOnlyList<BusinessRuleMetadataDto> createdRules = SimpleToFullBusinessRuleConverter.ToEntityMetadata(
					attributeContext.Attributes,
					rule,
					attributeContext.EntitySchema.Name,
					staticFilterContext?.SchemaProvider,
					staticFilterContext?.LookupResolver);
				if (createdRules.Count == 0) {
					results[index] = new BusinessRuleBatchItemResult(caption, false, null, "Rule produced no metadata.");
					continue;
				}

				pending.Add((index, caption, createdRules[0].Name));
				toAppend.AddRange(createdRules);
			} catch (Exception exception) {
				results[index] = new BusinessRuleBatchItemResult(caption, false, null, exception.Message);
			}
		}

		if (toAppend.Count > 0) {
			AddonGetRequestDto addonRequest = BuildAddonSchemaRequest(attributeContext.EntitySchema, packageUId);
			BusinessRuleBatchSave.StampOutcome(results, pending, () => businessRuleAddonService.AppendRules(addonRequest, toAppend));
		}

		return results;
	}

	public IReadOnlyList<BusinessRule> Read(EntityBusinessRulesReadRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		RequireSchemaFields(request.PackageName, request.EntitySchemaName);

		Guid packageUId = packageResolver.ResolveUId(request.PackageName);
		EntityBusinessRuleAttributeContext attributeContext = attributeProvider.GetAttributes(
			request.EntitySchemaName,
			packageUId);
		return businessRuleAddonService.ReadRules(
			BuildAddonSchemaRequest(attributeContext.EntitySchema, packageUId));
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> Update(EntityBusinessRulesBatchRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		BusinessRuleBatchValidation.RequireBatchFields(
			request.PackageName, request.EntitySchemaName, "entity-schema-name", request.Rules);

		Guid packageUId = packageResolver.ResolveUId(request.PackageName);
		EntityBusinessRuleAttributeContext attributeContext = attributeProvider.GetAttributes(
			request.EntitySchemaName,
			packageUId);

		var results = new BusinessRuleBatchItemResult[request.Rules.Count];
		var pending = new List<(int Index, BusinessRuleUpdateItem Item)>();
		StaticFilterContext? batchStaticFilterContext = null;

		for (int index = 0; index < request.Rules.Count; index++) {
			BusinessRule rule = request.Rules[index];
			string identifier = rule?.Name ?? rule?.Caption ?? string.Empty;
			try {
				ArgumentNullException.ThrowIfNull(rule);
				if (string.IsNullOrWhiteSpace(rule.Name)) {
					throw new ArgumentException("name is required to update a business rule.");
				}

				StaticFilterContext? staticFilterContext = RequiresStaticFilterScope(rule)
					? batchStaticFilterContext ??= staticFilterContextFactory.Create(packageUId, attributeContext.EntitySchema)
					: null;

				businessRuleValidator.ValidateEntity(rule, attributeContext.Attributes, staticFilterContext?.SchemaProvider);
				ValidateFormulas(attributeContext.EntitySchema.Name, attributeContext.Attributes, rule);

				IReadOnlyList<BusinessRuleMetadataDto> generatedRules = SimpleToFullBusinessRuleConverter.ToEntityMetadata(
					attributeContext.Attributes,
					rule,
					attributeContext.EntitySchema.Name,
					staticFilterContext?.SchemaProvider,
					staticFilterContext?.LookupResolver);
				if (generatedRules.Count == 0) {
					results[index] = new BusinessRuleBatchItemResult(identifier, false, null, "Rule produced no metadata.");
					continue;
				}

				pending.Add((index, new BusinessRuleUpdateItem(rule.Name.Trim(), rule.Enabled, generatedRules)));
			} catch (Exception exception) {
				results[index] = new BusinessRuleBatchItemResult(identifier, false, null, exception.Message);
			}
		}

		if (pending.Count > 0) {
			AddonGetRequestDto addonRequest = BuildAddonSchemaRequest(attributeContext.EntitySchema, packageUId);
			BusinessRuleBatchSave.MergeUpdateOutcome(results, pending,
				items => businessRuleAddonService.UpdateRules(addonRequest, items));
		}

		return results;
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> Delete(EntityBusinessRulesDeleteRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		RequireSchemaFields(request.PackageName, request.EntitySchemaName);
		if (request.RuleNames is null || request.RuleNames.Count == 0) {
			throw new ArgumentException("rule-names is required and must contain at least one rule name.");
		}

		Guid packageUId = packageResolver.ResolveUId(request.PackageName);
		EntityBusinessRuleAttributeContext attributeContext = attributeProvider.GetAttributes(
			request.EntitySchemaName,
			packageUId);
		return businessRuleAddonService.DeleteRules(
			BuildAddonSchemaRequest(attributeContext.EntitySchema, packageUId),
			request.RuleNames);
	}

	private static void RequireSchemaFields(string packageName, string entitySchemaName) {
		if (string.IsNullOrWhiteSpace(packageName)) {
			throw new ArgumentException("package-name is required.");
		}

		if (string.IsNullOrWhiteSpace(entitySchemaName)) {
			throw new ArgumentException("entity-schema-name is required.");
		}
	}

	private static bool RequiresStaticFilterScope(BusinessRule rule) =>
		rule?.Actions?.Any(action =>
			string.Equals(action?.ActionType, ApplyStaticFilterActionTypeName, StringComparison.OrdinalIgnoreCase))
		?? false;

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

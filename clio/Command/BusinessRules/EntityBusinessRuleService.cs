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
	/// uses the batch <see cref="Create(BusinessRulesBatchRequest)"/> overload exclusively (a
	/// single MCP rule is sent as a one-element batch).
	/// </remarks>
	/// <param name="request">Business-rule creation input.</param>
	/// <returns>Generated metadata about the created rule.</returns>
	BusinessRuleCreateResult Create(BusinessRuleCreateRequest request);

	/// <summary>
	/// Creates multiple business rules on the same package and entity schema in a single
	/// add-on round-trip (one <c>SaveSchema</c> + cache reset + configuration rebuild for the whole
	/// batch). Per-rule validation/conversion failures are isolated and reported; the remaining rules
	/// are still saved.
	/// </summary>
	/// <param name="request">Batch business-rule creation input.</param>
	/// <returns>Per-rule outcomes, one entry per input rule, in input order.</returns>
	IReadOnlyList<BusinessRuleBatchItemResult> Create(BusinessRulesBatchRequest request);

	IReadOnlyList<BusinessRule> Read(BusinessRulesReadRequest request);

	IReadOnlyList<BusinessRuleBatchItemResult> Update(BusinessRulesBatchRequest request);

	IReadOnlyList<BusinessRuleBatchItemResult> Delete(BusinessRulesDeleteRequest request);
}

internal sealed class EntityBusinessRuleService(
	IBusinessRulePackageResolver packageResolver,
	IEntityBusinessRuleAttributeProvider attributeProvider,
	IBusinessRuleAddonService businessRuleAddonService,
	IBusinessRuleFormulaValidationService formulaValidationService,
	IBusinessRuleValidator businessRuleValidator,
	IStaticFilterContextFactory staticFilterContextFactory,
	ISysSettingConditionOperandResolver sysSettingResolver)
	: BaseBusinessRuleService(packageResolver, businessRuleAddonService), IEntityBusinessRuleService {

	private const string EntitySchemaNameField = "entity-schema-name";

	public BusinessRuleCreateResult Create(BusinessRuleCreateRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		ValidateCreateRequest(request);

		Guid packageUId = PackageResolver.ResolveUId(request.PackageName);
		EntityBusinessRuleAttributeContext attributeContext = attributeProvider.GetAttributes(
			request.SchemaName,
			packageUId);

		BusinessRule rule = request.Rule;
		StaticFilterContext? staticFilterContext = RequiresStaticFilterScope(rule)
			? staticFilterContextFactory.Create(packageUId, attributeContext.EntitySchema)
			: null;

		IReadOnlyDictionary<string, SysSettingOperandDescriptor> sysSettingMap = sysSettingResolver.Resolve(rule);
		businessRuleValidator.ValidateEntity(rule, attributeContext.Attributes, staticFilterContext?.SchemaProvider, sysSettingMap);
		ValidateFormulas(attributeContext.EntitySchema.Name, attributeContext.Attributes, rule);

		IReadOnlyList<BusinessRuleMetadataDto> createdRules = SimpleToFullBusinessRuleConverter.ToEntityMetadata(
			attributeContext.Attributes,
			rule,
			attributeContext.EntitySchema.Name,
			staticFilterContext?.SchemaProvider,
			staticFilterContext?.LookupResolver,
			sysSettingMap: sysSettingMap);
		StaticFilterProbeResult? probe = ProbeStaticFilter(rule, attributeContext.Attributes, createdRules, staticFilterContext);
		BusinessRuleCreateResult created = AddonService.AppendRule(
			BuildAddonSchemaRequest(attributeContext.EntitySchema, packageUId),
			rule,
			createdRules);
		return probe is null ? created : created with { FilterMatchCount = probe.MatchCount, Warning = probe.Warning };
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> Create(BusinessRulesBatchRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		BusinessRuleBatchValidation.RequireBatchFields(
			request.PackageName, request.SchemaName, EntitySchemaNameField, request.Rules);

		Guid packageUId = PackageResolver.ResolveUId(request.PackageName);
		EntityBusinessRuleAttributeContext attributeContext = attributeProvider.GetAttributes(
			request.SchemaName,
			packageUId);

		// One static-filter context is shared across the whole batch: its schema/lookup caches then
		// accumulate across rules instead of being rebuilt (and re-fetched) for every static-filter rule.
		StaticFilterContext? batchStaticFilterContext = null;
		var probes = new Dictionary<BusinessRule, StaticFilterProbeResult>(ReferenceEqualityComparer.Instance);

		IReadOnlyList<BusinessRuleBatchItemResult> results = CreateBatch(
			BuildAddonSchemaRequest(attributeContext.EntitySchema, packageUId),
			request.Rules,
			rule => {
				StaticFilterContext? staticFilterContext = RequiresStaticFilterScope(rule)
					? batchStaticFilterContext ??= staticFilterContextFactory.Create(packageUId, attributeContext.EntitySchema)
					: null;

				IReadOnlyDictionary<string, SysSettingOperandDescriptor> sysSettingMap = sysSettingResolver.Resolve(rule);
				businessRuleValidator.ValidateEntity(rule, attributeContext.Attributes, staticFilterContext?.SchemaProvider, sysSettingMap);
				ValidateFormulas(attributeContext.EntitySchema.Name, attributeContext.Attributes, rule);

				IReadOnlyList<BusinessRuleMetadataDto> createdRules = SimpleToFullBusinessRuleConverter.ToEntityMetadata(
					attributeContext.Attributes,
					rule,
					attributeContext.EntitySchema.Name,
					staticFilterContext?.SchemaProvider,
					staticFilterContext?.LookupResolver,
					sysSettingMap: sysSettingMap);
				if (ProbeStaticFilter(rule, attributeContext.Attributes, createdRules, staticFilterContext) is { } probe) {
					probes[rule] = probe;
				}
				return createdRules;
			});
		return WithProbeResults(request.Rules, results, probes);
	}

	public IReadOnlyList<BusinessRule> Read(BusinessRulesReadRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		RequireSchemaFields(request.PackageName, request.SchemaName, EntitySchemaNameField);

		Guid packageUId = PackageResolver.ResolveUId(request.PackageName);
		EntityBusinessRuleAttributeContext attributeContext = attributeProvider.GetAttributes(
			request.SchemaName,
			packageUId);
		return ReadCore(BuildAddonSchemaRequest(attributeContext.EntitySchema, packageUId));
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> Update(BusinessRulesBatchRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		BusinessRuleBatchValidation.RequireBatchFields(
			request.PackageName, request.SchemaName, EntitySchemaNameField, request.Rules);

		Guid packageUId = PackageResolver.ResolveUId(request.PackageName);
		EntityBusinessRuleAttributeContext attributeContext = attributeProvider.GetAttributes(
			request.SchemaName,
			packageUId);
		StaticFilterContext? batchStaticFilterContext = null;
		var probes = new Dictionary<BusinessRule, StaticFilterProbeResult>(ReferenceEqualityComparer.Instance);

		IReadOnlyList<BusinessRuleBatchItemResult> results = UpdateBatch(
			BuildAddonSchemaRequest(attributeContext.EntitySchema, packageUId),
			request.Rules,
			(rule, existing) => {
				StaticFilterContext? staticFilterContext = RequiresStaticFilterScope(rule)
					? batchStaticFilterContext ??= staticFilterContextFactory.Create(packageUId, attributeContext.EntitySchema)
					: null;

				IReadOnlyDictionary<string, SysSettingOperandDescriptor> sysSettingMap = sysSettingResolver.Resolve(rule);
				businessRuleValidator.ValidateEntity(rule, attributeContext.Attributes, staticFilterContext?.SchemaProvider, sysSettingMap);
				ValidateFormulas(attributeContext.EntitySchema.Name, attributeContext.Attributes, rule);

				IReadOnlyList<BusinessRuleMetadataDto> updatedRules = SimpleToFullBusinessRuleConverter.ToEntityMetadata(
					attributeContext.Attributes,
					rule,
					attributeContext.EntitySchema.Name,
					staticFilterContext?.SchemaProvider,
					staticFilterContext?.LookupResolver,
					existing,
					sysSettingMap);
				if (ProbeStaticFilter(rule, attributeContext.Attributes, updatedRules, staticFilterContext) is { } probe) {
					probes[rule] = probe;
				}
				return updatedRules;
			});
		return WithProbeResults(request.Rules, results, probes);
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> Delete(BusinessRulesDeleteRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		RequireSchemaFields(request.PackageName, request.SchemaName, EntitySchemaNameField);

		Guid packageUId = PackageResolver.ResolveUId(request.PackageName);
		EntityBusinessRuleAttributeContext attributeContext = attributeProvider.GetAttributes(
			request.SchemaName,
			packageUId);
		return DeleteCore(
			BuildAddonSchemaRequest(attributeContext.EntitySchema, packageUId),
			request.RuleNames);
	}

	// ENG-95270: runs the built filter on the environment before the rule is saved; null when there is nothing to probe.
	private static StaticFilterProbeResult? ProbeStaticFilter(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributes,
		IReadOnlyList<BusinessRuleMetadataDto> builtRules,
		StaticFilterContext? context) {
		if (context is null
			|| rule.Actions?.OfType<ApplyStaticFilterBusinessRuleAction>().FirstOrDefault() is not { } action
			|| !attributes.TryGetValue(action.TargetAttribute, out BusinessRuleAttributeDescriptor? target)
			|| string.IsNullOrEmpty(target.ReferenceSchemaName)) {
			return null;
		}
		string? envelope = builtRules
			.SelectMany(built => built.Cases)
			.SelectMany(@case => @case.Actions)
			.OfType<BusinessRuleSetFilterActionMetadataDto>()
			.Select(setFilter => setFilter.Value?.Value as string)
			.FirstOrDefault(value => !string.IsNullOrEmpty(value));
		return envelope is null ? null : context.Probe.Probe(envelope, target.ReferenceSchemaName, action.TargetAttribute);
	}

	private static IReadOnlyList<BusinessRuleBatchItemResult> WithProbeResults(
		IReadOnlyList<BusinessRule> rules,
		IReadOnlyList<BusinessRuleBatchItemResult> results,
		IReadOnlyDictionary<BusinessRule, StaticFilterProbeResult> probes) {
		if (probes.Count == 0) {
			return results;
		}
		return results
			.Select((result, index) => index < rules.Count && rules[index] is { } rule && probes.TryGetValue(rule, out StaticFilterProbeResult? probe)
				? WithProbeResult(result, probe)
				: result)
			.ToList();
	}

	private static BusinessRuleBatchItemResult WithProbeResult(BusinessRuleBatchItemResult result, StaticFilterProbeResult probe) =>
		result with { FilterMatchCount = result.Success ? probe.MatchCount : null, Warning = probe.Warning };

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

	private static void ValidateCreateRequest(BusinessRuleCreateRequest request) {
		if (string.IsNullOrWhiteSpace(request.PackageName)) {
			throw new ArgumentException("package-name is required.");
		}

		if (string.IsNullOrWhiteSpace(request.SchemaName)) {
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

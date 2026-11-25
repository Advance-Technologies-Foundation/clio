using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.AddonSchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;
using Clio.Command.BusinessRules.Converters;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Creates page business rules by appending add-on metadata to the target page schema.
/// </summary>
public interface IPageBusinessRuleService {
	/// <summary>
	/// Creates a single page business rule in the target package and page schema.
	/// </summary>
	/// <remarks>
	/// This single-rule overload backs the <c>create-page-business-rule</c> CLI command; the MCP tool
	/// uses the batch <see cref="Create(BusinessRulesBatchRequest)"/> overload exclusively (a single
	/// MCP rule is sent as a one-element batch).
	/// </remarks>
	/// <param name="request">Page business-rule creation input.</param>
	/// <returns>Generated metadata about the created rule.</returns>
	BusinessRuleCreateResult Create(BusinessRuleCreateRequest request);

	/// <summary>
	/// Creates multiple page business rules on the same package and page schema in a single add-on
	/// round-trip (one <c>SaveSchema</c> + cache reset + configuration rebuild for the whole batch).
	/// Per-rule validation/conversion failures are isolated and reported; the remaining rules are
	/// still saved.
	/// </summary>
	/// <param name="request">Batch page business-rule creation input.</param>
	/// <returns>Per-rule outcomes, one entry per input rule, in input order.</returns>
	IReadOnlyList<BusinessRuleBatchItemResult> Create(BusinessRulesBatchRequest request);

	IReadOnlyList<BusinessRule> Read(BusinessRulesReadRequest request);

	IReadOnlyList<BusinessRuleBatchItemResult> Update(BusinessRulesBatchRequest request);

	IReadOnlyList<BusinessRuleBatchItemResult> Delete(BusinessRulesDeleteRequest request);
}

internal sealed class PageBusinessRuleService(
	IBusinessRulePackageResolver packageResolver,
	IPageBusinessRuleSchemaProvider schemaProvider,
	IPageBusinessRuleAttributeProvider attributeProvider,
	IPageBusinessRuleElementProvider elementProvider,
	IBusinessRuleAddonService businessRuleAddonService,
	IPageBusinessRuleValidator pageBusinessRuleValidator,
	ISysSettingConditionOperandResolver sysSettingResolver)
	: BaseBusinessRuleService(packageResolver, businessRuleAddonService), IPageBusinessRuleService {

	private const string PageSchemaNameField = "page-schema-name";

	public BusinessRuleCreateResult Create(BusinessRuleCreateRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		ValidateCreateRequest(request);

		Guid packageUId = PackageResolver.ResolveUId(request.PackageName);
		PageBusinessRuleSchemaContext pageContext = schemaProvider.GetSchema(request.SchemaName, packageUId);
		Dictionary<string, string> rejectedAttributes = new(StringComparer.Ordinal);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = attributeProvider.GetAttributes(
			pageContext.Bundle,
			packageUId,
			rejectedAttributes);
		IReadOnlySet<string> elementNames = elementProvider.GetElementNames(pageContext.Bundle);
		BusinessRule rule = request.Rule;
		IReadOnlyDictionary<string, SysSettingOperandDescriptor> sysSettingMap = sysSettingResolver.Resolve(rule);
		pageBusinessRuleValidator.Validate(rule, attributeMap, elementNames, sysSettingMap, rejectedAttributes);

		BusinessRuleMetadataDto createdRule = SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule, existingRule: null, sysSettingMap);
		return AddonService.AppendRule(
			BuildAddonSchemaRequest(pageContext, packageUId),
			rule,
			[createdRule]);
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> Create(BusinessRulesBatchRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		BusinessRuleBatchValidation.RequireBatchFields(
			request.PackageName, request.SchemaName, PageSchemaNameField, request.Rules);

		Guid packageUId = PackageResolver.ResolveUId(request.PackageName);
		PageBusinessRuleSchemaContext pageContext = schemaProvider.GetSchema(request.SchemaName, packageUId);
		Dictionary<string, string> rejectedAttributes = new(StringComparer.Ordinal);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = attributeProvider.GetAttributes(
			pageContext.Bundle,
			packageUId,
			rejectedAttributes);
		IReadOnlySet<string> elementNames = elementProvider.GetElementNames(pageContext.Bundle);
		AddonGetRequestDto addonRequest = BuildAddonSchemaRequest(pageContext, packageUId);

		IReadOnlyList<BusinessRuleBatchItemResult> results = CreateBatch(
			addonRequest,
			request.Rules,
			rule => {
				IReadOnlyDictionary<string, SysSettingOperandDescriptor> sysSettingMap = sysSettingResolver.Resolve(rule);
				pageBusinessRuleValidator.Validate(rule, attributeMap, elementNames, sysSettingMap, rejectedAttributes);
				return [SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule, existingRule: null, sysSettingMap)];
			});
		if (!results.Any(result => result.Success)) {
			return results;
		}
		// Read after the save, so the check costs nothing when no rule was saved. The batch's own rules are then
		// stored too; giving each one its generated name lets the check replace its stored copy with it.
		IReadOnlyList<BusinessRule>? stored = TryReadStoredRules(addonRequest);
		List<BusinessRule> savedRules = request.Rules
			.Select((rule, index) => results[index].Success ? rule with { Name = results[index].RuleName } : rule)
			.ToList();
		return WithRedundancyWarnings(stored, savedRules, results);
	}

	public IReadOnlyList<BusinessRule> Read(BusinessRulesReadRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		RequireSchemaFields(request.PackageName, request.SchemaName, PageSchemaNameField);

		Guid packageUId = PackageResolver.ResolveUId(request.PackageName);
		PageBusinessRuleSchemaContext pageContext = schemaProvider.GetSchema(request.SchemaName, packageUId);
		return ReadCore(BuildAddonSchemaRequest(pageContext, packageUId));
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> Update(BusinessRulesBatchRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		BusinessRuleBatchValidation.RequireBatchFields(
			request.PackageName, request.SchemaName, PageSchemaNameField, request.Rules);

		Guid packageUId = PackageResolver.ResolveUId(request.PackageName);
		PageBusinessRuleSchemaContext pageContext = schemaProvider.GetSchema(request.SchemaName, packageUId);
		Dictionary<string, string> rejectedAttributes = new(StringComparer.Ordinal);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = attributeProvider.GetAttributes(
			pageContext.Bundle,
			packageUId,
			rejectedAttributes);
		IReadOnlySet<string> elementNames = elementProvider.GetElementNames(pageContext.Bundle);
		AddonGetRequestDto addonRequest = BuildAddonSchemaRequest(pageContext, packageUId);
		IReadOnlyList<BusinessRule>? stored = null;

		IReadOnlyList<BusinessRuleBatchItemResult> results = UpdateBatch(
			addonRequest,
			request.Rules,
			(rule, existing) => {
				IReadOnlyDictionary<string, SysSettingOperandDescriptor> sysSettingMap = sysSettingResolver.Resolve(rule);
				pageBusinessRuleValidator.Validate(rule, attributeMap, elementNames, sysSettingMap, rejectedAttributes);
				return [SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule, existing, sysSettingMap)];
			},
			storedRules => stored = storedRules);
		return WithRedundancyWarnings(stored, request.Rules, results);
	}

	/// <summary>
	/// Reads the rules already on the page for the advisory redundancy check (ENG-107305). Fails open: a read
	/// failure only skips the check, it never fails the save.
	/// </summary>
	private IReadOnlyList<BusinessRule>? TryReadStoredRules(AddonGetRequestDto addonRequest) {
		try {
			return ReadCore(addonRequest);
		} catch (Exception) {
			return null;
		}
	}

	/// <summary>
	/// Adds the redundancy warnings to the saved rules of the batch. Only saved rules are compared, so a rule
	/// that failed is neither warned about nor used as the reason for another rule's warning. When the stored
	/// rules could not be read, the batch's rules are still compared with each other.
	/// </summary>
	private static IReadOnlyList<BusinessRuleBatchItemResult> WithRedundancyWarnings(
		IReadOnlyList<BusinessRule>? stored, IReadOnlyList<BusinessRule?> rules,
		IReadOnlyList<BusinessRuleBatchItemResult> results) {
		List<BusinessRule?> saved = rules
			.Select((rule, index) => results[index].Success ? rule : null)
			.ToList();
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze(stored ?? [], saved);
		return results
			.Select((result, index) => result.Success && warnings[index].Count > 0
				? result with { Warnings = warnings[index] }
				: result)
			.ToList();
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> Delete(BusinessRulesDeleteRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		RequireSchemaFields(request.PackageName, request.SchemaName, PageSchemaNameField);

		Guid packageUId = PackageResolver.ResolveUId(request.PackageName);
		PageBusinessRuleSchemaContext pageContext = schemaProvider.GetSchema(request.SchemaName, packageUId);
		return DeleteCore(
			BuildAddonSchemaRequest(pageContext, packageUId),
			request.RuleNames);
	}

	private static void ValidateCreateRequest(BusinessRuleCreateRequest request) {
		if (string.IsNullOrWhiteSpace(request.PackageName)) {
			throw new ArgumentException("package-name is required.");
		}

		if (string.IsNullOrWhiteSpace(request.SchemaName)) {
			throw new ArgumentException("page-schema-name is required.");
		}

		if (request.Rule is null) {
			throw new ArgumentException("rule is required.");
		}
	}

	private static AddonGetRequestDto BuildAddonSchemaRequest(
		PageBusinessRuleSchemaContext pageContext,
		Guid packageUId) =>
		new() {
			AddonName = BusinessRuleAddonName,
			// The page is passed as the PARENT, not the target: passing the committed page uId as
			// TargetSchemaUId makes the backend pin the add-on to the page's own package (locked for
			// file-installed pages). An unresolvable target + the real page as parent takes the
			// backend's "resolve via parent, keep requested package" path, so the add-on lands in the
			// requested writable package. Mirrors the entity add-on flow (see spec business-rules).
			TargetSchemaUId = Guid.NewGuid(),
			TargetParentSchemaUId = Guid.Parse(pageContext.SchemaUId),
			TargetPackageUId = packageUId,
			TargetSchemaManagerName = ClientUnitSchemaManagerName,
			UseFullHierarchy = true
		};
}

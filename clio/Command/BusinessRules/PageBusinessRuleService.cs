using System;
using System.Collections.Generic;
using Clio.Command.AddonSchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

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
	/// uses the batch <see cref="Create(PageBusinessRulesBatchRequest)"/> overload exclusively (a single
	/// MCP rule is sent as a one-element batch).
	/// </remarks>
	/// <param name="request">Page business-rule creation input.</param>
	/// <returns>Generated metadata about the created rule.</returns>
	BusinessRuleCreateResult Create(PageBusinessRuleCreateRequest request);

	/// <summary>
	/// Creates multiple page business rules on the same package and page schema in a single add-on
	/// round-trip (one <c>SaveSchema</c> + cache reset + configuration rebuild for the whole batch).
	/// Per-rule validation/conversion failures are isolated and reported; the remaining rules are
	/// still saved.
	/// </summary>
	/// <param name="request">Batch page business-rule creation input.</param>
	/// <returns>Per-rule outcomes, one entry per input rule, in input order.</returns>
	IReadOnlyList<BusinessRuleBatchItemResult> Create(PageBusinessRulesBatchRequest request);

	IReadOnlyList<BusinessRule> Read(PageBusinessRulesReadRequest request);

	IReadOnlyList<BusinessRuleBatchItemResult> Update(PageBusinessRulesBatchRequest request);

	IReadOnlyList<BusinessRuleBatchItemResult> Delete(PageBusinessRulesDeleteRequest request);
}

/// <summary>
/// Describes the package, page schema, and business-rule definition to create.
/// </summary>
public sealed record PageBusinessRuleCreateRequest(
	string PackageName,
	string PageSchemaName,
	BusinessRule Rule
);

/// <summary>
/// Describes the package, page schema, and business-rule definitions to create in one batch.
/// </summary>
public sealed record PageBusinessRulesBatchRequest(
	string PackageName,
	string PageSchemaName,
	IReadOnlyList<BusinessRule> Rules
);

public sealed record PageBusinessRulesReadRequest(
	string PackageName,
	string PageSchemaName
);

public sealed record PageBusinessRulesDeleteRequest(
	string PackageName,
	string PageSchemaName,
	IReadOnlyList<string> RuleNames
);

internal sealed class PageBusinessRuleService(
	IBusinessRulePackageResolver packageResolver,
	IPageBusinessRuleSchemaProvider schemaProvider,
	IPageBusinessRuleAttributeProvider attributeProvider,
	IPageBusinessRuleElementProvider elementProvider,
	IBusinessRuleAddonService businessRuleAddonService,
	IPageBusinessRuleValidator pageBusinessRuleValidator)
	: IPageBusinessRuleService {

	public BusinessRuleCreateResult Create(PageBusinessRuleCreateRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		ValidateCreateRequest(request);

		Guid packageUId = packageResolver.ResolveUId(request.PackageName);
		PageBusinessRuleSchemaContext pageContext = schemaProvider.GetSchema(request.PageSchemaName, packageUId);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = attributeProvider.GetAttributes(
			pageContext.Bundle,
			packageUId);
		IReadOnlySet<string> elementNames = elementProvider.GetElementNames(pageContext.Bundle);
		BusinessRule rule = BusinessRuleHelpers.StripBlockUIds(request.Rule);
		pageBusinessRuleValidator.Validate(rule, attributeMap, elementNames);

		BusinessRuleMetadataDto createdRule = BusinessRuleMetadataConverter.ToPageMetadata(attributeMap, rule);
		return businessRuleAddonService.AppendRule(
			BuildAddonSchemaRequest(pageContext, packageUId),
			rule,
			[createdRule]);
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> Create(PageBusinessRulesBatchRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		BusinessRuleBatchValidation.RequireBatchFields(
			request.PackageName, request.PageSchemaName, "page-schema-name", request.Rules);

		Guid packageUId = packageResolver.ResolveUId(request.PackageName);
		PageBusinessRuleSchemaContext pageContext = schemaProvider.GetSchema(request.PageSchemaName, packageUId);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = attributeProvider.GetAttributes(
			pageContext.Bundle,
			packageUId);
		IReadOnlySet<string> elementNames = elementProvider.GetElementNames(pageContext.Bundle);

		var results = new BusinessRuleBatchItemResult[request.Rules.Count];
		var pending = new List<(int Index, string Caption, string RuleName)>();
		var toAppend = new List<BusinessRuleMetadataDto>();

		for (int index = 0; index < request.Rules.Count; index++) {
			BusinessRule rule = request.Rules[index];
			string caption = rule?.Caption ?? string.Empty;
			try {
				ArgumentNullException.ThrowIfNull(rule);
				rule = BusinessRuleHelpers.StripBlockUIds(rule);
				pageBusinessRuleValidator.Validate(rule, attributeMap, elementNames);
				BusinessRuleMetadataDto createdRule = BusinessRuleMetadataConverter.ToPageMetadata(attributeMap, rule);
				pending.Add((index, caption, createdRule.Name));
				toAppend.Add(createdRule);
			} catch (Exception exception) {
				results[index] = new BusinessRuleBatchItemResult(caption, false, null, exception.Message);
			}
		}

		if (toAppend.Count > 0) {
			AddonGetRequestDto addonRequest = BuildAddonSchemaRequest(pageContext, packageUId);
			BusinessRuleBatchSave.StampOutcome(results, pending, () => businessRuleAddonService.AppendRules(addonRequest, toAppend));
		}

		return results;
	}

	public IReadOnlyList<BusinessRule> Read(PageBusinessRulesReadRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		RequireSchemaFields(request.PackageName, request.PageSchemaName);

		Guid packageUId = packageResolver.ResolveUId(request.PackageName);
		PageBusinessRuleSchemaContext pageContext = schemaProvider.GetSchema(request.PageSchemaName, packageUId);
		return businessRuleAddonService.ReadRules(BuildAddonSchemaRequest(pageContext, packageUId));
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> Update(PageBusinessRulesBatchRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		BusinessRuleBatchValidation.RequireBatchFields(
			request.PackageName, request.PageSchemaName, "page-schema-name", request.Rules);

		Guid packageUId = packageResolver.ResolveUId(request.PackageName);
		PageBusinessRuleSchemaContext pageContext = schemaProvider.GetSchema(request.PageSchemaName, packageUId);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = attributeProvider.GetAttributes(
			pageContext.Bundle,
			packageUId);
		IReadOnlySet<string> elementNames = elementProvider.GetElementNames(pageContext.Bundle);

		var results = new BusinessRuleBatchItemResult[request.Rules.Count];
		var pending = new List<(int Index, BusinessRuleUpdateItem Item)>();

		for (int index = 0; index < request.Rules.Count; index++) {
			BusinessRule rule = request.Rules[index];
			string identifier = rule?.Name ?? rule?.Caption ?? string.Empty;
			try {
				ArgumentNullException.ThrowIfNull(rule);
				if (string.IsNullOrWhiteSpace(rule.Name)) {
					throw new ArgumentException("name is required to update a business rule.");
				}

				pageBusinessRuleValidator.Validate(rule, attributeMap, elementNames);
				BusinessRuleMetadataDto generatedRule = BusinessRuleMetadataConverter.ToPageMetadata(attributeMap, rule);
				pending.Add((index, new BusinessRuleUpdateItem(rule.Name.Trim(), rule.Enabled, [generatedRule])));
			} catch (Exception exception) {
				results[index] = new BusinessRuleBatchItemResult(identifier, false, null, exception.Message);
			}
		}

		if (pending.Count > 0) {
			AddonGetRequestDto addonRequest = BuildAddonSchemaRequest(pageContext, packageUId);
			BusinessRuleBatchSave.MergeUpdateOutcome(results, pending,
				items => businessRuleAddonService.UpdateRules(addonRequest, items));
		}

		return results;
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> Delete(PageBusinessRulesDeleteRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		RequireSchemaFields(request.PackageName, request.PageSchemaName);
		if (request.RuleNames is null || request.RuleNames.Count == 0) {
			throw new ArgumentException("rule-names is required and must contain at least one rule name.");
		}

		Guid packageUId = packageResolver.ResolveUId(request.PackageName);
		PageBusinessRuleSchemaContext pageContext = schemaProvider.GetSchema(request.PageSchemaName, packageUId);
		return businessRuleAddonService.DeleteRules(
			BuildAddonSchemaRequest(pageContext, packageUId),
			request.RuleNames);
	}

	private static void RequireSchemaFields(string packageName, string pageSchemaName) {
		if (string.IsNullOrWhiteSpace(packageName)) {
			throw new ArgumentException("package-name is required.");
		}

		if (string.IsNullOrWhiteSpace(pageSchemaName)) {
			throw new ArgumentException("page-schema-name is required.");
		}
	}

	private static void ValidateCreateRequest(PageBusinessRuleCreateRequest request) {
		if (string.IsNullOrWhiteSpace(request.PackageName)) {
			throw new ArgumentException("package-name is required.");
		}

		if (string.IsNullOrWhiteSpace(request.PageSchemaName)) {
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
			TargetSchemaUId = Guid.Parse(pageContext.SchemaUId),
			TargetParentSchemaUId = pageContext.ParentSchemaUId,
			TargetPackageUId = packageUId,
			TargetSchemaManagerName = ClientUnitSchemaManagerName,
			UseFullHierarchy = true
		};
}

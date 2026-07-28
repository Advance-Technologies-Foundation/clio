using System;
using System.Collections.Generic;
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
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = attributeProvider.GetAttributes(
			pageContext.Bundle,
			packageUId);
		IReadOnlySet<string> elementNames = elementProvider.GetElementNames(pageContext.Bundle);
		BusinessRule rule = request.Rule;
		IReadOnlyDictionary<string, SysSettingOperandDescriptor> sysSettingMap = sysSettingResolver.Resolve(rule);
		pageBusinessRuleValidator.Validate(rule, attributeMap, elementNames, sysSettingMap);

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
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = attributeProvider.GetAttributes(
			pageContext.Bundle,
			packageUId);
		IReadOnlySet<string> elementNames = elementProvider.GetElementNames(pageContext.Bundle);

		return CreateBatch(
			BuildAddonSchemaRequest(pageContext, packageUId),
			request.Rules,
			rule => {
				IReadOnlyDictionary<string, SysSettingOperandDescriptor> sysSettingMap = sysSettingResolver.Resolve(rule);
				pageBusinessRuleValidator.Validate(rule, attributeMap, elementNames, sysSettingMap);
				return [SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule, existingRule: null, sysSettingMap)];
			});
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
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = attributeProvider.GetAttributes(
			pageContext.Bundle,
			packageUId);
		IReadOnlySet<string> elementNames = elementProvider.GetElementNames(pageContext.Bundle);

		return UpdateBatch(
			BuildAddonSchemaRequest(pageContext, packageUId),
			request.Rules,
			(rule, existing) => {
				IReadOnlyDictionary<string, SysSettingOperandDescriptor> sysSettingMap = sysSettingResolver.Resolve(rule);
				pageBusinessRuleValidator.Validate(rule, attributeMap, elementNames, sysSettingMap);
				return [SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule, existing, sysSettingMap)];
			});
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

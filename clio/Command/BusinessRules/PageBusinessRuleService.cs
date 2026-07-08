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
	: BaseBusinessRuleService(packageResolver, businessRuleAddonService), IPageBusinessRuleService {

	public BusinessRuleCreateResult Create(PageBusinessRuleCreateRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		ValidateCreateRequest(request);

		Guid packageUId = PackageResolver.ResolveUId(request.PackageName);
		PageBusinessRuleSchemaContext pageContext = schemaProvider.GetSchema(request.PageSchemaName, packageUId);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = attributeProvider.GetAttributes(
			pageContext.Bundle,
			packageUId);
		IReadOnlySet<string> elementNames = elementProvider.GetElementNames(pageContext.Bundle);
		BusinessRule rule = BusinessRuleHelpers.StripBlockUIds(request.Rule);
		pageBusinessRuleValidator.Validate(rule, attributeMap, elementNames);

		BusinessRuleMetadataDto createdRule = SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule);
		return AddonService.AppendRule(
			BuildAddonSchemaRequest(pageContext, packageUId),
			rule,
			[createdRule]);
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> Create(PageBusinessRulesBatchRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		BusinessRuleBatchValidation.RequireBatchFields(
			request.PackageName, request.PageSchemaName, "page-schema-name", request.Rules);

		Guid packageUId = PackageResolver.ResolveUId(request.PackageName);
		PageBusinessRuleSchemaContext pageContext = schemaProvider.GetSchema(request.PageSchemaName, packageUId);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = attributeProvider.GetAttributes(
			pageContext.Bundle,
			packageUId);
		IReadOnlySet<string> elementNames = elementProvider.GetElementNames(pageContext.Bundle);

		return CreateBatch(
			BuildAddonSchemaRequest(pageContext, packageUId),
			request.Rules,
			rule => {
				pageBusinessRuleValidator.Validate(rule, attributeMap, elementNames);
				return [SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule)];
			});
	}

	public IReadOnlyList<BusinessRule> Read(PageBusinessRulesReadRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		RequireSchemaFields(request.PackageName, request.PageSchemaName, "page-schema-name");

		Guid packageUId = PackageResolver.ResolveUId(request.PackageName);
		PageBusinessRuleSchemaContext pageContext = schemaProvider.GetSchema(request.PageSchemaName, packageUId);
		return ReadCore(BuildAddonSchemaRequest(pageContext, packageUId));
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> Update(PageBusinessRulesBatchRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		BusinessRuleBatchValidation.RequireBatchFields(
			request.PackageName, request.PageSchemaName, "page-schema-name", request.Rules);

		Guid packageUId = PackageResolver.ResolveUId(request.PackageName);
		PageBusinessRuleSchemaContext pageContext = schemaProvider.GetSchema(request.PageSchemaName, packageUId);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = attributeProvider.GetAttributes(
			pageContext.Bundle,
			packageUId);
		IReadOnlySet<string> elementNames = elementProvider.GetElementNames(pageContext.Bundle);

		return UpdateBatch(
			BuildAddonSchemaRequest(pageContext, packageUId),
			request.Rules,
			(rule, existing) => {
				pageBusinessRuleValidator.Validate(rule, attributeMap, elementNames);
				return [SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule, existing)];
			});
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> Delete(PageBusinessRulesDeleteRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		RequireSchemaFields(request.PackageName, request.PageSchemaName, "page-schema-name");

		Guid packageUId = PackageResolver.ResolveUId(request.PackageName);
		PageBusinessRuleSchemaContext pageContext = schemaProvider.GetSchema(request.PageSchemaName, packageUId);
		return DeleteCore(
			BuildAddonSchemaRequest(pageContext, packageUId),
			request.RuleNames);
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

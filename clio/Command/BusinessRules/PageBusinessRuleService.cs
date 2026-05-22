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
	/// Creates a new page business rule in the target package and page schema.
	/// </summary>
	/// <param name="request">Page business-rule creation input.</param>
	/// <returns>Generated metadata about the created rule.</returns>
	BusinessRuleCreateResult Create(PageBusinessRuleCreateRequest request);
}

/// <summary>
/// Describes the package, page schema, and business-rule definition to create.
/// </summary>
public sealed record PageBusinessRuleCreateRequest(
	string PackageName,
	string PageSchemaName,
	BusinessRule Rule
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
		pageBusinessRuleValidator.Validate(request.Rule, attributeMap, elementNames);

		BusinessRuleMetadataDto createdRule = BusinessRuleMetadataConverter.ToPageMetadata(attributeMap, request.Rule);
		return businessRuleAddonService.AppendRule(
			BuildAddonSchemaRequest(pageContext, packageUId),
			request.Rule,
			createdRule);
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

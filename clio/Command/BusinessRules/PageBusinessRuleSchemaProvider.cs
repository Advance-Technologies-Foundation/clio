using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;

namespace Clio.Command.BusinessRules;

internal interface IPageBusinessRuleSchemaProvider {
	PageBusinessRuleSchemaContext GetSchema(string pageSchemaName, Guid packageUId);
}

internal sealed record PageBusinessRuleSchemaContext(
	string SchemaUId,
	Guid ParentSchemaUId,
	PageBundleInfo Bundle);

internal sealed class PageBusinessRuleSchemaProvider(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IPageDesignerHierarchyClient hierarchyClient,
	IPageSchemaBodyParser bodyParser,
	IPageBundleBuilder bundleBuilder)
	: IPageBusinessRuleSchemaProvider {

	public PageBusinessRuleSchemaContext GetSchema(string pageSchemaName, Guid packageUId) {
		string normalizedSchemaName = pageSchemaName.Trim();
		string targetPackageUId = packageUId.ToString();
		var (schemaUId, schemaLookupError) = PageSchemaMetadataHelper.QueryExistingSchemaInPackage(
			applicationClient,
			serviceUrlBuilder,
			normalizedSchemaName,
			targetPackageUId);
		if (!string.IsNullOrWhiteSpace(schemaLookupError)) {
			throw new InvalidOperationException(schemaLookupError);
		}

		if (string.IsNullOrWhiteSpace(schemaUId)) {
			schemaUId = ResolveRootSchemaUId(normalizedSchemaName, targetPackageUId);
		}

		IReadOnlyList<PageDesignerHierarchySchema> hierarchy =
			hierarchyClient.GetParentSchemas(schemaUId, targetPackageUId);
		if (hierarchy.Count == 0) {
			throw new InvalidOperationException($"Page schema '{pageSchemaName}' hierarchy is empty.");
		}

		List<PageSchemaBundlePart> parts = hierarchy
			.Where(schema => schema.Body is not null)
			.Select(schema => new PageSchemaBundlePart(schema, bodyParser.Parse(schema.Body)))
			.ToList();
		PageBundleInfo bundle = bundleBuilder.Build(parts);
		PageDesignerHierarchySchema currentSchema = hierarchy[0];
		string parentSchemaUId = hierarchy.Skip(1).FirstOrDefault()?.UId;
		return new PageBusinessRuleSchemaContext(
			currentSchema.UId,
			string.IsNullOrWhiteSpace(parentSchemaUId) ? Guid.Empty : Guid.Parse(parentSchemaUId),
			bundle);
	}

	private string ResolveRootSchemaUId(string pageSchemaName, string targetPackageUId) {
		var (metadata, error) = PageSchemaMetadataHelper.QuerySysSchemaRow(
			applicationClient,
			serviceUrlBuilder,
			pageSchemaName,
			("UId", "UId"),
			("PackageUId", "SysPackage.UId"));
		if (metadata is null) {
			throw new InvalidOperationException(error);
		}

		string schemaUId = metadata["UId"]?.ToString();
		if (string.IsNullOrWhiteSpace(schemaUId)) {
			throw new InvalidOperationException($"Page schema '{pageSchemaName}' metadata is missing schema UId.");
		}

		string metadataPackageUId = metadata["PackageUId"]?.ToString();
		string designPackageUId = hierarchyClient.GetDesignPackageUId(schemaUId);
		if (string.IsNullOrWhiteSpace(designPackageUId)) {
			designPackageUId = string.IsNullOrWhiteSpace(metadataPackageUId)
				? targetPackageUId
				: metadataPackageUId;
		}

		IReadOnlyList<PageDesignerHierarchySchema> initialHierarchy =
			hierarchyClient.GetParentSchemas(schemaUId, designPackageUId);
		return FindRootSchemaUId(initialHierarchy, pageSchemaName) ?? schemaUId;
	}

	private static string FindRootSchemaUId(
		IReadOnlyList<PageDesignerHierarchySchema> hierarchy,
		string schemaName) {
		for (int i = hierarchy.Count - 1; i >= 0; i--) {
			if (string.Equals(hierarchy[i].Name, schemaName, StringComparison.OrdinalIgnoreCase)) {
				return hierarchy[i].UId;
			}
		}
		return null;
	}
}

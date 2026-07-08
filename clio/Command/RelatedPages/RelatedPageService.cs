using System;
using System.Text.Json;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;

namespace Clio.Command.RelatedPages;

/// <summary>Target UI of a related page: the web designer or the mobile app.</summary>
public enum RelatedPageSchemaType {
	Web,
	Mobile
}

/// <summary>Input for registering a related page on an entity.</summary>
public sealed record RelatedPageRegistration(
	string PackageName,
	string EntitySchemaName,
	string PageSchemaName,
	RelatedPageSchemaType SchemaType,
	bool IsDefault);

public sealed record RelatedPageResult(
	string EntitySchemaName,
	string PageSchemaName,
	Guid PageSchemaUId,
	string AddonName,
	bool IsDefault);

/// <summary>
/// Registers a page as a related page of an entity for the web (<c>RelatedPage</c> add-on) or the
/// mobile app (<c>MobileRelatedPage</c> add-on), optionally as the default page. Resolves the package,
/// entity and page UIds, then delegates the add-on get/modify/save to <see cref="IRelatedPageAddonService"/>.
/// </summary>
public interface IRelatedPageService {
	RelatedPageResult Register(RelatedPageRegistration request);
}

internal sealed class RelatedPageService(
	IBusinessRulePackageResolver packageResolver,
	IEntityBusinessRuleAttributeProvider attributeProvider,
	IRelatedPageAddonService addonService,
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder)
	: IRelatedPageService {

	private const string EntitySchemaManagerName = "EntitySchemaManager";
	private const string WebAddonName = "RelatedPage";
	private const string MobileAddonName = "MobileRelatedPage";

	public RelatedPageResult Register(RelatedPageRegistration request) {
		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.PackageName)) {
			throw new ArgumentException("package-name is required.");
		}
		if (string.IsNullOrWhiteSpace(request.EntitySchemaName)) {
			throw new ArgumentException("entity-schema-name is required.");
		}
		if (string.IsNullOrWhiteSpace(request.PageSchemaName)) {
			throw new ArgumentException("page-schema-name is required.");
		}

		string addonName = request.SchemaType == RelatedPageSchemaType.Mobile ? MobileAddonName : WebAddonName;
		Guid packageUId = packageResolver.ResolveUId(request.PackageName);
		EntityDesignSchemaDto entity = attributeProvider.GetAttributes(request.EntitySchemaName, packageUId).EntitySchema;
		Guid pageUId = ResolveSchemaUId(request.PageSchemaName);

		AddonGetRequestDto addonRequest = new() {
			AddonName = addonName,
			TargetSchemaUId = entity.UId,
			TargetParentSchemaUId = entity.ParentSchema?.UId ?? Guid.Empty,
			TargetPackageUId = packageUId,
			TargetSchemaManagerName = EntitySchemaManagerName,
			UseFullHierarchy = true
		};
		addonService.UpsertRelatedPage(addonRequest, pageUId, request.IsDefault);

		return new RelatedPageResult(entity.Name, request.PageSchemaName, pageUId, addonName, request.IsDefault);
	}

	/// <summary>Resolves a schema's UId by name via OData (<c>SysSchema</c>).</summary>
	private Guid ResolveSchemaUId(string schemaName) {
		// Escape the OData string literal by doubling single quotes before building the $filter.
		// Uri.EscapeDataString only encodes for URL transport (the server decodes it before the
		// OData parser sees it), so it does not neutralize a quote — without this a name containing
		// a single quote alters the filter (injection) or simply fails to resolve. See ODataKeyFormatter.
		string literal = schemaName.Replace("'", "''");
		string filter = Uri.EscapeDataString($"Name eq '{literal}'");
		string url = serviceUrlBuilder.Build($"odata/SysSchema?$select=UId&$filter={filter}&$top=1");
		string json = applicationClient.ExecuteGetRequest(url, 30_000);
		using JsonDocument doc = JsonDocument.Parse(json);
		if (doc.RootElement.TryGetProperty("value", out JsonElement value)
			&& value.ValueKind == JsonValueKind.Array
			&& value.GetArrayLength() > 0
			&& value[0].TryGetProperty("UId", out JsonElement uidElement)
			&& Guid.TryParse(uidElement.GetString(), out Guid uid)) {
			return uid;
		}
		throw new InvalidOperationException(
			$"Could not resolve the schema UId for '{schemaName}'. Ensure the page exists on the environment.");
	}
}

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using Clio.Common;

namespace Clio.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Read-only environment probe that detects whether a source web page is registered as a section
/// (SysModule) and what it would take to make that section available in the Creatio Mobile app
/// (set MobileSectionSchemaUId, add to a workplace). It performs OData v4 GET queries only — it never
/// writes. The model performs the writes (odata-update / odata-create) after the user approves
/// (Gate S). Any query failure degrades gracefully to <see cref="SectionRegistrationInfo.ProbeOk"/> =
/// false with a note; it never throws so the conversion guide can always be returned.
/// </summary>
[SuppressMessage("Critical Code Smell", "S3776:Cognitive Complexity of methods should not be too high", Justification = "The probe threads several best-effort OData reads with graceful degradation; keeping them in one flow preserves the never-throws contract.")]
public static class MobileSectionRegistrationProbe {

	private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";

	/// <summary>
	/// Probes the environment for section / workplace registration facts about <paramref name="pageSchemaUId"/>.
	/// </summary>
	public static SectionRegistrationInfo Probe(
		IToolCommandResolver commandResolver,
		string environment, string uri, string login, string password,
		string pageSchemaUId, bool isFormPage) {
		if (commandResolver is null || string.IsNullOrWhiteSpace(pageSchemaUId)) {
			return new SectionRegistrationInfo {
				IsFormPage = isFormPage,
				ProbeOk = false,
				Note = "Section registration was not probed (missing environment client or page schema UId).",
				RegistrationActions = [ManualEditPageActionOrSkip(isFormPage)]
			};
		}

		try {
			var options = new EnvironmentOptions {
				Environment = environment, Uri = uri, Login = login, Password = password
			};
			IApplicationClient client = commandResolver.Resolve<IApplicationClient>(options);
			IServiceUrlBuilder urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);

			// 1. Is this page a section? Match SysModule.SectionSchemaUId or SysPageSchemaUId == page UId.
			// Parse the UId first: OData v4 GUID literals are unquoted, so a non-GUID value would inject
			// filter syntax. A malformed value degrades gracefully to a "not probed" result below.
			if (!Guid.TryParse(pageSchemaUId, out Guid pageUId)) {
				return new SectionRegistrationInfo {
					IsFormPage = isFormPage,
					ProbeOk = false,
					Note = "Section registration was not probed (page schema UId is not a valid GUID).",
					RegistrationActions = [ManualEditPageActionOrSkip(isFormPage)]
				};
			}
			string moduleFilter = $"SectionSchemaUId eq {pageUId} or SysPageSchemaUId eq {pageUId}";
			JsonElement[] modules = Query(client, urlBuilder,
				"SysModule",
				"$select=Id,Code,Caption,SectionSchemaUId,SysPageSchemaUId,MobileSectionSchemaUId&$filter=" + moduleFilter);

			if (modules.Length == 0) {
				return new SectionRegistrationInfo {
					SourcePageIsSection = false,
					IsFormPage = isFormPage,
					ProbeOk = true,
					Note = "The source page is not registered as a section in SysModule.",
					RegistrationActions = [
						isFormPage
							? ManualEditPageActionOrSkip(true)
							: "The source page is not a registered section — no SysModule registration is needed (or register it as a new section manually)."
					]
				};
			}

			return ProbeModule(client, urlBuilder, modules[0], isFormPage, entitySchemaName: null, note: null);
		} catch (Exception ex) {
			return new SectionRegistrationInfo {
				IsFormPage = isFormPage,
				ProbeOk = false,
				Note = $"Could not query the environment for section registration ({ex.Message}). Verify section / workplace registration manually.",
				RegistrationActions = [ManualEditPageActionOrSkip(isFormPage)]
			};
		}
	}

	/// <summary>
	/// Probes section / workplace registration for the ENTITY a page is bound to (ENG-95730): a legacy
	/// Mobile-wizard settings schema is not itself a SysModule page, so the section is found through
	/// <c>SysModuleEntity.SysEntitySchemaUId</c> → <c>SysModule.SysModuleEntityId</c>. Read-only, never throws;
	/// when the entity has several sections the first is probed and the rest are named in the note so the user
	/// decides.
	/// </summary>
	public static SectionRegistrationInfo ProbeByEntity(
		IToolCommandResolver commandResolver,
		string environment, string uri, string login, string password,
		string entitySchemaName) {
		if (commandResolver is null || string.IsNullOrWhiteSpace(entitySchemaName)) {
			return new SectionRegistrationInfo {
				IsFormPage = false,
				EntitySchemaName = entitySchemaName,
				ProbeOk = false,
				Note = "Section registration was not probed (missing environment client or entity schema name).",
				RegistrationActions = [ManualEditPageActionOrSkip(false)]
			};
		}
		try {
			var options = new EnvironmentOptions {
				Environment = environment, Uri = uri, Login = login, Password = password
			};
			IApplicationClient client = commandResolver.Resolve<IApplicationClient>(options);
			IServiceUrlBuilder urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);

			// The entity name is interpolated into an OData string literal: accept identifiers only, so a stray
			// quote can never reach the filter. A malformed name degrades to "not probed".
			if (!PageSchemaMetadataHelper.IsValidSchemaName(entitySchemaName)) {
				return new SectionRegistrationInfo {
					IsFormPage = false,
					EntitySchemaName = entitySchemaName,
					ProbeOk = false,
					Note = $"Section registration was not probed: '{entitySchemaName}' is not a valid entity schema name.",
					RegistrationActions = [ManualEditPageActionOrSkip(false)]
				};
			}
			// SysModuleEntity references the entity's ROOT schema UId. An entity replaced in many packages has one
			// SysSchema row per package with a DIFFERENT UId each, so the lookup must pick the root row
			// (ExtendParent = false) — a Name-only first-row lookup returns an arbitrary replacing layer.
			var entityGuids = new List<Guid>();
			foreach (JsonElement schema in Query(client, urlBuilder,
				"SysSchema",
				$"$select=UId&$filter=Name eq '{entitySchemaName}' and ManagerName eq 'EntitySchemaManager' and ExtendParent eq false")) {
				if (Guid.TryParse(Str(schema, "UId"), out Guid uid)) {
					entityGuids.Add(uid);
				}
			}
			if (entityGuids.Count == 0) {
				return new SectionRegistrationInfo {
					IsFormPage = false,
					EntitySchemaName = entitySchemaName,
					ProbeOk = false,
					Note = $"Section registration was not probed: no root entity schema named '{entitySchemaName}' was found.",
					RegistrationActions = [ManualEditPageActionOrSkip(false)]
				};
			}
			var moduleEntities = new List<JsonElement>();
			foreach (Guid entityGuid in entityGuids) {
				moduleEntities.AddRange(Query(client, urlBuilder,
					"SysModuleEntity", $"$select=Id&$filter=SysEntitySchemaUId eq {entityGuid}"));
			}
			var modules = new List<JsonElement>();
			foreach (JsonElement moduleEntity in moduleEntities) {
				string moduleEntityId = Str(moduleEntity, "Id");
				if (!Guid.TryParse(moduleEntityId, out Guid moduleEntityGuid)) {
					continue;
				}
				modules.AddRange(Query(client, urlBuilder,
					"SysModule",
					"$select=Id,Code,Caption,SectionSchemaUId,SysPageSchemaUId,MobileSectionSchemaUId&$filter=" +
					$"SysModuleEntity/Id eq {moduleEntityGuid}"));
			}
			if (modules.Count == 0) {
				return new SectionRegistrationInfo {
					SourcePageIsSection = false,
					IsFormPage = false,
					EntitySchemaName = entitySchemaName,
					ProbeOk = true,
					Note = $"No section (SysModule) is registered for entity '{entitySchemaName}'.",
					RegistrationActions = [
						$"No section exists for '{entitySchemaName}' — register one manually (or with create-app-section) before adding the mobile list page to a mobile workplace."
					]
				};
			}
			string note = modules.Count > 1
				? $"Entity '{entitySchemaName}' has {modules.Count} sections ({string.Join(", ", modules.Select(m => Str(m, "Code")).Where(c => !string.IsNullOrWhiteSpace(c)))}); the first one is reported — the user decides which section receives the mobile list page."
				: null;
			return ProbeModule(client, urlBuilder, modules[0], isFormPage: false, entitySchemaName, note);
		} catch (Exception ex) {
			return new SectionRegistrationInfo {
				IsFormPage = false,
				EntitySchemaName = entitySchemaName,
				ProbeOk = false,
				Note = $"Could not query the environment for section registration ({ex.Message}). Verify section / workplace registration manually.",
				RegistrationActions = [ManualEditPageActionOrSkip(false)]
			};
		}
	}

	/// <summary>Shared tail of both probes: workplace membership, the Mobile client type and the registration actions for one SysModule row.</summary>
	private static SectionRegistrationInfo ProbeModule(
		IApplicationClient client, IServiceUrlBuilder urlBuilder, JsonElement module, bool isFormPage,
		string entitySchemaName, string note) {
		string sysModuleId = Str(module, "Id");
		string mobileUId = Str(module, "MobileSectionSchemaUId");
		bool mobileRegistered = !string.IsNullOrWhiteSpace(mobileUId)
			&& !string.Equals(mobileUId, EmptyGuid, StringComparison.OrdinalIgnoreCase);

		// 2. Which workplaces is this section already in? A lookup is filtered through its NAVIGATION path
		//    (SysModule/Id): the Id-suffixed column works in $select but "Column by path SysModuleId not found"
		//    in $filter, which Query() would swallow as an empty result — silently reporting no workplaces.
		var currentWorkplaceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (JsonElement link in Query(client, urlBuilder,
			"SysModuleInWorkplace",
			$"$select=SysWorkplaceId&$filter=SysModule/Id eq {sysModuleId}")) {
			string wpId = Str(link, "SysWorkplaceId");
			if (!string.IsNullOrWhiteSpace(wpId)) {
				currentWorkplaceIds.Add(wpId);
			}
		}

		// 3. Resolve the Mobile client type and list workplaces.
		string mobileClientTypeId = ResolveMobileClientTypeId(client, urlBuilder);
		var current = new List<WorkplaceInfo>();
		var availableMobile = new List<WorkplaceInfo>();
		foreach (JsonElement wp in Query(client, urlBuilder,
			"SysWorkplace", "$select=Id,Name,SysApplicationClientTypeId&$top=100")) {
			string id = Str(wp, "Id");
			string clientTypeId = Str(wp, "SysApplicationClientTypeId");
			bool isMobile = mobileClientTypeId is not null
				&& string.Equals(clientTypeId, mobileClientTypeId, StringComparison.OrdinalIgnoreCase);
			bool contains = currentWorkplaceIds.Contains(id);
			var info = new WorkplaceInfo { Id = id, Name = Str(wp, "Name"), IsMobile = isMobile, ContainsSection = contains };
			if (contains) {
				current.Add(info);
			}
			if (isMobile) {
				availableMobile.Add(info);
			}
		}

		return new SectionRegistrationInfo {
			SourcePageIsSection = true,
			SysModuleId = sysModuleId,
			SectionCode = Str(module, "Code"),
			SectionCaption = Str(module, "Caption"),
			EntitySchemaName = entitySchemaName,
			MobileSectionSchemaUId = mobileRegistered ? mobileUId : null,
			MobileSectionRegistered = mobileRegistered,
			IsFormPage = isFormPage,
			CurrentWorkplaces = current,
			AvailableMobileWorkplaces = availableMobile,
			ProbeOk = true,
			Note = note,
			RegistrationActions = BuildActions(sysModuleId, Str(module, "Code"), mobileRegistered, availableMobile, isFormPage)
		};
	}

	private static List<string> BuildActions(
		string sysModuleId, string sectionCode, bool mobileRegistered,
		IReadOnlyList<WorkplaceInfo> availableMobile, bool isFormPage) {
		var actions = new List<string>();
		actions.Add(mobileRegistered
			? $"Section '{sectionCode}' already has a mobile page (SysModule.MobileSectionSchemaUId is set); updating it would replace the current mobile section page."
			: $"After creating the mobile list page, set SysModule.MobileSectionSchemaUId = <new mobile page schema UId> on row '{sectionCode}' (id {sysModuleId}) with odata-update (confirm=true).");
		actions.Add(availableMobile.Count > 0
			? $"Add the section to a mobile workplace (e.g. {string.Join(", ", availableMobile.Select(w => w.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Take(5))}) with odata-create SysModuleInWorkplace {{SysModuleId, SysWorkplaceId, Position}}, or create a new mobile workplace — the user decides."
			: "No mobile workplace was found. Create one with odata-create SysWorkplace (Mobile client type), then add the section with odata-create SysModuleInWorkplace — the user decides.");
		if (isFormPage) {
			actions.Add(ManualEditPageActionOrSkip(true));
		}
		return actions;
	}

	private static string ManualEditPageActionOrSkip(bool isFormPage) =>
		isFormPage
			? "Edit page: after creating the mobile form page, register it as the object's default MOBILE edit page with create-related-page-addon (schema-type=mobile, a single is-default page → the MobileRelatedPage add-on)."
			: "No additional section registration action is required for this page.";

	/// <summary>Best-effort: finds the Id of the Mobile <c>SysApplicationClientType</c> (matched by name).</summary>
	private static string ResolveMobileClientTypeId(IApplicationClient client, IServiceUrlBuilder urlBuilder) {
		try {
			foreach (JsonElement ct in Query(client, urlBuilder, "SysApplicationClientType", "$select=Id,Name&$top=50")) {
				string name = Str(ct, "Name");
				if (!string.IsNullOrWhiteSpace(name) && name.Contains("Mobile", StringComparison.OrdinalIgnoreCase)) {
					return Str(ct, "Id");
				}
			}
		} catch {
			// Best-effort: workplaces are still listed, just without the mobile flag.
		}
		return null;
	}

	/// <summary>Runs an OData v4 GET and returns the <c>value</c> array elements (empty on a single/!array body).</summary>
	private static JsonElement[] Query(IApplicationClient client, IServiceUrlBuilder urlBuilder, string entity, string query) {
		string url = urlBuilder.Build($"odata/{entity}?{query}");
		string json = client.ExecuteGetRequest(url, 30_000);
		using JsonDocument doc = JsonDocument.Parse(json);
		if (doc.RootElement.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.Array) {
			return value.EnumerateArray().Select(e => e.Clone()).ToArray();
		}
		return [];
	}

	private static string Str(JsonElement element, string property) =>
		element.TryGetProperty(property, out JsonElement value) && value.ValueKind is not JsonValueKind.Null
			? value.ToString()
			: null;
}

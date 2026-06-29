namespace Clio.Command {
	using System;
	using System.Collections.Generic;
	using System.Runtime.Serialization;
	using Clio.Command.RelatedPages;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;
	using JsonPropertyNameAttribute = System.Text.Json.Serialization.JsonPropertyNameAttribute;

	/// <summary>
	/// Describes a single related-page entry to store in the <c>RelatedPage</c> add-on metadata.
	/// </summary>
	public sealed record RelatedPageSpec(
		string PageSchemaName,
		bool IsDefault = false,
		bool IsAdd = false,
		bool IsSspDefault = false,
		string Role = null,
		string TypeColumnValue = null,
		string RoleName = null);

	/// <summary>
	/// Options for the <c>create-related-page-addon</c> command. Configures which Freedom UI pages open
	/// by default and for record creation on an entity (object), by writing the <c>RelatedPage</c>
	/// add-on schema attached to that object via <c>AddonSchemaDesignerService.svc</c>.
	/// </summary>
	[Verb("create-related-page-addon", Aliases = ["related-page-addon", "set-related-pages"],
		HelpText = "Configure the RelatedPage add-on (default/add page per role and type) for an object")]
	public class CreateRelatedPageAddonOptions : EnvironmentOptions {
		[Option("entity-schema-name", Required = true, HelpText = "Object (entity schema) name the related pages belong to, e.g. 'UsrDeliveryItem'")]
		public string EntitySchemaName { get; set; }

		[Option("package-name", Required = true, HelpText = "Package that owns the add-on configuration")]
		public string PackageName { get; set; }

		[Option("default-page", Required = false, HelpText = "Page schema name shown by default when opening a record")]
		public string DefaultPage { get; set; }

		[Option("add-page", Required = false, HelpText = "Page schema name used when adding a new record (defaults to default-page when omitted)")]
		public string AddPage { get; set; }

		[Option("portal-default-page", Required = false, HelpText = "Page schema name shown by default to portal (self-service / 'All external users') users when opening a record")]
		public string PortalDefaultPage { get; set; }

		[Option("portal-add-page", Required = false, HelpText = "Page schema name portal users use when adding a record (defaults to portal-default-page when omitted)")]
		public string PortalAddPage { get; set; }

		[Option("type-column-uid", Required = false, HelpText = "Optional UId of the type column that drives type-specific page sets")]
		public string TypeColumnUId { get; set; }

		/// <summary>
		/// Full set of related-page entries to store. When non-null this fully replaces the page list and
		/// the scalar <see cref="DefaultPage"/>/<see cref="AddPage"/> options are ignored. Populated by the
		/// MCP tool; the CLI verb builds it from the scalar options instead.
		/// </summary>
		public IReadOnlyList<RelatedPageSpec> Pages { get; set; }
	}

	/// <summary>
	/// Configures the <c>RelatedPage</c> add-on for an object. Thin CLI/MCP front-end over
	/// <see cref="IRelatedPageAddonService"/>; the service performs the resolution and the single
	/// <c>GetSchema</c>/<c>SaveSchema</c> round-trip against <c>AddonSchemaDesignerService.svc</c>.
	/// </summary>
	public class CreateRelatedPageAddonCommand : Command<CreateRelatedPageAddonOptions> {
		private readonly IRelatedPageAddonService _relatedPageAddonService;
		private readonly ILogger _logger;

		public CreateRelatedPageAddonCommand(
			IRelatedPageAddonService relatedPageAddonService,
			ILogger logger) {
			_relatedPageAddonService = relatedPageAddonService;
			_logger = logger;
		}

		public override int Execute(CreateRelatedPageAddonOptions options) {
			bool success = TryCreate(options, out CreateRelatedPageAddonResponse response);
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
		}

		public virtual bool TryCreate(CreateRelatedPageAddonOptions options, out CreateRelatedPageAddonResponse response) {
			if (options is null) {
				response = Fail("options is required");
				return false;
			}
			try {
				IReadOnlyList<RelatedPageSpec> pages = ResolvePageSpecs(options);
				var request = new RelatedPageAddonRequest(
					options.PackageName,
					options.EntitySchemaName,
					pages,
					options.TypeColumnUId);

				RelatedPageAddonResult result = _relatedPageAddonService.Create(request);

				response = new CreateRelatedPageAddonResponse {
					Success = true,
					EntitySchemaName = options.EntitySchemaName,
					EntitySchemaUId = result.EntitySchemaUId,
					PackageName = options.PackageName,
					PackageUId = result.PackageUId,
					AddonName = result.AddonName,
					PageCount = result.PageCount
				};
				return true;
			} catch (Exception ex) {
				response = Fail(ex.Message);
				_logger.WriteInfo($"  failed: {ex.Message}");
				return false;
			}
		}

		// Standard platform role names. The related-page audience is conveyed by the role on each page entry
		// (verified against Cases: "All employees" vs "All external users" sets — IsSspDefault stays false).
		private const string EmployeesRoleName = "All employees";
		private const string PortalRoleName = "All external users";

		private static IReadOnlyList<RelatedPageSpec> ResolvePageSpecs(CreateRelatedPageAddonOptions options) {
			if (options.Pages is { Count: > 0 }) {
				return options.Pages;
			}
			bool hasPortal = !string.IsNullOrWhiteSpace(options.PortalDefaultPage)
				|| !string.IsNullOrWhiteSpace(options.PortalAddPage);
			// With a portal audience configured the employee pages must be scoped to "All employees" so the two
			// audiences stay separate sets; without it, the employee pages stay role-less (apply to everyone).
			string employeeRole = hasPortal ? EmployeesRoleName : null;
			var built = new List<RelatedPageSpec>();
			AddAudiencePages(built, options.DefaultPage, options.AddPage, employeeRole);
			AddAudiencePages(built, options.PortalDefaultPage, options.PortalAddPage, PortalRoleName);
			return built;
		}

		/// <summary>
		/// Adds the default and add page entries for one audience: an empty default/add page is skipped and
		/// the add page falls back to the default page when omitted (so a single page can serve both).
		/// </summary>
		private static void AddAudiencePages(
			List<RelatedPageSpec> pages, string defaultPage, string addPage, string roleName) {
			if (!string.IsNullOrWhiteSpace(defaultPage)) {
				pages.Add(new RelatedPageSpec(defaultPage.Trim(), IsDefault: true, RoleName: roleName));
			}
			string effectiveAddPage = string.IsNullOrWhiteSpace(addPage) ? defaultPage : addPage;
			if (!string.IsNullOrWhiteSpace(effectiveAddPage)) {
				pages.Add(new RelatedPageSpec(effectiveAddPage.Trim(), IsAdd: true, RoleName: roleName));
			}
		}

		private static CreateRelatedPageAddonResponse Fail(string error) =>
			new() { Success = false, Error = error };
	}

	/// <summary>
	/// Represents the <c>create-related-page-addon</c> response envelope.
	/// </summary>
	[DataContract]
	public sealed class CreateRelatedPageAddonResponse {
		[DataMember(Name = "success")]
		[JsonProperty("success")]
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[DataMember(Name = "entitySchemaName")]
		[JsonProperty("entitySchemaName", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("entitySchemaName")]
		public string EntitySchemaName { get; set; }

		[DataMember(Name = "entitySchemaUId")]
		[JsonProperty("entitySchemaUId", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("entitySchemaUId")]
		public string EntitySchemaUId { get; set; }

		[DataMember(Name = "packageName")]
		[JsonProperty("packageName", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("packageName")]
		public string PackageName { get; set; }

		[DataMember(Name = "packageUId")]
		[JsonProperty("packageUId", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("packageUId")]
		public string PackageUId { get; set; }

		[DataMember(Name = "addonName")]
		[JsonProperty("addonName", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("addonName")]
		public string AddonName { get; set; }

		[DataMember(Name = "pageCount")]
		[JsonProperty("pageCount")]
		[JsonPropertyName("pageCount")]
		public int PageCount { get; set; }

		[DataMember(Name = "error")]
		[JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("error")]
		public string Error { get; set; }
	}
}

namespace Clio.Command {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Runtime.Serialization;
	using Clio.Command.RelatedPages;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;
	using JsonPropertyNameAttribute = System.Text.Json.Serialization.JsonPropertyNameAttribute;

	/// <summary>
	/// Options for the <c>get-related-page-addon</c> command. Reads an object's current <c>RelatedPage</c>
	/// configuration (the bound default/add pages, per audience and type) so a caller can inspect it before a
	/// replace-not-merge <c>create-related-page-addon</c> write.
	/// </summary>
	[Verb("get-related-page-addon", Aliases = ["related-page-addon-get", "get-related-pages"],
		HelpText = "Read an object's current RelatedPage configuration (bound default/add pages per role and type)")]
	public class GetRelatedPageAddonOptions : EnvironmentOptions {
		[Option("entity-schema-name", Required = true, HelpText = "Object (entity schema) name whose related pages to read, e.g. 'UsrDeliveryItem'")]
		public string EntitySchemaName { get; set; }

		[Option("package-name", Required = true, HelpText = "Package that owns the add-on configuration")]
		public string PackageName { get; set; }
	}

	/// <summary>
	/// Reads the <c>RelatedPage</c> add-on of an object. Thin CLI/MCP front-end over
	/// <see cref="IRelatedPageAddonService.Get"/>; the service resolves the object and decodes the page set
	/// from the add-on metadata. Read-only — performs no save.
	/// </summary>
	public sealed class GetRelatedPageAddonCommand : Command<GetRelatedPageAddonOptions> {
		private readonly IRelatedPageAddonService _relatedPageAddonService;
		private readonly ILogger _logger;

		public GetRelatedPageAddonCommand(
			IRelatedPageAddonService relatedPageAddonService,
			ILogger logger) {
			_relatedPageAddonService = relatedPageAddonService;
			_logger = logger;
		}

		public override int Execute(GetRelatedPageAddonOptions options) {
			bool success = TryGet(options, out GetRelatedPageAddonResponse response);
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
		}

		public bool TryGet(GetRelatedPageAddonOptions options, out GetRelatedPageAddonResponse response) {
			if (options is null) {
				response = Fail("options is required");
				return false;
			}
			try {
				RelatedPageAddonReadResult result = _relatedPageAddonService.Get(
					new RelatedPageAddonReadRequest(options.PackageName, options.EntitySchemaName));

				response = new GetRelatedPageAddonResponse {
					Success = true,
					EntitySchemaName = result.EntitySchemaName,
					EntitySchemaUId = result.EntitySchemaUId,
					PackageName = result.PackageName,
					PackageUId = result.PackageUId,
					AddonName = result.AddonName,
					TypeColumnUId = result.TypeColumnUId,
					PageCount = result.PageCount,
					Pages = result.Pages.Select(page => new RelatedPageEntryDto {
						PageSchemaUId = page.PageSchemaUId,
						PageSchemaName = page.PageSchemaName,
						IsDefault = page.IsDefault,
						IsAdd = page.IsAdd,
						IsSspDefault = page.IsSspDefault,
						Role = page.Role,
						RoleName = page.RoleName,
						TypeColumnValue = page.TypeColumnValue
					}).ToList()
				};
				return true;
			} catch (Exception ex) {
				response = Fail(ex.Message);
				_logger.WriteInfo($"  failed: {ex.Message}");
				return false;
			}
		}

		private static GetRelatedPageAddonResponse Fail(string error) =>
			new() { Success = false, Error = error };
	}

	/// <summary>
	/// Represents the <c>get-related-page-addon</c> response envelope.
	/// </summary>
	[DataContract]
	public sealed class GetRelatedPageAddonResponse {
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

		[DataMember(Name = "typeColumnUId")]
		[JsonProperty("typeColumnUId", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("typeColumnUId")]
		public string TypeColumnUId { get; set; }

		[DataMember(Name = "pageCount")]
		[JsonProperty("pageCount")]
		[JsonPropertyName("pageCount")]
		public int PageCount { get; set; }

		[DataMember(Name = "pages")]
		[JsonProperty("pages", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("pages")]
		public List<RelatedPageEntryDto> Pages { get; set; }

		[DataMember(Name = "error")]
		[JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("error")]
		public string Error { get; set; }
	}

	/// <summary>
	/// One decoded related-page entry in a <see cref="GetRelatedPageAddonResponse"/>.
	/// </summary>
	[DataContract]
	public sealed class RelatedPageEntryDto {
		[DataMember(Name = "pageSchemaUId")]
		[JsonProperty("pageSchemaUId", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("pageSchemaUId")]
		public string PageSchemaUId { get; set; }

		[DataMember(Name = "pageSchemaName")]
		[JsonProperty("pageSchemaName", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("pageSchemaName")]
		public string PageSchemaName { get; set; }

		[DataMember(Name = "isDefault")]
		[JsonProperty("isDefault")]
		[JsonPropertyName("isDefault")]
		public bool IsDefault { get; set; }

		[DataMember(Name = "isAdd")]
		[JsonProperty("isAdd")]
		[JsonPropertyName("isAdd")]
		public bool IsAdd { get; set; }

		[DataMember(Name = "isSspDefault")]
		[JsonProperty("isSspDefault")]
		[JsonPropertyName("isSspDefault")]
		public bool IsSspDefault { get; set; }

		[DataMember(Name = "role")]
		[JsonProperty("role", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("role")]
		public string Role { get; set; }

		[DataMember(Name = "roleName")]
		[JsonProperty("roleName", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("roleName")]
		public string RoleName { get; set; }

		[DataMember(Name = "typeColumnValue")]
		[JsonProperty("typeColumnValue", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("typeColumnValue")]
		public string TypeColumnValue { get; set; }
	}
}

namespace Clio.Command {
	using System;
	using System.Runtime.Serialization;
	using Clio.Command.PageModalSize;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;
	using JsonPropertyNameAttribute = System.Text.Json.Serialization.JsonPropertyNameAttribute;

	/// <summary>
	/// Options for the <c>get-page-modal-size</c> command: reads the modal window size of a Freedom UI modal page
	/// from its <c>AppearanceSettings</c> add-on.
	/// </summary>
	[Verb("get-page-modal-size", Aliases = ["page-modal-size-get"],
		HelpText = "Read the modal window size of a Freedom UI modal page (AppearanceSettings add-on)")]
	public class GetPageModalSizeOptions : EnvironmentOptions {
		[Option("page-schema-name", Required = true, HelpText = "Modal page schema name, e.g. 'UsrOrder_MiniPage'")]
		public string PageSchemaName { get; set; }

		[Option("package-name", Required = true, HelpText = "Package the page is read from")]
		public string PackageName { get; set; }
	}

	/// <summary>
	/// Reads the modal window size of a page. Thin CLI/MCP front-end over <see cref="IPageModalSizeService.Get"/>.
	/// Read-only.
	/// </summary>
	public sealed class GetPageModalSizeCommand : Command<GetPageModalSizeOptions> {
		private readonly IPageModalSizeService _pageModalSizeService;
		private readonly ILogger _logger;

		public GetPageModalSizeCommand(IPageModalSizeService pageModalSizeService, ILogger logger) {
			_pageModalSizeService = pageModalSizeService;
			_logger = logger;
		}

		public override int Execute(GetPageModalSizeOptions options) {
			bool success = TryGet(options, out GetPageModalSizeResponse response);
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
		}

		/// <summary>Reads the size and maps the outcome, success or failure, to the response envelope.</summary>
		public bool TryGet(GetPageModalSizeOptions options, out GetPageModalSizeResponse response) {
			if (options is null) {
				response = new GetPageModalSizeResponse { Success = false, Error = "options is required" };
				return false;
			}
			try {
				PageModalSizeReadResult result = _pageModalSizeService.Get(
					new PageModalSizeReadRequest(options.PackageName, options.PageSchemaName));
				response = new GetPageModalSizeResponse {
					Success = true,
					PageSchemaName = result.PageSchemaName,
					PackageName = result.PackageName,
					Size = result.Size,
					SizeValue = result.SizeValue,
					SourcePackageName = result.SourcePackageName,
					EffectiveSize = result.EffectiveSize,
					AvailableSizes = result.AvailableSizes,
					Warning = result.Warning
				};
				return true;
			} catch (Exception ex) {
				response = new GetPageModalSizeResponse { Success = false, Error = ex.Message };
				return false;
			}
		}
	}

	/// <summary>The <c>get-page-modal-size</c> response envelope.</summary>
	[DataContract]
	public sealed class GetPageModalSizeResponse : PageModalSizeResponseBase {
		/// <summary>The raw stored <c>modalSettings.size</c>; absent when no size is set.</summary>
		[DataMember(Name = "sizeValue")]
		[JsonProperty("sizeValue", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("sizeValue")]
		public int? SizeValue { get; set; }

		/// <summary>The package of the add-on layer the size comes from; absent when no size is set.</summary>
		[DataMember(Name = "sourcePackageName")]
		[JsonProperty("sourcePackageName", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("sourcePackageName")]
		public string SourcePackageName { get; set; }

		/// <summary>The size the window opens with, including the runtime default when no size is set.</summary>
		[DataMember(Name = "effectiveSize")]
		[JsonProperty("effectiveSize", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("effectiveSize")]
		public string EffectiveSize { get; set; }
	}
}

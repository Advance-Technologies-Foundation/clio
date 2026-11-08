namespace Clio.Command {
	using System;
	using System.Runtime.Serialization;
	using Clio.Command.PageModalSize;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;
	using JsonPropertyNameAttribute = System.Text.Json.Serialization.JsonPropertyNameAttribute;

	/// <summary>
	/// Options for the <c>set-page-modal-size</c> command: writes the modal window size of a Freedom UI modal page
	/// to its <c>AppearanceSettings</c> add-on.
	/// </summary>
	[Verb("set-page-modal-size", Aliases = ["page-modal-size"],
		HelpText = "Set the modal window size of a Freedom UI modal page (AppearanceSettings add-on)")]
	public class SetPageModalSizeOptions : EnvironmentOptions {
		[Option("page-schema-name", Required = true, HelpText = "Modal page schema name, e.g. 'UsrOrder_MiniPage'")]
		public string PageSchemaName { get; set; }

		[Option("package-name", Required = true, HelpText = "Package the AppearanceSettings add-on is written to")]
		public string PackageName { get; set; }

		[Option("size", Required = true, HelpText = "Window size: small (S), medium (M), large (L) or extra-large (XL)")]
		public string Size { get; set; }
	}

	/// <summary>
	/// Writes the modal window size of a page. Thin CLI/MCP front-end over <see cref="IPageModalSizeService.Set"/>.
	/// </summary>
	public sealed class SetPageModalSizeCommand : Command<SetPageModalSizeOptions> {
		private readonly IPageModalSizeService _pageModalSizeService;
		private readonly ILogger _logger;

		public SetPageModalSizeCommand(IPageModalSizeService pageModalSizeService, ILogger logger) {
			_pageModalSizeService = pageModalSizeService;
			_logger = logger;
		}

		public override int Execute(SetPageModalSizeOptions options) {
			bool success = TrySet(options, out SetPageModalSizeResponse response);
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
		}

		/// <summary>Writes the size and maps the outcome, success or failure, to the response envelope.</summary>
		public bool TrySet(SetPageModalSizeOptions options, out SetPageModalSizeResponse response) {
			if (options is null) {
				response = new SetPageModalSizeResponse { Success = false, Error = "options is required" };
				return false;
			}
			try {
				PageModalSizeWriteResult result = _pageModalSizeService.Set(
					new PageModalSizeWriteRequest(options.PackageName, options.PageSchemaName, options.Size));
				response = new SetPageModalSizeResponse {
					Success = true,
					PageSchemaName = result.PageSchemaName,
					PackageName = result.PackageName,
					PreviousSize = result.PreviousSize,
					PreviousSizeValue = result.PreviousSizeValue,
					PreviousSourcePackageName = result.PreviousSourcePackageName,
					Size = result.Size,
					Warning = result.Warning
				};
				return true;
			} catch (Exception ex) {
				response = new SetPageModalSizeResponse {
					Success = false,
					Error = ex.Message,
					AvailableSizes = ex is UnsupportedPageModalSizeException
						? PageModalSizeCatalog.Names
						: null
				};
				return false;
			}
		}
	}

	/// <summary>The <c>set-page-modal-size</c> response envelope.</summary>
	[DataContract]
	public sealed class SetPageModalSizeResponse : PageModalSizeResponseBase {
		/// <summary>The size name stored before the write; absent when none was set (the window opened small).</summary>
		[DataMember(Name = "previousSize")]
		[JsonProperty("previousSize", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("previousSize")]
		public string PreviousSize { get; set; }

		/// <summary>The raw size stored before the write; absent when none was set.</summary>
		[DataMember(Name = "previousSizeValue")]
		[JsonProperty("previousSizeValue", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("previousSizeValue")]
		public int? PreviousSizeValue { get; set; }

		/// <summary>The package of the add-on layer the previous size came from; absent when none was set.</summary>
		[DataMember(Name = "previousSourcePackageName")]
		[JsonProperty("previousSourcePackageName", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("previousSourcePackageName")]
		public string PreviousSourcePackageName { get; set; }
	}
}

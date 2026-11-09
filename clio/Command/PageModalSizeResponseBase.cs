namespace Clio.Command {
	using System.Collections.Generic;
	using System.Runtime.Serialization;
	using Newtonsoft.Json;
	using JsonPropertyNameAttribute = System.Text.Json.Serialization.JsonPropertyNameAttribute;

	/// <summary>
	/// Fields shared by the <c>get-page-modal-size</c> and <c>set-page-modal-size</c> response envelopes.
	/// </summary>
	[DataContract]
	public abstract class PageModalSizeResponseBase {
		[DataMember(Name = "success")]
		[JsonProperty("success")]
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[DataMember(Name = "pageSchemaName")]
		[JsonProperty("pageSchemaName", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("pageSchemaName")]
		public string PageSchemaName { get; set; }

		/// <summary>The package the page is read from, or the add-on was written to.</summary>
		[DataMember(Name = "packageName")]
		[JsonProperty("packageName", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("packageName")]
		public string PackageName { get; set; }

		/// <summary>The stored size name after the operation; absent when no size is set.</summary>
		[DataMember(Name = "size")]
		[JsonProperty("size", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("size")]
		public string Size { get; set; }

		/// <summary>The accepted sizes: always on a read, only on a refused size for a write.</summary>
		[DataMember(Name = "availableSizes")]
		[JsonProperty("availableSizes", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("availableSizes")]
		public IReadOnlyList<string> AvailableSizes { get; set; }

		/// <summary>
		/// Non-fatal warnings: a layer above the package overrides the size, the add-on metadata is unreadable, the
		/// size the window opens with or the package of the size could not be determined, or a post-save cache reset
		/// or rebuild failed.
		/// </summary>
		[DataMember(Name = "warning")]
		[JsonProperty("warning", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("warning")]
		public string Warning { get; set; }

		[DataMember(Name = "error")]
		[JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("error")]
		public string Error { get; set; }
	}
}

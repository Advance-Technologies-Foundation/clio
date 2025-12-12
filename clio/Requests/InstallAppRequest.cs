using System.Text.Json.Serialization;

namespace Clio.Requests
{
	/// <summary>
	/// Request data for installing an application.
	/// </summary>
	public class InstallAppRequest
	{
		/// <summary>
		/// Application name.
		/// </summary>
		[JsonPropertyName("Name")]
		public string Name { get; set; }

		/// <summary>
		/// Application code.
		/// </summary>
		[JsonPropertyName("Code")]
		public string Code { get; set; }

		/// <summary>
		/// Name of the uploaded zip package file.
		/// </summary>
		[JsonPropertyName("ZipPackageName")]
		public string ZipPackageName { get; set; }

		/// <summary>
		/// Last update timestamp.
		/// </summary>
		[JsonPropertyName("LastUpdateString")]
		public int LastUpdateString { get; set; }

		/// <summary>
		/// Optional flag to check for compilation errors during installation.
		/// When null, the property will not be included in the JSON.
		/// </summary>
		[JsonPropertyName("CheckCompilationErrors")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public bool? CheckCompilationErrors { get; set; }
	}
}


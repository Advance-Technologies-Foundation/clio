using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Resolves the pre-created artifact identifiers used by <c>create-dcm</c> and
/// <c>create-business-process</c>. Identifiers are loaded from
/// <c>&lt;clio-home&gt;/artifact-config.json</c> on each call so they can be changed without a
/// rebuild; when the file is absent or malformed the compiled-in defaults are used.
/// </summary>
internal static class ArtifactLinkConfig {

	internal const string DefaultDcmId = "ab83d6f5-ae44-47b7-897c-a49273412351";
	internal const string DefaultBpId = "ac620a68-2361-47f7-bb54-dcfa44baf5cf";

	private static readonly string ConfigFilePath =
		Path.Combine(ClioRuntimePaths.Home, "artifact-config.json");

	/// <summary>
	/// Returns the resolved DCM and business-process identifiers, falling back to the
	/// compiled-in defaults for any value missing from the on-disk config file.
	/// </summary>
	public static (string DcmId, string BpId) Load() {
		try {
			if (File.Exists(ConfigFilePath)) {
				string json = File.ReadAllText(ConfigFilePath);
				ConfigFile cfg = JsonSerializer.Deserialize<ConfigFile>(json,
					new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
				if (cfg is not null) {
					return (
						string.IsNullOrWhiteSpace(cfg.DcmId) ? DefaultDcmId : cfg.DcmId.Trim(),
						string.IsNullOrWhiteSpace(cfg.BpId) ? DefaultBpId : cfg.BpId.Trim());
				}
			}
		} catch (IOException) {
			// Unreadable file → fall through to defaults so the tool still works.
		} catch (JsonException) {
			// Malformed file → fall through to defaults.
		}
		return (DefaultDcmId, DefaultBpId);
	}

	/// <summary>
	/// Builds the scheme+host+port prefix for a Creatio environment URL, stripping any path
	/// and trailing slash (e.g. <c>http://host:8081/0/Shell</c> → <c>http://host:8081</c>).
	/// </summary>
	public static string ResolveBaseUrl(string environmentUri) {
		if (string.IsNullOrWhiteSpace(environmentUri)) {
			return string.Empty;
		}
		if (Uri.TryCreate(environmentUri, UriKind.Absolute, out Uri parsed)) {
			return parsed.GetLeftPart(UriPartial.Authority);
		}
		return environmentUri.TrimEnd('/');
	}

	/// <summary>Composes the DCM designer link for the given base URL and identifier.</summary>
	public static string BuildDcmLink(string baseUrl, string dcmId) =>
		$"{baseUrl}/0/Nui/ViewModule.aspx?vm=DcmDesigner#case/{dcmId}";

	/// <summary>Composes the process designer link for the given base URL and identifier.</summary>
	public static string BuildProcessLink(string baseUrl, string bpId) =>
		$"{baseUrl}/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/{bpId}";

	private sealed record ConfigFile(
		[property: JsonPropertyName("dcmId")] string DcmId,
		[property: JsonPropertyName("bpId")] string BpId);
}

using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Loads versioned web→mobile page-conversion rules. Mirrors the academy component-registry
/// pipeline: the underlying <see cref="IWebToMobilePageConversionRulesRegistryClient"/> resolves
/// bytes per version through the local-override → cache → CDN chain. The CDN rules file is not
/// published yet, so when the client cannot serve the rules this catalog falls back to the bundled
/// rules shipped with clio. Publishing the CDN file later switches the source with no code change.
/// </summary>
public interface IWebToMobilePageConversionRulesCatalog {
	/// <summary>Returns the web→mobile conversion rules for the requested version (or "latest").</summary>
	Task<WebToMobilePageConversionRules> GetRulesAsync(string requestedVersion, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class WebToMobilePageConversionRulesCatalog : IWebToMobilePageConversionRulesCatalog {

	/// <summary>Manifest name of the bundled fallback rules resource (today's source of truth).</summary>
	internal const string BundledResourceName = "Clio.Command.McpServer.Data.WebToMobilePageConversionRules.json";

	private static readonly JsonSerializerOptions Options = new() {
		PropertyNameCaseInsensitive = true
	};

	private readonly IWebToMobilePageConversionRulesRegistryClient _client;

	public WebToMobilePageConversionRulesCatalog(IWebToMobilePageConversionRulesRegistryClient client) {
		_client = client ?? throw new ArgumentNullException(nameof(client));
	}

	/// <inheritdoc />
	public async Task<WebToMobilePageConversionRules> GetRulesAsync(string requestedVersion, CancellationToken cancellationToken = default) {
		string version = string.IsNullOrWhiteSpace(requestedVersion)
			? ComponentRegistryClient.LatestVersion
			: requestedVersion.Trim();
		try {
			ComponentRegistryFetchResult fetch = await _client.GetAsync(version, cancellationToken).ConfigureAwait(false);
			using (fetch.Content) {
				WebToMobilePageConversionRules rules = ParseStream(fetch.Content);
				if (rules is not null) {
					return rules;
				}
			}
		} catch (Exception ex) when (
			ex is ComponentRegistryUnavailableException
			or System.Text.Json.JsonException
			or IOException
			or System.Net.Http.HttpRequestException) {
			// CDN rules file not published yet / unreadable / parse error —
			// fall back to the bundled rules shipped with clio. Cancellation is not caught (it propagates).
		}
		return LoadBundled();
	}

	/// <summary>Parses a rules JSON stream. Exposed for tests.</summary>
	internal static WebToMobilePageConversionRules ParseStream(Stream stream) {
		using var reader = new StreamReader(stream);
		string json = reader.ReadToEnd();
		return string.IsNullOrWhiteSpace(json)
			? null
			: JsonSerializer.Deserialize<WebToMobilePageConversionRules>(json, Options);
	}

	/// <summary>Loads the bundled fallback rules embedded in the clio assembly.</summary>
	internal static WebToMobilePageConversionRules LoadBundled() {
		Assembly assembly = typeof(WebToMobilePageConversionRulesCatalog).Assembly;
		using Stream stream = assembly.GetManifestResourceStream(BundledResourceName)
			?? throw new InvalidOperationException(
				$"Bundled conversion-rules resource '{BundledResourceName}' was not found in the clio assembly.");
		return ParseStream(stream) ?? new WebToMobilePageConversionRules();
	}
}

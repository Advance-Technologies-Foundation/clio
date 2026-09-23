using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Tools.MobilePageConverter;

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
		PropertyNameCaseInsensitive = true,
		// A componentRemovals filter is discriminated by `filterType`, and the rules document is authored by
		// hand in another repository — so the discriminator must be findable wherever the author put it.
		// BindingsModule.CreateMcpSerializerOptions sets the same option for the same class of reason.
		AllowOutOfOrderMetadataProperties = true
	};

	/// <summary>
	/// The reason codes a <c>componentRemovals</c> rule may report. A NARROW subset of the published
	/// vocabulary, and narrow deliberately.
	/// </summary>
	/// <remarks>
	/// <see cref="ReasonCodes"/> is ONE flat vocabulary spanning four record kinds — a dropped element, a
	/// dropped or flagged request binding, a dropped page business rule, a skipped normalization. Accepting all
	/// of it would let a rules push put <c>flag-request-unmapped</c> on a <c>droppedElements</c> record, whose
	/// published article describes a binding that was KEPT, or <c>drop-inherited-chrome</c>, whose article tells
	/// the caller the loss is not real and must not be re-added. A code that means the WRONG thing is worse
	/// than an unknown one, because the caller acts on it.
	/// <para>
	/// Written as constant references rather than reflected over the class, so renaming a code breaks this at
	/// COMPILE time — and so no trimmer can quietly empty the set, which for a fail-closed check would turn
	/// every conversion into a thrown exception.
	/// </para>
	/// </remarks>
	private static readonly IReadOnlySet<string> RemovalReasonCodes = new HashSet<string>(StringComparer.Ordinal) {
		ReasonCodes.DropUnsupportedRequest,
		ReasonCodes.DropExcludedByRule
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
		if (string.IsNullOrWhiteSpace(json)) {
			return null;
		}
		WebToMobilePageConversionRules rules =
			JsonSerializer.Deserialize<WebToMobilePageConversionRules>(json, Options);
		ValidateComponentRemovals(rules);
		return rules;
	}

	/// <summary>
	/// Refuses a rules document whose <c>componentRemovals</c> names a reason code clio does not publish.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Thrown as a <see cref="JsonException"/> on purpose: <see cref="GetRulesAsync"/> already catches that to
	/// mean "this document is unusable", so a CDN file with an invented code degrades to the BUNDLED rules
	/// exactly as an unparseable one does, rather than reaching a caller. The BLAST RADIUS of that is the whole
	/// document — templates, components, requests and every other section revert with it, and nothing logs the
	/// substitution. That is the right polarity for this section (a rule that quietly never fires means
	/// unsupported actions SHIP) and the wrong one for a rules author, who gets no signal their push was
	/// refused. Weigh both before adding a throw here.
	/// </para>
	/// <para>
	/// The reason vocabulary is a TWO-REPOSITORY contract — clio pins <see cref="ReasonCodes"/>, the published
	/// guidance article documents each one — so a code no article explains is worse than no removal at all: the
	/// response would tell the caller an element was dropped and hand them a token nothing defines
	/// (invariant 9.2). Validating on LOAD rather than at emission is what keeps that impossible instead of
	/// merely unlikely.
	/// </para>
	/// </remarks>
	private static void ValidateComponentRemovals(WebToMobilePageConversionRules rules) {
		foreach (ComponentRemovalRule rule in rules?.ComponentRemovals ?? []) {
			if (rule is null) {
				throw new JsonException("componentRemovals carries a null rule.");
			}
			if (rule.Type is not { Length: > 0 }) {
				throw new JsonException("A componentRemovals rule declares no 'type'.");
			}
			if (rule.Filters is null) {
				throw new JsonException($"The componentRemovals rule for '{rule.Type}' declares no 'filters'.");
			}
			if (rule.Reason is { Length: > 0 } reason && !RemovalReasonCodes.Contains(reason)) {
				throw new JsonException(
					$"The componentRemovals rule for '{rule.Type}' names the reason code '{reason}', which is not "
					+ "valid on a droppedElements record.");
			}
			ValidateRemovalFilter(rule.Filters, rule.Type);
		}
	}

	/// <summary>
	/// Refuses a filter tree that would silently match nothing. Every one of these is a plausible authoring
	/// slip, and every one of them currently degrades to a rule that quietly never fires — which for THIS
	/// section means unsupported actions ship, the exact outcome the bundled fallback exists to prevent.
	/// </summary>
	private static void ValidateRemovalFilter(ComponentPropertyFilter filter, string type) {
		switch (filter) {
			case ComponentPropertyGroupFilter group:
				if (group.Items is not { Count: > 0 }) {
					throw new JsonException(
						$"A componentRemovals Group filter for '{type}' declares no 'items'.");
				}
				foreach (ComponentPropertyFilter item in group.Items) {
					if (item is null) {
						throw new JsonException(
							$"A componentRemovals Group filter for '{type}' carries a null item.");
					}
					ValidateRemovalFilter(item, type);
				}
				break;
			case ComponentPropertyIsEmptyFilter isEmpty:
				if (isEmpty.LeftExpression is not { Length: > 0 }) {
					throw new JsonException(
						$"A componentRemovals IsEmpty filter for '{type}' declares no 'leftExpression'.");
				}
				break;
			default:
				throw new JsonException($"Unsupported componentRemovals filter on '{type}'.");
		}
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

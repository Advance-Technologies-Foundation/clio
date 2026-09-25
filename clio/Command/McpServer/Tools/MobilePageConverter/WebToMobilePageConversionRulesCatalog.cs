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
	/// It is a set of ONE, and the singleton is the point rather than a shape waiting to be filled. Even
	/// <c>drop-excluded-by-rule</c> - the nearest neighbour, minted by the sibling removal pass onto the same
	/// record kind - is refused here: its article defines it as a deliberate exclusion that is NOT conversion
	/// loss, and it carries positional params this pass does not produce. Widening the set is a decision about
	/// what the response MEANS, so it belongs in a commit that says so, not in whatever push first needs it.
	/// </para>
	/// <para>
	/// Written as constant references rather than reflected over the class, so renaming a code breaks this at
	/// COMPILE time — and so no trimmer can quietly empty the set, which for a fail-closed check would turn
	/// every conversion into a thrown exception.
	/// </para>
	/// </remarks>
	private static readonly IReadOnlySet<string> RemovalReasonCodes = new HashSet<string>(StringComparer.Ordinal) {
		ReasonCodes.DropUnsupportedRequest
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
		WebToMobilePageConversionRules rules;
		try {
			rules = JsonSerializer.Deserialize<WebToMobilePageConversionRules>(json, Options);
		} catch (NotSupportedException ex) {
			// System.Text.Json reports a MISSING or miscased polymorphic discriminator as NotSupportedException,
			// not JsonException - and `filterType` is the first discriminator any section of this document has.
			// GetRulesAsync catches JsonException to mean "unusable document, use the bundled rules", so left
			// alone this one escapes the catalog entirely and every get-mobile-page-conversion-guide call fails
			// until the CDN file is fixed. Restated as the exception the fallback is written against.
			throw new JsonException(
				"A componentRemovals filter does not declare a recognizable 'filterType' discriminator.", ex);
		}
		ValidateRules(rules);
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
	private static void ValidateRules(WebToMobilePageConversionRules rules) {
		ValidateActionComponents(rules);
		ValidateComponentRemovals(rules);
	}

	/// <summary>
	/// Refuses an <c>actionComponents</c> section that would silently disable the drop rules.
	/// </summary>
	/// <remarks>
	/// This section decides which component types are removed OUTRIGHT when their request does not convert -
	/// the behaviour that was hard-coded to <c>crt.Button</c> before ENG-96178 and is now read from a
	/// CDN-controlled document. Both failure directions are silent, which is why the check exists at all:
	/// <c>[{ "type": "" }]</c> has a non-zero <c>Count</c>, so <c>ActionComponentPropertiesOf</c> prefers it
	/// over the bundled section and then filters the entry away - leaving an EMPTY gate, under which
	/// unsupported actions ship again and nothing in the response says the rule stopped running. A refusal
	/// here reaches <see cref="GetRulesAsync"/>'s fallback instead, so the shipped list keeps running.
	/// </remarks>
	private static void ValidateActionComponents(WebToMobilePageConversionRules rules) {
		foreach (ActionComponentRule rule in rules?.ActionComponents ?? []) {
			if (rule is null) {
				throw new JsonException("actionComponents carries a null rule.");
			}
			if (string.IsNullOrWhiteSpace(rule.Type)) {
				throw new JsonException("An actionComponents rule declares no 'type'.");
			}
			foreach (string name in rule.ActionPropertyNames ?? []) {
				if (string.IsNullOrWhiteSpace(name)) {
					throw new JsonException(
						$"The actionComponents rule for '{rule.Type}' declares a blank action property name.");
				}
			}
		}
	}

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
				// The evaluator reads anything that is not `or` as `and`. That is the right DEFAULT for an
				// absent value and the wrong answer for `"any"` or `"either"`, where the author asked for the
				// other operation and gets the narrower one with no signal at all.
				if (group.LogicalOperation is { Length: > 0 } operation
					&& !string.Equals(
						operation, ComponentPropertyLogicalOperations.And, StringComparison.OrdinalIgnoreCase)
					&& !string.Equals(
						operation, ComponentPropertyLogicalOperations.Or, StringComparison.OrdinalIgnoreCase)) {
					throw new JsonException(
						$"A componentRemovals Group filter for '{type}' names the logical operation "
						+ $"'{operation}', which is neither 'and' nor 'or'.");
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

	/// <summary>
	/// The bundled fallback rules embedded in the clio assembly, parsed once.
	/// </summary>
	/// <remarks>
	/// Memoized because it is read on the fallback path AND by the two sections whose absence falls back to
	/// the bundled copy (<c>actionComponents</c>, <c>componentRemovals</c>) - up to three parses of the same
	/// immutable embedded resource per conversion. <see cref="Lazy{T}"/> in its default thread-safe mode,
	/// since the catalog is a singleton served to concurrent MCP calls.
	/// </remarks>
	internal static WebToMobilePageConversionRules LoadBundled() => BundledRules.Value;

	private static readonly Lazy<WebToMobilePageConversionRules> BundledRules = new(LoadBundledCore);

	private static WebToMobilePageConversionRules LoadBundledCore() {
		Assembly assembly = typeof(WebToMobilePageConversionRulesCatalog).Assembly;
		using Stream stream = assembly.GetManifestResourceStream(BundledResourceName)
			?? throw new InvalidOperationException(
				$"Bundled conversion-rules resource '{BundledResourceName}' was not found in the clio assembly.");
		return ParseStream(stream) ?? new WebToMobilePageConversionRules();
	}
}

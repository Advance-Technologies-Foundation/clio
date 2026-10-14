using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Warnings about a <c>mappings[]</c> / <c>addMapping</c> entry whose target is a PROCESS parameter
/// (<c>targetProcessParameter</c>). Such a mapping is not an assignment made when the flow reaches it: it is the
/// parameter's ONE stored value source, a binding the runtime evaluates whenever the parameter is read.
/// <para>Measured on a sandbox stand in October 2026 (ENG-103081): a parameter bound to a Read data
/// column reads empty in a Formula placed BEFORE the read and the column value in one placed after it; a second
/// mapping onto the same parameter silently replaces the first (the LAST one listed is saved); and in a process
/// with two signal starts, each mapping its own branch's read into one shared parameter, the run started by the
/// first start reads the parameter EMPTY, because the surviving binding points at the other branch's element,
/// which never ran. A Formula element with <c>resultProcessParameter</c> on each branch assigns per run, and wins
/// over a binding that is still stored.</para>
/// <para>Two warnings follow from that, and only those two, because each is true whenever it fires:</para>
/// <list type="bullet">
/// <item>the same process parameter is bound more than once - in one payload, or on a modify over a binding the
/// process already stored - so all but one binding is lost;</item>
/// <item>in a process with SEVERAL start events, the binding's source element is not reached from every one of
/// them, so on a run started from the others it never executes and the parameter reads its default.</item>
/// </list>
/// <para>NOT warned: a binding whose source element runs after the start of a single-start process. That is the
/// supported "expose an element output as a process output" pattern and reads correctly once the element ran;
/// warning on it would be false.</para>
/// <para>Pure and tolerant: an unparseable payload, a missing read-back or an element it cannot find produces no
/// warning rather than an exception, because these run after a write that already succeeded.</para>
/// </summary>
public static class ProcessParameterBindingWarnings {

	private const string MappingsKey = "mappings";
	private const string MappingKey = "mapping";
	private const string ElementsKey = "elements";
	private const string FlowsKey = "flows";
	private const string OpKey = "op";
	private const string AddMappingOp = "addMapping";
	private const string TargetProcessParameterKey = "targetProcessParameter";
	private const string SourceElementKey = "sourceElement";
	private const string SourceElementParameterKey = "sourceElementParameter";
	private const string SourceColumnKey = "sourceColumn";
	private const string ProcessParameterKey = "processParameter";
	private const string ValueKey = "value";
	private const string ExpressionKey = "expression";

	// Stored value sources that are not a binding: an unset parameter, and a literal default - the shape a
	// parameter's own declared value takes. Re-mapping either replaces nothing a caller would miss.
	private static readonly string[] NonBindingSources = ["None", "ConstValue"];

	private const int MaxRenderedTextLength = 120;

	// A whole element output as the platform stores it when the server cannot name a column: the element and its
	// parameter by UId. Resolved back to names so re-applying an identical binding is not reported as a change.
	private static readonly Regex ElementParameterMetaPath = new(
		@"^\[#(?:\[IsOwnerSchema:false\]\.)?(?:\[IsSchema:false\]\.)?\[Element:\{(?<element>[0-9a-fA-F-]{36})\}\]\."
		+ @"\[Parameter:\{(?<parameter>[0-9a-fA-F-]{36})\}\]#\]$",
		RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	/// <summary>One process-parameter binding a payload asks for.</summary>
	/// <param name="Parameter">The target process parameter, trimmed.</param>
	/// <param name="SourceElement">The source element as the caller named it (name or UId), or <c>null</c>.</param>
	/// <param name="SourceParameter">The source element's output parameter, or <c>null</c>.</param>
	/// <param name="SourceColumn">The column of that output's record, or <c>null</c>.</param>
	/// <param name="Source">The source rendered for a reader, e.g. <c>ReadA.ResultEntity.Name</c>.</param>
	public sealed record Binding(string Parameter, string? SourceElement, string? SourceParameter,
		string? SourceColumn, string Source);

	/// <summary>
	/// The warnings for a BUILD descriptor: every process parameter bound more than once in <c>mappings[]</c>, and
	/// in a process with several start events every binding whose source element some start does not reach.
	/// </summary>
	/// <param name="descriptorJson">The build descriptor JSON as the command received it.</param>
	public static IReadOnlyList<string> ForDescriptor(string descriptorJson) {
		if (BlockExpectationJson.Parse(descriptorJson) is not JsonObject descriptor) {
			return [];
		}

		List<Binding> bindings = descriptor[MappingsKey] is JsonArray mappings
			? [.. mappings.OfType<JsonObject>().Select(ReadBinding).OfType<Binding>()]
			: [];
		if (bindings.Count == 0) {
			return [];
		}

		List<string> warnings = [.. DuplicateWarnings(bindings, "mappings[]")];
		BindingGraph graph = BindingGraph.FromDescriptor(descriptor);
		warnings.AddRange(UnreachedSourceWarnings(LastPerParameter(bindings), graph));
		return warnings;
	}

	/// <summary>
	/// Whether a MODIFY operations array binds a process parameter through <c>addMapping</c>, so
	/// <see cref="ForOperations"/> needs the read taken after the write to judge it.
	/// </summary>
	/// <param name="operationsJson">The operations array as the command received it.</param>
	public static bool BindsAProcessParameter(string operationsJson) =>
		BlockExpectationJson.Parse(operationsJson) is JsonArray operations
		&& operations.OfType<JsonObject>().Any(op =>
			string.Equals(BlockExpectationJson.ReadText(op[OpKey]), AddMappingOp, StringComparison.OrdinalIgnoreCase)
			&& op[MappingKey] is JsonObject mapping
			&& !string.IsNullOrWhiteSpace(BlockExpectationJson.ReadText(mapping[TargetProcessParameterKey])));

	/// <summary>
	/// The warnings for a MODIFY operations array: a process parameter bound by more than one <c>addMapping</c>
	/// in the array, one whose <c>addMapping</c> replaced a binding to a DIFFERENT element output the process
	/// already stored, and - judged on the saved graph - an added binding whose source element some start event
	/// does not reach. A binding the saved read-back no longer carries (a later operation in the batch replaced
	/// it with a literal) earns neither of the last two.
	/// </summary>
	/// <param name="operationsJson">The operations array JSON as the command received it.</param>
	/// <param name="before">The process read before the edit; <c>null</c> when that read failed.</param>
	/// <param name="after">The process read back after the edit; <c>null</c> when that read failed.</param>
	public static IReadOnlyList<string> ForOperations(string operationsJson, DescribeProcessResult? before,
		DescribeProcessResult? after) {
		if (BlockExpectationJson.Parse(operationsJson) is not JsonArray operations) {
			return [];
		}

		List<Binding> bindings = [.. operations.OfType<JsonObject>()
			.Where(op => string.Equals(BlockExpectationJson.ReadText(op[OpKey]), AddMappingOp,
				StringComparison.OrdinalIgnoreCase))
			.Select(op => op[MappingKey] as JsonObject)
			.OfType<JsonObject>()
			.Select(ReadBinding)
			.OfType<Binding>()];
		if (bindings.Count == 0) {
			return [];
		}

		List<string> warnings = [.. DuplicateWarnings(bindings, "this addMapping batch")];
		List<Binding> saved = [.. LastPerParameter(bindings).Where(binding => IsStillBound(binding, after))];
		warnings.AddRange(ReplacedBindingWarnings(saved, before));
		if (after is not null) {
			warnings.AddRange(UnreachedSourceWarnings(saved, BindingGraph.FromDescribed(after)));
		}
		return warnings;
	}

	private static Binding? ReadBinding(JsonObject mapping) {
		string? parameter = BlockExpectationJson.ReadText(mapping[TargetProcessParameterKey])?.Trim();
		if (string.IsNullOrEmpty(parameter)) {
			return null;
		}

		string? element = BlockExpectationJson.ReadText(mapping[SourceElementKey])?.Trim();
		if (!string.IsNullOrEmpty(element)) {
			string? output = BlockExpectationJson.ReadText(mapping[SourceElementParameterKey])?.Trim();
			string? column = BlockExpectationJson.ReadText(mapping[SourceColumnKey])?.Trim();
			return new Binding(parameter, element, output, column, Join(element, output, column));
		}

		string? processParameter = BlockExpectationJson.ReadText(mapping[ProcessParameterKey]);
		if (!string.IsNullOrWhiteSpace(processParameter)) {
			return new Binding(parameter, null, null, null, $"process parameter {Quote(processParameter.Trim())}");
		}
		string? expression = BlockExpectationJson.ReadText(mapping[ExpressionKey]);
		if (!string.IsNullOrWhiteSpace(expression)) {
			return new Binding(parameter, null, null, null, $"expression {Quote(expression.Trim())}");
		}
		// A value may be any JSON scalar; render it as written.
		string value = mapping[ValueKey]?.ToJsonString() ?? "(no source)";
		return new Binding(parameter, null, null, null, $"value {Clip(value)}");
	}

	// Only a read-back that POSITIVELY shows a literal or nothing stored contradicts the payload; without one, or
	// when it does not list the parameter, the payload is taken at its word.
	private static bool IsStillBound(Binding binding, DescribeProcessResult? after) {
		DescribedParameter? stored = after is null ? null : FindParameter(after, binding.Parameter);
		return stored is null || !NonBindingSources.Contains(stored.Source, StringComparer.OrdinalIgnoreCase);
	}

	private static IEnumerable<string> DuplicateWarnings(List<Binding> bindings, string where) =>
		bindings
			.GroupBy(binding => binding.Parameter, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1)
			.Select(group => {
				Binding kept = group.Last();
				return $"{where} binds process parameter {Quote(kept.Parameter)} {group.Count()} times "
					+ $"({string.Join("; ", group.Select(binding => binding.Source))}). A process parameter keeps ONE "
					+ $"binding, so only the last one listed ({kept.Source}) is saved. A targetProcessParameter "
					+ "mapping is the value source the parameter is read through, not an assignment made when a "
					+ "branch runs: to set it per branch (e.g. one shared parameter fed by each start of a "
					+ "multi-start process) add a formulaTask on each branch with formula.resultProcessParameter "
					+ $"{Quote(kept.Parameter)}.";
			});

	// Only a stored binding to an ELEMENT output is reported as replaced: that is the binding a branch relies on.
	// A stored formula, process-parameter or system-value source is a default the edit may well mean to replace,
	// and its read-back cannot be compared with the request reliably, so it is left alone rather than guessed at.
	private static IEnumerable<string> ReplacedBindingWarnings(List<Binding> bindings,
		DescribeProcessResult? before) {
		if (before?.Parameters is null) {
			yield break;
		}

		foreach (Binding binding in bindings) {
			DescribedParameter? existing = FindParameter(before, binding.Parameter);
			ElementSource? previous = existing is null ? null : StoredElementSource(existing, before);
			if (previous is null || IsSameSource(previous, binding, before)) {
				continue;
			}

			yield return $"addMapping replaced the existing binding of process parameter {Quote(binding.Parameter)} "
				+ $"(was {previous.Render()}, now {binding.Source}): a process parameter keeps ONE binding. If both "
				+ "were meant to apply - e.g. one per branch - bind neither and add a formulaTask on each branch with "
				+ $"formula.resultProcessParameter {Quote(binding.Parameter)}; a Formula assignment also wins over "
				+ "a binding that stays stored.";
		}
	}

	private static IEnumerable<string> UnreachedSourceWarnings(List<Binding> bindings, BindingGraph graph) {
		if (graph.Starts.Count < 2) {
			yield break;
		}

		foreach (Binding binding in bindings) {
			if (binding.SourceElement is null || !graph.Contains(binding.SourceElement)) {
				continue;
			}

			// A start whose path assigns the parameter with a Formula is covered: the assignment wins over the
			// binding, which is the remedy these warnings recommend, so warning there would be false.
			List<string> missed = [.. graph.Starts.Where(start => !graph.Reaches(start, binding.SourceElement)
				&& !graph.FormulaWriters(binding.Parameter).Any(writer => graph.Reaches(start, writer)))];
			if (missed.Count == 0) {
				continue;
			}

			yield return $"Process parameter {Quote(binding.Parameter)} is bound to {binding.Source}, which a run "
				+ $"started by {string.Join(", ", missed.Select(Quote))} does not reach: on such a run "
				+ $"{Quote(binding.SourceElement)} never executes and {Quote(binding.Parameter)} reads its "
				+ "default (empty) value. If that branch reads it too, assign it there with a formulaTask "
				+ $"(formula.resultProcessParameter {Quote(binding.Parameter)}).";
		}
	}

	/// <summary>A stored binding to an element output, by names.</summary>
	private sealed record ElementSource(string Element, string Parameter, string? Column) {
		internal string Render() => Join(Element, Parameter, Column);
	}

	private static DescribedParameter? FindParameter(DescribeProcessResult process, string name) =>
		process.Parameters?.FirstOrDefault(parameter =>
			string.Equals(parameter?.Name, name, StringComparison.OrdinalIgnoreCase));

	private static ElementSource? StoredElementSource(DescribedParameter existing, DescribeProcessResult before) {
		if (!string.IsNullOrWhiteSpace(existing.SourceElement)
			&& !string.IsNullOrWhiteSpace(existing.SourceElementParameter)) {
			return new ElementSource(existing.SourceElement, existing.SourceElementParameter, existing.SourceColumn);
		}
		if (string.IsNullOrWhiteSpace(existing.Value)
			|| NonBindingSources.Contains(existing.Source, StringComparer.OrdinalIgnoreCase)) {
			return null;
		}

		Match match = ElementParameterMetaPath.Match(existing.Value.Trim());
		if (!match.Success) {
			return null;
		}
		DescribedElement? element = before.Elements?.FirstOrDefault(e =>
			string.Equals(e?.Uid, match.Groups["element"].Value, StringComparison.OrdinalIgnoreCase));
		DescribedParameter? output = element?.Parameters?.FirstOrDefault(p =>
			string.Equals(p?.UId, match.Groups["parameter"].Value, StringComparison.OrdinalIgnoreCase));
		return output is null || string.IsNullOrWhiteSpace(element.Name) || string.IsNullOrWhiteSpace(output.Name)
			? null
			: new ElementSource(element.Name, output.Name, null);
	}

	// The request may name the element by UId where the read-back names it, so both sides are compared by name.
	private static bool IsSameSource(ElementSource stored, Binding binding, DescribeProcessResult before) {
		if (binding.SourceElement is null) {
			return false;
		}
		string requested = BlockExpectationJson.ResolveElement(before, binding.SourceElement)?.Name
			?? binding.SourceElement;
		return string.Equals(stored.Element, requested, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(stored.Parameter, binding.SourceParameter, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(stored.Column ?? string.Empty, binding.SourceColumn ?? string.Empty,
				StringComparison.OrdinalIgnoreCase);
	}

	// Only the binding that is saved matters for reachability and for what it replaced.
	private static List<Binding> LastPerParameter(List<Binding> bindings) =>
		[.. bindings.GroupBy(binding => binding.Parameter, StringComparer.OrdinalIgnoreCase).Select(g => g.Last())];

	private static string Join(string element, string? parameter, string? column) =>
		string.Join(".", new[] { element, parameter, column }
			.Where(part => !string.IsNullOrWhiteSpace(part))
			.Select(part => part.Trim()));

	private static string Quote(string text) => $"'{Clip(text)}'";

	private static string Clip(string text) =>
		text.Length <= MaxRenderedTextLength ? text : text[..MaxRenderedTextLength] + "...";

	/// <summary>
	/// The element names, the start events among them, the Formula elements writing each process parameter and the
	/// directed flows between them. Named apart from <see cref="ProcessGraph"/>, the validator's planned graph,
	/// because this one is read from a descriptor or a describe read-back and answers only reachability.
	/// </summary>
	private sealed class BindingGraph {
		private readonly HashSet<string> _elements = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, List<string>> _next = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, string> _uidToName = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, List<string>> _formulaWriters = new(StringComparer.OrdinalIgnoreCase);

		internal List<string> Starts { get; } = [];

		internal static BindingGraph FromDescriptor(JsonObject descriptor) {
			BindingGraph graph = new();
			if (descriptor[ElementsKey] is JsonArray elements) {
				foreach (JsonObject element in elements.OfType<JsonObject>()) {
					string? name = BlockExpectationJson.ReadText(element["name"]);
					graph.AddElement(name, null, BlockExpectationJson.ReadText(element["type"]));
					graph.AddFormulaWriter(name,
						BlockExpectationJson.ReadText((element["formula"] as JsonObject)?["resultProcessParameter"]));
				}
			}
			if (descriptor[FlowsKey] is JsonArray flows) {
				foreach (JsonObject flow in flows.OfType<JsonObject>()) {
					graph.AddFlow(BlockExpectationJson.ReadText(flow["source"]),
						BlockExpectationJson.ReadText(flow["target"]));
				}
			}
			return graph;
		}

		internal static BindingGraph FromDescribed(DescribeProcessResult described) {
			BindingGraph graph = new();
			foreach (DescribedElement element in described.Elements ?? []) {
				graph.AddElement(element?.Name, element?.Uid, element?.BuildType);
				graph.AddFormulaWriter(element?.Name, element?.Formula?.Target?.ResultProcessParameter);
			}
			foreach (DescribedFlow flow in described.Flows ?? []) {
				graph.AddFlow(flow?.Source, flow?.Target);
			}
			return graph;
		}

		internal bool Contains(string element) => _elements.Contains(Canonical(element));

		internal IReadOnlyList<string> FormulaWriters(string parameter) =>
			_formulaWriters.GetValueOrDefault(parameter) ?? [];

		internal bool Reaches(string from, string to) {
			string target = Canonical(to);
			HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase) { from };
			Queue<string> queue = new([from]);
			while (queue.Count > 0) {
				string current = queue.Dequeue();
				if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase)) {
					return true;
				}
				foreach (string next in (_next.GetValueOrDefault(current) ?? []).Where(seen.Add)) {
					queue.Enqueue(next);
				}
			}
			return false;
		}

		// The build token and the describe buildType resolve through the same table the graph validator uses.
		private void AddElement(string? name, string? uid, string? type) {
			if (string.IsNullOrWhiteSpace(name)) {
				return;
			}
			string trimmed = name.Trim();
			_elements.Add(trimmed);
			if (!string.IsNullOrWhiteSpace(uid)) {
				_uidToName[uid.Trim()] = trimmed;
			}
			if (ManagerMap.ResolveRole(ManagerMap.ResolveDataId(type)) == ManagerMap.ProcessElementRole.Start) {
				Starts.Add(trimmed);
			}
		}

		private void AddFormulaWriter(string? element, string? parameter) {
			if (string.IsNullOrWhiteSpace(element) || string.IsNullOrWhiteSpace(parameter)) {
				return;
			}
			if (!_formulaWriters.TryGetValue(parameter.Trim(), out List<string>? writers)) {
				writers = [];
				_formulaWriters[parameter.Trim()] = writers;
			}
			writers.Add(element.Trim());
		}

		private void AddFlow(string? source, string? target) {
			if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)) {
				return;
			}
			string from = Canonical(source);
			if (!_next.TryGetValue(from, out List<string>? targets)) {
				targets = [];
				_next[from] = targets;
			}
			targets.Add(Canonical(target));
		}

		// A caller may name an element by its UId on the modify path; the read-back graph is keyed by name.
		private string Canonical(string element) {
			string trimmed = element.Trim();
			return _uidToName.GetValueOrDefault(trimmed) ?? trimmed;
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Common;
using ErrorOr;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Tells which result parameters of a run may carry what a Call web service element received, so that run-process
/// can withhold them: an API may echo the request it was sent - the Authorization header, a key the platform
/// filled in from a system setting - and the run hands that echo back as an ordinary parameter value.
/// </summary>
/// <remarks>
/// The decision rests on where a value comes from, never on the value: clio cannot read the setting that holds a key
/// (a SecureText setting reads as <c>***</c>), so it cannot recognise the key inside an answer.
/// </remarks>
public interface IProcessResultSourceClassifier {

	/// <summary>
	/// Finds the result parameters whose value may come from what a Call web service element received.
	/// </summary>
	/// <param name="model">The process to run, with its parameters and flow elements.</param>
	/// <param name="resultParameters">The result parameters the run reads back.</param>
	/// <param name="stopAt">
	/// When the reads of called processes must stop. A called process not read by then counts as one that calls a web
	/// service, so the check never delays the launch past it.
	/// </param>
	/// <returns>
	/// The parameters to withhold, by name (ordinal), each with the element it may come from, as a reader names it.
	/// Empty when nothing is to be withheld. A schema that could not be read in time counts as one that calls a web
	/// service.
	/// </returns>
	IReadOnlyDictionary<string, string> FindWebServiceFed(ProcessModel model, IReadOnlyList<string> resultParameters,
		DateTimeOffset stopAt);
}

/// <summary>What clio could establish about a process a sub-process element calls.</summary>
public enum CalleeVerdict {

	/// <summary>No version that can run calls a web service.</summary>
	Clean,

	/// <summary>The process, or one it calls, has a Call web service element.</summary>
	CallsWebService,

	/// <summary>The process could not be read (failed, out of time, too deep): treated as calling one.</summary>
	Unchecked
}

/// <summary>
/// The rules of <see cref="IProcessResultSourceClassifier"/>, over one schema, with the sub-process reads left to the
/// caller.
/// </summary>
/// <remarks>
/// <para>
/// A value is followed through the meta-path tokens (<c>[#...#]</c>) of the stored sources: a process parameter's
/// own value and its collection items' values, the parameter values of the elements it maps an output from, and the
/// body of a Formula element that writes it. Nested flow elements (an event sub-process keeps its own) are read as
/// part of the schema. A value comes from a web service when that chain reaches:
/// </para>
/// <list type="bullet">
/// <item>any parameter of a Call web service element except <c>Success</c> and <c>StatusCode</c>, a flag and a
/// number that cannot carry an echo;</item>
/// <item>an output of a sub-process element whose called process may call a web service, or could not be read;</item>
/// <item>an output of any other element one of whose inputs does (the element may pass it on);</item>
/// <item>a parameter a Formula element writes from such a value (a Formula element whose target cannot be read counts
/// as writing every parameter);</item>
/// <item>an element the schema does not list.</item>
/// </list>
/// <para>
/// Taint is propagated from the sources until nothing changes, so a loop between elements or parameters cannot hide
/// one. A Script task can write any parameter, so in a process with a web service source every requested result is
/// withheld. Records a process writes and reads back (Modify data, then Read data) are not followed: the data tools
/// read those records anyway.
/// </para>
/// </remarks>
public static class ProcessResultSources {

	/// <summary>The value run-process returns in place of a withheld one.</summary>
	public const string WithheldMarker = "[withheld]";

	// A flag and a number: neither can carry an echoed credential.
	private static readonly HashSet<string> SafeWebServiceOutputs = new(StringComparer.Ordinal) {
		"Success", "StatusCode"
	};

	private static readonly Regex Token = new(@"\[#(?<body>.*?)#\]", RegexOptions.CultureInvariant | RegexOptions.Singleline,
		TimeSpan.FromSeconds(1));

	// The element nearest the parameter: in a chained path [Element:{A}].[Element:{B}].[Parameter:{p}] that is B.
	private static readonly Regex ElementReference = new(
		@"\[Element:\{(?<element>[0-9a-fA-F-]{36})\}\]\.\[Parameter:\{(?<parameter>[0-9a-fA-F-]{36})\}\]",
		RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	private static readonly Regex ParameterReference = new(@"\[Parameter:\{(?<parameter>[0-9a-fA-F-]{36})\}\]",
		RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	/// <summary>
	/// Applies the rules to one schema.
	/// </summary>
	/// <param name="parameters">The process parameters.</param>
	/// <param name="elements">The process flow elements, nested ones included or not (they are flattened).</param>
	/// <param name="resultParameters">The result parameters the run reads back.</param>
	/// <param name="calleeVerdict">
	/// What is known of the process a sub-process element calls, by schema UId; asked at most once per process, and
	/// only for an element a requested value reaches or, with a Script task, for every sub-process element.
	/// </param>
	/// <returns>The parameters to withhold, by name, each with the element it may come from.</returns>
	internal static IReadOnlyDictionary<string, string> Classify(IReadOnlyList<ProcessParameter> parameters,
		IReadOnlyList<FlowElement> elements, IReadOnlyList<string> resultParameters,
		Func<Guid, CalleeVerdict> calleeVerdict) {
		Dictionary<string, string> withheld = new(StringComparer.Ordinal);
		if (resultParameters is not { Count: > 0 }) {
			return withheld;
		}
		Graph graph = new(parameters ?? [], Flatten(elements), calleeVerdict);
		List<string> requested = resultParameters.Where(n => !string.IsNullOrWhiteSpace(n))
			.Distinct(StringComparer.Ordinal).ToList();
		string scriptTask = graph.ScriptTaskInWebServiceProcess();
		if (scriptTask is not null) {
			foreach (string name in requested) {
				withheld[name] = scriptTask;
			}
			return withheld;
		}
		graph.Propagate(requested);
		foreach (string name in requested) {
			string origin = graph.OriginOfParameter(name);
			if (origin is not null) {
				withheld[name] = origin;
			}
		}
		return withheld;
	}

	/// <summary>Every element of a schema, the ones nested in an event sub-process or a sub-process included.</summary>
	/// <param name="elements">The schema's top-level flow elements.</param>
	/// <returns>The elements, depth first.</returns>
	internal static List<FlowElement> Flatten(IEnumerable<FlowElement> elements) {
		List<FlowElement> flat = [];
		Stack<FlowElement> pending = new((elements ?? []).Where(e => e is not null).Reverse());
		while (pending.Count > 0) {
			FlowElement element = pending.Pop();
			flat.Add(element);
			foreach (FlowElement child in (element.FlowElements ?? []).Where(e => e is not null).Reverse()) {
				pending.Push(child);
			}
		}
		return flat;
	}

	/// <summary>Whether the element is a Call web service element (or the BPMN service task, read the same way).</summary>
	internal static bool IsWebService(FlowElement element) =>
		element.EventType is ManagerMap.EventType.WebServiceTask or ManagerMap.EventType.ServiceTask;

	/// <summary>The process a sub-process element calls; <c>null</c> for an embedded one that calls none.</summary>
	internal static Guid? CalleeOf(FlowElement element) =>
		element.EventType == ManagerMap.EventType.SubProcess && element.SchemaUId is { } callee && callee != Guid.Empty
			? callee
			: null;

	/// <summary>Whether the element is a sub-process element that names no process and keeps no elements of its own.</summary>
	internal static bool IsUnconfiguredSubProcess(FlowElement element) =>
		element.EventType == ManagerMap.EventType.SubProcess && CalleeOf(element) is null
		&& element.FlowElements is not { Count: > 0 };

	/// <summary>
	/// Every meta-path reference in a stored source: element parameters as (element, parameter) pairs, and bare
	/// process parameters.
	/// </summary>
	/// <param name="value">A stored source value (a mapping, a formula, a constant).</param>
	/// <returns>The element parameters, then the process parameters, referenced by the value.</returns>
	internal static (List<(Guid Element, Guid Parameter)> Elements, List<Guid> Parameters) References(string value) {
		List<(Guid, Guid)> elementReferences = [];
		List<Guid> parameterReferences = [];
		if (string.IsNullOrEmpty(value) || !value.Contains("[#", StringComparison.Ordinal)) {
			return (elementReferences, parameterReferences);
		}
		foreach (Match token in Token.Matches(value)) {
			string body = token.Groups["body"].Value;
			Match element = ElementReference.Match(body);
			if (element.Success) {
				elementReferences.Add((Guid.Parse(element.Groups["element"].Value),
					Guid.Parse(element.Groups["parameter"].Value)));
				continue;
			}
			Match parameter = ParameterReference.Match(body);
			if (parameter.Success) {
				parameterReferences.Add(Guid.Parse(parameter.Groups["parameter"].Value));
			}
		}
		return (elementReferences, parameterReferences);
	}

	private static string ReadString(JsonElement? value) =>
		value is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

	private static string Describe(FlowElement element) {
		string caption = element.Captions?.TryGetValue("en-US", out string english) == true
			? english
			: element.Captions?.Values.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
		string name = string.IsNullOrWhiteSpace(element.Name) ? element.UId.ToString() : element.Name;
		// Fenced: a caption is stored text an agent reads in the warning.
		return string.IsNullOrWhiteSpace(caption) || caption == name ? $"'{name}'" : $"'{name}' ({UntrustedText.Fenced(caption)})";
	}

	private static string DescribeCallee(FlowElement element, CalleeVerdict verdict) =>
		verdict == CalleeVerdict.CallsWebService
			? $"the sub-process element {Describe(element)}, whose process calls a web service"
			: $"the sub-process element {Describe(element)}, whose process clio could not check in time or at all";

	// One reference of a stored source: an element parameter, or a bare process parameter (Element null).
	private readonly record struct Reference(Guid? Element, Guid Parameter);

	private sealed class Graph {

		private readonly Dictionary<Guid, ProcessParameter> parametersById = [];
		private readonly Dictionary<string, ProcessParameter> parametersByName = new(StringComparer.Ordinal);
		private readonly Dictionary<Guid, FlowElement> elementsById = [];
		private readonly List<FlowElement> elements;
		private readonly List<FlowElement> formulaTasks = [];
		private readonly List<FlowElement> scriptTasks = [];
		private readonly Func<Guid, CalleeVerdict> calleeVerdict;
		private readonly Dictionary<Guid, CalleeVerdict> calleeVerdicts = [];
		private readonly Dictionary<Guid, string> parameterOrigins = [];
		private readonly Dictionary<Guid, string> elementOrigins = [];

		// Formula elements by the parameter they write; those whose target cannot be read write every parameter.
		private readonly Dictionary<Guid, List<FlowElement>> writers = [];
		private readonly List<FlowElement> writersOfEverything = [];

		internal Graph(IReadOnlyList<ProcessParameter> parameters, List<FlowElement> elements,
			Func<Guid, CalleeVerdict> calleeVerdict) {
			this.elements = elements;
			this.calleeVerdict = calleeVerdict;
			foreach (ProcessParameter parameter in parameters.Where(p => p is not null)) {
				parametersById.TryAdd(parameter.UId, parameter);
				if (!string.IsNullOrEmpty(parameter.Name)) {
					parametersByName.TryAdd(parameter.Name, parameter);
				}
			}
			foreach (FlowElement element in elements) {
				elementsById.TryAdd(element.UId, element);
				if (element.EventType == ManagerMap.EventType.FormulaTask) {
					formulaTasks.Add(element);
				}
				else if (element.EventType == ManagerMap.EventType.ScriptTask) {
					scriptTasks.Add(element);
				}
			}
			foreach (FlowElement formula in formulaTasks) {
				List<Guid> targets = Targets(formula);
				if (targets is null) {
					writersOfEverything.Add(formula);
					continue;
				}
				foreach (Guid target in targets) {
					if (target == Guid.Empty) {
						continue;
					}
					(writers.TryGetValue(target, out List<FlowElement> list) ? list : writers[target] = []).Add(formula);
				}
			}
		}

		// The parameter a Formula element writes: its `resultParameterMetaPath` (a bare UId, or a parameter meta-path);
		// null when it names no process parameter, and such a formula counts as writing every parameter.
		private static List<Guid> Targets(FlowElement formula) {
			string target = ReadString(formula.ResultParameterMetaPath);
			if (Guid.TryParse(target, out Guid targetUId)) {
				return [targetUId];
			}
			(List<(Guid Element, Guid Parameter)> elementTargets, List<Guid> parameterTargets) = References(target);
			return elementTargets.Count == 0 && parameterTargets.Count > 0 ? parameterTargets : null;
		}

		internal string ScriptTaskInWebServiceProcess() {
			if (scriptTasks.Count == 0) {
				return null;
			}
			FlowElement webService = elements.FirstOrDefault(IsWebService);
			if (webService is not null) {
				return ScriptTaskOrigin($"the Call web service element {Describe(webService)}");
			}
			foreach (FlowElement subProcess in elements.Where(e => e.EventType == ManagerMap.EventType.SubProcess)) {
				CalleeVerdict verdict = VerdictOf(subProcess);
				if (verdict != CalleeVerdict.Clean) {
					return ScriptTaskOrigin(DescribeCallee(subProcess, verdict));
				}
			}
			return null;
		}

		private string ScriptTaskOrigin(string source) =>
			$"the Script task {Describe(scriptTasks[0])}, which can write any parameter of a process with {source}";

		internal string OriginOfParameter(string name) =>
			parametersByName.TryGetValue(name, out ProcessParameter parameter)
				? parameterOrigins.GetValueOrDefault(parameter.UId)
				: null;

		/// <summary>
		/// Finds every node the requested parameters depend on, then marks the tainted ones until nothing changes.
		/// Only the reached sub-process elements have their called processes read.
		/// </summary>
		internal void Propagate(IReadOnlyList<string> requested) {
			(HashSet<Guid> parameters, HashSet<Guid> reachedElements) = Reach(requested);
			bool changed = true;
			while (changed) {
				changed = false;
				foreach (Guid uid in reachedElements.Where(uid => !elementOrigins.ContainsKey(uid))) {
					string origin = OriginOfElement(elementsById[uid]);
					if (origin is not null) {
						elementOrigins[uid] = origin;
						changed = true;
					}
				}
				foreach (Guid uid in parameters.Where(uid => !parameterOrigins.ContainsKey(uid))) {
					string origin = OriginOfParameterNode(parametersById[uid]);
					if (origin is not null) {
						parameterOrigins[uid] = origin;
						changed = true;
					}
				}
			}
		}

		private (HashSet<Guid> Parameters, HashSet<Guid> Elements) Reach(IEnumerable<string> requested) {
			HashSet<Guid> parameters = [];
			HashSet<Guid> reached = [];
			Queue<Reference> queue = new();
			foreach (string name in requested) {
				if (parametersByName.TryGetValue(name, out ProcessParameter parameter)) {
					queue.Enqueue(new Reference(null, parameter.UId));
				}
			}
			while (queue.Count > 0) {
				Reference next = queue.Dequeue();
				if (next.Element is { } elementUId) {
					if (!elementsById.TryGetValue(elementUId, out FlowElement element) || !reached.Add(elementUId)) {
						continue;
					}
					if (!IsWebService(element)) {
						foreach (Reference reference in ReferencesOf(element)) {
							queue.Enqueue(reference);
						}
					}
					continue;
				}
				if (!parametersById.TryGetValue(next.Parameter, out ProcessParameter node) || !parameters.Add(node.UId)) {
					continue;
				}
				foreach (Reference reference in ReferencesOf(node)) {
					queue.Enqueue(reference);
				}
				foreach (FlowElement formula in WritersOf(node.UId)) {
					queue.Enqueue(new Reference(formula.UId, Guid.Empty));
				}
			}
			return (parameters, reached);
		}

		private IEnumerable<FlowElement> WritersOf(Guid parameterUId) =>
			(writers.TryGetValue(parameterUId, out List<FlowElement> list) ? list : []).Concat(writersOfEverything);

		private static IEnumerable<Reference> ReferencesOf(ProcessParameter parameter) {
			IEnumerable<Reference> own = ReferencesOfValue(parameter.SourceValue?.Value);
			return (parameter.ItemProperties ?? []).Where(item => item is not null)
				.Aggregate(own, (all, item) => all.Concat(ReferencesOf(item)));
		}

		private static IEnumerable<Reference> ReferencesOf(FlowElement element) {
			IEnumerable<Reference> all = ReferencesOfValue(ReadString(element.Body));
			foreach (FlowElementParameter parameter in element.Parameters ?? []) {
				if (parameter is null) {
					continue;
				}
				all = all.Concat(ReferencesOfValue(parameter.SourceValue?.Value));
				all = (parameter.ItemProperties ?? []).Where(item => item is not null)
					.Aggregate(all, (current, item) => current.Concat(ReferencesOf(item)));
			}
			return all;
		}

		private static IEnumerable<Reference> ReferencesOfValue(string value) {
			(List<(Guid Element, Guid Parameter)> elementReferences, List<Guid> parameterReferences) = References(value);
			return elementReferences.Select(r => new Reference(r.Element, r.Parameter))
				.Concat(parameterReferences.Select(p => new Reference(null, p)));
		}

		// An element that is itself a source, or one whose inputs reach a tainted node.
		private string OriginOfElement(FlowElement element) {
			if (IsWebService(element)) {
				return null;
			}
			if (CalleeOf(element) is not null || IsUnconfiguredSubProcess(element)) {
				CalleeVerdict verdict = VerdictOf(element);
				if (verdict != CalleeVerdict.Clean) {
					return DescribeCallee(element, verdict);
				}
			}
			string reached = FirstTainted(ReferencesOf(element));
			if (reached is null) {
				return null;
			}
			return element.EventType == ManagerMap.EventType.FormulaTask
				? $"the Formula element {Describe(element)}, which reads {reached}"
				: reached;
		}

		private string OriginOfParameterNode(ProcessParameter parameter) =>
			FirstTainted(ReferencesOf(parameter))
			?? WritersOf(parameter.UId).Select(formula => elementOrigins.GetValueOrDefault(formula.UId))
				.FirstOrDefault(origin => origin is not null);

		private string FirstTainted(IEnumerable<Reference> references) {
			foreach (Reference reference in references) {
				string origin = OriginOfReference(reference);
				if (origin is not null) {
					return origin;
				}
			}
			return null;
		}

		private string OriginOfReference(Reference reference) {
			if (reference.Element is not { } elementUId) {
				return parameterOrigins.GetValueOrDefault(reference.Parameter);
			}
			if (!elementsById.TryGetValue(elementUId, out FlowElement element)) {
				return $"an element this schema does not list ({elementUId})";
			}
			if (IsWebService(element)) {
				string parameterName = element.Parameters?
					.FirstOrDefault(p => Guid.TryParse(p?.UId, out Guid id) && id == reference.Parameter)?.Name;
				return parameterName is not null && SafeWebServiceOutputs.Contains(parameterName)
					? null
					: $"the Call web service element {Describe(element)}";
			}
			return elementOrigins.GetValueOrDefault(elementUId);
		}

		private CalleeVerdict VerdictOf(FlowElement element) {
			if (calleeVerdicts.TryGetValue(element.UId, out CalleeVerdict known)) {
				return known;
			}
			CalleeVerdict verdict = CalleeOf(element) is { } callee
				? calleeVerdict(callee)
				: IsUnconfiguredSubProcess(element) ? CalleeVerdict.Unchecked : CalleeVerdict.Clean;
			calleeVerdicts[element.UId] = verdict;
			return verdict;
		}
	}
}

/// <summary>
/// Reads the processes that sub-process elements call, for <see cref="ProcessResultSources"/>.
/// </summary>
/// <remarks>
/// A sub-process element runs the ACTIVE version of the process it names, so that version is read; only when the
/// process library cannot name it are the stored version and the family read. Reading is bounded by
/// <see cref="MaxSchemaReads"/> schemas, <see cref="MaxDepth"/> levels of nesting, and the caller's stop time; past
/// any of them, or on a failed read, the callee is <see cref="CalleeVerdict.Unchecked"/>.
/// </remarks>
internal sealed class ProcessResultSourceClassifier(IProcessModelGenerator generator,
	IProcessVersionLibReader versionReader) : IProcessResultSourceClassifier {

	internal const int MaxSchemaReads = 12;
	internal const int MaxDepth = 4;

	// A read is not started with less than this left: it would only time out.
	private const int MinimumReadMs = 500;

	/// <inheritdoc />
	public IReadOnlyDictionary<string, string> FindWebServiceFed(ProcessModel model,
		IReadOnlyList<string> resultParameters, DateTimeOffset stopAt) {
		if (model is null || resultParameters is not { Count: > 0 }) {
			return new Dictionary<string, string>(StringComparer.Ordinal);
		}
		CalleeReader reader = new(generator, versionReader, stopAt);
		return ProcessResultSources.Classify(model.Parameters, model.FlowElements ?? [], resultParameters,
			callee => reader.VerdictOf(callee, 1));
	}

	private sealed class CalleeReader(IProcessModelGenerator generator, IProcessVersionLibReader versionReader,
		DateTimeOffset stopAt) {

		private readonly Dictionary<Guid, CalleeVerdict> verdicts = [];
		private readonly HashSet<Guid> reading = [];
		private int reads;

		internal CalleeVerdict VerdictOf(Guid schemaUId, int depth) {
			if (verdicts.TryGetValue(schemaUId, out CalleeVerdict known)) {
				return known;
			}
			// A process that calls itself, directly or through another, adds nothing on the second visit; the verdict
			// of the visit in progress is not cached, so mutual recursion cannot freeze a provisional answer.
			if (!reading.Add(schemaUId)) {
				return CalleeVerdict.Clean;
			}
			CalleeVerdict verdict = depth > MaxDepth ? CalleeVerdict.Unchecked : ReadFamily(schemaUId, depth);
			reading.Remove(schemaUId);
			if (reading.Count == 0 || verdict != CalleeVerdict.Clean) {
				verdicts[schemaUId] = verdict;
			}
			return verdict;
		}

		private CalleeVerdict ReadFamily(Guid schemaUId, int depth) {
			if (!TryBudget(out int budgetMs)) {
				return CalleeVerdict.Unchecked;
			}
			ProcessVersionFacts facts;
			try {
				facts = versionReader.Read(schemaUId.ToString("D"), TimeSpan.FromMilliseconds(budgetMs));
			}
			catch (Exception exception) when (exception is not OutOfMemoryException) {
				return CalleeVerdict.Unchecked;
			}
			List<Guid> members;
			if (Guid.TryParse(facts?.ActiveVersionSchemaUId, out Guid active) && active != Guid.Empty) {
				members = [active];
			}
			else if (facts?.Versions is null || facts.FamilyTruncated) {
				return CalleeVerdict.Unchecked;
			}
			else {
				members = [schemaUId, .. facts.Versions
					.Select(member => Guid.TryParse(member.SchemaUId, out Guid id) ? id : Guid.Empty)
					.Where(id => id != Guid.Empty)];
			}
			CalleeVerdict verdict = CalleeVerdict.Clean;
			foreach (Guid member in members.Distinct()) {
				CalleeVerdict memberVerdict = ReadSchema(member, depth);
				if (memberVerdict == CalleeVerdict.CallsWebService) {
					return memberVerdict;
				}
				if (memberVerdict == CalleeVerdict.Unchecked) {
					verdict = CalleeVerdict.Unchecked;
				}
			}
			return verdict;
		}

		private CalleeVerdict ReadSchema(Guid schemaUId, int depth) {
			if (++reads > MaxSchemaReads || !TryBudget(out int budgetMs)) {
				return CalleeVerdict.Unchecked;
			}
			ErrorOr<IReadOnlyList<FlowElement>> read;
			try {
				read = generator.ReadFlowElements(schemaUId, budgetMs);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException) {
				return CalleeVerdict.Unchecked;
			}
			if (read.IsError) {
				return CalleeVerdict.Unchecked;
			}
			List<FlowElement> elements = ProcessResultSources.Flatten(read.Value);
			if (elements.Any(ProcessResultSources.IsWebService)) {
				return CalleeVerdict.CallsWebService;
			}
			CalleeVerdict verdict = CalleeVerdict.Clean;
			foreach (FlowElement element in elements.Where(e => e.EventType == ManagerMap.EventType.SubProcess)) {
				CalleeVerdict nested = ProcessResultSources.CalleeOf(element) is { } callee
					? VerdictOf(callee, depth + 1)
					: ProcessResultSources.IsUnconfiguredSubProcess(element) ? CalleeVerdict.Unchecked : CalleeVerdict.Clean;
				if (nested == CalleeVerdict.CallsWebService) {
					return nested;
				}
				if (nested == CalleeVerdict.Unchecked) {
					verdict = CalleeVerdict.Unchecked;
				}
			}
			return verdict;
		}

		private bool TryBudget(out int budgetMs) {
			double left = (stopAt - DateTimeOffset.UtcNow).TotalMilliseconds;
			budgetMs = (int)Math.Min(left, int.MaxValue);
			return left >= MinimumReadMs;
		}
	}
}

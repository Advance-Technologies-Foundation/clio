using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clio.Common;
using ErrorOr;
using Newtonsoft.Json.Linq;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Tells whether a process can put a credential into its result parameters, so that run-process returns only values
/// that cannot carry one. Two sources are known: a Call web service element, whose response may echo the request it
/// was sent (the Authorization header, a key filled in from a system setting), and a SecureText system setting, which
/// the platform decrypts when a parameter or a formula reads it.
/// </summary>
/// <remarks>
/// The rule is per process, not per value: clio cannot recognise a key by its value (a SecureText setting reads as
/// <c>***</c> through the API), and following each value through mappings and formulas proved fragile. This is
/// hygiene for an agent's context, not a security boundary: whoever can build and run a process can read any setting
/// through the platform.
/// </remarks>
public interface IProcessCredentialSourceDetector {

	/// <summary>
	/// Finds why the process may return a credential in a result parameter.
	/// </summary>
	/// <param name="model">The process to run, with its parameters and flow elements.</param>
	/// <param name="stopAt">
	/// When the reads of called processes and setting types must stop; what is not read by then counts as a source.
	/// </param>
	/// <returns>
	/// A verb phrase that completes "the process ...", e.g. <c>calls a web service (element 'CallEcho')</c> or
	/// <c>reads the SecureText system setting 'UsrApiKey'</c>; <see langword="null"/> when there is none.
	/// </returns>
	string FindCredentialSource(ProcessModel model, DateTimeOffset stopAt);
}

/// <summary>Reads of a process schema for <see cref="IProcessCredentialSourceDetector"/>, without HTTP.</summary>
internal static class ProcessCredentialSources {

	// The platform's own pattern (GeneratorUtilities): [#SysSettings.Code#], optionally typed [#SysSettings.Code<Text>#].
	private static readonly Regex SettingMacro = new(@"\[#SysSettings\.(?<code>[\w-]+)(?:<\w+>)?#\]",
		RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	// A formula is C#: it can read a setting without the macro (SysSettings.GetValue(UserConnection, "Code")).
	private static readonly Regex SettingCall = new(@"\bSysSettings\s*\.\s*\w+\s*\(|\bValueProvider\s*\.\s*\w*SysSetting\w*\s*\(",
		RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	// ProcessSchemaParameterValueSource.SystemSetting; the value is the setting's code.
	private const int SystemSettingSource = 5;

	// A user task's managerItemUId is its schema: the legacy SOAP "Web service" task (WebServiceUserTask).
	private static readonly Guid LegacyWebServiceUserTask = new("E7ACB2E5-EFC6-41D3-8C02-589AE6B943F1");

	/// <summary>A setting code the scan could not name: one read in C# rather than through the macro.</summary>
	internal const string SettingReadInCode = "*";

	/// <summary>Every element of a schema, the ones nested in an event sub-process or a sub-process included.</summary>
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

	/// <summary>The first Call web service element (or BPMN service task, or legacy SOAP task), or null.</summary>
	internal static FlowElement FirstWebService(IEnumerable<FlowElement> elements) =>
		elements.FirstOrDefault(e => e.EventType is ManagerMap.EventType.WebServiceTask or ManagerMap.EventType.ServiceTask
			|| e.ManagerItemUId == LegacyWebServiceUserTask);

	/// <summary>The process each sub-process element calls; an element that names none is reported as Guid.Empty.</summary>
	internal static IEnumerable<Guid> Callees(IEnumerable<FlowElement> elements) =>
		elements.Where(e => e.EventType == ManagerMap.EventType.SubProcess && e.FlowElements is not { Count: > 0 })
			.Select(e => e.SchemaUId ?? Guid.Empty);

	/// <summary>
	/// The codes of every system setting the schema reads: <c>[#SysSettings.Code#]</c> in a stored value, a formula
	/// body or a flow condition, and a parameter whose value source is a system setting. A setting read in C# (a
	/// formula calling <c>SysSettings.GetValue</c>) is reported as <see cref="SettingReadInCode"/>.
	/// </summary>
	internal static IReadOnlyCollection<string> SettingCodes(IEnumerable<ProcessParameter> parameters,
		IEnumerable<FlowElement> elements) {
		HashSet<string> codes = new(StringComparer.OrdinalIgnoreCase);
		foreach (ProcessParameter parameter in parameters ?? []) {
			AddFrom(parameter, codes);
		}
		foreach (FlowElement element in elements ?? []) {
			AddFromText(element.Body is { ValueKind: System.Text.Json.JsonValueKind.String } body ? body.GetString() : null,
				codes);
			AddFromText(element.ConditionExpression, codes);
			foreach (FlowElementParameter parameter in element.Parameters ?? []) {
				if (parameter is null) {
					continue;
				}
				AddFrom(parameter.SourceValue, codes);
				foreach (ProcessParameter item in parameter.ItemProperties ?? []) {
					AddFrom(item, codes);
				}
			}
		}
		return codes;
	}

	private static void AddFrom(ProcessParameter parameter, HashSet<string> codes) {
		if (parameter is null) {
			return;
		}
		AddFrom(parameter.SourceValue, codes);
		foreach (ProcessParameter item in parameter.ItemProperties ?? []) {
			AddFrom(item, codes);
		}
	}

	private static void AddFrom(SourceValue source, HashSet<string> codes) {
		string value = source?.Value?.Trim();
		if (string.IsNullOrEmpty(value)) {
			return;
		}
		if (source.Source == SystemSettingSource) {
			codes.Add(value);
			return;
		}
		AddFromText(value, codes);
	}

	private static void AddFromText(string text, HashSet<string> codes) {
		if (string.IsNullOrEmpty(text) || !text.Contains("SysSetting", StringComparison.Ordinal)) {
			return;
		}
		foreach (Match match in SettingMacro.Matches(text)) {
			codes.Add(match.Groups["code"].Value);
		}
		if (SettingCall.IsMatch(text)) {
			codes.Add(SettingReadInCode);
		}
	}

	/// <summary>A reader's name for an element: its code, with its caption when they differ, fenced.</summary>
	internal static string Describe(FlowElement element) {
		string caption = element.Captions?.TryGetValue("en-US", out string english) == true
			? english
			: element.Captions?.Values.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
		string name = string.IsNullOrWhiteSpace(element.Name) ? element.UId.ToString() : element.Name;
		return string.IsNullOrWhiteSpace(caption) || caption == name
			? $"'{name}'"
			: $"'{name}' ({UntrustedText.Fenced(caption)})";
	}
}

/// <summary>
/// Reads the process, the processes its sub-process elements call (their ACTIVE versions, which is what a sub-process
/// element runs) and the types of the settings they read, within a stop time.
/// </summary>
/// <remarks>
/// Bounded by <see cref="MaxSchemaReads"/> called schemas, <see cref="MaxDepth"/> levels and the stop time. A called
/// process not read, one whose active version is not established and whose family is not fully known, a setting whose
/// type cannot be read, and a sub-process element that names no process all count as a source - the check exists to
/// keep a credential out of the answer.
/// </remarks>
internal sealed class ProcessCredentialSourceDetector(IProcessModelGenerator generator,
	IProcessVersionLibReader versionReader, IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder)
	: IProcessCredentialSourceDetector {

	private readonly IProcessModelGenerator generator = generator;
	private readonly IProcessVersionLibReader versionReader = versionReader;
	private readonly IApplicationClient applicationClient = applicationClient;
	private readonly IServiceUrlBuilder serviceUrlBuilder = serviceUrlBuilder;

	private const int MaxSchemaReads = 12;
	private const int MaxDepth = 4;
	private const int MinimumReadMs = 500;
	private const int VersionReadMs = 3_000;
	// Kept back from the callee reads, so the settings query still has time.
	private const int SettingsQueryReserveMs = 1_500;
	private const string SecureTextType = "SecureText";
	private const int TextDataValueType = 1;

	/// <inheritdoc />
	public string FindCredentialSource(ProcessModel model, DateTimeOffset stopAt) {
		if (model is null) {
			return null;
		}
		Reader reader = new(this, stopAt);
		reader.Visited.Add(model.Id);
		HashSet<string> settings = new(StringComparer.OrdinalIgnoreCase);
		string source = reader.Scan(model.Parameters, model.FlowElements, settings, depth: 0);
		return source ?? reader.SecureSetting(settings);
	}

	private sealed class Reader(ProcessCredentialSourceDetector owner, DateTimeOffset stopAt) {

		internal HashSet<Guid> Visited { get; } = [];
		private int reads;

		internal string Scan(IEnumerable<ProcessParameter> parameters, IEnumerable<FlowElement> elements,
			HashSet<string> settings, int depth) {
			List<FlowElement> flat = ProcessCredentialSources.Flatten(elements);
			if (ProcessCredentialSources.FirstWebService(flat) is { } webService) {
				return $"calls a web service (element {ProcessCredentialSources.Describe(webService)}"
					+ (depth == 0 ? ")" : ", in a process it calls)");
			}
			settings.UnionWith(ProcessCredentialSources.SettingCodes(parameters, flat));
			foreach (Guid callee in ProcessCredentialSources.Callees(flat)) {
				string found = callee == Guid.Empty
					? "has a sub-process element that names no process"
					: ScanCallee(callee, settings, depth + 1);
				if (found is not null) {
					return found;
				}
			}
			return null;
		}

		private string ScanCallee(Guid callee, HashSet<string> settings, int depth) {
			if (!Visited.Add(callee)) {
				return null;
			}
			if (depth > MaxDepth) {
				return "calls processes nested deeper than clio checks";
			}
			if (reads >= MaxSchemaReads) {
				return "calls more processes than clio checks";
			}
			if (!TryBudget(SettingsQueryReserveMs, out int budgetMs)) {
				return "calls a process clio had no time left to check";
			}
			ProcessVersionFacts facts;
			try {
				facts = owner.versionReader.Read(callee.ToString("D"),
					TimeSpan.FromMilliseconds(Math.Min(budgetMs, VersionReadMs)));
			}
			catch (Exception exception) when (exception is not OutOfMemoryException) {
				return "calls a process clio could not check";
			}
			// A sub-process element runs the ACTIVE version. Without one established, every version that might run is read.
			List<Guid> targets;
			if (Guid.TryParse(facts?.ActiveVersionSchemaUId, out Guid active) && active != Guid.Empty) {
				targets = [active];
			}
			else if (facts?.Versions is null || facts.FamilyTruncated) {
				return "calls a process whose running version clio could not establish";
			}
			else {
				targets = [callee, .. facts.Versions
					.Select(member => Guid.TryParse(member.SchemaUId, out Guid id) ? id : Guid.Empty)
					.Where(id => id != Guid.Empty)];
			}
			foreach (Guid target in targets.Distinct()) {
				if (target != callee && !Visited.Add(target)) {
					continue;
				}
				string found = ScanSchema(target, settings, depth);
				if (found is not null) {
					return found;
				}
			}
			return null;
		}

		private string ScanSchema(Guid schemaUId, HashSet<string> settings, int depth) {
			if (++reads > MaxSchemaReads) {
				return "calls more processes than clio checks";
			}
			if (!TryBudget(SettingsQueryReserveMs, out int budgetMs)) {
				return "calls a process clio had no time left to check";
			}
			ErrorOr<MetaDataSchema> read;
			try {
				read = owner.generator.ReadSchema(schemaUId, budgetMs);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException) {
				return "calls a process clio could not check";
			}
			return read.IsError
				? "calls a process clio could not check"
				: Scan(read.Value.Parameters, read.Value.FlowElements, settings, depth);
		}

		// One query for every setting the process tree reads; a code with no row, or no type, counts as secret.
		internal string SecureSetting(HashSet<string> codes) {
			if (codes.Count == 0) {
				return null;
			}
			if (codes.Contains(ProcessCredentialSources.SettingReadInCode)) {
				return "reads a system setting in a formula, which clio cannot type";
			}
			List<string> ordered = codes.Order(StringComparer.OrdinalIgnoreCase).ToList();
			if (!TryBudget(0, out int budgetMs)) {
				return $"reads system settings ({Fenced(ordered)}) whose type clio had no time left to check";
			}
			Dictionary<string, string> types = new(StringComparer.OrdinalIgnoreCase);
			try {
				JObject query = ClassicEntitySchemaQuery.Query("SysSettings",
					new JObject {
						["Code"] = ClassicEntitySchemaQuery.Column("Code"),
						["ValueTypeName"] = ClassicEntitySchemaQuery.Column("ValueTypeName")
					},
					ClassicEntitySchemaQuery.Group(("byCode",
						ClassicEntitySchemaQuery.InFilter("Code", ordered, TextDataValueType))),
					ordered.Count);
				string json = owner.applicationClient.ExecutePostRequest(
					owner.serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select),
					query.ToString(Newtonsoft.Json.Formatting.None), budgetMs, maxAttempts: 1);
				foreach (JToken row in DataServiceSelectResponse.ReadRows(json)) {
					string code = row["Code"]?.ToString();
					if (!string.IsNullOrEmpty(code)) {
						types[code] = row["ValueTypeName"]?.ToString();
					}
				}
			}
			catch (Exception exception) when (exception is not OutOfMemoryException) {
				return $"reads system settings ({Fenced(ordered)}) whose type clio could not check";
			}
			string secret = ordered.FirstOrDefault(code => !types.TryGetValue(code, out string type)
				|| string.IsNullOrEmpty(type) || string.Equals(type, SecureTextType, StringComparison.OrdinalIgnoreCase));
			return secret is null ? null : $"reads the SecureText system setting {Fenced([secret])}";
		}

		// A code from a value source is stored text that reaches the agent: an identifier is quoted, anything else fenced.
		private static string Fenced(IEnumerable<string> codes) =>
			string.Join(", ", codes.Select(code => Regex.IsMatch(code, @"\A[\w-]{1,128}\z", RegexOptions.None,
				TimeSpan.FromSeconds(1)) ? $"'{code}'" : UntrustedText.Fenced(code)));

		private bool TryBudget(int reserveMs, out int budgetMs) {
			double left = (stopAt - DateTimeOffset.UtcNow).TotalMilliseconds - reserveMs;
			budgetMs = (int)Math.Clamp(left, 0, int.MaxValue);
			return left >= MinimumReadMs;
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Common;

namespace Clio.Command.ProcessModel;

/// <summary>
/// The outcome of <see cref="ProcessBlockKeyCheck"/>: at most one refusal, plus the describe read-back fields the
/// write path ignores.
/// </summary>
/// <param name="Error">The refusal, or <c>null</c> when every key the caller sent is one the server reads.</param>
/// <param name="Warnings">Describe-only fields that were sent back and will be ignored - reported, never refused.</param>
public sealed record ProcessBlockKeyCheckResult(string Error, IReadOnlyList<string> Warnings) {

	/// <summary>Nothing to say about this payload.</summary>
	public static readonly ProcessBlockKeyCheckResult Clean = new(null, []);

}

/// <summary>
/// Refuses a process element, or one of its configuration blocks, that carries a key the server does not read
/// (ENG-103085).
/// </summary>
/// <remarks>
/// <para>CrtProcessBuilder reads the request through WCF's <c>DataContractJsonSerializer</c>, which DROPS a member
/// its contract does not declare. A misplaced key therefore never reaches a server-side guard: a <c>filter</c>
/// nested inside <c>readData</c> instead of beside it saved green, and every Read data read the first record of
/// the object. The only refusal the block ever produced was indirect - <c>object</c> in place of <c>source</c> was
/// stopped by "requires a 'source'", not because <c>object</c> was unknown - so a misplaced key next to a valid
/// <c>source</c> had no defence at all. clio is the last side that still sees the caller's keys, which is why this
/// check sits beside <see cref="IProcessPageFactsChecker"/>, the other pre-send guard of the same payloads.</para>
/// <para>The key sets mirror the server's request contracts one type per <see cref="KeySet"/> and are held equal
/// to the bundled CrtProcessBuilder by <c>ProcessBlockKeyCheckTests</c>, so a new server member cannot be refused
/// here by a stale list. Fields <c>describe-business-process</c> reports but the write path does not take
/// (<c>sourceSchemaUId</c>, <c>outputs</c>, <c>position</c>, ...) are DERIVED read-backs: a described block fed
/// back keeps building, and the ignored fields are named in a warning instead of vanishing.</para>
/// </remarks>
internal static class ProcessBlockKeyCheck {

	private const int MaxReportedKeys = 8;
	private const string Filter = "filter";
	private const string Signal = "signal";
	private const string Source = "source";
	private const string ElementKey = "element";
	private const string ElementUpdateKey = "elementUpdate";
	private const string ElementNameKey = "elementName";

	/// <summary>One server request contract: the keys it declares and, for object-valued keys, their own sets.</summary>
	internal sealed class KeySet {

		private readonly Dictionary<string, KeySet> _children = new(StringComparer.Ordinal);
		private readonly HashSet<string> _keys;

		internal KeySet(string contractType, string label, string describeType, IEnumerable<string> keys,
				IEnumerable<string> readBack = null, IReadOnlyDictionary<string, string> pointers = null) {
			ContractType = contractType;
			Label = label;
			DescribeType = describeType;
			_keys = new HashSet<string>(keys, StringComparer.Ordinal);
			ReadBack = new HashSet<string>(readBack ?? [], StringComparer.Ordinal);
			Pointers = pointers ?? new Dictionary<string, string>(StringComparer.Ordinal);
		}

		/// <summary>The server's contract type these keys mirror.</summary>
		internal string ContractType { get; }

		/// <summary>The caller-facing name of the block, as the contracts spell it.</summary>
		internal string Label { get; }

		/// <summary>The server's describe type whose extra members <see cref="ReadBack"/> names, or <c>null</c>.</summary>
		internal string DescribeType { get; }

		/// <summary>Every key the server's contract declares.</summary>
		internal IReadOnlyCollection<string> Keys => _keys;

		/// <summary>Describe-only, derived keys: ignored by the write path, warned about, never refused.</summary>
		internal IReadOnlySet<string> ReadBack { get; }

		/// <summary>Where a commonly misplaced or misspelled key belongs, keyed by that key.</summary>
		internal IReadOnlyDictionary<string, string> Pointers { get; }

		/// <summary>The nested key sets, by the key whose value (an object or an array of objects) they describe.</summary>
		internal IReadOnlyDictionary<string, KeySet> Children => _children;

		internal bool Declares(string key) => _keys.Contains(key);

		internal KeySet With(string key, KeySet child) {
			_children[key] = child;
			return this;
		}

	}

	private static readonly string[] DataSourceSynonyms = ["object", "entity", "entityName", "entitySchemaName",
		"entitySchema", "schema", "schemaName", "objectName"];

	private static Dictionary<string, string> DataBlockPointers(string block) {
		Dictionary<string, string> pointers = new(StringComparer.Ordinal);
		foreach (string synonym in DataSourceSynonyms) {
			pointers[synonym] = $"the {block} block names its object in 'source' (for example \"source\":\"Contact\")";
		}
		return pointers;
	}

	#region Key sets (mirror the bundled CrtProcessBuilder request contracts)

	private static readonly KeySet ElementParameterRef = new("FilterElementParameterRef", "elementParameter", null,
		["elementName", "parameter", "column"]);

	private static readonly KeySet FilterCondition = new KeySet("FilterConditionDescriptor", "filter condition", null,
		["column", "comparison", "value", "displayValue", "processParameter", "elementParameter", "expression",
			"macro", "macroArgument", "datePart"])
		.With("elementParameter", ElementParameterRef);

	private static readonly KeySet FilterGroup = BuildFilterGroup();

	private static KeySet BuildFilterGroup() {
		KeySet group = new KeySet("FilterGroupDescriptor", "filter group", null,
				["logicalOperation", "conditions", "groups"])
			.With("conditions", FilterCondition);
		return group.With("groups", group);
	}

	private static readonly KeySet RecordFilter = new KeySet("FilterDescriptor", Filter, null,
			["object", "logicalOperation", "conditions", "groups"],
			pointers: new Dictionary<string, string>(StringComparer.Ordinal) {
				[Source] = "a filter names the object it filters in 'object'",
				["filters"] = "a filter's conditions go in 'conditions' and nested groups in 'groups'"
			})
		.With("conditions", FilterCondition)
		.With("groups", FilterGroup);

	private static readonly KeySet SignalBlock = new("ProcessSignalDescriptor", Signal, "DescribeSignalInfo",
		["entity", "on", "changedColumns"], readBack: ["entitySchemaUId"]);

	private static readonly KeySet ReadDataSort = new("ReadDataSortDescriptor", "sort", null, ["column", "direction"]);

	private static readonly KeySet ReadDataAggregation = new("ReadDataAggregationDescriptor", "aggregation", null,
		["function", "column"]);

	private static readonly KeySet ReadDataBlock = new KeySet("ReadDataConfigDescriptor", "readData",
			"DescribeReadDataInfo", ["source", "mode", "aggregation", "columns", "numberOfRecords", "sort"],
			readBack: ["sourceSchemaUId", "outputs"], pointers: DataBlockPointers("readData"))
		.With("aggregation", ReadDataAggregation)
		.With("sort", ReadDataSort);

	private static readonly KeySet ColumnValue = new("ChangeDataValueDescriptor", "values entry", null,
		["column", "value", "processParameter", "sourceElement", "sourceElementParameter", "sourceColumn",
			"expression", "selectionColumn"]);

	private static readonly KeySet ChangeDataBlock = new KeySet("ChangeDataConfigDescriptor", "changeData",
			"DescribeChangeDataInfo", ["source", "values"], readBack: ["sourceSchemaUId"],
			pointers: DataBlockPointers("changeData"))
		.With("values", ColumnValue);

	private static readonly KeySet AddDataBlock = new KeySet("AddDataConfigDescriptor", "addData",
			"DescribeAddDataInfo", ["source", "mode", "selection", "values"],
			readBack: ["sourceSchemaUId", "selectionSchemaUId"], pointers: DataBlockPointers("addData"))
		.With("values", ColumnValue);

	private static readonly KeySet DeleteDataBlock = new("DeleteDataConfigDescriptor", "deleteData",
		"DescribeDeleteDataInfo", ["source"], readBack: ["sourceSchemaUId"], pointers: DataBlockPointers("deleteData"));

	private static readonly KeySet TaskPerformer = new("UserTaskPerformerDescriptor", "performer",
		"DescribeUserTaskPerformerInfo", ["type", "contact", "role", "showPage"], readBack: ["roleDisplay"]);

	private static readonly KeySet EmailTemplateEntity = new("SendEmailTemplateEntityDescriptor", "templateEntity",
		null, ["processParameter", "sourceElement", "sourceElementParameter", "expression"]);

	private static readonly KeySet EmailRecipient = new("SendEmailRecipientDescriptor", "recipient", null,
		["value", "processParameter", "expression", "referenceSchema"]);

	private static readonly KeySet EmailBlock = new KeySet("SendEmailDescriptor", "email", "DescribeSendEmailInfo",
			["messageSource", "template", "templateEntity", "body", "bodyFormat", "mode", "sender", "subject", "to",
				"cc", "bcc", "importance", "ignoreErrors", "performer"],
			readBack: ["templateDisplay", "senderDisplay", "hasBody"])
		.With("templateEntity", EmailTemplateEntity)
		.With("to", EmailRecipient)
		.With("cc", EmailRecipient)
		.With("bcc", EmailRecipient)
		.With("performer", TaskPerformer);

	private static readonly KeySet AccessRightsGrantee = new KeySet("AccessRightsGranteeDescriptor", "grantee",
			"DescribeAccessRightsGrantee", ["type", "role", "contact", Filter], readBack: ["display"])
		.With(Filter, RecordFilter);

	private static readonly KeySet AccessRightsEntry = new KeySet("AccessRightsEntryDescriptor", "access rights entry",
			null, ["operations", "level", "grantee"])
		.With("grantee", AccessRightsGrantee);

	private static readonly KeySet AccessRightsBlock = new KeySet("ChangeAccessRightsDescriptor", "accessRights",
			"DescribeAccessRightsInfo", ["object", "considerTimeInFilter", "add", "remove"],
			readBack: ["objectSchemaUId", "addUnreadable", "removeUnreadable"],
			pointers: new Dictionary<string, string>(StringComparer.Ordinal) {
				[Source] = "the accessRights block names its object in 'object'"
			})
		.With("add", AccessRightsEntry)
		.With("remove", AccessRightsEntry);

	private static readonly KeySet ApprovalRecord = new("ApprovalRecordDescriptor", "recordId", null,
		["recordId", "processParameter", "sourceElement", "sourceElementParameter"]);

	private static readonly KeySet ApprovalApprover = new("ApprovalApproverDescriptor", "approver", null,
		["type", "employee", "role"]);

	private static readonly KeySet ApprovalNotification = new("ApprovalNotificationDescriptor", "notifyApprover", null,
		["emailTemplate"]);

	private static readonly KeySet ApprovalRecipient = new("ApprovalRecipientDescriptor", "recipient", null,
		["value", "processParameter"]);

	private static readonly KeySet ApprovalAuthorNotification = new KeySet("ApprovalAuthorNotificationDescriptor",
			"notifyAuthor", null, ["emailTemplate", "recipient"])
		.With("recipient", ApprovalRecipient);

	private static readonly KeySet ApprovalBlock = new KeySet("ApprovalDescriptor", "approval", "DescribeApprovalInfo",
			["purpose", "object", "recordId", "approver", "allowDelegation", "notifyApprover", "notifyAuthor",
				"ignoreEmailErrors"],
			readBack: ["objectUId", "recordIdDisplay", "approverEmployeeDisplay", "approverRoleDisplay",
				"approverEmailTemplateDisplay", "authorEmailTemplateDisplay", "approvalSchemaUId"],
			pointers: new Dictionary<string, string>(StringComparer.Ordinal) {
				["approverType"] = "write the approver nested: \"approver\":{\"type\":...}",
				["approverEmployee"] = "write the approver nested: \"approver\":{\"type\":\"employee\",\"employee\":...}",
				["approverRole"] = "write the approver nested: \"approver\":{\"type\":\"role\",\"role\":...}",
				["approverEmailTemplate"] = "write it nested: \"notifyApprover\":{\"emailTemplate\":...}",
				["authorEmailTemplate"] = "write it nested: \"notifyAuthor\":{\"emailTemplate\":...}",
				["recipient"] = "write it nested: \"notifyAuthor\":{\"recipient\":{...}}"
			})
		.With("recordId", ApprovalRecord)
		.With("approver", ApprovalApprover)
		.With("notifyApprover", ApprovalNotification)
		.With("notifyAuthor", ApprovalAuthorNotification);

	private static readonly KeySet FormulaTarget = new("FormulaTargetDescriptor", "target", null,
		["resultProcessParameter", "elementName", "elementParameter", "entityColumnUId", "unresolved"]);

	private static readonly KeySet FormulaBlock = new KeySet("FormulaTaskDescriptor", "formula", "DescribeFormulaInfo",
			["body", "target", "resultProcessParameter", "elementName", "elementParameter", "entityColumnUId"])
		.With("target", FormulaTarget);

	private static readonly KeySet ActivityInterval = new("ActivityIntervalDescriptor", "interval",
		"DescribeActivityIntervalInfo", ["value", "unit"], readBack: ["period"]);

	private static readonly KeySet LogActivity = new KeySet("LogActivityDescriptor", "logActivity",
			"DescribeOpenEditPageLogActivityInfo",
			["enabled", "startIn", "duration", "remindIn", "showInCalendar", "priority"], readBack: ["priorityId"])
		.With("startIn", ActivityInterval)
		.With("duration", ActivityInterval)
		.With("remindIn", ActivityInterval);

	private static readonly KeySet ResultsByColumn = new("OpenEditPageResultsByColumnDescriptor", "resultsByColumn",
		"DescribeOpenEditPageResultsByColumnInfo", ["enabled", "column"], readBack: ["columnUId"]);

	private static readonly KeySet OpenEditPagePerformer = new("OpenEditPagePerformerDescriptor", "performer",
		"DescribeOpenEditPagePerformerInfo", ["type", "contact", "role", "showPage"], readBack: ["roleDisplay"]);

	private static readonly KeySet OpenEditPageRecord = new("OpenEditPageRecordDescriptor", "recordId", null,
		["value", "processParameter", "sourceElement", "sourceElementParameter", "sourceColumn", "expression"]);

	private static readonly KeySet OpenEditPageCompletion = new("OpenEditPageCompletionDescriptor", "completion", null,
		["mode"]);

	private static readonly KeySet OpenEditPageBlock = new KeySet("OpenEditPageConfigDescriptor", "openEditPage",
			"DescribeOpenEditPageInfo",
			["page", "pageSchemaUId", "recordType", "editMode", "defaultValues", "recordId", "recommendation", "hint",
				"performer", "logActivity", "resultsByColumn", "completion"],
			readBack: ["object", "pageTypeUId"],
			pointers: new Dictionary<string, string>(StringComparer.Ordinal) {
				["completionMode"] = "write it nested: \"completion\":{\"mode\":...}"
			})
		.With("defaultValues", ColumnValue)
		.With("recordId", OpenEditPageRecord)
		.With("performer", OpenEditPagePerformer)
		.With("logActivity", LogActivity)
		.With("resultsByColumn", ResultsByColumn)
		.With("completion", OpenEditPageCompletion);

	private static readonly KeySet PreconfiguredPagePerformer = new("PreconfiguredPagePerformerDescriptor",
		"performer", "DescribePreconfiguredPagePerformer", ["type", "contact", "role", "showPage"]);

	private static readonly KeySet PreconfiguredPageButton = new("PreconfiguredPageButtonDescriptor", "button",
		"DescribePreconfiguredPageButton", ["name", "caption", "event", "validate"]);

	private static readonly KeySet PreconfiguredPageDataSource = new("PreconfiguredPageDataSourceDescriptor",
		"dataSource", "DescribePreconfiguredPageDataSource", ["name", "entitySchemaName"], readBack: ["parameter"]);

	private static readonly KeySet PreconfiguredPageBlock = new KeySet("PreconfiguredPageDescriptor",
			"preconfiguredPage", "DescribePreconfiguredPageInfo",
			["page", "performer", "buttons", "dataSources", "recommendation"],
			readBack: ["pageUiType", "connectedObject", "connectedObjectRecord", "inSync", "shadowedPageParameters"])
		.With("performer", PreconfiguredPagePerformer)
		.With("buttons", PreconfiguredPageButton)
		.With("dataSources", PreconfiguredPageDataSource);

	private static readonly KeySet MultiInstanceOptions = new("MultiInstanceOptionsDescriptor", "multiInstanceOptions",
		"DescribeMultiInstanceOptions", ["enabled", "executionMode", "ignoreErrors"],
		readBack: ["inputCollection", "outputCollection", "completedIterationsCount", "terminatedIterationsCount",
			"totalIterationsCount", "calleeInSync"]);

	private static readonly KeySet SubProcessBlock = new KeySet("SubProcessDescriptor", "subProcess",
			"DescribeSubProcessInfo", ["processName", "processUId", "resync", "multiInstanceOptions"],
			readBack: ["processCaption", "multiInstance", "inSync"],
			pointers: new Dictionary<string, string>(StringComparer.Ordinal) {
				["process"] = "name the called process in 'processName' (or 'processUId')"
			})
		.With("multiInstanceOptions", MultiInstanceOptions);

	private static readonly KeySet ScriptTaskBlock = new("ScriptTaskDescriptor", "scriptTask", "DescribeScriptTaskInfo",
		["body", "forInterpretedProcess"]);

	/// <summary>
	/// The element's configuration blocks, shared by the create element and the <c>setElement</c> update: each is
	/// added to both, and the drift test catches one the server gained on either side only.
	/// </summary>
	private static readonly (string Key, KeySet Set)[] Blocks = [
		("readData", ReadDataBlock), ("changeData", ChangeDataBlock), ("addData", AddDataBlock),
		("deleteData", DeleteDataBlock), ("email", EmailBlock), ("performer", TaskPerformer),
		("accessRights", AccessRightsBlock), ("approval", ApprovalBlock), ("formula", FormulaBlock),
		("openEditPage", OpenEditPageBlock), ("preconfiguredPage", PreconfiguredPageBlock),
		("subProcess", SubProcessBlock), ("scriptTask", ScriptTaskBlock)
	];

	/// <summary>An element of a create descriptor's <c>elements[]</c>, or of an <c>addElement</c> operation.</summary>
	internal static readonly KeySet Element = WithBlocks(new KeySet("ProcessElementDescriptor", ElementKey,
			"DescribeProcessElement",
			["name", "type", "userTaskName", "caption", Signal, "lane", "useBackgroundMode", Filter, "readData", "email",
				"performer", "changeData", "addData", "deleteData", "accessRights", "approval", "formula",
				"openEditPage", "preconfiguredPage", "subProcess", "scriptTask"],
			readBack: ["uid", "buildType", "managerItemUId", "position", "size", "filterDecodedCompletely", "deprecated",
				"writesConnectionsAtRuntime"],
			pointers: new Dictionary<string, string>(StringComparer.Ordinal) {
				["parameters"] = "an element's inputs are bound through the descriptor's top-level 'mappings' "
					+ "(modify-business-process: an addMapping operation)",
				["connections"] = "\"Connected to\" links are set after the build with a modify-business-process "
					+ "setConnections operation",
				["filters"] = "the element's record filter is the single element-level 'filter' block"
			})
		.With(Signal, SignalBlock)
		.With(Filter, RecordFilter));

	/// <summary>The <c>elementUpdate</c> of a <c>setElement</c> operation.</summary>
	internal static readonly KeySet ElementUpdate = WithBlocks(new KeySet("ProcessElementUpdateDescriptor",
		ElementUpdateKey, null,
		["useBackgroundMode", "email", "approval", "formula", "scriptTask", "readData", "preconfiguredPage",
			"changeData", "addData", "deleteData", "performer", "openEditPage", "accessRights", "subProcess"],
		pointers: new Dictionary<string, string>(StringComparer.Ordinal) {
			[Filter] = "a record filter is changed by its own operation: {\"op\":\"setFilter\",\"elementName\":...,"
				+ "\"filter\":{...}}",
			[Signal] = "a start signal is changed by its own operation: {\"op\":\"setSignal\",\"elementName\":...,"
				+ "\"signal\":{...}}"
		}));

	private static KeySet WithBlocks(KeySet element) {
		foreach ((string key, KeySet set) in Blocks) {
			element.With(key, set);
		}
		return element;
	}

	/// <summary>Every key set this check walks, for the drift test.</summary>
	internal static IReadOnlyList<KeySet> All {
		get {
			List<KeySet> found = [];
			Collect(Element, found);
			Collect(ElementUpdate, found);
			return found;
		}
	}

	private static void Collect(KeySet set, List<KeySet> found) {
		if (found.Contains(set)) {
			return;
		}
		found.Add(set);
		foreach (KeySet child in set.Children.Values) {
			Collect(child, found);
		}
	}

	#endregion

	/// <summary>
	/// Checks every element of a create descriptor (<c>elements[]</c>), or every element-configuring operation of a
	/// modify operations array (<c>addElement</c>, <c>setElement</c>, <c>setFilter</c>, <c>setSignal</c>).
	/// </summary>
	/// <param name="payload">A create descriptor object or a modify operations array.</param>
	internal static ProcessBlockKeyCheckResult Check(JsonNode payload) {
		Walk walk = new();
		// JsonNode.Parse accepts a repeated key and throws only when the object is first read, so every read of a
		// caller object sits behind a catch that turns the repeat into a refusal instead of a stack trace.
		if (payload is JsonObject descriptor) {
			JsonArray elements = null;
			if (!walk.Guard("the descriptor", () => elements = descriptor["elements"] as JsonArray)) {
				return walk.Result();
			}
			for (int index = 0; index < (elements?.Count ?? 0); index++) {
				if (elements[index] is JsonObject element) {
					string where = $"elements[{index}]";
					walk.Guard(where, () => walk.Visit(element, Element, ElementLabel(element["name"], where), "",
						Placement.Element));
				}
			}
		} else if (payload is JsonArray operations) {
			for (int index = 0; index < operations.Count; index++) {
				if (operations[index] is JsonObject operation) {
					int position = index;
					walk.Guard($"operations[{index}]", () => VisitOperation(walk, operation, position));
				}
			}
		}
		return walk.Result();
	}

	/// <summary>
	/// Runs <see cref="Check"/> on a payload about to be sent: throws the refusal, so nothing is posted, and writes
	/// the read-back warnings to <paramref name="logger"/>.
	/// </summary>
	/// <param name="payload">A create descriptor object or a modify operations array.</param>
	/// <param name="logger">Where the read-back warnings go.</param>
	/// <exception cref="InvalidOperationException">The payload carries a key the server would drop.</exception>
	internal static void Enforce(JsonNode payload, ILogger logger) {
		ProcessBlockKeyCheckResult result = Check(payload);
		if (!string.IsNullOrWhiteSpace(result.Error)) {
			throw new InvalidOperationException(result.Error);
		}
		foreach (string warning in result.Warnings) {
			logger.WriteWarning(warning);
		}
	}

	private static void VisitOperation(Walk walk, JsonObject operation, int index) {
		string op = (operation["op"] as JsonValue)?.TryGetValue(out string token) == true ? token : null;
		string target = ElementLabel(operation[ElementNameKey], null);
		string where = $"operations[{index}] ({op}{(target is null ? "" : " " + target)})";
		if (string.Equals(op, "addElement", StringComparison.OrdinalIgnoreCase)
			&& operation[ElementKey] is JsonObject element) {
			walk.Visit(element, Element,
				$"operations[{index}] (addElement {ElementLabel(element["name"], "element")})", ElementKey + ".",
				Placement.Element);
		} else if (string.Equals(op, "setElement", StringComparison.OrdinalIgnoreCase)
			&& operation[ElementUpdateKey] is JsonObject update) {
			walk.Visit(update, ElementUpdate, where, ElementUpdateKey + ".", Placement.Update);
		} else if (string.Equals(op, "setFilter", StringComparison.OrdinalIgnoreCase)
			&& operation[Filter] is JsonObject filter) {
			walk.Visit(filter, RecordFilter, where, Filter + ".", Placement.Nested);
		} else if (string.Equals(op, "setSignal", StringComparison.OrdinalIgnoreCase)
			&& operation[Signal] is JsonObject signal) {
			walk.Visit(signal, SignalBlock, where, Signal + ".", Placement.Nested);
		}
	}

	private static string ElementLabel(JsonNode name, string fallback) =>
		(name as JsonValue)?.TryGetValue(out string text) == true && !string.IsNullOrWhiteSpace(text)
			? $"'{text}'"
			: fallback;

	/// <summary>Where the walk's root object sits, which decides where a misplaced element-level key belongs.</summary>
	private enum Placement {

		/// <summary>A create element or an addElement element: element-level keys go on the element itself.</summary>
		Element,

		/// <summary>A setElement update: filter and signal have their own operations.</summary>
		Update,

		/// <summary>A setFilter / setSignal payload: no element level to point at.</summary>
		Nested

	}

	private sealed class Walk {

		private readonly List<string> _refusals = [];
		private readonly List<string> _readBacks = [];

		internal void Visit(JsonObject node, KeySet set, string where, string prefix, Placement placement) {
			Visit(node, set, where, prefix, placement, depth: 0);
		}

		// depth: 0 for the walk's root, 1 for a block directly under it - the only level at which a key the
		// element declares is "misplaced into the block" rather than some nested object's own field.
		private void Visit(JsonObject node, KeySet set, string where, string prefix, Placement placement,
				int depth) {
			if (!TryReadMembers(node, out List<KeyValuePair<string, JsonNode>> members)) {
				// JsonNode.Parse accepts a repeated key and only throws when the object is first enumerated. One of
				// the two values would reach the server and the other would be lost as silently as an unknown key.
				_refusals.Add($"{where}: {(prefix.Length == 0 ? "the object" : $"'{prefix.TrimEnd('.')}'")} "
					+ "repeats a key, which leaves the value the server reads ambiguous. Send each key once.");
				return;
			}
			foreach ((string key, JsonNode value) in members) {
				if (set.Declares(key)) {
					VisitChild(key, value, set, where, prefix, placement, depth);
				} else if (set.ReadBack.Contains(key)) {
					_readBacks.Add($"{where}: {prefix}{key}");
				} else {
					_refusals.Add($"{where}: '{prefix}{key}' is not a key of {Describe(set, depth)}"
						+ $"{Pointer(key, set, depth, placement)}. Accepted {set.Label} keys: "
						+ $"{string.Join(", ", set.Keys)}.");
				}
			}
		}

		/// <summary>Runs <paramref name="read"/>; a repeated key it trips over becomes a refusal for <paramref name="where"/>.</summary>
		internal bool Guard(string where, Action read) {
			try {
				read();
				return true;
			} catch (ArgumentException) {
				_refusals.Add($"{where}: the object repeats a key, which leaves the value the server reads "
					+ "ambiguous. Send each key once.");
				return false;
			}
		}

		private static bool TryReadMembers(JsonObject node, out List<KeyValuePair<string, JsonNode>> members) {
			try {
				members = [.. node];
				return true;
			} catch (ArgumentException) {
				members = null;
				return false;
			}
		}

		private void VisitChild(string key, JsonNode value, KeySet set, string where, string prefix,
				Placement placement, int depth) {
			if (!set.Children.TryGetValue(key, out KeySet child)) {
				return;
			}
			if (value is JsonObject nested) {
				Visit(nested, child, where, $"{prefix}{key}.", placement, depth + 1);
				return;
			}
			if (value is JsonArray items) {
				for (int index = 0; index < items.Count; index++) {
					if (items[index] is JsonObject item) {
						Visit(item, child, where, $"{prefix}{key}[{index}].", placement, depth + 1);
					}
				}
			}
		}

		private static string Describe(KeySet set, int depth) =>
			depth == 0 && set == Element ? "the element" : $"the '{set.Label}' block";

		private static string Pointer(string key, KeySet set, int depth, Placement placement) {
			if (set.Pointers.TryGetValue(key, out string pointer)) {
				return " - " + pointer;
			}
			string sameKey = set.Keys.FirstOrDefault(known => string.Equals(known, key,
				StringComparison.OrdinalIgnoreCase));
			if (sameKey is not null) {
				return $" - keys are case-sensitive: write '{sameKey}'";
			}
			if (depth == 0 && set == ElementUpdate && Element.Declares(key)) {
				return $" - '{key}' is set when the element is added; setElement does not change it (elementUpdate "
					+ "carries useBackgroundMode and the configuration blocks only)";
			}
			// Only a configuration block's own level: the element's filter and signal are not "a block" a key can
			// be misplaced into (filter.filter must not be told to use the element-level filter it is already in).
			if (depth != 1 || !Element.Declares(key) || !Blocks.Any(block => block.Set == set)) {
				return string.Empty;
			}
			return placement switch {
				Placement.Element when key == Filter =>
					" - the record filter is the ELEMENT-level 'filter' block, a sibling of the configuration block: "
					+ "{\"name\":...,\"type\":...,\"readData\":{\"source\":...},\"filter\":{\"object\":...,"
					+ "\"conditions\":[...]}}",
				Placement.Element => $" - '{key}' is an element-level key: move it out of the block onto the element",
				Placement.Update when ElementUpdate.Pointers.TryGetValue(key, out string update) => " - " + update,
				Placement.Update when ElementUpdate.Declares(key) =>
					$" - '{key}' is a key of elementUpdate itself: move it out of the block",
				_ => string.Empty
			};
		}

		internal ProcessBlockKeyCheckResult Result() {
			if (_refusals.Count == 0 && _readBacks.Count == 0) {
				return ProcessBlockKeyCheckResult.Clean;
			}
			string error = null;
			if (_refusals.Count > 0) {
				IEnumerable<string> shown = _refusals.Take(MaxReportedKeys);
				string more = _refusals.Count > MaxReportedKeys
					? $" (and {_refusals.Count - MaxReportedKeys} more)"
					: string.Empty;
				error = "Nothing was sent: the server reads its request with a contract that DROPS an undeclared key "
					+ "without a word, so these keys would have saved a process that ignores them. "
					+ string.Join(" ", shown) + more;
			}
			IReadOnlyList<string> warnings = _readBacks.Count == 0
				? []
				: [
					"Ignored describe-business-process read-back fields (derived on read, never written): "
					+ string.Join("; ", _readBacks.Take(MaxReportedKeys))
					+ (_readBacks.Count > MaxReportedKeys ? $" (and {_readBacks.Count - MaxReportedKeys} more)" : "")
					+ "."
				];
			return new ProcessBlockKeyCheckResult(error, warnings);
		}

	}

}

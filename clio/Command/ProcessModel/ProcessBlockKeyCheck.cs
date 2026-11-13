using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Command.ProcessModel;

/// <summary>
/// The outcome of <see cref="ProcessBlockKeyCheck"/>: at most one refusal, plus the describe read-back fields the
/// write path ignores.
/// </summary>
/// <param name="Error">The refusal, or <c>null</c> when every key the caller sent is one the server reads.</param>
/// <param name="IgnoredReadBacks">
/// Describe-only fields that were sent back and will be ignored, in one summary line - tolerated, never refused. Not
/// written to the caller: the server names the same fields in its own warning (see <see cref="ProcessBlockKeyCheck"/>).
/// </param>
public sealed record ProcessBlockKeyCheckResult(string Error, IReadOnlyList<string> IgnoredReadBacks) {

	/// <summary>Nothing to say about this payload.</summary>
	public static readonly ProcessBlockKeyCheckResult Clean = new(null, []);

}

/// <summary>
/// Refuses a process element, or one of its configuration blocks, that carries a key the server does not read
/// (ENG-103085).
/// </summary>
/// <remarks>
/// <para>CrtProcessBuilder reads the request through WCF's <c>DataContractJsonSerializer</c>. Before 1.6.6.136 it
/// DROPPED a member its contract does not declare, so a misplaced key never reached a server-side guard: a
/// <c>filter</c> nested inside <c>readData</c> instead of beside it saved green, and every Read data read the first
/// record of the object. From 1.6.6.136 the server refuses an undeclared key and ignores a describe read-back with a
/// warning (ENG-95244). This check still runs first: it answers before anything is sent, with where the key belongs,
/// and it covers a host whose server check reports itself unavailable. Every caller requires the create/modify floor
/// (1.6.6.162 today), which keeps out every older package; the floor compares numbers only, so it keeps out the other package line (1.6.3.x today)
/// only while that line stamps lower numbers - should it ever stamp that floor or above without the ENG-95244 check, a
/// replayed read-back member would be dropped there with no word from either side. It sits beside <see cref="IProcessPageFactsChecker"/>, the other pre-send guard of the same
/// payloads.</para>
/// <para>The key sets mirror the server's request contracts one type per <see cref="KeySet"/> and are held equal
/// to the bundled CrtProcessBuilder by <c>ProcessBlockKeyCheckTests</c>, so a new server member cannot be refused
/// here by a stale list. Fields <c>describe-business-process</c> reports but the write path does not take
/// (<c>sourceSchemaUId</c>, <c>outputs</c>, <c>position</c>, ...) are DERIVED read-backs: a described block fed
/// back keeps building. clio tolerates them and says nothing: the server they reach (every caller requires
/// the create/modify floor, 1.6.6.162 today) ignores them and names them in its own "Read-only fields copied from describe were
/// ignored" warning, which names them and, where it can, the key that changes each. A second warning from clio about
/// the same fields only doubled the noise of a describe -> setElement round trip. The accepted cost: on a host whose
/// server check reports itself unavailable, the server's warning says the request was not checked and may name none of
/// these fields, where clio used to name them.</para>
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

		/// <param name="contractType">The server request contract the keys mirror.</param>
		/// <param name="label">The caller-facing name of the block.</param>
		/// <param name="describeType">The describe type whose extra members <paramref name="readBack"/> names.</param>
		/// <param name="keys">The contract's keys, space-separated.</param>
		/// <param name="readBack">Describe-only keys tolerated (the server names them), space-separated.</param>
		/// <param name="pointers">Where a commonly misplaced or misspelled key belongs.</param>
		internal KeySet(string contractType, string label, string describeType, string keys, string readBack = null,
				IReadOnlyDictionary<string, string> pointers = null) {
			ContractType = contractType;
			Label = label;
			DescribeType = describeType;
			_keys = new HashSet<string>(Split(keys), StringComparer.Ordinal);
			ReadBack = new HashSet<string>(Split(readBack), StringComparer.Ordinal);
			Pointers = pointers ?? new Dictionary<string, string>(StringComparer.Ordinal);
		}

		private static string[] Split(string keys) =>
			(keys ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

		/// <summary>The server's contract type these keys mirror.</summary>
		internal string ContractType { get; }

		/// <summary>The caller-facing name of the block, as the contracts spell it.</summary>
		internal string Label { get; }

		/// <summary>The server's describe type whose extra members <see cref="ReadBack"/> names, or <c>null</c>.</summary>
		internal string DescribeType { get; }

		/// <summary>Every key the server's contract declares.</summary>
		internal IReadOnlyCollection<string> Keys => _keys;

		/// <summary>Describe-only, derived keys: ignored by the write path, never refused; the server names them.</summary>
		internal IReadOnlySet<string> ReadBack { get; }

		/// <summary>Where a commonly misplaced or misspelled key belongs, keyed by that key.</summary>
		internal IReadOnlyDictionary<string, string> Pointers { get; }

		/// <summary>The nested key sets, by the key whose value (an object or an array of objects) they describe.</summary>
		internal IReadOnlyDictionary<string, KeySet> Children => _children;

		internal bool Declares(string key) => _keys.Contains(key);

		/// <summary>Links <paramref name="child"/> under every space-separated key in <paramref name="keys"/>.</summary>
		internal KeySet With(string keys, KeySet child) {
			foreach (string key in Split(keys)) {
				_children[key] = child;
			}
			return this;
		}

		/// <summary>Links <paramref name="child"/> under its own label.</summary>
		internal KeySet With(KeySet child) => With(child.Label, child);

	}

	private const string Performer = "performer";
	private const string Recipient = "recipient";
	private const string RecordId = "recordId";

	private static readonly IReadOnlyDictionary<string, string> DataBlockPointers = BuildDataBlockPointers();

	private static Dictionary<string, string> BuildDataBlockPointers() {
		Dictionary<string, string> pointers = new(StringComparer.Ordinal);
		foreach (string synonym in "object entity entityName entitySchemaName entitySchema schema schemaName objectName"
				.Split(' ')) {
			pointers[synonym] = $"a data block names its object in '{Source}' (for example \"{Source}\":\"Contact\")";
		}
		return pointers;
	}

	private static Dictionary<string, string> PointerMap(params (string Key, string Text)[] entries) =>
		entries.ToDictionary(entry => entry.Key, entry => entry.Text, StringComparer.Ordinal);

	#region Key sets (mirror the bundled CrtProcessBuilder request contracts)

	// Keys are space-separated and mirror each contract's [DataMember] names; ProcessBlockKeyCheckTests holds them
	// equal to the bundled archive, so the order here is irrelevant and a typo fails the drift test.

	private static readonly KeySet ElementParameterRef = new("FilterElementParameterRef", "elementParameter", null,
		"elementName parameter column");

	private static readonly KeySet FilterCondition = new KeySet("FilterConditionDescriptor", "filter condition", null,
			"column comparison value values displayValue processParameter elementParameter expression macro macroArgument "
			+ "datePart")
		.With(ElementParameterRef);

	private static readonly KeySet FilterGroup = BuildFilterGroup();

	private static KeySet BuildFilterGroup() {
		KeySet group = new KeySet("FilterGroupDescriptor", "filter group", null, "logicalOperation conditions groups")
			.With("conditions", FilterCondition);
		return group.With("groups", group);
	}

	private static readonly KeySet RecordFilter = new KeySet("FilterDescriptor", Filter, null,
			"object logicalOperation conditions groups",
			pointers: PointerMap(
				(Source, "a filter names the object it filters in 'object'"),
				("filters", "a filter's conditions go in 'conditions' and nested groups in 'groups'")))
		.With("conditions", FilterCondition)
		.With("groups", FilterGroup);

	private static readonly KeySet SignalBlock = new("ProcessSignalDescriptor", Signal, "DescribeSignalInfo",
		"entity on changedColumns", "entitySchemaUId");

	private static readonly KeySet ReadDataSort = new("ReadDataSortDescriptor", "sort", null, "column direction");

	private static readonly KeySet ReadDataBlock = new KeySet("ReadDataConfigDescriptor", "readData",
			"DescribeReadDataInfo", "source mode aggregation columns numberOfRecords sort", "sourceSchemaUId outputs",
			DataBlockPointers)
		.With(new KeySet("ReadDataAggregationDescriptor", "aggregation", null, "function column"))
		.With(ReadDataSort);

	private static readonly KeySet ColumnValue = new("ChangeDataValueDescriptor", "values entry", null,
		"column value processParameter sourceElement sourceElementParameter sourceColumn expression selectionColumn");

	private static readonly KeySet ChangeDataBlock = new KeySet("ChangeDataConfigDescriptor", "changeData",
			"DescribeChangeDataInfo", "source values", "sourceSchemaUId", DataBlockPointers)
		.With("values", ColumnValue);

	private static readonly KeySet AddDataBlock = new KeySet("AddDataConfigDescriptor", "addData",
			"DescribeAddDataInfo", "source mode selection values", "sourceSchemaUId selectionSchemaUId", DataBlockPointers)
		.With("values", ColumnValue);

	private static readonly KeySet DeleteDataBlock = new("DeleteDataConfigDescriptor", "deleteData",
		"DescribeDeleteDataInfo", Source, "sourceSchemaUId", DataBlockPointers);

	private static readonly KeySet TaskPerformer = new("UserTaskPerformerDescriptor", Performer,
		"DescribeUserTaskPerformerInfo", "type contact role showPage", "roleDisplay");

	// One entry per attachment row. A described entry carries its stored row (slot), the levels bound and any
	// issues; fed back verbatim (the modify recipe: describe, edit the list, send it back) they are tolerated.
	// Named on its own because email.attachments is the one LIST of this walk whose wrong shape is refused before
	// sending (see Walk.RefusesAttachmentsShape).
	private static readonly KeySet EmailAttachment = new("SendEmailAttachmentDescriptor", "attachment",
		"DescribeSendEmailAttachment", "processParameter sourceElement sourceElementParameter expression value",
		"slot bindingLevels issues");

	private static readonly KeySet EmailBlock = new KeySet("SendEmailDescriptor", "email", "DescribeSendEmailInfo",
			"messageSource template templateEntity body bodyFormat mode sender subject to cc bcc importance ignoreErrors "
			+ "attachments " + Performer,
			"templateDisplay senderDisplay hasBody")
		.With(new KeySet("SendEmailTemplateEntityDescriptor", "templateEntity", null,
			"processParameter sourceElement sourceElementParameter expression"))
		.With("to cc bcc", new KeySet("SendEmailRecipientDescriptor", Recipient, null,
			"value processParameter expression referenceSchema"))
		.With("attachments", EmailAttachment)
		.With(TaskPerformer);

	private static readonly KeySet AccessRightsEntry = new KeySet("AccessRightsEntryDescriptor", "access rights entry",
			null, "operations level grantee")
		.With(new KeySet("AccessRightsGranteeDescriptor", "grantee", "DescribeAccessRightsGrantee",
				"type role contact " + Filter, "display")
			.With(RecordFilter));

	private static readonly KeySet AccessRightsBlock = new KeySet("ChangeAccessRightsDescriptor", "accessRights",
			"DescribeAccessRightsInfo", "object considerTimeInFilter add remove",
			"objectSchemaUId addUnreadable removeUnreadable",
			PointerMap((Source, "the accessRights block names its object in 'object'")))
		.With("add remove", AccessRightsEntry);

	private static readonly KeySet ApprovalBlock = new KeySet("ApprovalDescriptor", "approval", "DescribeApprovalInfo",
			"purpose object recordId approver allowDelegation notifyApprover notifyAuthor ignoreEmailErrors",
			"objectUId recordIdDisplay approverEmployeeDisplay approverRoleDisplay approverEmailTemplateDisplay "
			+ "authorEmailTemplateDisplay approvalSchemaUId",
			PointerMap(
				("approverType", "write the approver nested: \"approver\":{\"type\":...}"),
				("approverEmployee", "write the approver nested: \"approver\":{\"type\":\"employee\",\"employee\":...}"),
				("approverRole", "write the approver nested: \"approver\":{\"type\":\"role\",\"role\":...}"),
				("approverEmailTemplate", "write it nested: \"notifyApprover\":{\"emailTemplate\":...}"),
				("authorEmailTemplate", "write it nested: \"notifyAuthor\":{\"emailTemplate\":...}"),
				(Recipient, "write it nested: \"notifyAuthor\":{\"recipient\":{...}}")))
		.With(new KeySet("ApprovalRecordDescriptor", RecordId, null,
			"recordId processParameter sourceElement sourceElementParameter"))
		.With(new KeySet("ApprovalApproverDescriptor", "approver", null, "type employee role"))
		.With(new KeySet("ApprovalNotificationDescriptor", "notifyApprover", null, "emailTemplate"))
		.With(new KeySet("ApprovalAuthorNotificationDescriptor", "notifyAuthor", null, "emailTemplate recipient")
			.With(new KeySet("ApprovalRecipientDescriptor", Recipient, null, "value processParameter")));

	private static readonly KeySet FormulaBlock = new KeySet("FormulaTaskDescriptor", "formula", "DescribeFormulaInfo",
			"body target resultProcessParameter elementName elementParameter entityColumnUId")
		.With(new KeySet("FormulaTargetDescriptor", "target", null,
			"resultProcessParameter elementName elementParameter entityColumnUId unresolved"));

	private static readonly KeySet OpenEditPageBlock = new KeySet("OpenEditPageConfigDescriptor", "openEditPage",
			"DescribeOpenEditPageInfo",
			"page pageSchemaUId recordType editMode defaultValues recordId recommendation hint performer logActivity "
			+ "resultsByColumn completion",
			"object pageTypeUId",
			PointerMap(("completionMode", "write it nested: \"completion\":{\"mode\":...}")))
		.With("defaultValues", ColumnValue)
		.With(new KeySet("OpenEditPageRecordDescriptor", RecordId, null,
			"value processParameter sourceElement sourceElementParameter sourceColumn expression"))
		.With(new KeySet("OpenEditPagePerformerDescriptor", Performer, "DescribeOpenEditPagePerformerInfo",
			"type contact role showPage", "roleDisplay"))
		.With(new KeySet("LogActivityDescriptor", "logActivity", "DescribeOpenEditPageLogActivityInfo",
				"enabled startIn duration remindIn showInCalendar priority", "priorityId")
			.With("startIn duration remindIn", new KeySet("ActivityIntervalDescriptor", "interval",
				"DescribeActivityIntervalInfo", "value unit", "period")))
		.With(new KeySet("OpenEditPageResultsByColumnDescriptor", "resultsByColumn",
			"DescribeOpenEditPageResultsByColumnInfo", "enabled column", "columnUId"))
		.With(new KeySet("OpenEditPageCompletionDescriptor", "completion", null, "mode"));

	private static readonly KeySet PreconfiguredPageBlock = new KeySet("PreconfiguredPageDescriptor",
			"preconfiguredPage", "DescribePreconfiguredPageInfo", "page performer buttons dataSources recommendation",
			"pageUiType connectedObject connectedObjectRecord inSync shadowedPageParameters")
		.With(new KeySet("PreconfiguredPagePerformerDescriptor", Performer, "DescribePreconfiguredPagePerformer",
			"type contact role showPage"))
		.With("buttons", new KeySet("PreconfiguredPageButtonDescriptor", "button", "DescribePreconfiguredPageButton",
			"name caption event validate"))
		.With("dataSources", new KeySet("PreconfiguredPageDataSourceDescriptor", "dataSource",
			"DescribePreconfiguredPageDataSource", "name entitySchemaName", "parameter"));

	private static readonly KeySet SubProcessBlock = new KeySet("SubProcessDescriptor", "subProcess",
			"DescribeSubProcessInfo", "processName processUId resync multiInstanceOptions",
			"processCaption multiInstance inSync",
			PointerMap(("process", "name the called process in 'processName' (or 'processUId')")))
		.With(new KeySet("MultiInstanceOptionsDescriptor", "multiInstanceOptions", "DescribeMultiInstanceOptions",
			"enabled executionMode ignoreErrors",
			"inputCollection outputCollection completedIterationsCount terminatedIterationsCount totalIterationsCount "
			+ "calleeInSync"));

	private static readonly KeySet ScriptTaskBlock = new("ScriptTaskDescriptor", "scriptTask", "DescribeScriptTaskInfo",
		"body forInterpretedProcess");

	// One value-source vocabulary for every value the block takes; describe adds the designer's 'display' to each.
	private static readonly KeySet FileProcessingValueSource = new("FileProcessingValueSourceDescriptor",
		"value source", "DescribeFileProcessingValueSource",
		"value processParameter sourceElement sourceElementParameter sourceColumn expression", "display");

	private static readonly KeySet FileProcessingBlock = new KeySet("FileProcessingDescriptor", "fileProcessing",
			"DescribeFileProcessingInfo", "source action attachments report files saveTo",
			"actionStored bindingLevels outputs issues")
		.With(new KeySet("FileProcessingAttachmentsDescriptor", "attachments", "DescribeFileProcessingAttachmentsInfo",
				"object recordId numberOfRecords sort storage fileObject linkColumn")
			.With(RecordId, FileProcessingValueSource)
			.With(ReadDataSort))
		.With(new KeySet("FileProcessingReportDescriptor", "report", "DescribeFileProcessingReportInfo",
				"printable recordId separateReports fileNameSuffix fileNameSuffixColumn object type",
				"caption convertToPdf")
			.With($"{RecordId} fileNameSuffix", FileProcessingValueSource))
		// 'files' is described in the shared value-source shape, so only its 'display' is a read-back. Describe DOES
		// report a 'sourceColumn' there - FileProcessingBlockDescriber decodes a Files binding to a column of a record -
		// but the write contract has no such member: dropped, the binding would name the record, not its column, so it
		// stays refused, and the pointer tells the resubmitting caller how to keep the stored binding.
		.With(new KeySet("FileProcessingFilesDescriptor", "files", "DescribeFileProcessingValueSource",
			"processParameter sourceElement sourceElementParameter expression value", "display",
			PointerMap(("sourceColumn", "describe reports 'files' this way when Files is bound to a column of a "
				+ "record, a binding the 'files' group cannot write; on setElement leave 'files' out to keep the "
				+ "stored binding, or bind another source"))))
		.With(new KeySet("FileProcessingSaveToDescriptor", "saveTo", "DescribeFileProcessingSaveToInfo",
				"object recordId linkColumn storage fileObject")
			.With(RecordId, FileProcessingValueSource));

	// The Call web service block. Describe reports every write member in its write shape ('service' is the name string
	// on both sides, the raw UId sits in the describe-only 'serviceUId'), so a described block replays unchanged and its
	// five describe-only members are tolerated, never refused - except a fractional 'timeoutSeconds', which the
	// server cannot read (RefuseFractionalTimeout).
	private static readonly KeySet WebServiceBlock = new KeySet("WebServiceDescriptor", "webService", "DescribeWebServiceInfo",
		"service method timeoutSeconds ignoreErrors", "serviceUId serviceResolved methodResolved issues parameters");

	// The Send chat message block (ENG-96034). 'from' and 'to' take the shared value-source vocabulary of the
	// fileProcessing block; 'columnValues' entries are changeData values entries, named here for the block they sit in.
	// Describe reports every write member in its write shape, so a described block replays unchanged and its three
	// describe-only members are tolerated, never refused.
	private static readonly KeySet ChatMessageBlock = new KeySet("SendChatMessageDescriptor", "chatMessage",
			"DescribeSendChatMessageInfo", "from to messageSource template message ignoreErrors columnValues",
			"templateName outputs issues",
			PointerMap(
				("body", "the chat text is 'message' (plain text; a formula goes through mappings on Message)"),
				("channel", "the channel the message is sent from is 'from'"),
				("contact", "the contact the message is sent to is 'to'")))
		.With("from to", FileProcessingValueSource)
		.With("columnValues", new KeySet(ColumnValue.ContractType, "columnValues entry", null,
			string.Join(" ", ColumnValue.Keys)));

	/// <summary>
	/// The element's configuration blocks, each linked under its own label into both the create element and the
	/// <c>setElement</c> update; the drift test catches a block the server gained on either side only.
	/// </summary>
	private static readonly KeySet[] Blocks = [
		ReadDataBlock, ChangeDataBlock, AddDataBlock, DeleteDataBlock, EmailBlock, TaskPerformer, AccessRightsBlock,
		ApprovalBlock, FormulaBlock, OpenEditPageBlock, PreconfiguredPageBlock, SubProcessBlock, ScriptTaskBlock,
		FileProcessingBlock, WebServiceBlock, ChatMessageBlock
	];

	private static readonly string BlockKeys = string.Join(" ", Blocks.Select(block => block.Label));

	/// <summary>An element of a create descriptor's <c>elements[]</c>, or of an <c>addElement</c> operation.</summary>
	internal static readonly KeySet Element = WithBlocks(new KeySet("ProcessElementDescriptor", ElementKey,
			"DescribeProcessElement", $"name type userTaskName caption {Signal} lane useBackgroundMode {Filter} {BlockKeys}",
			"uid buildType managerItemUId position size filterDecodedCompletely deprecated writesConnectionsAtRuntime",
			PointerMap(
				("parameters", "an element's inputs are bound through the descriptor's top-level 'mappings' "
					+ "(modify-business-process: an addMapping operation)"),
				("connections", "\"Connected to\" links are set after the build with a modify-business-process "
					+ "setConnections operation"),
				("filters", "the element's record filter is the single element-level 'filter' block")))
		.With(SignalBlock)
		.With(RecordFilter));

	/// <summary>The <c>elementUpdate</c> of a <c>setElement</c> operation.</summary>
	internal static readonly KeySet ElementUpdate = WithBlocks(new KeySet("ProcessElementUpdateDescriptor",
		ElementUpdateKey, null, $"useBackgroundMode {BlockKeys}",
		pointers: PointerMap(
			(Filter, "a record filter is changed by its own operation: {\"op\":\"setFilter\",\"elementName\":...,"
				+ "\"filter\":{...}}"),
			(Signal, "a start signal is changed by its own operation: {\"op\":\"setSignal\",\"elementName\":...,"
				+ "\"signal\":{...}}"))));

	private static KeySet WithBlocks(KeySet element) {
		foreach (KeySet block in Blocks) {
			element.With(block);
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
	/// Runs <see cref="Check"/> on a payload about to be sent: throws the refusal, so nothing is posted. The ignored
	/// read-back fields are not written: the server reports them itself.
	/// </summary>
	/// <param name="payload">A create descriptor object or a modify operations array.</param>
	/// <exception cref="InvalidOperationException">
	/// The payload carries a key the server does not read: a current package refuses or ignores it, an older one drops it.
	/// </exception>
	internal static void Enforce(JsonNode payload) {
		ProcessBlockKeyCheckResult result = Check(payload);
		if (!string.IsNullOrWhiteSpace(result.Error)) {
			throw new InvalidOperationException(result.Error);
		}
	}

	private static void VisitOperation(Walk walk, JsonObject operation, int index) {
		string op = (operation["op"] as JsonValue)?.TryGetValue(out string token) == true ? token : null;
		string target = ElementLabel(operation[ElementNameKey], null);
		string where = $"operations[{index}] ({op}{(target is null ? "" : " " + target)})";
		// 'element' is the addElement OBJECT on the server for every op. A name there is not dropped like an unknown
		// key: WCF fails to read the WHOLE request and answers an error string, which used to reach the caller only
		// as "could not read the response" - agents guessed 'element' for setElement and blamed the server.
		if (operation[ElementKey] is JsonValue misplacedName) {
			string value = misplacedName.ToJsonString(ReadableJson);
			string remedy = string.Equals(op, "addElement", StringComparison.OrdinalIgnoreCase)
				? $"Send the new element itself there, as an object with its 'name' and 'type' (\"{ElementKey}\": "
					+ $"{{\"name\": {value}, \"type\": ...}})."
				: $"Name an existing element with '{ElementNameKey}' (\"{ElementNameKey}\": {value}).";
			walk.RefuseShape($"{where}: '{ElementKey}' takes the element OBJECT that addElement adds, so a name there "
				+ $"makes the server fail the whole request. {remedy}");
		}
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

	// The misplaced value is printed with the MCP result encoder: the default one escapes for HTML, so a non-ASCII
	// name or an apostrophe would read as \uXXXX in the very key the caller copies back.
	private static readonly JsonSerializerOptions ReadableJson = new() {
		Encoder = Clio.Command.McpServer.McpResultJsonEncoder.Instance
	};

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
		private readonly List<string> _shapeRefusals = [];
		private readonly List<string> _readBacks = [];

		/// <summary>Records an operation the server cannot read at all, as opposed to a key it would drop.</summary>
		internal void RefuseShape(string sentence) => _shapeRefusals.Add(sentence);

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
				if (set == FilterCondition) {
					RefuseFilterValueShape(key, value, where, prefix + key);
				}
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
			if (child == EmailAttachment && RefusesAttachmentsShape(value, where, prefix + key)) {
				return;
			}
			if (child == WebServiceBlock && value is JsonObject webService) {
				RefuseFractionalTimeout(webService, where, prefix + key);
			}
			if (child == ChatMessageBlock && value is JsonObject chatMessage) {
				RefuseChatValueSourceShape(chatMessage, where, prefix + key);
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

		// chatMessage.from and chatMessage.to are ONE value-source object each on the server's contract. A list - the
		// shape an agent reaches for to message several contacts - or a bare string is not a key the contract drops:
		// the serializer fails to read the WHOLE request before the package's own refusal can run, so the reply could
		// not say that the element sends to one contact. This check says it before anything is sent.
		private void RefuseChatValueSourceShape(JsonObject chatMessage, string where, string blockPath) {
			foreach (string member in new[] { "from", "to" }) {
				JsonNode value;
				try {
					if (!chatMessage.TryGetPropertyValue(member, out value) || value is null or JsonObject) {
						continue;
					}
				} catch (ArgumentException) {
					// A repeated key: the walk of the block reports it with its path.
					return;
				}
				string path = $"{blockPath}.{member}";
				string example = $"\"{member}\": {{\"processParameter\": \"...\"}}";
				RefuseShape(value is JsonArray
					? member == "to"
						? $"{where}: '{path}' is a list, which the server cannot read: a Send chat message element sends to "
							+ $"ONE contact. Send one value source, for example {example}; to message several contacts, add "
							+ "one element per contact or run the element in a multi-instance sub-process over the list."
						: $"{where}: '{path}' is a list, which the server cannot read: a Send chat message element sends from "
							+ $"ONE channel. Send one value source, for example {example}."
					: $"{where}: '{path}' is {Echo(value)}, which the server cannot read: it takes "
						+ $"ONE value-source object, for example \"{member}\": {{\"value\": {Echo(value)}}}.");
			}
		}

		// A caller's value echoed into a refusal, cut so a large one does not swamp the message.
		private static string Echo(JsonNode value) {
			string text = value.ToJsonString(ReadableJson);
			return text.Length <= 200 ? text : text[..200] + "...";
		}

		// A filter condition's 'value' is ONE string on the server's contract and 'values' a LIST of strings
		// (ENG-105557). A list or an object in 'value', and anything but a list of scalars in 'values', is not a key
		// the contract drops: the serializer fails to read the WHOLE request, which reached the caller only as
		// "BuildProcess returned a response clio could not read" - the form agents reach for first when a column
		// should match one of several records. Numbers and booleans in 'value' read fine and are left alone.
		private void RefuseFilterValueShape(string key, JsonNode value, string where, string path) {
			if (key == "value" && value is JsonArray) {
				RefuseShape($"{where}: '{path}' is a list, which the server cannot read. Several records of ONE lookup "
					+ "column are one condition: send them as \"values\": [...] with 'equal' (any of them) or 'notEqual' "
					+ "(none of them); one record is \"value\": \"...\".");
				return;
			}
			if (key == "value" && value is JsonObject) {
				RefuseShape($"{where}: '{path}' is an object, which the server cannot read. Send the constant as a "
					+ "string - for a lookup, the record id or its display name.");
				return;
			}
			if (key != "values" || value is null) {
				return;
			}
			if (value is not JsonArray entries) {
				RefuseShape($"{where}: '{path}' takes a LIST of records, so {(value is JsonObject ? "an object" : value.ToJsonString(ReadableJson))} "
					+ "there makes the server fail the whole request. Send \"values\": [\"<record>\", \"<record>\"].");
				return;
			}
			if (entries.Any(entry => entry is JsonObject or JsonArray)) {
				RefuseShape($"{where}: every entry of '{path}' is one record as a string (its id or display name); a "
					+ "nested object or list there makes the server fail the whole request.");
			}
		}

		// email.attachments is a LIST on the server's contract. A single object there, or an entry that is not an
		// object, is not a key the contract drops: the serializer fails to read the WHOLE request, which reached the
		// caller only as an unreadable reply. A null list or a null entry reads fine (the package refuses an entry
		// with no source in its own words), so neither is refused here. Returns whether the value is not a list at
		// all, so there is nothing to walk.
		private bool RefusesAttachmentsShape(JsonNode value, string where, string path) {
			const string entryShape = "{\"processParameter\": ...}, {\"sourceElement\": ..., "
				+ "\"sourceElementParameter\": ...} or {\"expression\": ...}";
			string? sent = value switch {
				JsonObject => "a single object",
				JsonValue scalar => scalar.ToJsonString(ReadableJson),
				_ => null
			};
			if (sent is not null) {
				RefuseShape($"{where}: '{path}' takes a LIST of attachment entries, so {sent} there makes the server "
					+ $"fail the whole request. Send an array, one object per attachment: \"attachments\": [{entryShape}].");
				return true;
			}
			if (value is not JsonArray items) {
				return false;
			}
			List<int> wrong = [.. Enumerable.Range(0, items.Count).Where(index => items[index] is JsonValue or JsonArray)];
			if (wrong.Count == 0) {
				return false;
			}
			string entries = string.Join(", ", wrong.Take(MaxReportedKeys).Select(index => $"'{path}[{index}]'"))
				+ (wrong.Count > MaxReportedKeys ? $" (and {wrong.Count - MaxReportedKeys} more)" : string.Empty);
			RefuseShape($"{where}: {entries} {(wrong.Count == 1 ? "is not an object" : "are not objects")}, so the "
				+ "server fails the whole request. Each attachment entry is an object naming one source: "
				+ $"{entryShape}.");
			// The object entries are still walked: a key the contract drops is reported in the same refusal.
			return false;
		}

		// webService.timeoutSeconds is an Int32 on the server's write contract, while describe reports a timeout
		// stored in milliseconds as fractional seconds (1500 ms reads back as 1.5) and a whole one as e.g. 7.0, which
		// the server reads fine - so the VALUE is judged, not the spelling. A fraction or a value outside Int32 is not
		// dropped: the serializer fails to read the WHOLE request, in a reply that does not name the member. Only a
		// JSON number is judged here, and one too large for decimal (1e30) counts as beyond Int32; any other shape, and a
		// whole value above the server's own maximum (2147483 seconds, whose milliseconds still fit), is left to the
		// server's refusal, which names that bound - the one owner of it.
		private void RefuseFractionalTimeout(JsonObject webService, string where, string path) {
			JsonNode? timeout = null;
			try {
				if (!webService.TryGetPropertyValue("timeoutSeconds", out timeout)) {
					return;
				}
			} catch (ArgumentException) {
				return; // a repeated key, which the walk refuses on its own
			}
			if (timeout is not JsonValue number || number.GetValueKind() != JsonValueKind.Number) {
				return;
			}
			string text = number.ToJsonString();
			bool parsed = decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value);
			if (parsed && value == decimal.Truncate(value) && value >= int.MinValue && value <= int.MaxValue) {
				return;
			}
			string problem = !parsed || value == decimal.Truncate(value)
				? "beyond what the server can read"
				: "a fraction, and the server takes whole seconds";
			RefuseShape($"{where}: '{path}.timeoutSeconds' is {text}, {problem}, so the server fails the whole request. "
				+ "On setElement leave timeoutSeconds out to keep the stored timeout (describe reports a timeout stored in "
				+ "milliseconds as a fraction); otherwise send whole seconds (0 clears the timeout; the server refuses more "
				+ "than 2147483).");
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
			if (depth != 1 || !Element.Declares(key) || !Blocks.Contains(set)) {
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
			if (_refusals.Count == 0 && _shapeRefusals.Count == 0 && _readBacks.Count == 0) {
				return ProcessBlockKeyCheckResult.Clean;
			}
			string error = null;
			if (_refusals.Count > 0) {
				IEnumerable<string> shown = _refusals.Take(MaxReportedKeys);
				string more = _refusals.Count > MaxReportedKeys
					? $" (and {_refusals.Count - MaxReportedKeys} more)"
					: string.Empty;
				error = "Nothing was sent: these keys are not part of the request the server reads - a current "
					+ "CrtProcessBuilder refuses or ignores them, an older one drops them without a word and saves a "
					+ "process that ignores them. " + string.Join(" ", shown) + more;
			}
			if (_shapeRefusals.Count > 0) {
				string moreShapes = _shapeRefusals.Count > MaxReportedKeys
					? $" (and {_shapeRefusals.Count - MaxReportedKeys} more)"
					: string.Empty;
				string shapes = string.Join(" ", _shapeRefusals.Take(MaxReportedKeys)) + moreShapes;
				error = error is null ? "Nothing was sent: " + shapes : error + " Also: " + shapes;
			}
			IReadOnlyList<string> readBacks = [];
			if (_readBacks.Count > 0) {
				string moreReadBacks = _readBacks.Count > MaxReportedKeys
					? $" (and {_readBacks.Count - MaxReportedKeys} more)"
					: string.Empty;
				readBacks = [
					"Ignored describe-business-process read-back fields (derived on read, never written): "
					+ string.Join("; ", _readBacks.Take(MaxReportedKeys)) + moreReadBacks + "."
				];
			}
			return new ProcessBlockKeyCheckResult(error, readBacks);
		}

	}

}

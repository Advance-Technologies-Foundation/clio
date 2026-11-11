using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command.ProcessModel;
using Clio.Tests.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Unit tests for <see cref="ProcessBlockKeyCheck"/> - the pre-send refusal of element and block keys the
/// CrtProcessBuilder contract does not declare: a package before 1.6.6.136 drops them without a word, a current one
/// refuses or ignores them, and this check answers first (ENG-103085, ENG-95244).
/// </summary>
[TestFixture]
[Property("Module", "ProcessModel")]
[Category("Unit")]
public sealed class ProcessBlockKeyCheckTests {

	private const string ReproElement = """
		{
		  "name": "ReadFiledRequest",
		  "type": "readData",
		  "readData": {
		    "source": "AppRequest",
		    "mode": "first",
		    "filter": {
		      "object": "AppRequest",
		      "conditions": [
		        { "column": "Id", "comparison": "equal",
		          "elementParameter": { "elementName": "RequestFiledSignal", "parameter": "RecordId" } }
		      ]
		    }
		  }
		}
		""";

	private static ProcessBlockKeyCheckResult CheckElements(params string[] elements) =>
		ProcessBlockKeyCheck.Check(JsonNode.Parse(
			$"{{\"name\":\"UsrP\",\"elements\":[{string.Join(",", elements)}],\"flows\":[]}}"));

	private static ProcessBlockKeyCheckResult CheckOperations(string operations) =>
		ProcessBlockKeyCheck.Check(JsonNode.Parse(operations));

	[Test]
	[Description("The ENG-103085 repro: a filter nested inside the readData block is refused, names the element and the key path, and points to the element-level filter - instead of the build saving a Read data that reads the first record.")]
	public void Check_Should_RefuseAFilterNestedInReadData_AndPointToTheElementLevelFilter() {
		// Act
		ProcessBlockKeyCheckResult result = CheckElements(ReproElement);

		// Assert
		result.Error.Should().NotBeNull(because: "the server drops the nested filter and the element reads the first record");
		result.Error.Should().Contain("'ReadFiledRequest'", because: "the refusal names the element");
		result.Error.Should().Contain("'readData.filter'", because: "the refusal names the key path the caller sent");
		result.Error.Should().Contain("ELEMENT-level 'filter'",
			because: "the caller is told where the filter belongs");
		result.Error.Should().Contain("Accepted readData keys: source, mode, aggregation, columns, numberOfRecords, sort",
			because: "the refusal lists the block's real keys");
	}

	[TestCase("setElement")]
	[TestCase("removeElement")]
	[Description("A name sent in 'element' - the addElement OBJECT on the server - is refused for an operation on an existing element and points to 'elementName'; the same operation naming its element by 'elementName' passes clean.")]
	public void Check_Should_RefuseANameInElement_AndPointToElementName(string op) {
		// Act
		ProcessBlockKeyCheckResult refused = CheckOperations($"[{{\"op\":\"{op}\",\"element\":\"Send1\"}}]");
		ProcessBlockKeyCheckResult accepted = CheckOperations($"[{{\"op\":\"{op}\",\"elementName\":\"Send1\"}}]");

		// Assert
		refused.Error.Should().StartWith("Nothing was sent:", because: "the server cannot read a name in 'element'");
		refused.Error.Should().Contain($"operations[0] ({op})", because: "the refusal names the operation");
		refused.Error.Should().Contain("\"elementName\": \"Send1\"",
			because: "the refusal shows the key to use, with the caller's own element name");
		accepted.Should().BeSameAs(ProcessBlockKeyCheckResult.Clean,
			because: "'elementName' is the key the server reads for an existing element");
	}

	[Test]
	[Description("A name sent in 'element' on addElement is refused with the fix for that operation: 'element' takes the new element as an object with its name and type. It is not told to name an existing element with 'elementName', which addElement has none of.")]
	public void Check_Should_RefuseANameInElement_OnAddElement_AndAskForTheElementObject() {
		// Act
		ProcessBlockKeyCheckResult refused = CheckOperations("[{\"op\":\"addElement\",\"element\":\"Send1\"}]");

		// Assert
		refused.Error.Should().StartWith("Nothing was sent:", because: "the server cannot read a name in 'element'");
		refused.Error.Should().Contain("operations[0] (addElement)", because: "the refusal names the operation");
		refused.Error.Should().Contain("as an object with its 'name' and 'type'",
			because: "addElement adds a new element, so the fix is the element object, not a reference to an existing one");
		refused.Error.Should().Contain("\"element\": {\"name\": \"Send1\", \"type\": ...}",
			because: "the refusal shows the object shape with the caller's own value as its name");
		refused.Error.Should().NotContain("Name an existing element",
			because: "addElement has no existing element to name");
	}

	private const string OneAttachmentObject = """{"processParameter":"Docs"}""";

	private static ProcessBlockKeyCheckResult CheckAttachments(string route, string attachments) {
		string email = $"{{\"subject\":\"s\",\"attachments\":{attachments}}}";
		return route switch {
			"create" => CheckElements($"{{\"name\":\"SE1\",\"type\":\"sendEmail\",\"email\":{email}}}"),
			"addElement" => CheckOperations(
				$"[{{\"op\":\"addElement\",\"element\":{{\"name\":\"SE1\",\"type\":\"sendEmail\",\"email\":{email}}}}}]"),
			_ => CheckOperations(
				$"[{{\"op\":\"setElement\",\"elementName\":\"SE1\",\"elementUpdate\":{{\"email\":{email}}}}}]")
		};
	}

	[TestCase("create")]
	[TestCase("setElement")]
	[TestCase("addElement")]
	[Description("email.attachments sent as one object instead of a list is refused before sending, on create, setElement and addElement: the server's contract reads a list there and fails the whole request on an object, so the refusal says nothing was sent, names the member and shows the array shape.")]
	public void Check_Should_RefuseAttachmentsSentAsOneObject(string route) {
		// Act
		ProcessBlockKeyCheckResult refused = CheckAttachments(route, OneAttachmentObject);
		ProcessBlockKeyCheckResult accepted = CheckAttachments(route, $"[{OneAttachmentObject}]");

		// Assert
		refused.Error.Should().StartWith("Nothing was sent:",
			because: "an object where the contract reads a list fails the whole request on the server");
		refused.Error.Should().Contain("email.attachments' takes a LIST of attachment entries, so a single object",
			because: "the refusal names the member and what was wrong with it");
		refused.Error.Should().Contain("\"attachments\": [",
			because: "the refusal shows the array shape to send instead");
		accepted.Should().BeSameAs(ProcessBlockKeyCheckResult.Clean,
			because: "the same entry wrapped in an array is the shape the server reads");
	}

	[TestCase("create")]
	[TestCase("setElement")]
	[TestCase("addElement")]
	[Description("email.attachments whose entries are not objects - a string and a number among object entries - is refused before sending on every route, naming each wrong entry by its index; a null entry is not refused here, because the server reads it and refuses an entry with no source in its own words.")]
	public void Check_Should_RefuseAttachmentEntriesThatAreNotObjects(string route) {
		// Act
		ProcessBlockKeyCheckResult refused = CheckAttachments(route, $"[{OneAttachmentObject}, \"Docs\", 7]");
		ProcessBlockKeyCheckResult withNull = CheckAttachments(route, $"[{OneAttachmentObject}, null]");

		// Assert
		refused.Error.Should().StartWith("Nothing was sent:",
			because: "an entry that is not an object fails the whole request on the server");
		refused.Error.Should().Contain("email.attachments[1]', '",
			because: "the refusal names the string entry by its index");
		refused.Error.Should().Contain("email.attachments[2]' are not objects",
			because: "the refusal names the number entry too, in one sentence");
		refused.Error.Should().NotContain("attachments[0]",
			because: "the object entry is well formed");
		withNull.Error.Should().BeNull(because: "a null entry reaches the server, which refuses it in its own words");
	}

	[Test]
	[Description("A wrong attachments shape and an unknown key in a well-formed entry of the same list are refused together, so one round trip fixes both.")]
	public void Check_Should_ReportAnUnknownEntryKey_AlongsideANonObjectEntry() {
		// Act
		ProcessBlockKeyCheckResult refused = CheckAttachments("setElement", """[{"parameter":"Docs"}, "Doc"]""");

		// Assert
		refused.Error.Should().Contain("'elementUpdate.email.attachments[0].parameter' is not a key",
			because: "the object entry is still walked for keys the contract drops");
		refused.Error.Should().Contain("email.attachments[1]' is not an object",
			because: "the non-object entry is refused in the same reply");
	}

	[Test]
	[Description("The misplaced value is printed as the caller wrote it: a non-ASCII name and an apostrophe appear as themselves rather than as HTML-safe escapes, so the key the refusal shows can be copied back.")]
	public void Check_Should_PrintTheMisplacedNameReadably() {
		// Act
		ProcessBlockKeyCheckResult refused =
			CheckOperations("[{\"op\":\"setElement\",\"element\":\"Лист O'Brien\"}]");

		// Assert
		refused.Error.Should().Contain("\"elementName\": \"Лист O'Brien\"",
			because: "the printed key is meant to be copied back, so its characters must appear as themselves");
		refused.Error.Should().NotContain("\\u",
			because: "neither a Cyrillic letter nor an apostrophe needs an escape in JSON");
	}

	[Test]
	[Description("The repro with the filter moved to element level - the documented shape - passes without an error or a warning.")]
	public void Check_Should_AcceptAnElementLevelFilter() {
		// Arrange
		string element = """
			{ "name": "ReadFiledRequest", "type": "readData",
			  "readData": { "source": "AppRequest", "mode": "first", "columns": ["Id"], "sort": { "column": "CreatedOn", "direction": "desc" } },
			  "filter": { "object": "AppRequest", "logicalOperation": "and",
			    "conditions": [ { "column": "Id", "comparison": "equal",
			      "elementParameter": { "elementName": "RequestFiledSignal", "parameter": "RecordId" } } ],
			    "groups": [ { "logicalOperation": "or", "conditions": [ { "column": "Name", "comparison": "contains", "value": "x" } ] } ] } }
			""";

		// Act
		ProcessBlockKeyCheckResult result = CheckElements(element);

		// Assert
		result.Should().BeSameAs(ProcessBlockKeyCheckResult.Clean,
			because: "every key is one the server declares, so there is nothing to refuse or warn about");
	}

	private static ProcessBlockKeyCheckResult CheckFilterCondition(string route, string condition) {
		string filter = $"{{\"object\":\"Contact\",\"conditions\":[{condition}]}}";
		return route == "create"
			? CheckElements(
				$"{{\"name\":\"ReadContacts\",\"type\":\"readData\",\"readData\":{{\"source\":\"Contact\"}},\"filter\":{filter}}}")
			: CheckOperations($"[{{\"op\":\"setFilter\",\"elementName\":\"ReadContacts\",\"filter\":{filter}}}]");
	}

	[TestCase("create")]
	[TestCase("setFilter")]
	[Description("ENG-105557: a filter condition over several lookup records - 'values' with equal - is a key the server declares from CrtProcessBuilder 1.6.6.154, so it passes clean on create and on setFilter.")]
	public void Check_Should_AcceptAValueList_OnCreateAndSetFilter(string route) {
		// Act
		ProcessBlockKeyCheckResult result = CheckFilterCondition(route,
			"""{"column":"Country","comparison":"equal","values":["Algeria","Andorra"]}""");

		// Assert
		result.Should().BeSameAs(ProcessBlockKeyCheckResult.Clean,
			because: "'values' is a FilterConditionDescriptor key of the bundled package");
	}

	[TestCase("create")]
	[TestCase("setFilter")]
	[Description("ENG-105557 D7: a list in a filter condition's 'value' - the form agents reach for first - is refused before sending, because the server cannot read the whole request (it answered 'a response clio could not read'); the refusal points to 'values'.")]
	public void Check_Should_RefuseAListInValue_AndPointToValues(string route) {
		// Act
		ProcessBlockKeyCheckResult refused = CheckFilterCondition(route,
			"""{"column":"Country","comparison":"equal","value":["Algeria","Andorra"]}""");

		// Assert
		refused.Error.Should().StartWith("Nothing was sent:",
			because: "a list where the contract reads one string fails the whole request on the server");
		refused.Error.Should().Contain("conditions[0].value' is a list",
			because: "the refusal names the member and what was wrong with it");
		refused.Error.Should().Contain("\"values\": [...]", because: "the refusal shows the shape to send instead");
	}

	[Test]
	[Description("ENG-105557 D7: an object in a filter condition's 'value' fails the whole request as well, so it is refused before sending and the refusal asks for the constant as a string.")]
	public void Check_Should_RefuseAnObjectInValue() {
		// Act
		ProcessBlockKeyCheckResult refused = CheckFilterCondition("setFilter",
			"""{"column":"Country","comparison":"equal","value":{"value":"Algeria"}}""");

		// Assert
		refused.Error.Should().StartWith("Nothing was sent:", because: "the server cannot read an object there");
		refused.Error.Should().Contain("conditions[0].value' is an object",
			because: "the refusal names the member and what was wrong with it");
	}

	[TestCase("\"Algeria\"", "\"Algeria\"")]
	[TestCase("{\"value\":\"Algeria\"}", "an object")]
	[Description("ENG-105557: 'values' that is not a list - one string or an object - fails the whole request on the server, so it is refused before sending with the list shape to send instead.")]
	public void Check_Should_RefuseValuesThatIsNotAList(string values, string printed) {
		// Act
		ProcessBlockKeyCheckResult refused = CheckFilterCondition("create",
			$"{{\"column\":\"Country\",\"comparison\":\"equal\",\"values\":{values}}}");

		// Assert
		refused.Error.Should().Contain($"conditions[0].values' takes a LIST of records, so {printed}",
			because: "the refusal names the member and what was sent there");
		refused.Error.Should().Contain("\"values\": [\"<record>\", \"<record>\"]",
			because: "the refusal shows the list shape");
	}

	[Test]
	[Description("ENG-105557: an entry of 'values' that is an object or a list fails the whole request, so it is refused before sending; plain strings are what the server reads.")]
	public void Check_Should_RefuseAnObjectEntryInValues() {
		// Act
		ProcessBlockKeyCheckResult refused = CheckFilterCondition("setFilter",
			"""{"column":"Country","comparison":"equal","values":["Algeria",{"value":"Andorra"}]}""");

		// Assert
		refused.Error.Should().Contain("every entry of '", because: "the refusal is about the entries")
			.And.Contain("conditions[0].values' is one record as a string",
				because: "the refusal names the member and the shape each entry takes");
	}

	[TestCase("{\"object\":\"Contact\"}")]
	[TestCase("{\"object\":\"Contact\",\"source\":\"Contact\"}")]
	[Description("'object' inside readData stays refused and points to 'source' - with or without a source beside it. Before, only the source-less shape was stopped (by 'requires a source'), and the object next to a source was dropped silently.")]
	public void Check_Should_RefuseObjectInReadData_AndPointToSource(string readData) {
		// Act
		ProcessBlockKeyCheckResult result = CheckElements(
			$"{{\"name\":\"Read1\",\"type\":\"readData\",\"readData\":{readData}}}");

		// Assert
		result.Error.Should().Contain("'readData.object'", because: "object is not a readData key");
		result.Error.Should().Contain("names its object in 'source'", because: "the caller is pointed to source");
	}

	[TestCase("changeData", "{\"source\":\"Contact\",\"values\":[],\"filter\":{}}", "changeData.filter", "ELEMENT-level 'filter'")]
	[TestCase("addData", "{\"source\":\"Contact\",\"rows\":[]}", "addData.rows", "Accepted addData keys")]
	[TestCase("deleteData", "{\"source\":\"Contact\",\"conditions\":[]}", "deleteData.conditions", "Accepted deleteData keys")]
	[TestCase("formula", "{\"body\":\"1\",\"expression\":\"2\"}", "formula.expression", "Accepted formula keys")]
	[TestCase("scriptTask", "{\"body\":\"return true;\",\"code\":\"x\"}", "scriptTask.code", "Accepted scriptTask keys")]
	[TestCase("subProcess", "{\"process\":\"UsrChild\"}", "subProcess.process", "'processName'")]
	[TestCase("email", "{\"subject\":\"s\",\"recipients\":[]}", "email.recipients", "Accepted email keys")]
	[TestCase("approval", "{\"approverType\":\"employee\"}", "approval.approverType", "\"approver\":{\"type\"")]
	[TestCase("openEditPage", "{\"page\":\"P\",\"completionMode\":\"x\"}", "openEditPage.completionMode", "\"completion\":{\"mode\"")]
	[TestCase("preconfiguredPage", "{\"page\":\"P\",\"buttons\":[{\"name\":\"B\",\"action\":\"x\"}]}", "preconfiguredPage.buttons[0].action", "Accepted button keys")]
	[TestCase("accessRights", "{\"source\":\"Contact\"}", "accessRights.source", "names its object in 'object'")]
	[TestCase("signal", "{\"entity\":\"Contact\",\"on\":\"added\",\"columns\":[]}", "signal.columns", "Accepted signal keys")]
	[TestCase("readData", "{\"Source\":\"Contact\"}", "readData.Source", "keys are case-sensitive: write 'source'")]
	[TestCase("readData", "{\"source\":\"Contact\",\"caption\":\"x\"}", "readData.caption", "element-level key: move it out")]
	[TestCase("readData", "{\"source\":\"Contact\",\"sort\":{\"column\":\"Name\",\"order\":\"desc\"}}", "readData.sort.order", "Accepted sort keys")]
	[TestCase("fileProcessing", "{\"attachments\":{\"object\":\"Account\"},\"filter\":{}}", "fileProcessing.filter", "ELEMENT-level 'filter'")]
	[TestCase("fileProcessing", "{\"report\":{\"printable\":\"P\",\"fileNameSuffix\":{\"text\":\"x\"}}}", "fileProcessing.report.fileNameSuffix.text", "Accepted value source keys")]
	[TestCase("fileProcessing", "{\"files\":{\"parameter\":\"PFiles\"}}", "fileProcessing.files.parameter", "Accepted files keys")]
	[Description("An unknown key in any configuration block - not only readData - is refused with its key path and a pointer: a known synonym, a case-only mismatch, an element-level key misplaced into the block, or the block's accepted keys.")]
	public void Check_Should_RefuseAnUnknownKeyInAnyBlock(string block, string body, string path, string pointer) {
		// Act
		ProcessBlockKeyCheckResult result = CheckElements($"{{\"name\":\"E1\",\"type\":\"x\",\"{block}\":{body}}}");

		// Assert
		result.Error.Should().Contain($"'{path}'", because: "the refusal names the exact key path");
		result.Error.Should().Contain(pointer, because: "the refusal says where the key belongs or what is accepted");
	}

	[Test]
	[Description("A Process file element carrying a valid fileProcessing block - every group of the Object attachments variant, the shared value-source vocabulary and the reused Read data sort - passes clean on create and in a setElement update, so clio does not refuse the block before the server reads it.")]
	public void Check_Should_AcceptAValidFileProcessingBlock_OnCreateAndSetElement() {
		// Arrange
		string element = """
			{ "name": "ReadFiles", "type": "fileProcessing",
			  "fileProcessing": { "source": "attachments", "action": "saveToAttachments",
			    "attachments": { "object": "Account", "storage": "dedicated", "fileObject": "AccountFile", "linkColumn": "Account",
			      "recordId": { "processParameter": "PAccount" }, "numberOfRecords": 10,
			      "sort": { "column": "CreatedOn", "direction": "desc" } },
			    "saveTo": { "object": "Contact", "linkColumn": "Contact", "storage": "dedicated", "fileObject": "ContactFile",
			      "recordId": { "sourceElement": "ReadContact", "sourceElementParameter": "ResultEntity", "sourceColumn": "Id" } } },
			  "filter": { "object": "AccountFile", "conditions": [ { "column": "Name", "comparison": "contains", "value": ".pdf" } ] } }
			""";
		string operations = """
			[ { "op": "setElement", "elementName": "ReadFiles", "elementUpdate": {
			    "fileProcessing": { "attachments": { "recordId": { "expression": "[#SysVariable.CurrentUserContact#]" } } } } } ]
			""";

		// Act
		ProcessBlockKeyCheckResult create = CheckElements(element);
		ProcessBlockKeyCheckResult update = CheckOperations(operations);

		// Assert
		create.Should().BeSameAs(ProcessBlockKeyCheckResult.Clean,
			because: "every fileProcessing key is one FileProcessingDescriptor and its groups declare");
		update.Should().BeSameAs(ProcessBlockKeyCheckResult.Clean,
			because: "setElement takes the same fileProcessing block through ProcessElementUpdateDescriptor.FileProcessing");
	}

	[Test]
	[Description("A misspelled key inside fileProcessing.attachments is refused naming the element and the full key path, with the case-only pointer - instead of the server dropping it and reading the attachments of no record.")]
	public void Check_Should_RefuseATypoInsideFileProcessingAttachments_NamingThePath() {
		// Act
		ProcessBlockKeyCheckResult result = CheckElements(
			"{\"name\":\"ReadFiles\",\"type\":\"fileProcessing\",\"fileProcessing\":{\"attachments\":{\"object\":\"Account\","
			+ "\"recordID\":{\"processParameter\":\"PAccount\"},\"numberOfRecord\":5}}}");

		// Assert
		result.Error.Should().Contain("'ReadFiles'", because: "the refusal names the element");
		result.Error.Should().Contain("'fileProcessing.attachments.recordID'",
			because: "the refusal names the exact key path inside the attachments group");
		result.Error.Should().Contain("keys are case-sensitive: write 'recordId'",
			because: "a case-only mismatch is pointed to the declared spelling");
		result.Error.Should().Contain("'fileProcessing.attachments.numberOfRecord'",
			because: "every unknown key is reported, not only the first");
		result.Error.Should().Contain("Accepted attachments keys:",
			because: "the refusal lists the attachments group's real keys");
	}

	[Test]
	[Description("A described Process file element fed back keeps building: the describe-only members (actionStored, outputs, issues on the block, display on every value source) are not refused but listed as ignored read-backs.")]
	public void Check_Should_TolerateNotRefuse_FileProcessingDescribeOnlyFields() {
		// Arrange
		string element = """
			{ "name": "ReadFiles", "type": "fileProcessing",
			  "fileProcessing": { "source": "attachments", "action": "useInProcess", "actionStored": false,
			    "outputs": [ { "parameter": "ObjectFiles", "item": "File" } ], "issues": [ "an unset action" ],
			    "attachments": { "object": "Account", "recordId": { "processParameter": "PAccount", "display": "Account" } },
			    "saveTo": { "object": "Contact", "recordId": { "value": "c1", "display": "John Best" } } } }
			""";

		// Act
		ProcessBlockKeyCheckResult result = CheckElements(element);

		// Assert
		result.Error.Should().BeNull(because: "describe-only members are derived and harmless to ignore");
		result.IgnoredReadBacks.Should().ContainSingle(because: "the ignored fields are listed in one summary line")
			.Which.Should().Contain("fileProcessing.actionStored", because: "the block's describe-only flag is named")
			.And.Contain("fileProcessing.outputs", because: "the derived outputs are named")
			.And.Contain("fileProcessing.issues", because: "the derived issues are named")
			.And.Contain("fileProcessing.attachments.recordId.display",
				because: "a value source's display is describe-only")
			.And.Contain("fileProcessing.saveTo.recordId.display",
				because: "display is tolerated on every value source, saveTo's included");
	}

	[Test]
	[Description("A Generated report and a Process parameter element with valid report and files groups pass clean, so clio does not refuse the two variants' write members before the server reads them.")]
	public void Check_Should_AcceptValidReportAndFilesGroups() {
		// Arrange
		string elements = """
			{ "name": "Report1", "type": "fileProcessing",
			  "fileProcessing": { "source": "report", "action": "saveToAttachments",
			    "report": { "printable": "6c1a3a39-0000-4000-8000-000000000001", "type": "MS Word", "object": "Invoice",
			      "recordId": { "processParameter": "PInvoice" }, "separateReports": true,
			      "fileNameSuffix": { "value": "copy" } },
			    "saveTo": { "object": "Invoice", "recordId": { "processParameter": "PInvoice" } } } },
			{ "name": "Files1", "type": "fileProcessing",
			  "fileProcessing": { "source": "files", "action": "saveToAttachments",
			    "files": { "sourceElement": "Report1", "sourceElementParameter": "ReportFiles" },
			    "saveTo": { "object": "Contact", "recordId": { "processParameter": "PContact" } } } }
			""";

		// Act
		ProcessBlockKeyCheckResult result = CheckElements(elements);

		// Assert
		result.Should().BeSameAs(ProcessBlockKeyCheckResult.Clean,
			because: "every report and files key is one FileProcessingReportDescriptor or FileProcessingFilesDescriptor declares");
	}

	[Test]
	[Description("Described Generated report and Process parameter elements fed back keep building: report.caption, report.convertToPdf, bindingLevels and the display of files are listed as ignored read-backs, never refused.")]
	public void Check_Should_TolerateNotRefuse_ReportAndFilesDescribeOnlyFields() {
		// Arrange
		string elements = """
			{ "name": "Report1", "type": "fileProcessing",
			  "fileProcessing": { "source": "report", "action": "useInProcess", "actionStored": true,
			    "report": { "printable": "p1", "caption": "Invoice", "convertToPdf": false, "type": "MS Word",
			      "object": "Invoice", "recordId": { "processParameter": "PInvoice", "display": "Invoice" },
			      "separateReports": true } } },
			{ "name": "Files1", "type": "fileProcessing",
			  "fileProcessing": { "source": "files", "bindingLevels": "both",
			    "files": { "processParameter": "Docs", "display": "Documents" },
			    "saveTo": { "object": "Contact", "recordId": { "value": "c1", "display": "John Best" } } } }
			""";

		// Act
		ProcessBlockKeyCheckResult result = CheckElements(elements);

		// Assert
		result.Error.Should().BeNull(because: "the two variants' describe-only members are derived and harmless to ignore");
		result.IgnoredReadBacks.Should().ContainSingle(
				because: "the ignored fields of both elements are reported in one warning, and their seven stay below the cap that would shorten it")
			.Which.Should().NotContain("more)", because: "every ignored field is listed, none folded into a count")
			.And.Contain("fileProcessing.actionStored", because: "the block's describe-only flag is named")
			.And.Contain("fileProcessing.report.caption", because: "the printable's caption is describe-only")
			.And.Contain("fileProcessing.report.convertToPdf", because: "the printable's PDF flag is describe-only")
			.And.Contain("fileProcessing.report.recordId.display", because: "display is tolerated on the report's value sources")
			.And.Contain("fileProcessing.bindingLevels", because: "the bound levels of Files are describe-only")
			.And.Contain("fileProcessing.files.display", because: "the files source carries describe's display too");
	}

	[Test]
	[Description("A sourceColumn on the files group is refused, not tolerated as a read-back: the write side has no such member, and dropping it would copy the files of another source than the caller named.")]
	public void Check_Should_RefuseASourceColumnOnTheFilesGroup() {
		// Act
		ProcessBlockKeyCheckResult result = CheckElements(
			"{\"name\":\"Files1\",\"type\":\"fileProcessing\",\"fileProcessing\":{\"files\":{\"sourceElement\":\"R\","
			+ "\"sourceElementParameter\":\"ResultEntity\",\"sourceColumn\":\"File\"}}}");

		// Assert
		result.Error.Should().Contain("'fileProcessing.files.sourceColumn'",
			because: "files takes no sourceColumn, and the refusal names the exact key path");
	}

	[Test]
	[Description("A described files group that names a record column - the shape describe reports when Files is bound to a column of a record - is refused on a setElement resubmit with a pointer that says why and how to keep the stored binding: leave files out. Without it a caller resubmitting the describe output reads a bare unknown-key refusal for a key describe itself produced.")]
	public void Check_Should_PointToKeepingTheStoredBinding_WhenADescribedFilesSourceColumnIsResubmitted() {
		// Arrange
		const string operations = """
			[ { "op": "setElement", "elementName": "Files1", "elementUpdate": { "fileProcessing": {
			    "files": { "sourceElement": "ReadInvoice", "sourceElementParameter": "ResultEntity",
			      "sourceColumn": "Attachment", "display": "Invoice.Attachment" } } } } ]
			""";

		// Act
		ProcessBlockKeyCheckResult result = CheckOperations(operations);

		// Assert
		result.Error.Should().Contain("'elementUpdate.fileProcessing.files.sourceColumn' is not a key of the 'files' block",
			because: "the write contract has no sourceColumn, and dropping it would bind another source than the described column");
		result.Error.Should().Contain("describe reports 'files' this way when Files is bound to a column of a record",
			because: "the refusal must say the key came from describe, so the caller does not hunt for a typo");
		result.Error.Should().Contain("on setElement leave 'files' out to keep the stored binding, or bind another source",
			because: "an omitted files group is kept as stored, which is the one way to resubmit such an element unchanged");
	}

	[Test]
	[Description("An unknown key deep inside the element's own filter (a condition, an element-parameter reference) is refused too, because the server drops it the same way.")]
	public void Check_Should_RefuseAnUnknownKeyInsideTheFilterTree() {
		// Arrange
		string element = """
			{ "name": "R", "type": "readData", "readData": { "source": "Contact" },
			  "filter": { "object": "Contact", "conditions": [ { "column": "Id", "operator": "equal",
			    "elementParameter": { "elementName": "S", "param": "RecordId" } } ] } }
			""";

		// Act
		ProcessBlockKeyCheckResult result = CheckElements(element);

		// Assert
		result.Error.Should().Contain("'filter.conditions[0].operator'", because: "the condition's operator key is comparison");
		result.Error.Should().Contain("'filter.conditions[0].elementParameter.param'",
			because: "every unknown key is reported, not only the first");
	}

	[Test]
	[Description("An unknown key at element level is refused too; parameters and connections point to where they are really written.")]
	public void Check_Should_RefuseUnknownElementKeys_WithPointers() {
		// Act
		ProcessBlockKeyCheckResult result = CheckElements(
			"{\"name\":\"T\",\"type\":\"userTask\",\"parameters\":[],\"connections\":[],\"color\":\"red\"}");

		// Assert
		result.Error.Should().Contain("'parameters' is not a key of the element - an element's inputs are bound through",
			because: "parameters are bound through mappings");
		result.Error.Should().Contain("setConnections", because: "connections are set by their own operation");
		result.Error.Should().Contain("'color' is not a key of the element", because: "an unknown element key is refused");
	}

	[Test]
	[Description("A described element fed back keeps building: the derived read-back fields describe-business-process emits (position, uid, sourceSchemaUId, outputs, ...) are not refused but listed as ignored read-backs.")]
	public void Check_Should_TolerateNotRefuse_DescribeReadBackFields() {
		// Arrange
		string element = """
			{ "name": "R", "uid": "u", "type": "readData", "buildType": "readData", "position": { "x": 1, "y": 2 },
			  "size": {}, "readData": { "source": "Contact", "sourceSchemaUId": "g", "outputs": ["ResultEntity"] },
			  "signal": null }
			""";

		// Act
		ProcessBlockKeyCheckResult result = CheckElements(element);

		// Assert
		result.Error.Should().BeNull(because: "read-back fields are derived and harmless to ignore");
		result.IgnoredReadBacks.Should().ContainSingle(because: "the ignored fields are listed in one summary line")
			.Which.Should().Contain("readData.sourceSchemaUId", because: "the block's ignored field is named")
			.And.Contain("position", because: "the element's ignored field is named")
			.And.Contain("readData.outputs", because: "every ignored field is named");
	}

	[Test]
	[Description("modify-business-process: a filter nested in setElement's readData points to the setFilter operation; one inside an addElement element points to the element-level filter; setFilter and setSignal payloads are checked as well.")]
	public void Check_Should_RefuseMisplacedKeysInModifyOperations() {
		// Arrange
		string operations = """
			[
			  { "op": "setElement", "elementName": "Read1", "elementUpdate": { "readData": { "source": "Contact", "filter": {} } } },
			  { "op": "setElement", "elementName": "Read2", "elementUpdate": { "filter": {} } },
			  { "op": "addElement", "element": { "name": "Read3", "type": "readData", "readData": { "source": "Contact", "filter": {} } } },
			  { "op": "setFilter", "elementName": "Read4", "filter": { "object": "Contact", "where": [] } },
			  { "op": "SETSIGNAL", "elementName": "Start", "signal": { "entity": "Contact", "event": "added" } }
			]
			""";

		// Act
		ProcessBlockKeyCheckResult result = CheckOperations(operations);

		// Assert
		result.Error.Should().Contain("operations[0] (setElement 'Read1'): 'elementUpdate.readData.filter'",
			because: "the setElement block key is refused with its operation and path");
		result.Error.Should().Contain("{\"op\":\"setFilter\"", because: "setElement points a filter to setFilter");
		result.Error.Should().Contain("operations[1] (setElement 'Read2'): 'elementUpdate.filter'",
			because: "elementUpdate has no filter member");
		result.Error.Should().Contain("operations[2] (addElement 'Read3'): 'element.readData.filter'",
			because: "an addElement element is checked like a create element");
		result.Error.Should().Contain("'filter.where'", because: "a setFilter payload is checked");
		result.Error.Should().Contain("'signal.event'", because: "a setSignal payload is checked, op case-insensitively");
	}

	[Test]
	[Description("A key the element declares is pointed back to element level only when it sits directly in a block; deeper (a recipient's 'name') it is that object's own unknown field, and setElement names the element-level keys it cannot change.")]
	public void Check_Should_PointToElementLevel_OnlyDirectlyInsideABlock() {
		// Arrange
		string operations = """
			[
			  { "op": "addElement", "element": { "name": "Mail", "type": "sendEmail", "email": { "to": [ { "value": "a@b.c", "name": "A" } ] } } },
			  { "op": "setElement", "elementName": "Mail", "elementUpdate": { "caption": "New" } }
			]
			""";

		// Act
		ProcessBlockKeyCheckResult result = CheckOperations(operations);

		// Assert
		result.Error.Should().Contain("'element.email.to[0].name' is not a key of the 'recipient' block. Accepted",
			because: "a recipient's name is not the element's name, so no element-level pointer is given");
		result.Error.Should().Contain("'elementUpdate.caption' is not a key of the 'elementUpdate' block - 'caption' is set when the element is added",
			because: "setElement cannot change an element-level key and says so");
	}

	[Test]
	[Description("Inside the element's own filter or signal, an element-level key name is that object's unknown field - not a key misplaced into a configuration block - so no element-level pointer is given.")]
	public void Check_Should_NotPointToElementLevel_FromInsideTheElementFilterOrSignal() {
		// Act
		ProcessBlockKeyCheckResult result = CheckElements(
			"{\"name\":\"S\",\"type\":\"signalStart\",\"signal\":{\"entity\":\"Contact\",\"caption\":\"x\"},"
			+ "\"filter\":{\"object\":\"Contact\",\"filter\":{}}}");

		// Assert
		result.Error.Should().Contain("'signal.caption' is not a key of the 'signal' block. Accepted",
			because: "a signal's unknown caption gets the accepted keys, not a move-to-element pointer");
		result.Error.Should().Contain("'filter.filter' is not a key of the 'filter' block. Accepted",
			because: "a filter nested in the element's filter must not be told to use the filter it is already in");
	}

	[Test]
	[Description("A repeated key - which JsonNode.Parse accepts and only trips over when the object is read - is refused with its location instead of escaping as an ArgumentException, in a block and in an operation.")]
	public void Check_Should_RefuseARepeatedKey() {
		// Act
		ProcessBlockKeyCheckResult inBlock = CheckElements(
			"{\"name\":\"R\",\"type\":\"readData\",\"readData\":{\"source\":\"A\",\"source\":\"B\"}}");
		ProcessBlockKeyCheckResult inOperation = CheckOperations(
			"[{\"op\":\"setFilter\",\"op\":\"setSignal\",\"elementName\":\"S\"}]");

		// Assert
		inBlock.Error.Should().Contain("'R': 'readData' repeats a key",
			because: "a repeated block key is ambiguous and is refused with the block it sits in");
		inOperation.Error.Should().Contain("operations[0]: the object repeats a key",
			because: "a repeated operation key is refused, not thrown as an ArgumentException");
	}

	[Test]
	[Description("Valid modify operations - including ops whose payload this check does not walk - pass clean.")]
	public void Check_Should_AcceptValidModifyOperations() {
		// Arrange
		string operations = """
			[
			  { "op": "setElement", "elementName": "Read1", "elementUpdate": { "readData": { "source": "Contact", "mode": "count" }, "useBackgroundMode": true } },
			  { "op": "setFilter", "elementName": "Read1", "filter": { "object": "Contact", "conditions": [ { "column": "Name", "comparison": "equal", "value": "A" } ] } },
			  { "op": "addMapping", "mapping": { "elementName": "Read1", "elementParameter": "X", "value": "1" } },
			  { "op": "addFlow", "source": "A", "target": "B" }
			]
			""";

		// Act
		ProcessBlockKeyCheckResult result = CheckOperations(operations);

		// Assert
		result.Should().BeSameAs(ProcessBlockKeyCheckResult.Clean, because: "every walked key is declared by the server");
	}

	[Test]
	[Description("An attachments list on the Send email block passes clean on every route the block travels - a create element, an addElement element, a setElement update, and an empty list (which removes every attachment) - so clio no longer refuses email.attachments before the server reads it.")]
	public void Check_Should_AcceptAnAttachmentsList_OnEveryRoute() {
		// Arrange
		const string email = """
			{ "mode": "manual", "subject": "Your invoice", "to": [ { "processParameter": "Contact" } ],
			  "attachments": [
			    { "sourceElement": "GenerateInvoice", "sourceElementParameter": "ReportFiles" },
			    { "sourceElement": "ReadContractFiles", "sourceElementParameter": "ObjectFiles.File" },
			    { "processParameter": "Documents" },
			    { "expression": "[#Documents#]" } ] }
			""";
		string element = $$"""{ "name": "SE1", "type": "sendEmail", "email": {{email}} }""";
		string operations = $$"""
			[ { "op": "addElement", "element": { "name": "SE2", "type": "sendEmail", "email": {{email}} } },
			  { "op": "setElement", "elementName": "SE1", "elementUpdate": { "email": {{email}} } },
			  { "op": "setElement", "elementName": "SE3", "elementUpdate": { "email": { "attachments": [] } } } ]
			""";

		// Act
		ProcessBlockKeyCheckResult create = CheckElements(element);
		ProcessBlockKeyCheckResult modify = CheckOperations(operations);

		// Assert
		create.Should().BeSameAs(ProcessBlockKeyCheckResult.Clean,
			because: "SendEmailDescriptor declares attachments and every entry key is a SendEmailAttachmentDescriptor member");
		modify.Should().BeSameAs(ProcessBlockKeyCheckResult.Clean,
			because: "addElement and setElement carry the same email block, and an empty list is how every attachment is removed");
	}

	[TestCase("sourceElementParam", "Accepted attachment keys: processParameter, sourceElement, sourceElementParameter, expression, value")]
	[TestCase("file", "Accepted attachment keys:")]
	[TestCase("SourceElement", "keys are case-sensitive: write 'sourceElement'")]
	[Description("An unknown key inside an attachment entry is refused naming the element and the entry's key path, with the entry's accepted keys - instead of the server dropping the key and attaching nothing from the source the caller meant.")]
	public void Check_Should_RefuseAnUnknownAttachmentEntryKey_NamingItsPath(string key, string pointer) {
		// Arrange
		string element =
			$"{{\"name\":\"SE1\",\"type\":\"sendEmail\",\"email\":{{\"subject\":\"s\",\"attachments\":[{{\"{key}\":\"OF1\"}}]}}}}";

		// Act
		ProcessBlockKeyCheckResult result = CheckElements(element);

		// Assert
		result.Error.Should().NotBeNull(because: "the server drops an undeclared entry key without a word");
		result.Error.Should().Contain("'SE1'", because: "the refusal names the element");
		result.Error.Should().Contain($"'email.attachments[0].{key}'",
			because: "the refusal names the entry and the key inside it");
		result.Error.Should().Contain(pointer, because: "the refusal says what an attachment entry accepts");
	}

	[Test]
	[Description("A described attachment entry fed back verbatim - the modify recipe is describe, edit the list, send it back - keeps building: its slot, bindingLevels and issues are listed as ignored read-backs, never refused.")]
	public void Check_Should_TolerateTheReadBackFields_OfADescribedEntry() {
		// Arrange
		string operations = """
			[ { "op": "setElement", "elementName": "SE1", "elementUpdate": { "email": { "attachments": [
			    { "sourceElement": "OF1", "sourceElementParameter": "ObjectFiles", "slot": "Attachments1", "bindingLevels": "both" },
			    { "processParameter": "Doc", "slot": "Attachments3", "bindingLevels": "itemOnly",
			      "issues": [ "'Attachments3' is empty and attaches nothing" ] } ] } } } ]
			""";

		// Act
		ProcessBlockKeyCheckResult result = CheckOperations(operations);

		// Assert
		result.Error.Should().BeNull(because: "the read-back members are derived on describe and harmless to ignore");
		result.IgnoredReadBacks.Should().ContainSingle(because: "the ignored fields are listed in one summary line")
			.Which.Should().Contain("email.attachments[0].slot", because: "the stored row name is describe-only")
			.And.Contain("email.attachments[0].bindingLevels", because: "the bound levels are describe-only")
			.And.Contain("email.attachments[1].issues", because: "the issues are describe-only");
	}

	[Test]
	[Description("A Call web service element carrying a valid webService block - service, method, timeoutSeconds and ignoreErrors - passes clean on every route the block travels: a create element, an addElement element and a setElement update, a partial one included (Q2), so clio does not refuse the block before the server reads it.")]
	public void Check_Should_AcceptTheWebServiceBlock_OnEveryRoute() {
		// Arrange
		const string block = """{ "service": "UsrWscEchoService", "method": "UsrWscEcho", "timeoutSeconds": 30, "ignoreErrors": true }""";
		string element = $$"""{ "name": "WS1", "type": "webService", "webService": {{block}} }""";
		string operations = $$"""
			[ { "op": "addElement", "element": { "name": "WS2", "type": "webService", "webService": {{block}} } },
			  { "op": "setElement", "elementName": "WS1", "elementUpdate": { "webService": {{block}} } },
			  { "op": "setElement", "elementName": "WS1", "elementUpdate": { "webService": { "method": "UsrWscGet", "timeoutSeconds": 0 } } } ]
			""";

		// Act
		ProcessBlockKeyCheckResult create = CheckElements(element);
		ProcessBlockKeyCheckResult modify = CheckOperations(operations);

		// Assert
		create.Should().BeSameAs(ProcessBlockKeyCheckResult.Clean,
			because: "every key of the block is a WebServiceDescriptor member");
		modify.Should().BeSameAs(ProcessBlockKeyCheckResult.Clean,
			because: "addElement and setElement carry the same block, and a setElement may send only the members it changes");
	}

	[TestCase("multiInstanceOptions")]
	[TestCase("timeout")]
	[TestCase("Method")]
	[Description("An unknown key in the webService block is refused before sending, naming the element, the key path and the block's accepted keys - webService.multiInstanceOptions included, because a Call web service element is never multi-instance on this route (Q20) - instead of the server refusing it, or an older one dropping it and saving a different call than the caller described.")]
	public void Check_Should_RefuseAnUnknownWebServiceKey_NamingItsPath(string key) {
		// Arrange
		string element = $$"""{ "name": "WS1", "type": "webService", "webService": { "service": "UsrWscEchoService", "method": "UsrWscEcho", "{{key}}": 1 } }""";

		// Act
		ProcessBlockKeyCheckResult result = CheckElements(element);

		// Assert
		result.Error.Should().StartWith("Nothing was sent", because: "the refusal is clio's own, before anything is posted");
		result.Error.Should().Contain("'WS1'", because: "the refusal names the element");
		result.Error.Should().Contain($"'webService.{key}'", because: "the refusal names the key path inside the block");
		result.Error.Should().Contain("Accepted webService keys: service, method, timeoutSeconds, ignoreErrors",
			because: "the refusal says what the block accepts");
	}

	[TestCase("1.5")]
	[TestCase("0.4")]
	[TestCase("3000000000")]
	[TestCase("1e30")]
	[Description("A webService.timeoutSeconds that is not a whole Int32 - the fractional seconds describe reports for a timeout stored in milliseconds, or a number beyond Int32, one too large even for decimal included - is refused before sending with the remedy, because the server's serializer cannot read it and fails the whole request in a reply that does not name the member.")]
	public void Check_Should_RefuseATimeoutThatIsNotWholeSeconds(string timeout) {
		// Arrange
		string element = $$"""{ "name": "WS1", "type": "webService", "webService": { "service": "UsrWscEchoService", "method": "UsrWscEcho", "timeoutSeconds": {{timeout}} } }""";
		string operations = $$"""[ { "op": "setElement", "elementName": "WS1", "elementUpdate": { "webService": { "timeoutSeconds": {{timeout}} } } } ]""";

		// Act
		ProcessBlockKeyCheckResult create = CheckElements(element);
		ProcessBlockKeyCheckResult modify = CheckOperations(operations);

		// Assert
		create.Error.Should().StartWith("Nothing was sent", because: "the refusal is clio's own, before anything is posted");
		create.Error.Should().Contain($"'webService.timeoutSeconds' is {timeout}",
			because: "the refusal names the member and the value sent");
		create.Error.Should().Contain("leave timeoutSeconds out to keep the stored timeout",
			because: "the refusal carries the remedy that keeps the stored value");
		create.Error.Should().Contain("the server refuses more than 2147483",
			because: "the whole-seconds bound is the server's, which clio does not check, so the text says whose it is");
		modify.Error.Should().Contain("'elementUpdate.webService.timeoutSeconds'",
			because: "a setElement sending only the timeout is checked the same way");
	}

	[TestCase("30")]
	[TestCase("0")]
	[TestCase("7.0")]
	[TestCase("2147484")]
	[Description("A whole-seconds webService.timeoutSeconds passes the check - 0 included, which clears a stored timeout, 7.0, the spelling describe uses for a whole timeout, which the server reads, and a whole value above the server's own 2147483-second maximum, which the server refuses naming that bound.")]
	public void Check_Should_AcceptAWholeSecondsTimeout(string timeout) {
		// Arrange
		string operations = $$"""[ { "op": "setElement", "elementName": "WS1", "elementUpdate": { "webService": { "timeoutSeconds": {{timeout}} } } } ]""";

		// Act
		ProcessBlockKeyCheckResult result = CheckOperations(operations);

		// Assert
		result.Should().BeSameAs(ProcessBlockKeyCheckResult.Clean, because: "a whole number of seconds is what the server reads");
	}

	[Test]
	[Description("A described webService block fed back unchanged - the replay rule describe states (Q14), service being the name string on both sides - keeps building: serviceUId, serviceResolved, methodResolved, issues and parameters are describe-only, so they are listed as ignored read-backs, never refused.")]
	public void Check_Should_TolerateTheReadBackFields_OfADescribedBlock() {
		// Arrange
		string operations = """
			[ { "op": "setElement", "elementName": "WS1", "elementUpdate": { "webService": {
			    "service": "UsrWscEchoService", "serviceUId": "2ac0b905-591d-4623-a8bf-990846cea7d3", "method": "UsrWscEcho",
			    "timeoutSeconds": 30, "ignoreErrors": true, "serviceResolved": true, "methodResolved": true, "issues": [],
			    "parameters": [ { "name": "UsrWscText_In", "tag": "UsrWscText", "direction": "in", "type": "MaxSizeText", "bound": true } ] } } } ]
			""";

		// Act
		ProcessBlockKeyCheckResult result = CheckOperations(operations);

		// Assert
		result.Error.Should().BeNull(because: "the read-back members are derived on describe and harmless to ignore");
		result.IgnoredReadBacks.Should().ContainSingle(because: "the ignored fields are listed in one summary line")
			.Which.Should().Contain("webService.serviceUId", because: "the stored service UId is describe-only")
			.And.Contain("webService.serviceResolved", because: "the service flag is describe-only")
			.And.Contain("webService.methodResolved", because: "the method flag is describe-only")
			.And.Contain("webService.issues", because: "the issues are describe-only")
			.And.Contain("webService.parameters", because: "the stored parameter tree is describe-only");
	}

	[Test]
	[Description("Enforcing a described webService block fed back unchanged does not refuse it: the describe-only members are tolerated, and the check still lists them as ignored. clio writes no warning of its own about them - Enforce takes no logger - because the server names the same members in its own warning.")]
	public void Enforce_Should_NotRefuseADescribedBlock() {
		// Arrange
		JsonNode operations = JsonNode.Parse("""
			[ { "op": "setElement", "elementName": "WS1", "elementUpdate": { "webService": {
			    "service": "UsrWscEchoService", "serviceUId": "2ac0b905-591d-4623-a8bf-990846cea7d3", "method": "UsrWscEcho",
			    "serviceResolved": true, "methodResolved": true, "issues": [] } } } ]
			""");

		// Act
		Action act = () => ProcessBlockKeyCheck.Enforce(operations);

		// Assert
		act.Should().NotThrow(because: "describe-only members are tolerated, never refused");
		ProcessBlockKeyCheck.Check(operations).IgnoredReadBacks.Should().ContainSingle(
			because: "the check still knows which members it tolerated, though Enforce no longer writes them anywhere");
	}

	[Test]
	[Description("Every key set mirrors its server request contract in the bundled CrtProcessBuilder exactly, so a new server member is never refused here by a stale list and a key the server lacks is never accepted.")]
	public void KeySets_Should_EqualTheMembersOfTheBundledServerContracts() {
		// Arrange
		string archive = BundledProcessBuilderSource.ReadAsText();

		// Act / Assert
		ProcessBlockKeyCheck.All.Should().HaveCountGreaterThan(40, because: "anti-vacuity: the whole tree was walked");
		foreach (ProcessBlockKeyCheck.KeySet set in ProcessBlockKeyCheck.All) {
			List<string> members = [.. BundledProcessBuilderSource.DataMemberNames(archive, set.ContractType)];
			if (set.ContractType == "FilterDescriptor") {
				members.AddRange(BundledProcessBuilderSource.DataMemberNames(archive, "FilterGroupDescriptor"));
			}
			members.Should().NotBeEmpty(because: $"{set.ContractType} must exist in the bundled archive");
			set.Keys.Should().BeEquivalentTo(members, because: $"{set.Label} mirrors {set.ContractType}");
			set.Pointers.Keys.Should().NotIntersectWith(members,
				because: $"a pointer on {set.Label} must never shadow a key the server accepts");
		}
	}

	[Test]
	[Description("Every tolerated read-back field is one describe-business-process really emits for that block and one the write contract lacks - so the tolerance cannot hide a misspelled write key.")]
	public void ReadBackFields_Should_BeDescribeOnlyMembers() {
		// Arrange
		string archive = BundledProcessBuilderSource.ReadAsText();

		// Act / Assert
		foreach (ProcessBlockKeyCheck.KeySet set in ProcessBlockKeyCheck.All.Where(set => set.ReadBack.Count > 0)) {
			set.DescribeType.Should().NotBeNull(because: $"{set.Label} tolerates read-back fields of a describe type");
			IReadOnlyList<string> described = BundledProcessBuilderSource.DataMemberNames(archive, set.DescribeType);
			set.ReadBack.Should().BeSubsetOf(described, because: $"{set.DescribeType} emits each tolerated field");
			set.ReadBack.Should().NotIntersectWith(set.Keys, because: "a write key is never treated as read-back");
		}
	}

}

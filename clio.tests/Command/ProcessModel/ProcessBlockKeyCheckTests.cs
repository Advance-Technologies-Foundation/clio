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
/// CrtProcessBuilder contract does not declare and its serializer would drop without a word (ENG-103085).
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
	[Description("A Process file element carrying a valid fileProcessing block - every group of the Object attachments variant, the shared value-source vocabulary and the reused Read data sort - passes clean on create and in a setElement update, so clio does not refuse the block before the server reads it (ENG-96505).")]
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
	[Description("A misspelled key inside fileProcessing.attachments is refused naming the element and the full key path, with the case-only pointer - instead of the server dropping it and reading the attachments of no record (ENG-96505).")]
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
	[Description("A described Process file element fed back keeps building: the describe-only members (actionStored, outputs, issues on the block, display on every value source) are not refused but named in the read-back warning (ENG-96505).")]
	public void Check_Should_WarnButNotRefuse_FileProcessingDescribeOnlyFields() {
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
		result.Warnings.Should().ContainSingle(because: "the ignored fields are reported in one warning")
			.Which.Should().Contain("fileProcessing.actionStored", because: "the block's describe-only flag is named")
			.And.Contain("fileProcessing.outputs", because: "the derived outputs are named")
			.And.Contain("fileProcessing.issues", because: "the derived issues are named")
			.And.Contain("fileProcessing.attachments.recordId.display",
				because: "a value source's display is describe-only")
			.And.Contain("fileProcessing.saveTo.recordId.display",
				because: "display is tolerated on every value source, saveTo's included");
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
	[Description("A described element fed back keeps building: the derived read-back fields describe-business-process emits (position, uid, sourceSchemaUId, outputs, ...) are not refused, but they are named in a warning rather than dropped in silence.")]
	public void Check_Should_WarnButNotRefuse_DescribeReadBackFields() {
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
		result.Warnings.Should().ContainSingle(because: "the ignored fields are reported in one warning")
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

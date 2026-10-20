using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Common;
using Clio.Mcp.E2E.Support;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using Clio.Package;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using static Clio.Mcp.E2E.Support.Mcp.ProcessDesignerE2ESupport;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the "Process file" element, Object attachments variant (ENG-96505, story 1 of ENG-92719)
/// over the real MCP path. NOT in CI - run manually against a stand carrying CrtProcessBuilder 1.6.6.109 or later.
/// <para>What only this level can show is the wire and the platform: the <c>fileProcessing</c> block travels as JSON
/// members a WCF contract drops in silence when they are misspelled, the attachment storage is derived from the
/// stand's REAL entity schemas (<c>AccountFile</c>, <c>ContactFile</c>, and an object with none, which is SysFile
/// storage), the record scope is compiled into a filter the real filter service writes, and describe reads all of
/// it back from a real saved schema. Every unit suite constructs these in C#, so a renamed member would leave them
/// green.</para>
/// <para>Cases reading the designer-built fixture <c>UsrFpOaDesignerFixture</c> (plan section 5.1, OA.0 / OA.11)
/// are Ignored, with the build instructions, when the stand does not carry it. The SysFile storage cases (ENG-103937)
/// have their own floor, 1.6.6.111, and are Ignored below it: an object without an attachment object of its own keeps
/// its files in SysFile, linked by <c>RecordId</c>, and the record scope on that plain-Guid column takes the designer's
/// shape per record object - a lookup <c>In</c> filter for an object with a primary display column (City), a Guid
/// compare for one without (Account address). Every other test builds its own processes and removes them, callers
/// before their callees, in <c>finally</c>. Strictly sequential: schema writes in a parallel burst take a .NET
/// Framework stand's app pool down.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ProcessFileElementToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;
	private const string DescribeToolName = DescribeProcessTool.ToolName;

	/// <summary>
	/// The create/modify floor (1.6.6.109): the Object attachments variant shipped in 1.6.6.103, and 1.6.6.105 registered
	/// the other two, so the fixture's F2 describes as a Generated report element and the generic route is refused
	/// for all three tasks. Named in the skip message.
	/// </summary>
	private const string MinimumPackageVersion = "1.6.6.109";

	/// <summary>What the skip message calls these tests.</summary>
	private const string Subject = "Process file element";

	/// <summary>The build type describe reports for a Process file element (<c>ElementTypes.FileProcessing</c>).</summary>
	private const string FileProcessingBuildType = "fileprocessing";

	/// <summary>The Object attachments task schema; its UId is also the element's <c>ManagerItemUId</c>.</summary>
	private const string ObjectTaskName = "ObjectFileProcessingUserTask";

	/// <summary>The Generated report task schema - the fixture's F2.</summary>
	private const string ReportTaskName = "ReportFileProcessingUserTask";

	/// <summary><c>ObjectFileProcessingUserTask</c>'s UId (package <c>CrtProcessDesigner</c>), what draws the element.</summary>
	private const string ObjectTaskSchemaUId = "9387c794-8d84-5925-ab77-c47e7d876286";

	/// <summary>An object with no attachment object of its own, so its files live in SysFile storage (TC-17).</summary>
	private const string SysFileRecordObject = "AccountAddress";

	/// <summary>The notice every record taken from the process gets (<c>FileProcessingRecordRules.MappedRecordNotice</c>).</summary>
	private const string MappedRecordNotice = "may be empty at run time";

	/// <summary>The sentence clio appends to a modify refusal that one operation caused - absent when the index is null.</summary>
	private const string FailedOperationSentence = "is the one that refused";

	/// <summary>The current user's contact, the only formula a record scope takes.</summary>
	private const string CurrentUserContact = "[#SysVariable.CurrentUserContact#]";

	/// <summary>The designer-built fixture of TC-I-46 (plan section 5.1, OA.0).</summary>
	private const string DesignerFixtureName = "UsrFpOaDesignerFixture";

	/// <summary>The Lookup data value type - what the designer stores on <c>ConnectedObjectId</c>.</summary>
	private const string LookupDataValueTypeUId = "b295071f-7ea9-4e62-8d1a-919bf3732ff2";

	/// <summary>The Guid data value type - what the user-task template declares on <c>ConnectedObjectId</c>.</summary>
	private const string GuidDataValueTypeUId = "23018567-a13c-4320-8687-fd6f9e3699bd";

	/// <summary>SysSchema - the template <c>L9</c> of a schema-lookup parameter.</summary>
	private const string SysSchemaUId = "6c7394db-06ff-4050-91ef-8278e21dce15";

	/// <summary>The SysFile floor: 1.6.6.111 writes SysFile storage; named in the skip message.</summary>
	private const string SysFileMinimumPackageVersion = "1.6.6.111";

	/// <summary>An object with no attachment object of its own and a primary display column (Name): a lookup scope.</summary>
	private const string SysFileLookupRecordObject = "City";

	#region Methods: Tests - create and describe

	[Test]
	[Description("TC-I-06: create-business-process builds a fileProcessing element reading the attachments of one Contact (dedicated storage, ContactFile) and saving them to one Account (dedicated storage, AccountFile), and describe-business-process reads it back as the designer draws it - build type fileprocessing, user task ObjectFileProcessingUserTask, ManagerItemUId = the task schema UId, size 69;55 - and with the block in the write vocabulary: source attachments, the inferred action saveToAttachments stored, both sides' derived storage, the record scope lifted out of the filter and named by its parameter, the count and sort sent, the outputs, and no issue.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process builds a Process file element in dedicated storage and describe reads it back")]
	public async Task CreateBusinessProcess_Should_BuildAProcessFileElement_InDedicatedStorage() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Create");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson, ("OF1", FileElement(SavingBlockJson))));

			// Assert
			ShouldHaveBuilt(callResult, "a fileProcessing element with an attachments group and a saveTo is an ordinary create");
			WarningText(callResult).Should().Contain(MappedRecordNotice,
				because: "a saveTo.recordId taken from a process parameter is known only at run time, and the binder says so");
			JsonObject element = Element(await DescribeGraphAsync(context, processName), "OF1");
			Text(element, "buildType").Should().BeEquivalentTo(FileProcessingBuildType,
				because: "the Process file handler is registered before the generic user-task handler, so it claims the element");
			Text(element, "userTaskName").Should().Be(ObjectTaskName,
				because: "the attachments group selects the Object attachments task schema");
			Guid.Parse(Text(element, "managerItemUId") ?? Guid.Empty.ToString()).Should().Be(Guid.Parse(ObjectTaskSchemaUId),
				because: "the task schema UId as ManagerItemUId is what draws the element as Process file in the designer");
			Text(element, "size").Should().Be("69;55", because: "the element is drawn at the designer's user-task size");
			JsonObject block = FileProcessingBlock(element);
			Text(block, "source").Should().Be("attachments", because: "the group present is the variant");
			Text(block, "action").Should().Be("saveToAttachments", because: "a saveTo infers the saving action on create");
			Flag(block, "actionStored").Should().BeTrue(because: "create always writes the inferred action");
			JsonObject attachments = Group(block, "attachments");
			ShouldBeDedicated(attachments, "Contact", "ContactFile", "the source");
			Text(Group(attachments, "recordId"), "processParameter").Should().Be("ContactRef",
				because: "the record scope is lifted out of the filter and named by the parameter it reads");
			Number(attachments, "numberOfRecords").Should().Be(20, because: "the count sent is stored");
			ShouldBeSortedBy(attachments, "CreatedOn", "desc");
			JsonObject saveTo = Group(block, "saveTo");
			ShouldBeDedicated(saveTo, "Account", "AccountFile", "the target");
			Text(Group(saveTo, "recordId"), "processParameter").Should().Be("AccountRef",
				because: "saveTo.recordId decodes back to the process parameter it is mapped from");
			OutputNames(block).Should().Contain(["ObjectFiles.File", "CreatedObjectFileIds.Id"],
				because: "the outputs name what a downstream mapping reads");
			Strings(block["issues"]).Should().BeEmpty(because: "a builder-made element carries no hazard");
			element["filter"].Should().BeNull(
				because: "the record scope is reported once, by the block, so a described element resubmits without setting it twice");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("Extends TC-I-06: setElement with a fileProcessing block that sends ONLY attachments.numberOfRecords is a member-wise merge - the count changes, and the sort, the record scope and the whole saveTo the element was built with are kept, as describe reads them back.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process setElement fileProcessing is partial and keeps the sort")]
	public async Task ModifyBusinessProcess_Should_KeepTheSort_WhenSetElementIsPartial() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Partial");
			await ArrangeProcessAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson, ("OF1", FileElement(SavingBlockJson))),
				"the saving Process file element with a sort");

			// Act
			CallToolResult callResult = await ModifyAsync(context, processName, """
				[ { "op": "setElement", "elementName": "OF1",
				    "elementUpdate": { "fileProcessing": { "attachments": { "numberOfRecords": 5 } } } } ]
				""");

			// Assert
			ShouldHaveLanded(callResult, "a partial fileProcessing block is an ordinary setElement");
			JsonObject block = FileProcessingBlock(Element(await DescribeGraphAsync(context, processName), "OF1"));
			JsonObject attachments = Group(block, "attachments");
			Number(attachments, "numberOfRecords").Should().Be(5, because: "the one member sent is applied");
			ShouldBeSortedBy(attachments, "CreatedOn", "desc");
			Text(Group(attachments, "recordId"), "processParameter").Should().Be("ContactRef",
				because: "an omitted recordId keeps the record scope");
			Text(block, "action").Should().Be("saveToAttachments", because: "an omitted action is not changed");
			Text(Group(Group(block, "saveTo"), "recordId"), "processParameter").Should().Be("AccountRef",
				because: "an omitted saveTo keeps the target and its record");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[TestCase("value")]
	[TestCase("processParameter")]
	[TestCase("sourceColumn")]
	[TestCase("currentUserContact")]
	[Description("TC-I-18: attachments.recordId from each value source - a constant Account record, a Lookup process parameter, one column (Account) of a Read data record, and [#SysVariable.CurrentUserContact#] on Contact attachments - is compiled into the element's filter as the record scope and lifted back by describe into the same source, never left in the element's filter.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process takes attachments.recordId from each value source")]
	public async Task CreateBusinessProcess_Should_ScopeTheAttachments_FromEachValueSource(string source) {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Scope");
			string accountId = await ReadFirstRecordIdAsync(context, "Account");
			(string recordObject, string recordIdJson) = source switch {
				"value" => ("Account", $$"""{ "value": "{{accountId}}" }"""),
				"processParameter" => ("Account", """{ "processParameter": "AccountRef" }"""),
				"sourceColumn" => ("Account",
					"""{ "sourceElement": "ReadData1", "sourceElementParameter": "ResultEntity", "sourceColumn": "Account" }"""),
				_ => ("Contact", $$"""{ "expression": "{{CurrentUserContact}}" }""")
			};
			string block = $$"""{ "attachments": { "object": "{{recordObject}}", "recordId": {{recordIdJson}} } }""";

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson,
					("ReadData1", ReadContactJson), ("OF1", FileElement(block))));

			// Assert
			ShouldHaveBuilt(callResult, $"a record scope from a {source} source is accepted");
			JsonObject element = Element(await DescribeGraphAsync(context, processName), "OF1");
			JsonObject recordId = Group(Group(FileProcessingBlock(element), "attachments"), "recordId");
			switch (source) {
				case "value":
					Guid.Parse(Text(recordId, "value") ?? Guid.Empty.ToString()).Should().Be(Guid.Parse(accountId),
						because: "a constant scope reads back as the record id it was given");
					break;
				case "processParameter":
					Text(recordId, "processParameter").Should().Be("AccountRef",
						because: "a parameter scope reads back as the parameter it reads");
					break;
				case "sourceColumn":
					Text(recordId, "sourceElement").Should().Be("ReadData1",
						because: "an element-output scope reads back as the element it reads");
					Text(recordId, "sourceColumn").Should().Be("Account",
						because: "and the one column of that element's record");
					break;
				default:
					Text(recordId, "expression").Should().Be(CurrentUserContact,
						because: "the CurrentUserContact macro reads back in the spelling the block takes");
					break;
			}
			element["filter"].Should().BeNull(because: "the scope is reported once, by the block, never also in the filter");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[TestCase("value")]
	[TestCase("processParameter")]
	[TestCase("sourceColumn")]
	[TestCase("currentUserContact")]
	[Description("TC-I-28: saveTo.recordId from a constant Account record, a Lookup process parameter, one column (Account) of a Read data record, and [#SysVariable.CurrentUserContact#] for a Contact target. Each is stored and decoded back by describe; every source taken from the process gets the notice that the record may be empty at run time, and the constant - checked against the record object when it is saved - does not.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process takes saveTo.recordId from each value source")]
	public async Task CreateBusinessProcess_Should_StoreTheTargetRecord_FromEachValueSource(string source) {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Target");
			string accountId = await ReadFirstRecordIdAsync(context, "Account");
			(string targetObject, string recordIdJson) = source switch {
				"value" => ("Account", $$"""{ "value": "{{accountId}}" }"""),
				"processParameter" => ("Account", """{ "processParameter": "AccountRef" }"""),
				"sourceColumn" => ("Account",
					"""{ "sourceElement": "ReadData1", "sourceElementParameter": "ResultEntity", "sourceColumn": "Account" }"""),
				_ => ("Contact", $$"""{ "expression": "{{CurrentUserContact}}" }""")
			};
			string block = $$"""
				{ "attachments": { "object": "Account", "recordId": { "processParameter": "AccountRef" } },
				  "saveTo": { "object": "{{targetObject}}", "recordId": {{recordIdJson}} } }
				""";

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson,
					("ReadData1", ReadContactJson), ("OF1", FileElement(block))));

			// Assert
			ShouldHaveBuilt(callResult, $"a target record from a {source} source is accepted");
			JsonObject saveTo = Group(FileProcessingBlock(Element(await DescribeGraphAsync(context, processName), "OF1")),
				"saveTo");
			Text(saveTo, "object").Should().Be(targetObject, because: "the target's record object is stored as sent");
			JsonObject recordId = Group(saveTo, "recordId");
			string warnings = WarningText(callResult);
			switch (source) {
				case "value":
					Guid.Parse(Text(recordId, "value") ?? Guid.Empty.ToString()).Should().Be(Guid.Parse(accountId),
						because: "a constant target record reads back as the record id");
					Text(recordId, "display").Should().NotBeNullOrWhiteSpace(
						because: "a constant is stored WITH the record's display value, as the designer stores it");
					warnings.Should().NotContain(MappedRecordNotice,
						because: "a constant was checked against the record object when it was saved");
					break;
				case "processParameter":
					Text(recordId, "processParameter").Should().Be("AccountRef",
						because: "a mapped target record decodes back to its process parameter");
					warnings.Should().Contain(MappedRecordNotice, because: "a record from the process is known only at run time");
					break;
				case "sourceColumn":
					Text(recordId, "sourceElement").Should().Be("ReadData1",
						because: "a column of an element's record decodes back to the element");
					Text(recordId, "sourceColumn").Should().Be("Account", because: "and to the column it reads");
					warnings.Should().Contain(MappedRecordNotice, because: "a record from the process is known only at run time");
					break;
				default:
					Text(recordId, "expression").Should().Be(CurrentUserContact,
						because: "a formula target record reads back as the expression it is");
					warnings.Should().Contain(MappedRecordNotice, because: "a formula is evaluated only at run time");
					break;
			}
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Tests - patterns with other elements

	[Test]
	[Description("TC-I-70: Read data -> Process file. A Read data element finds one Contact, and the Process file element reads that contact's attachments through attachments.recordId <- ReadData1.ResultEntity + sourceColumn Id. The build is accepted and describe reports the scope as the Read data element's output, not as a filter condition.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process chains Read data into a Process file element")]
	public async Task CreateBusinessProcess_Should_ReadTheAttachments_OfARecordFoundByReadData() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("ReadThen");
			const string block = """
				{ "attachments": { "object": "Contact",
				    "recordId": { "sourceElement": "ReadData1", "sourceElementParameter": "ResultEntity", "sourceColumn": "Id" } } }
				""";

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				LinearDescriptor(processName, "[]", ("ReadData1", ReadContactJson), ("OF1", FileElement(block))));

			// Assert
			ShouldHaveBuilt(callResult, "the record a Read data element found is a legal record scope");
			JsonObject element = Element(await DescribeGraphAsync(context, processName), "OF1");
			JsonObject recordId = Group(Group(FileProcessingBlock(element), "attachments"), "recordId");
			Text(recordId, "sourceElement").Should().Be("ReadData1",
				because: "the scope names the Read data element whose record it reads");
			(Text(recordId, "sourceElementParameter") ?? string.Empty).Should().StartWith("ResultEntity",
				because: "and the record output of that element");
			element["filter"].Should().BeNull(because: "the scope is lifted out of the filter, not reported twice");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-71: Process file -> Modify data re-finding the created file. The Process file element saves an Account's attachments to a Contact, and a Modify data element on ContactFile re-finds the copy by Contact = the target record, Name contains a fragment and CreatedOn = the current hour, and sets its Tag. Built and described, NOT run: describe reports the saving element and the Modify data element with its source, its value and its three conditions, the macro included.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process chains a saving Process file element into Modify data on the copy")]
	public async Task CreateBusinessProcess_Should_ChainAModifyData_ThatReFindsTheCreatedFile() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("ReFind");
			const string block = """
				{ "attachments": { "object": "Account", "recordId": { "processParameter": "AccountRef" } },
				  "saveTo": { "object": "Contact", "recordId": { "processParameter": "ContactRef" } } }
				""";
			const string modifyData = """
				{ "name": "ChangeData1", "type": "changeData", "caption": "Tag the copied file",
				  "changeData": { "source": "ContactFile", "values": [ { "column": "Tag", "value": "clio-e2e-copied" } ] },
				  "filter": { "object": "ContactFile", "logicalOperation": "and", "conditions": [
				    { "column": "Contact", "comparison": "equal", "processParameter": "ContactRef" },
				    { "column": "Name", "comparison": "contains", "value": "invoice" },
				    { "column": "CreatedOn", "comparison": "equal", "macro": "CurrentHour" } ] } }
				""";

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson, ("OF1", FileElement(block)), ("ChangeData1", modifyData)));

			// Assert
			ShouldHaveBuilt(callResult, "a Modify data element on the target's file object after a saving element is legal");
			JsonObject graph = await DescribeGraphAsync(context, processName);
			JsonObject fileBlock = FileProcessingBlock(Element(graph, "OF1"));
			Text(fileBlock, "action").Should().Be("saveToAttachments", because: "the Process file element saves its files");
			Text(Group(fileBlock, "saveTo"), "fileObject").Should().Be("ContactFile",
				because: "the copies land in ContactFile, which the Modify data element re-reads");
			JsonObject changeDataElement = Element(graph, "ChangeData1");
			JsonObject changeData = Group(changeDataElement, "changeData");
			Text(changeData, "source").Should().Be("ContactFile", because: "the Modify data element edits the copied file records");
			Objects(changeData["values"]).Select(value => Text(value, "column")).Should().Contain("Tag",
				because: "the value it sets is the copy's Tag");
			IReadOnlyList<JsonObject> conditions = Objects(Group(changeDataElement, "filter")["conditions"]);
			conditions.Select(condition => Text(condition, "column")).Should().Contain(["Contact", "Name", "CreatedOn"],
				because: "the copy is re-found by its record, a name fragment and the hour it was created in");
			conditions.Should().Contain(condition => Text(condition, "macro") == "CurrentHour",
				because: "the CreatedOn condition is the CurrentHour macro, which crossed the wire and was stored");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-35: two downstream consumers of the Process file element's ObjectFiles in ONE modify request - a process-level FileCollection Docs mapped from ObjectFiles, and a multi-instance sub-process whose InputRecordCollection.F is mapped from ObjectFiles.File. Both are stored at both levels: Docs and its File item are bound to ObjectFiles and its File item, and the sub-process's input collection (bound by P1) and its item F likewise.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process maps the Process file element's ObjectFiles into two consumers")]
	public async Task ModifyBusinessProcess_Should_BindObjectFiles_IntoTwoConsumers() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string calleeName = NewProcessName("TwoCallee");
			string callerName = NewProcessName("TwoCaller");
			await ArrangeProcessAsync(context, created, calleeName, PlainDescriptor(calleeName, FileCalleeJson),
				"the called process with a File parameter");
			await ArrangeProcessAsync(context, created, callerName,
				LinearDescriptor(callerName, """
					[ { "name": "ContactRef", "type": "Lookup", "referenceSchema": "Contact", "direction": "In" },
					  { "name": "Docs", "type": "FileCollection" } ]
					""",
					("OF1", FileElement(UseInProcessBlockJson)), ("SubProcess1", MultiInstanceJson(calleeName))),
				"the caller with a Process file element, a FileCollection and a multi-instance sub-process");

			// Act
			CallToolResult callResult = await ModifyAsync(context, callerName, """
				[
				  { "op": "addMapping", "mapping": { "targetProcessParameter": "Docs",
				      "sourceElement": "OF1", "sourceElementParameter": "ObjectFiles" } },
				  { "op": "addMapping", "mapping": { "elementName": "SubProcess1", "elementParameter": "InputRecordCollection.F",
				      "sourceElement": "OF1", "sourceElementParameter": "ObjectFiles.File" } }
				]
				""");

			// Assert
			ShouldHaveLanded(callResult, "two consumers of one file output in one request are ordinary mappings");
			JsonObject graph = await DescribeGraphAsync(context, callerName);
			JsonObject objectFiles = ElementParameter(Element(graph, "OF1"), "ObjectFiles");
			string objectFilesUId = UId(objectFiles);
			string fileUId = UId(Item(objectFiles, "File"));
			JsonObject docs = ProcessParameter(graph, "Docs");
			ShouldBeBoundTo(docs, objectFilesUId, "the FileCollection is bound to the element's ObjectFiles");
			ShouldBeBoundTo(Item(docs, "File"), fileUId,
				"and its File item to ObjectFiles.File, so every row carries its file (both levels stored)");
			JsonObject input = ElementParameter(Element(graph, "SubProcess1"), "InputRecordCollection");
			ShouldBeBoundTo(input, objectFilesUId, "P1 binds the multi-instance input collection to ObjectFiles");
			ShouldBeBoundTo(Item(input, "F"), fileUId, "and the item keeps the mapping it was given");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-72: Process file -> a multi-instance sub-process per file, built in one create (ENG-95984's File parameter): the callee takes one File Doc, and the caller maps InputRecordCollection.Doc from OF1.ObjectFiles.File. Describe shows both levels - the input collection bound to ObjectFiles by P1, and its item Doc bound to ObjectFiles.File - with no 'will be empty' notice.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process runs a sub-process once per file the Process file element reads")]
	public async Task CreateBusinessProcess_Should_RunASubProcessPerFile_OfTheProcessFileElement() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string calleeName = NewProcessName("PerCallee");
			string callerName = NewProcessName("PerCaller");
			await ArrangeProcessAsync(context, created, calleeName,
				PlainDescriptor(calleeName, """[ { "name": "Doc", "type": "File", "direction": "In" } ]"""),
				"the called process with a File parameter Doc");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, callerName,
				LinearDescriptor(callerName,
					"""[ { "name": "ContactRef", "type": "Lookup", "referenceSchema": "Contact", "direction": "In" } ]""",
					[("OF1", FileElement(UseInProcessBlockJson)), ("SubProcess1", MultiInstanceJson(calleeName))],
					"""
					[ { "elementName": "SubProcess1", "elementParameter": "InputRecordCollection.Doc",
					    "sourceElement": "OF1", "sourceElementParameter": "ObjectFiles.File" } ]
					"""));

			// Assert
			ShouldHaveBuilt(callResult, "a multi-instance sub-process per file of a Process file element is legal");
			WarningText(callResult).Should().NotContain("will be empty on every iteration",
				because: "Doc is mapped, so the callee's file is not reported as left empty");
			JsonObject graph = await DescribeGraphAsync(context, callerName);
			JsonObject objectFiles = ElementParameter(Element(graph, "OF1"), "ObjectFiles");
			JsonObject input = ElementParameter(Element(graph, "SubProcess1"), "InputRecordCollection");
			ShouldBeBoundTo(input, UId(objectFiles), "P1 binds the input collection to the element's ObjectFiles");
			ShouldBeBoundTo(Item(input, "Doc"), UId(Item(objectFiles, "File")),
				"the item keeps the mapping it was given: ObjectFiles.File crossed the wire");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-86: validate-process-graph over a described fileprocessing element. The element's build type is read from describe and validated in a Start -> element -> End graph: the graph validates and carries no UNKNOWN finding (the ManagerMap arm that maps fileprocessing to a user task).")]
	[AllureTag(ValidateProcessGraphTool.ToolName)]
	[AllureName("validate-process-graph classifies a described fileprocessing element as a known type")]
	public async Task ValidateProcessGraph_Should_ClassifyADescribedProcessFileElement_AsKnown() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Validate");
			await ArrangeProcessAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson, ("OF1", FileElement(UseInProcessBlockJson))),
				"the process with a Process file element");
			string buildType = Text(Element(await DescribeGraphAsync(context, processName), "OF1"), "buildType") ?? string.Empty;

			// Act
			CallToolResult callResult = await ProcessDesignerE2EArrange.CallToolAsync(context, ValidateProcessGraphTool.ToolName,
				new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["nodes"] = new[] { GraphNode("s", "startEvent"), GraphNode("f", buildType), GraphNode("e", "endEvent") },
					["edges"] = new[] { GraphEdge("s", "f"), GraphEdge("f", "e") }
				});

			// Assert
			buildType.Should().BeEquivalentTo(FileProcessingBuildType,
				because: "the node type validated is the one describe reported, so the graph is the described one");
			ValidateProcessGraphResponse response =
				EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(callResult);
			response.Success.Should().BeTrue(because: "the graph is well formed; the error was: {0}", response.Error);
			(response.Findings ?? []).Where(finding => finding.RuleId == "UNKNOWN").Should().BeEmpty(
				because: "a described fileprocessing element is a known build type, classified as a user task");
			response.HasErrors.Should().NotBe(true, because: "Start -> Process file -> End violates no connection rule");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Tests - refusals

	[TestCase("userTask", ObjectTaskName)]
	[TestCase("userTask", " ObjectFileProcessingUserTask ")]
	[Description("TC-I-12: the generic userTask route for ObjectFileProcessingUserTask - by its name, and by the name with padding - is refused (a name in another letter case is not found at all: the platform looks the user-task schema up case-sensitively, before the route is judged) on create, pointing to type fileProcessing with its attachments group: that route carries no file source, record or target, so the element would save green and fail at run time.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process refuses the generic user-task route for the Object attachments task")]
	public async Task CreateBusinessProcess_Should_RefuseTheGenericRoute_ForTheObjectTask(string type, string userTaskName) {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Generic");
			string element = $$"""{ "name": "OF1", "type": "{{type}}", "userTaskName": "{{userTaskName}}" }""";

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				LinearDescriptor(processName, "[]", ("OF1", element)));

			// Assert
			ShouldBeRefused(callResult, "the generic route would build an unconfigured element that fails at run time");
			string log = LogText(callResult);
			log.Should().Contain("cannot be built as type", because: "the refusal says the route is the problem");
			log.Should().Contain(ObjectTaskName, because: "the refusal names the task schema the name resolved to");
			log.Should().Contain("Use type 'fileProcessing'", because: "the refusal points to the element type that configures it");
			log.Should().NotContain(CreatedLine, because: "a refused build saves nothing");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-22: a saving element with no record scope and no filter condition is refused at the end of the request, before the pre-save gate, with no failedOperationIndex: setElement switching a use-in-process element to saveToAttachments is refused naming the element, the operation that touched it and the ARBITRARY files it would copy, and nothing is saved. The positive control: the same setElement followed by a setFilter in the SAME batch lands, because the check judges the final schema.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses a saving element with an empty filter, with no operation index")]
	public async Task ModifyBusinessProcess_Should_RefuseASavingElementWithAnEmptyFilter_WithNoOperationIndex() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Empty");
			string accountId = await ReadFirstRecordIdAsync(context, "Account");
			await ArrangeProcessAsync(context, created, processName,
				LinearDescriptor(processName, "[]", ("OF1", FileElement("""{ "attachments": { "object": "Contact" } }"""))),
				"the use-in-process element with no scope, which is only a notice");
			string switchToSave = $$"""
				{ "op": "setElement", "elementName": "OF1", "elementUpdate": { "fileProcessing": {
				    "action": "saveToAttachments", "saveTo": { "object": "Account", "recordId": { "value": "{{accountId}}" } } } } }
				""";

			// Act
			CallToolResult refused = await ModifyAsync(context, processName, $"[ {switchToSave} ]");
			CallToolResult landed = await ModifyAsync(context, processName, $$"""
				[ {{switchToSave}},
				  { "op": "setFilter", "elementName": "OF1",
				    "filter": { "logicalOperation": "and",
				      "conditions": [ { "column": "Name", "comparison": "contains", "value": "invoice" } ] } } ]
				""");

			// Assert
			ShouldBeRefused(refused, "a saving element with no selecting filter copies arbitrary files onto the record");
			string log = LogText(refused);
			log.Should().Contain("no record scope and no filter condition", because: "the refusal says what is missing");
			log.Should().Contain("ARBITRARY", because: "the refusal says what the run time would do instead");
			log.Should().Contain("setElement", because: "the refusal names the operation that configured the element");
			log.Should().Contain("Nothing was saved", because: "the whole request is refused, not one operation");
			log.Should().NotContain(FailedOperationSentence,
				because: "the check runs on the final schema outside the operation loop, so failedOperationIndex is null");
			ShouldHaveLanded(landed, "a setFilter later in the same batch satisfies the check");
			JsonObject element = Element(await DescribeGraphAsync(context, processName), "OF1");
			Text(FileProcessingBlock(element), "action").Should().Be("saveToAttachments",
				because: "the accepted batch switched the element to saving");
			Objects(Group(element, "filter")["conditions"]).Select(condition => Text(condition, "column"))
				.Should().Contain("Name", because: "the condition that satisfied the check is stored");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("On an existing element: setElement sending another group (files) is refused as a source change - the designer changes the file source only by replacing the element; setFilter with filter.object = the RECORD object (Contact) is refused, because the filter is evaluated on the attachment object, pointing to attachments.recordId. Neither changes the element.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses a source change and a foreign filter root")]
	public async Task ModifyBusinessProcess_Should_RefuseASourceChange_AndAForeignFilterRoot() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Change");
			await ArrangeProcessAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson, ("OF1", FileElement(UseInProcessBlockJson))),
				"the Process file element reading one contact's attachments");
			string before = FileProcessingBlock(Element(await DescribeGraphAsync(context, processName), "OF1")).ToJsonString();

			// Act
			CallToolResult otherGroup = await ModifyAsync(context, processName, """
				[ { "op": "setElement", "elementName": "OF1",
				    "elementUpdate": { "fileProcessing": { "files": { "processParameter": "ContactRef" } } } } ]
				""");
			CallToolResult foreignRoot = await ModifyAsync(context, processName, """
				[ { "op": "setFilter", "elementName": "OF1",
				    "filter": { "object": "Contact", "logicalOperation": "and",
				      "conditions": [ { "column": "Name", "comparison": "contains", "value": "invoice" } ] } } ]
				""");

			// Assert
			ShouldBeRefused(otherGroup, "the stored variant is fixed");
			string sourceChange = LogText(otherGroup);
			sourceChange.Should().Contain("its source cannot change", because: "the refusal says the variant is fixed");
			sourceChange.Should().Contain("replacing the element", because: "and how the designer changes it instead");
			ShouldBeRefused(foreignRoot, "the filter is evaluated on the attachment object, not on the record object");
			string rootRefusal = LogText(foreignRoot);
			rootRefusal.Should().Contain("ContactFile", because: "the refusal names the attachment object the filter runs on");
			rootRefusal.Should().Contain("attachments.recordId",
				because: "the refusal points to the record scope for reading one record's attachments");
			FileProcessingBlock(Element(await DescribeGraphAsync(context, processName), "OF1")).ToJsonString()
				.Should().Be(before, because: "two refused edits leave the element as it was");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("Extends TC-I-18: a record scope takes one value, so any formula other than [#SysVariable.CurrentUserContact#] in attachments.recordId.expression is refused, naming the four sources that are accepted, and nothing is built.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process refuses a formula record scope other than CurrentUserContact")]
	public async Task CreateBusinessProcess_Should_RefuseAFormulaRecordScope_OtherThanCurrentUserContact() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Formula");
			const string block = """{ "attachments": { "object": "Contact", "recordId": { "expression": "[#SysVariable.CurrentUser#]" } } }""";

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				LinearDescriptor(processName, "[]", ("OF1", FileElement(block))));

			// Assert
			ShouldBeRefused(callResult, "a record scope compiles into a filter, which takes one value");
			string log = LogText(callResult);
			log.Should().Contain("is not accepted", because: "the refusal says the formula is refused");
			log.Should().Contain(CurrentUserContact, because: "the refusal names the one formula a record scope takes");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-33b: moving a saving Process file element's target to another object is refused while a multi-instance sub-process iterates its CreatedObjectFileIds - the copies would land in another attachment object than the reader was built for, which the designer refuses too (onEntitySchemaChange). The refusal names the side, the member and the reader, and the element reads back unchanged.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses moving the target while CreatedObjectFileIds is read")]
	public async Task ModifyBusinessProcess_Should_RefuseATargetObjectChange_WhileCreatedObjectFileIdsIsRead() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string calleeName = NewProcessName("CopyCallee");
			string callerName = NewProcessName("CopyCaller");
			await ArrangeProcessAsync(context, created, calleeName,
				PlainDescriptor(calleeName, """[ { "name": "FileId", "type": "Guid", "direction": "In" } ]"""),
				"the called process taking one copy's Id");
			await ArrangeProcessAsync(context, created, callerName,
				LinearDescriptor(callerName, RecordRefParametersJson,
					[("OF1", FileElement(SavingBlockJson)), ("SubProcess1", MultiInstanceJson(calleeName))],
					"""
					[ { "elementName": "SubProcess1", "elementParameter": "InputRecordCollection.FileId",
					    "sourceElement": "OF1", "sourceElementParameter": "CreatedObjectFileIds.Id" } ]
					"""),
				"the saving Process file element whose copies a multi-instance sub-process iterates");
			string before = FileProcessingBlock(Element(await DescribeGraphAsync(context, callerName), "OF1")).ToJsonString();

			// Act
			CallToolResult callResult = await ModifyAsync(context, callerName, """
				[ { "op": "setElement", "elementName": "OF1",
				    "elementUpdate": { "fileProcessing": { "saveTo": { "object": "Contact",
				      "recordId": { "processParameter": "ContactRef" } } } } } ]
				""");

			// Assert
			ShouldBeRefused(callResult, "the sub-process was built for AccountFile copies, and ContactFile copies would not match it");
			string refusal = LogText(callResult);
			refusal.Should().Contain("cannot move its target to another object ('saveTo.object')",
				because: "the refusal names the side and the member that moved it");
			refusal.Should().Contain("SubProcess1", because: "the refusal names the reader of CreatedObjectFileIds");
			refusal.Should().Contain("SEPARATE, earlier call", because: "and the remedy: re-point the reader first");
			FileProcessingBlock(Element(await DescribeGraphAsync(context, callerName), "OF1")).ToJsonString()
				.Should().Be(before, because: "a refused update leaves the element as it was");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Tests - designer-built fixture (TC-I-46)

	[Test]
	[Description("TC-I-46 (2) on the designer-built fixture UsrFpOaDesignerFixture: an unrelated addParameter (a Text parameter with a unique name) re-saves the WHOLE designer-built process, so F1 (Object attachments) and F2 (Generated report) are at stake - read from the raw SysSchema.MetaData row and the SysLocalizableValue rows (keys BaseElements.<F2>.*), never from describe. The FIRST builder save of a designer-built element may differ only by the platform's design-session normalization and the builder's relayout (the allow-list, one-way: the position BL3; ConnectedObjectId retyped from Lookup to Guid, its user-task template; a template L9 = SysSchema added on an *EntitySchemaUId parameter that had none), F2's resource rows stay byte-identical; a SECOND unrelated save leaves both nodes byte-identical to the first, which is the guard that the builder does not drift. Probe parameters are removed afterwards. Ignored, with the build instructions, when the stand does not carry the fixture.")]
	[AllureTag(ModifyToolName)]
	[AllureName("an unrelated modify leaves designer-built Process file elements intact")]
	public async Task DesignerFixture_Should_ChangeOnlyByTheFirstSaveNormalization_OnUnrelatedModifies() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		await IgnoreUnlessDesignerFixtureExistsAsync(context);
		JsonObject graph = await DescribeGraphAsync(context, DesignerFixtureName);
		JsonObject f2 = SingleElementOfTask(graph, ReportTaskName, "F2");
		string f2Name = Text(f2, "name")!;
		string f2UId = UId(f2);
		string f1UId = UId(SingleElementOfTask(graph, ObjectTaskName, "F1"));
		string probeName = $"UsrFpE2eProbe{Guid.NewGuid():N}"[..21];
		string secondProbeName = $"UsrFpE2eProbe{Guid.NewGuid():N}"[..21];
		IReadOnlyList<string> nodesBefore = await ReadElementNodesAsync(context, DesignerFixtureName, f2UId);
		IReadOnlyList<string> resourcesBefore = await ReadElementResourceRowsAsync(context, DesignerFixtureName, f2Name);
		IReadOnlyList<string> f1NodesBefore = await ReadElementNodesAsync(context, DesignerFixtureName, f1UId);
		try {
			// Act
			CallToolResult callResult = await ModifyAsync(context, DesignerFixtureName, $$"""
				[ { "op": "addParameter", "parameter": { "name": "{{probeName}}", "type": "Text" } } ]
				""");
			IReadOnlyList<string> nodesAfterFirst = await ReadElementNodesAsync(context, DesignerFixtureName, f2UId);
			IReadOnlyList<string> f1NodesAfterFirst = await ReadElementNodesAsync(context, DesignerFixtureName, f1UId);
			IReadOnlyList<string> resourcesAfterFirst =
				await ReadElementResourceRowsAsync(context, DesignerFixtureName, f2Name);
			CallToolResult secondResult = await ModifyAsync(context, DesignerFixtureName, $$"""
				[ { "op": "addParameter", "parameter": { "name": "{{secondProbeName}}", "type": "Text" } } ]
				""");

			// Assert
			ShouldHaveLanded(callResult, "an unrelated addParameter is an ordinary edit of the designer-built process");
			ShouldHaveLanded(secondResult, "a second unrelated addParameter is an ordinary edit too");
			ParameterNames(await DescribeGraphAsync(context, DesignerFixtureName)).Should().Contain(probeName,
				because: "the positive control: the process WAS saved, so an intact F2 is evidence and not a no-op");
			resourcesBefore.Should().NotBeEmpty(
				because: "the read must be shown able to see F2's rows (its caption at least), or 'unchanged' proves nothing");
			nodesAfterFirst.Select(WithoutLayout).Should()
				.Equal(ExpectedAfterFirstSave(nodesBefore, nodesAfterFirst),
					because: "the first save of the whole process may change F2 only by the platform's design-session normalization and the relayout");
			f1NodesAfterFirst.Select(WithoutLayout).Should()
				.Equal(ExpectedAfterFirstSave(f1NodesBefore, f1NodesAfterFirst),
					because: "and F1, the designer-built Object attachments element, likewise");
			resourcesAfterFirst.Should().Equal(resourcesBefore, because: "F2's resource rows stay byte-identical");
			ParameterNames(await DescribeGraphAsync(context, DesignerFixtureName)).Should().Contain(secondProbeName,
				because: "the positive control of the second save: it WAS saved, so byte-identity after it is evidence");
			(await ReadElementNodesAsync(context, DesignerFixtureName, f2UId)).Should().Equal(nodesAfterFirst,
				because: "once normalized, a further save of the whole process leaves F2's stored node byte-identical");
			(await ReadElementNodesAsync(context, DesignerFixtureName, f1UId)).Should().Equal(f1NodesAfterFirst,
				because: "and F1's stored node byte-identical: the builder does not drift");
			(await ReadElementResourceRowsAsync(context, DesignerFixtureName, f2Name)).Should().Equal(resourcesAfterFirst,
				because: "and F2's resource rows byte-identical");
		} finally {
			await RemoveProbeParameterAsync(context, probeName);
			await RemoveProbeParameterAsync(context, secondProbeName);
		}
	}

	#endregion

	#region Methods: Tests - SysFile storage (ENG-103937)

	[TestCase("source")]
	[TestCase("target")]
	[Description("TC-I-17: an object with no attachment object of its own (AccountAddress) is written in SysFile storage as the attachments.object source and as the saveTo.object target, and describe reads it back as storage sysFile, file object SysFile, link column RecordId and the record object as sent, with no issue about the storage.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process writes SysFile storage on either side")]
	public async Task CreateBusinessProcess_Should_WriteSysFileStorage(string side) {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartSysFileAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("SysFile");
			string block = side == "source"
				? $$"""{ "attachments": { "object": "{{SysFileRecordObject}}" } }"""
				: $$"""
					{ "attachments": { "object": "Contact", "recordId": { "processParameter": "ContactRef" } },
					  "saveTo": { "object": "{{SysFileRecordObject}}", "recordId": { "processParameter": "AnyId" } } }
					""";

			// Act
			CallToolResult callResult = await CreateSysFileAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson, ("OF1", FileElement(block))));

			// Assert
			ShouldHaveBuilt(callResult, $"a SysFile {side} is written like any other");
			JsonObject block1 = FileProcessingBlock(Element(await DescribeGraphAsync(context, processName), "OF1"));
			ShouldBeSysFile(Group(block1, side == "source" ? "attachments" : "saveTo"), SysFileRecordObject,
				$"the {side}");
			Strings(block1["issues"]).Should().NotContain(issue => issue.Contains("SysFile", StringComparison.Ordinal),
				because: "a SysFile side is no hazard for an object without its own attachment object");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[TestCase("AccountAddress", "Terrasoft.CompareFilter", "Terrasoft.InFilter")]
	[TestCase("City", "Terrasoft.InFilter", "Terrasoft.CompareFilter")]
	[Description("TC-I-81 (TC-77, M7): attachments.recordId on a SysFile source is stored in the designer's shape for its record object - a Guid compare for an object with no primary display column (Account address), a lookup In filter for one with a primary display column (City) - and describe lifts it back as the constant record id. Ignored when the stand has no record of the object.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process writes the SysFile record scope in the designer's shape")]
	public async Task CreateBusinessProcess_Should_WriteTheSysFileRecordScope_InTheDesignersShape(string recordObject,
			string storedClass, string absentClass) {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartSysFileAsync();
		string recordId = await ReadFirstRecordIdAsync(context, recordObject);
		List<string> created = [];
		try {
			string processName = NewProcessName("SysScope");

			// Act
			CallToolResult callResult = await CreateSysFileAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson, ("OF1", FileElement($$"""
					{ "attachments": { "object": "{{recordObject}}", "recordId": { "value": "{{recordId}}" } } }
					"""))));

			// Assert
			ShouldHaveBuilt(callResult, "a constant record of the SysFile source's record object is a legal scope");
			JsonObject element = Element(await DescribeGraphAsync(context, processName), "OF1");
			JsonObject attachments = Group(FileProcessingBlock(element), "attachments");
			ShouldBeSysFile(attachments, recordObject, "the source");
			Text(Group(attachments, "recordId"), "value").Should().BeEquivalentTo(recordId,
				because: "the record scope is lifted out of the stored filter as the constant sent");
			string node = string.Join("\n", await ReadElementNodesAsync(context, processName, UId(element)));
			node.Should().Contain(storedClass, because: $"the designer writes this shape for {recordObject} (M7)");
			node.Should().NotContain(absentClass, because: "and not the other one");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-81 (D16) on a SysFile source: setFilter writes its conditions next to the stored record scope, and clearFilter writes the scope back alone - describe reads the scope (attachments.recordId) unchanged after both, and the stored element keeps the scope as the designer's Guid compare (twice as many compare leaves with the Name condition as after clearFilter, never an In filter). Ignored when the stand has no Account address record.")]
	[AllureTag(ModifyToolName)]
	[AllureName("setFilter and clearFilter keep the record scope of a SysFile source")]
	public async Task ModifyBusinessProcess_Should_KeepTheSysFileRecordScope_ThroughSetFilterAndClearFilter() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartSysFileAsync();
		string recordId = await ReadFirstRecordIdAsync(context, SysFileRecordObject);
		List<string> created = [];
		try {
			string processName = NewProcessName("SysFilter");
			await ArrangeSysFileProcessAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson, ("OF1", FileElement($$"""
					{ "attachments": { "object": "{{SysFileRecordObject}}", "recordId": { "value": "{{recordId}}" } } }
					"""))), "the SysFile element scoped to one Account address");

			// Act
			CallToolResult setFilter = await ModifySysFileAsync(context, processName, """
				[ { "op": "setFilter", "elementName": "OF1",
				    "filter": { "logicalOperation": "and",
				      "conditions": [ { "column": "Name", "comparison": "contains", "value": "invoice" } ] } } ]
				""");
			JsonObject afterSet = Element(await DescribeGraphAsync(context, processName), "OF1");
			string afterSetNode = string.Join("\n", await ReadElementNodesAsync(context, processName, UId(afterSet)));
			CallToolResult clearFilter = await ModifySysFileAsync(context, processName,
				"""[ { "op": "clearFilter", "elementName": "OF1" } ]""");
			JsonObject afterClear = Element(await DescribeGraphAsync(context, processName), "OF1");
			string afterClearNode = string.Join("\n", await ReadElementNodesAsync(context, processName, UId(afterClear)));

			// Assert
			ShouldHaveLanded(setFilter, "a filter on SysFile columns is written on a SysFile source");
			Text(Group(Group(FileProcessingBlock(afterSet), "attachments"), "recordId"), "value")
				.Should().BeEquivalentTo(recordId, because: "setFilter keeps the record scope the block wrote");
			afterSetNode.Should().NotContain("Terrasoft.InFilter",
				because: "setFilter does not retype the kept scope into a lookup");
			Objects(Group(afterSet, "filter")["conditions"]).Select(condition => Text(condition, "column"))
				.Should().Contain("Name", because: "the conditions sent are stored next to the scope");
			ShouldHaveLanded(clearFilter, "clearFilter on a SysFile source is accepted");
			Text(Group(Group(FileProcessingBlock(afterClear), "attachments"), "recordId"), "value")
				.Should().BeEquivalentTo(recordId, because: "clearFilter writes the record scope back alone");
			afterClear["filter"].Should().BeNull(because: "clearFilter removes the conditions setFilter wrote, leaving only the scope");
			int compareLeavesAfterClear = Occurrences(afterClearNode, "Terrasoft.CompareFilter");
			compareLeavesAfterClear.Should().BePositive(
				because: "clearFilter writes the scope back in the designer's Guid-compare shape");
			Occurrences(afterSetNode, "Terrasoft.CompareFilter").Should().Be(2 * compareLeavesAfterClear,
				because: "after setFilter the stored filter holds the kept scope and the Name condition, both compare "
					+ "leaves written in the same forms, so the scope was neither dropped nor retyped");
			afterClearNode.Should().NotContain("Terrasoft.InFilter",
				because: "clearFilter does not retype the scope into a lookup");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-81 (TC-78): on a SysFile source the attachment object is SysFile, so a sort on a column of the record object (Account address's GPSE, which the designer's column list offers) is refused naming SysFile, and nothing is saved. Ignored when the stand has no Account address record.")]
	[AllureTag(ModifyToolName)]
	[AllureName("a SysFile source refuses a sort on a record-object column")]
	public async Task ModifyBusinessProcess_Should_RefuseARecordObjectSortColumn_OnASysFileSource() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartSysFileAsync();
		string recordId = await ReadFirstRecordIdAsync(context, SysFileRecordObject);
		List<string> created = [];
		try {
			string processName = NewProcessName("SysSort");
			await ArrangeSysFileProcessAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson, ("OF1", FileElement($$"""
					{ "attachments": { "object": "{{SysFileRecordObject}}", "recordId": { "value": "{{recordId}}" } } }
					"""))), "the SysFile element scoped to one Account address");

			// Act
			CallToolResult sorted = await ModifySysFileAsync(context, processName, """
				[ { "op": "setElement", "elementName": "OF1",
				    "elementUpdate": { "fileProcessing": { "attachments": { "sort": { "column": "GPSE", "direction": "asc" } } } } } ]
				""");

			// Assert
			ShouldBeRefused(sorted, "GPSE is a column of Account address, not of SysFile, the source's attachment object");
			LogText(sorted).Should().Contain("not found on entity 'SysFile'",
				because: "the refusal names SysFile, the object the sort column is resolved on");
			Group(FileProcessingBlock(Element(await DescribeGraphAsync(context, processName), "OF1")), "attachments")["sort"]
				.Should().BeNull(because: "a refused setElement saves nothing");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-81 (TC-79): a SysFile source moved to another SysFile record object (Account address to City) keeps SysFile as its file object but changes the record object, so the record scope written for an Account address is cleared with a notice naming both objects. Ignored when the stand has no Account address record.")]
	[AllureTag(ModifyToolName)]
	[AllureName("a SysFile source moved to another record object clears its record scope")]
	public async Task ModifyBusinessProcess_Should_ClearTheScope_WhenTheSysFileRecordObjectChanges() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartSysFileAsync();
		string recordId = await ReadFirstRecordIdAsync(context, SysFileRecordObject);
		List<string> created = [];
		try {
			string processName = NewProcessName("SysMove");
			await ArrangeSysFileProcessAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson, ("OF1", FileElement($$"""
					{ "attachments": { "object": "{{SysFileRecordObject}}", "recordId": { "value": "{{recordId}}" } } }
					"""))), "the SysFile element scoped to one Account address");

			// Act
			CallToolResult moved = await ModifySysFileAsync(context, processName, $$"""
				[ { "op": "setElement", "elementName": "OF1",
				    "elementUpdate": { "fileProcessing": { "attachments": { "object": "{{SysFileLookupRecordObject}}" } } } } ]
				""");

			// Assert
			ShouldHaveLanded(moved, "moving a SysFile source to another object is a retarget, not a refusal");
			WarningText(moved).Should().Contain($"source changed from '{SysFileRecordObject}' to '{SysFileLookupRecordObject}'",
				because: "the caller is told the filter written for the previous object was cleared");
			JsonObject attachments = Group(FileProcessingBlock(Element(await DescribeGraphAsync(context, processName), "OF1")),
				"attachments");
			ShouldBeSysFile(attachments, SysFileLookupRecordObject, "the moved source");
			attachments["recordId"].Should().BeNull(because: "a scope on an Account address record selects no City file");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-81: a dedicated source moved to an object with no attachment object of its own (Contact to Account address) is a retarget to SysFile storage - the file object becomes SysFile, linked by RecordId, with the record object as sent, and the record scope written for a Contact is cleared with a notice naming both objects.")]
	[AllureTag(ModifyToolName)]
	[AllureName("a dedicated source moved to an object without its own attachment object becomes SysFile storage")]
	public async Task ModifyBusinessProcess_Should_RetargetADedicatedSourceToSysFileStorage() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartSysFileAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("SysRetarget");
			await ArrangeSysFileProcessAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson, ("OF1", FileElement("""
					{ "attachments": { "object": "Contact", "recordId": { "processParameter": "ContactRef" } } }
					"""))), "the dedicated element scoped to the process's contact");

			// Act
			CallToolResult moved = await ModifySysFileAsync(context, processName, $$"""
				[ { "op": "setElement", "elementName": "OF1",
				    "elementUpdate": { "fileProcessing": { "attachments": { "object": "{{SysFileRecordObject}}" } } } } ]
				""");

			// Assert
			ShouldHaveLanded(moved, "moving a dedicated source to an object without its own attachment object is a retarget");
			WarningText(moved).Should().Contain($"source changed from 'Contact' to '{SysFileRecordObject}'",
				because: "the caller is told the filter written for the previous object was cleared");
			JsonObject attachments = Group(FileProcessingBlock(Element(await DescribeGraphAsync(context, processName), "OF1")),
				"attachments");
			ShouldBeSysFile(attachments, SysFileRecordObject, "the retargeted source");
			attachments["recordId"].Should().BeNull(because: "a scope on a Contact selects no Account address file");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-46 (1) on the designer-built fixture UsrFpOaDesignerFixture: F1, a designer-built Object attachments element on Account address, is described as SysFile storage - build type fileprocessing, storage sysFile, file object SysFile, the record object - with no issue about the storage. Read-only. Ignored, with the build instructions, when the stand does not carry the fixture.")]
	[AllureTag(DescribeToolName)]
	[AllureName("a designer-built SysFile Process file element is described with no storage issue")]
	public async Task DesignerFixture_Should_DescribeTheSysFileElement_WithNoStorageIssue() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartSysFileAsync();
		await IgnoreUnlessDesignerFixtureExistsAsync(context);

		// Act
		JsonObject f1 = SingleElementOfTask(await DescribeGraphAsync(context, DesignerFixtureName), ObjectTaskName, "F1");

		// Assert
		Text(f1, "buildType").Should().BeEquivalentTo(FileProcessingBuildType,
			because: "a designer-built Object attachments element is claimed by the Process file handler");
		JsonObject block = FileProcessingBlock(f1);
		ShouldBeSysFile(Group(block, "attachments"), SysFileRecordObject, "F1's source");
		Strings(block["issues"]).Should().NotContain(issue => issue.Contains("SysFile", StringComparison.Ordinal),
			because: "SysFile storage is written like any other, so a designer-built SysFile element carries no storage issue");
	}

	#endregion

	#region Methods: SysFile helpers

	/// <summary>Opens a session, or ignores the test when the stand is behind the SysFile cut.</summary>
	private static Task<ProcessDesignerArrangeContext> StartSysFileAsync() =>
		ProcessDesignerE2EArrange.StartAsync("Process file element (SysFile storage)", SysFileMinimumPackageVersion,
			TimeSpan.FromMinutes(8));

	/// <summary>How many times <paramref name="token"/> occurs in <paramref name="text"/> (ordinal).</summary>
	private static int Occurrences(string text, string token) {
		int count = 0;
		for (int index = text.IndexOf(token, StringComparison.Ordinal); index >= 0;
				index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal)) {
			count++;
		}
		return count;
	}

	/// <summary><see cref="CreateAsync"/> for a SysFile case: an Ignore names the SysFile floor.</summary>
	private static Task<CallToolResult> CreateSysFileAsync(ProcessDesignerArrangeContext context, List<string> created,
			string processName, string descriptor) =>
		CreateAsync(context, created, processName, descriptor, SysFileMinimumPackageVersion);

	/// <summary><see cref="ArrangeProcessAsync"/> for a SysFile case: an Ignore names the SysFile floor.</summary>
	private static Task ArrangeSysFileProcessAsync(ProcessDesignerArrangeContext context, List<string> created,
			string processName, string descriptor, string what) =>
		ArrangeProcessAsync(context, created, processName, descriptor, what, SysFileMinimumPackageVersion);

	/// <summary><see cref="ModifyAsync"/> for a SysFile case: an Ignore names the SysFile floor.</summary>
	private static Task<CallToolResult> ModifySysFileAsync(ProcessDesignerArrangeContext context, string processName,
			string operations) =>
		ModifyAsync(context, processName, operations, SysFileMinimumPackageVersion);

	/// <summary>A side of the block in SysFile storage: the record object as sent, SysFile and its RecordId link.</summary>
	private static void ShouldBeSysFile(JsonObject side, string recordObject, string what) {
		Text(side, "object").Should().Be(recordObject, because: $"{what}'s record object is stored as the data object");
		Text(side, "storage").Should().Be("sysFile",
			because: $"{recordObject} has no attachment object of its own, so {what} derives SysFile storage");
		Text(side, "fileObject").Should().Be("SysFile", because: $"{what}'s files live in SysFile");
		Text(side, "linkColumn").Should().Be("RecordId", because: "a SysFile row links to its record through RecordId");
	}

	#endregion

	#region Methods: Descriptors

	// The record references most cases read: a Contact and an Account by Lookup, and a bare Guid for a record of any
	// object.
	private const string RecordRefParametersJson = """
		[
		  { "name": "ContactRef", "type": "Lookup", "referenceSchema": "Contact", "direction": "In" },
		  { "name": "AccountRef", "type": "Lookup", "referenceSchema": "Account", "direction": "In" },
		  { "name": "AnyId", "type": "Guid", "direction": "In" }
		]
		""";

	// One contact's attachments (ContactFile), at most 20, newest first, saved to one account's (AccountFile).
	private const string SavingBlockJson = """
		{ "attachments": { "object": "Contact", "recordId": { "processParameter": "ContactRef" },
		    "numberOfRecords": 20, "sort": { "column": "CreatedOn", "direction": "desc" } },
		  "saveTo": { "object": "Account", "recordId": { "processParameter": "AccountRef" } } }
		""";

	// One contact's attachments, handed to the following elements (no saveTo infers useInProcess).
	private const string UseInProcessBlockJson = """
		{ "attachments": { "object": "Contact", "recordId": { "processParameter": "ContactRef" } } }
		""";

	// A Read data element finding one contact: its ResultEntity is the record a value source reads.
	private const string ReadContactJson = """
		{ "name": "ReadData1", "type": "readData", "caption": "Read contact",
		  "readData": { "source": "Contact", "mode": "first" } }
		""";

	// A callee taking one File: converted to multi-instance, it becomes the input collection's item F.
	private const string FileCalleeJson = """[ { "name": "F", "type": "File", "direction": "In" } ]""";

	/// <summary>A Process file element named OF1 carrying <paramref name="blockJson"/> as its <c>fileProcessing</c> block.</summary>
	private static string FileElement(string blockJson) =>
		$$"""{ "name": "OF1", "type": "fileProcessing", "caption": "Process contact files", "fileProcessing": {{blockJson}} }""";

	/// <summary>A sub-process element SubProcess1 running <paramref name="calleeName"/> once per row of its input.</summary>
	private static string MultiInstanceJson(string calleeName) =>
		$$"""
		{ "name": "SubProcess1", "type": "subProcess", "caption": "Once per file",
		  "subProcess": { "processName": "{{calleeName}}", "multiInstanceOptions": { "enabled": true } } }
		""";

	/// <summary>A process with only a start and an end, carrying the given process parameters.</summary>
	private static string PlainDescriptor(string processName, string parametersJson) =>
		LinearDescriptor(processName, parametersJson);

	/// <summary>Start -> the elements in the order given -> End, with no mappings.</summary>
	private static string LinearDescriptor(string processName, string parametersJson,
			params (string Name, string Json)[] elements) =>
		LinearDescriptor(processName, parametersJson, elements, "[]");

	/// <summary>Start -> the elements in the order given -> End, with the given descriptor-level mappings.</summary>
	private static string LinearDescriptor(string processName, string parametersJson,
			(string Name, string Json)[] elements, string mappingsJson) {
		List<string> names = ["StartEvent1", .. elements.Select(element => element.Name), "EndEvent1"];
		string elementsJson = string.Join(",\n", new[] {
				"""{ "name": "StartEvent1", "type": "startEvent" }"""
			}
			.Concat(elements.Select(element => element.Json))
			.Append("""{ "name": "EndEvent1", "type": "endEvent" }"""));
		string flowsJson = string.Join(",\n", names.Zip(names.Skip(1),
			(source, target) => $$"""{ "source": "{{source}}", "target": "{{target}}" }"""));
		return $$"""
			{
			  "name": "{{processName}}",
			  "caption": "Clio BP Process file E2E",
			  "packageName": "Custom",
			  "parameters": {{parametersJson}},
			  "elements": [ {{elementsJson}} ],
			  "flows": [ {{flowsJson}} ],
			  "mappings": {{mappingsJson}}
			}
			""";
	}

	private static Dictionary<string, object?> GraphNode(string name, string type) =>
		new() { ["name"] = name, ["type"] = type };

	private static Dictionary<string, object?> GraphEdge(string source, string target) =>
		new() { ["source"] = source, ["target"] = target, ["flow-kind"] = "sequence" };

	#endregion

	#region Methods: Arrange and cleanup

	/// <summary>Opens a session, or ignores the test when the stand is missing, unreachable or behind the cut.</summary>
	private static Task<ProcessDesignerArrangeContext> StartAsync() =>
		ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion, TimeSpan.FromMinutes(8));

	/// <summary>A process code unique to this run, in the <c>UsrClioBpFp&lt;Case&gt;E2e</c> family.</summary>
	private static string NewProcessName(string caseName) => $"UsrClioBpFp{caseName}E2e{Guid.NewGuid():N}";

	/// <summary>
	/// Builds a process, recording its name for cleanup BEFORE the call: a build the server saved but whose answer
	/// never arrived must still be removed, and deleting a name that was never built only logs "Could not delete".
	/// Ignores the test when clio refused the call because the stand's CrtProcessBuilder is behind.
	/// </summary>
	private static async Task<CallToolResult> CreateAsync(ProcessDesignerArrangeContext context,
			List<string> created, string processName, string descriptor,
			string writtenAgainstVersion = MinimumPackageVersion) {
		created.Add(processName);
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = descriptor
			});
		IgnoreWhenProcessBuilderIsBehind(ToolText(result), writtenAgainstVersion);
		return result;
	}

	/// <summary>Builds a process for an ARRANGE step and fails the test on the spot unless it was created.</summary>
	private static async Task ArrangeProcessAsync(ProcessDesignerArrangeContext context, List<string> created,
			string processName, string descriptor, string what, string writtenAgainstVersion = MinimumPackageVersion) {
		CallToolResult result = await CreateAsync(context, created, processName, descriptor, writtenAgainstVersion);
		ShouldHaveBuilt(result, $"{what} is an arrange step, so every assertion below is about the wrong failure "
			+ "if it was not built");
	}

	private static async Task<CallToolResult> ModifyAsync(ProcessDesignerArrangeContext context, string processName,
			string operations, string writtenAgainstVersion = MinimumPackageVersion) {
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = operations
			});
		IgnoreWhenProcessBuilderIsBehind(ToolText(result), writtenAgainstVersion);
		return result;
	}

	/// <summary>Reads a process back in full (<c>mode=full</c>) and returns its graph.</summary>
	private static async Task<JsonObject> DescribeGraphAsync(ProcessDesignerArrangeContext context,
			string processName) =>
		DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName));

	/// <summary>
	/// Removes every process a test built, newest first so a caller goes before its callee. Reports a failed delete
	/// instead of throwing (<see cref="ProcessDesignerE2ESupport.DeleteProcessAsync"/>), so the assertion failure the
	/// test was already reporting is not replaced.
	/// </summary>
	private static async Task DeleteProcessesAsync(ProcessDesignerArrangeContext context, List<string> created) {
		for (int index = created.Count - 1; index >= 0; index--) {
			await DeleteProcessAsync(context.EnvironmentName, created[index]);
		}
	}

	/// <summary>
	/// Removes the probe parameter the fixture test added, best effort: called from finally, so a failure is
	/// reported rather than thrown. A refused or failed removal leaves one unused Text parameter on the fixture,
	/// which no other case reads.
	/// </summary>
	private static async Task RemoveProbeParameterAsync(ProcessDesignerArrangeContext context, string probeName) {
		try {
			CallToolResult removed = await ModifyAsync(context, DesignerFixtureName,
				$$"""[ { "op": "removeParameter", "parameterName": "{{probeName}}" } ]""");
			if (McpCommandExecutionParser.Extract(removed).ExitCode != 0) {
				await TestContext.Error.WriteLineAsync(
					$"Could not remove the probe parameter '{probeName}' from {DesignerFixtureName}: {LogText(removed)}");
			}
		} catch (Exception exception) when (exception is not OutOfMemoryException) {
			await TestContext.Error.WriteLineAsync(
				$"Removing the probe parameter '{probeName}' from {DesignerFixtureName} failed: {exception.Message}");
		}
	}

	/// <summary>
	/// Ignores the test, with the build instructions, when the stand does not carry the designer-built fixture.
	/// Read from <c>SysSchema</c> by name rather than inferred from a failed describe, so a broken describe still
	/// fails instead of reading as an absent fixture.
	/// </summary>
	private static async Task IgnoreUnlessDesignerFixtureExistsAsync(ProcessDesignerArrangeContext context) {
		JsonElement rows = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery("SysSchema",
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition("Name", DesignerFixtureName,
					SelectQueryHelper.TextDataValueType),
				new SelectQueryHelper.SelectQueryFilterDefinition("ManagerName", "ProcessSchemaManager",
					SelectQueryHelper.TextDataValueType)
			], rowCount: 1), "the designer fixture's SysSchema row");
		if (rows.GetArrayLength() == 0) {
			Assert.Ignore($"Designer fixture {DesignerFixtureName} not found on '{context.EnvironmentName}'. Build it as "
				+ "described in plan §5.1 OA.11 to run this test.");
		}
	}

	/// <summary>The id of one record of <paramref name="schemaName"/>, which a constant record id must name.</summary>
	private static async Task<string> ReadFirstRecordIdAsync(ProcessDesignerArrangeContext context, string schemaName) {
		JsonElement rows = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery(schemaName,
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")], [], rowCount: 1), $"one {schemaName} record");
		if (rows.GetArrayLength() == 0) {
			Assert.Ignore($"{Subject} MCP E2E needs at least one {schemaName} record on '{context.EnvironmentName}' "
				+ "for its constant record id.");
		}
		return rows[0].GetProperty("Id").GetString()!;
	}

	#endregion

	#region Methods: Raw database reads

	/// <summary>
	/// The raw JSON of the element with <paramref name="elementUId"/> in each stored <c>SysSchema.MetaData</c> row of
	/// <paramref name="schemaName"/>, ordered by row id.
	/// <para>The database and not describe, on purpose: describe is not a capture read path (serialization capture
	/// section 1, S6), and a preservation claim is about the stored bytes. The node is located by its <c>UId</c>
	/// and returned with <see cref="JsonElement.GetRawText"/>, so the comparison is of the stored text, not of a
	/// re-serialization. Through <c>execute-esq-to-file</c> because a process's metadata can exceed the inline
	/// response budget; DataService rather than SQL for the reason the caption reads give in
	/// <c>SubProcessElementToolE2ETests</c>.</para>
	/// </summary>
	private static async Task<IReadOnlyList<string>> ReadElementNodesAsync(ProcessDesignerArrangeContext context,
			string schemaName, string elementUId) {
		JsonElement rows = await SelectToFileAsync(context, SelectQueryHelper.BuildSelectQuery("SysSchema",
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id", OrderDirection: 1, OrderPosition: 0),
				new SelectQueryHelper.SelectQueryColumnDefinition("MetaData", "MetaData")
			],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition("Name", schemaName, SelectQueryHelper.TextDataValueType),
				new SelectQueryHelper.SelectQueryFilterDefinition("ManagerName", "ProcessSchemaManager",
					SelectQueryHelper.TextDataValueType)
			]), $"the SysSchema rows of '{schemaName}'");
		List<string> nodes = [];
		foreach (JsonElement row in rows.EnumerateArray()) {
			string metadata = DecodeMetaData(row.GetProperty("MetaData"));
			using JsonDocument document = JsonDocument.Parse(metadata);
			string? node = FindNodeByUId(document.RootElement, Guid.Parse(elementUId));
			if (node != null) {
				nodes.Add($"{row.GetProperty("Id")}: {node}");
			}
		}
		nodes.Should().NotBeEmpty(
			because: $"the element {elementUId} must be found in the stored metadata of '{schemaName}', or the "
				+ "comparison would compare nothing");
		return nodes;
	}

	/// <summary>
	/// The stored <c>SysLocalizableValue</c> rows of one element - keys <c>BaseElements.&lt;element&gt;.*</c>, every
	/// culture - as sorted <c>key | culture | value</c> lines. Read from the database, not describe: every read
	/// re-materializes captions, so describe can show a caption the database does not hold.
	/// </summary>
	private static async Task<IReadOnlyList<string>> ReadElementResourceRowsAsync(ProcessDesignerArrangeContext context,
			string schemaName, string elementName) {
		JsonElement rows = await SelectToFileAsync(context, SelectQueryHelper.BuildSelectQuery("SysLocalizableValue",
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("Key", "Key"),
				new SelectQueryHelper.SelectQueryColumnDefinition("SysCulture.Name", "Culture"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Value", "Value")
			],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition("SysSchema.Name", schemaName,
					SelectQueryHelper.TextDataValueType)
			]), $"the resource rows of '{schemaName}'");
		string prefix = $"BaseElements.{elementName}.";
		return rows.EnumerateArray()
			.Select(row => (Key: TextOf(row, "Key"), Culture: TextOf(row, "Culture"), Value: TextOf(row, "Value")))
			.Where(row => row.Key.StartsWith(prefix, StringComparison.Ordinal))
			.Select(row => $"{row.Key} | {row.Culture} | {row.Value}")
			.OrderBy(line => line, StringComparer.Ordinal)
			.ToList();
	}

	/// <summary>Runs a small SelectQuery through <c>execute-esq</c> and returns its rows array.</summary>
	private static async Task<JsonElement> SelectInlineAsync(ProcessDesignerArrangeContext context, object query,
			string what) {
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, ExecuteEsqTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["query"] = JsonSerializer.SerializeToElement(query)
			});
		ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(result);
		response.Success.Should().BeTrue(because: $"{what} must be readable for this case to measure anything; the read "
			+ "failed with: {0}", response.Error);
		JsonElement rows = response.Rows.GetValueOrDefault();
		rows.ValueKind.Should().Be(JsonValueKind.Array,
			because: "a SelectQuery answers with a rows array; anything else is a shape this read cannot use");
		return rows.Clone();
	}

	/// <summary>Runs a SelectQuery through <c>execute-esq-to-file</c> and returns the rows array the file holds.</summary>
	private static async Task<JsonElement> SelectToFileAsync(ProcessDesignerArrangeContext context, object query,
			string what) {
		string directory = ScratchDirectory.CreateUnderPhysicalTemp("clio-process-file-e2e");
		try {
			string outputFile = Path.Combine(directory, "rows.json");
			CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, ExecuteEsqToFileTool.ToolName,
				new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["query"] = JsonSerializer.SerializeToElement(query),
					["output-file"] = outputFile
				});
			ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(result);
			response.Success.Should().BeTrue(because: $"{what} must be readable for this case to measure anything; the "
				+ "read failed with: {0}", response.Error);
			using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(outputFile));
			document.RootElement.ValueKind.Should().Be(JsonValueKind.Array,
				because: "the file holds the rows array of the SelectQuery");
			return document.RootElement.Clone();
		} finally {
			ScratchDirectory.TryDelete(directory);
		}
	}

	/// <summary>
	/// The metadata JSON text of a <c>SysSchema.MetaData</c> value as DataService returns a binary column: a base64
	/// string, an array of bytes, or - tolerated - the JSON text itself. A UTF-8 byte-order mark is dropped.
	/// </summary>
	private static string DecodeMetaData(JsonElement value) {
		byte[]? bytes = null;
		if (value.ValueKind == JsonValueKind.String) {
			string text = value.GetString() ?? string.Empty;
			if (text.TrimStart().StartsWith('{')) {
				return text;
			}
			bytes = Convert.FromBase64String(text);
		} else if (value.ValueKind == JsonValueKind.Array) {
			bytes = value.EnumerateArray().Select(item => item.GetByte()).ToArray();
		}
		bytes.Should().NotBeNull(because: "SysSchema.MetaData must come back as binary content; the value read was: {0}",
			value.GetRawText());
		return Encoding.UTF8.GetString(bytes!).TrimStart('﻿');
	}

	/// <summary>The raw text of the first object whose <c>UId</c> is <paramref name="uid"/>, depth first.</summary>
	private static string? FindNodeByUId(JsonElement node, Guid uid) {
		switch (node.ValueKind) {
			case JsonValueKind.Object:
				if (node.TryGetProperty("UId", out JsonElement id) && id.ValueKind == JsonValueKind.String
						&& Guid.TryParse(id.GetString(), out Guid parsed) && parsed == uid) {
					return node.GetRawText();
				}
				foreach (JsonProperty property in node.EnumerateObject()) {
					string? found = FindNodeByUId(property.Value, uid);
					if (found != null) {
						return found;
					}
				}
				return null;
			case JsonValueKind.Array:
				foreach (JsonElement item in node.EnumerateArray()) {
					string? found = FindNodeByUId(item, uid);
					if (found != null) {
						return found;
					}
				}
				return null;
			default:
				return null;
		}
	}

	/// <summary>A row value as text: a string as is, a lookup by its display value, anything else raw.</summary>
	private static string TextOf(JsonElement row, string column) {
		if (!row.TryGetProperty(column, out JsonElement value)) {
			return string.Empty;
		}
		return value.ValueKind switch {
			JsonValueKind.String => value.GetString() ?? string.Empty,
			JsonValueKind.Object when value.TryGetProperty("displayValue", out JsonElement display) => display.ToString(),
			JsonValueKind.Null => string.Empty,
			_ => value.GetRawText()
		};
	}

	#endregion

	#region Methods: Graph reading and assertions

	/// <summary>The element's described <c>fileProcessing</c> block, failing the test when describe reports none.</summary>
	private static JsonObject FileProcessingBlock(JsonObject element) => Group(element, "fileProcessing");

	/// <summary>A side of the block in dedicated storage: the record object, its own file object and link column.</summary>
	private static void ShouldBeDedicated(JsonObject side, string recordObject, string fileObject, string what) {
		Text(side, "object").Should().Be(recordObject, because: $"{what}'s record object is stored as sent");
		Text(side, "storage").Should().Be("dedicated",
			because: $"{recordObject} has exactly one attachment object, so {what} derives dedicated storage");
		Text(side, "fileObject").Should().Be(fileObject, because: $"{what}'s files live in {fileObject}");
		Text(side, "linkColumn").Should().Be(recordObject,
			because: $"{fileObject} links a file to its record through the column named after {recordObject}");
	}

	/// <summary>The described sort: one column of the file object and its direction.</summary>
	private static void ShouldBeSortedBy(JsonObject attachments, string column, string direction) {
		JsonObject sort = Group(attachments, "sort");
		Text(sort, "column").Should().Be(column, because: "the sort column is stored and read back");
		Text(sort, "direction").Should().Be(direction, because: "and its direction");
	}

	/// <summary>
	/// A parameter bound to the parameter with <paramref name="sourceUId"/>: describe reports a formula value as the
	/// raw expression, so the binding is a Script value carrying the source's parameter segment.
	/// </summary>
	private static void ShouldBeBoundTo(JsonObject parameter, string sourceUId, string because) {
		(Text(parameter, "value") ?? string.Empty).Should().ContainEquivalentOf(ParameterSegment(sourceUId),
			because: Escape(because) + "; the parameter read: {0}", parameter.ToJsonString());
		Text(parameter, "source").Should().Be("Script",
			because: "a parameter-to-parameter binding is stored as a formula reference");
	}

	/// <summary>The one element of the fixture whose user task is <paramref name="taskName"/>.</summary>
	private static JsonObject SingleElementOfTask(JsonObject graph, string taskName, string label) {
		List<JsonObject> found = Objects(graph["elements"])
			.Where(element => string.Equals(Text(element, "userTaskName"), taskName, StringComparison.OrdinalIgnoreCase))
			.ToList();
		found.Should().HaveCount(1, because: $"the fixture carries exactly one {label} ({taskName}); what was read: {{0}}",
			graph.ToJsonString());
		return found[0];
	}

	/// <summary>The described outputs as <c>parameter.item</c>.</summary>
	private static IReadOnlyList<string> OutputNames(JsonObject block) =>
		Objects(block["outputs"]).Select(output => $"{Text(output, "parameter")}.{Text(output, "item")}").ToList();

	/// <summary>The described process parameter with that name.</summary>
	private static JsonObject ProcessParameter(JsonObject graph, string name) =>
		Named(Objects(graph["parameters"]), name, "process parameter", graph);

	/// <summary>The names of the described process parameters.</summary>
	private static IReadOnlyList<string?> ParameterNames(JsonObject graph) =>
		Objects(graph["parameters"]).Select(parameter => Text(parameter, "name")).ToList();

	/// <summary>The described element with that name.</summary>
	private static JsonObject Element(JsonObject graph, string name) =>
		Named(Objects(graph["elements"]), name, "element", graph);

	/// <summary>The described parameter with that name on an element.</summary>
	private static JsonObject ElementParameter(JsonObject element, string name) =>
		Named(Objects(element["parameters"]), name, "element parameter", element);

	/// <summary>The described item with that name of a collection.</summary>
	private static JsonObject Item(JsonObject collection, string name) =>
		Named(Objects(collection["itemProperties"]), name, "item", collection);

	/// <summary>The one candidate with that name, failing the test with what was read when there is none.</summary>
	private static JsonObject Named(IReadOnlyList<JsonObject> candidates, string name, string kind, JsonObject within) {
		JsonObject? found = candidates.SingleOrDefault(candidate => Text(candidate, "name") == name);
		found.Should().NotBeNull(because: $"the {kind} '{name}' must be described; what was read: {{0}}",
			within.ToJsonString());
		return found!;
	}

	/// <summary>A nested object member, failing the test with what was read when it is absent.</summary>
	private static JsonObject Group(JsonObject owner, string key) {
		JsonObject? group = owner[key] as JsonObject;
		group.Should().NotBeNull(because: $"'{key}' must be described; what was read: {{0}}", owner.ToJsonString());
		return group!;
	}

	/// <summary>The described uid of a parameter, item or element, failing the test when describe reports none.</summary>
	private static string UId(JsonObject node) {
		string? uid = Text(node, "uid");
		Guid.TryParse(uid, out _).Should().BeTrue(
			because: $"a binding and a stored node are found by UId, so '{Text(node, "name")}' must report one");
		return uid!;
	}

	/// <summary>The metapath segment that names the parameter with <paramref name="parameterUId"/>.</summary>
	private static string ParameterSegment(string parameterUId) =>
		"[Parameter:{" + Guid.Parse(parameterUId).ToString("D") + "}]";

	/// <summary>The integer value of a member, or <c>null</c> when it is absent or not a number.</summary>
	private static int? Number(JsonObject node, string key) =>
		node[key] is JsonValue value && value.TryGetValue(out int number) ? number : null;

	/// <summary>The boolean value of a member, or <c>null</c> when it is absent or not a boolean.</summary>
	private static bool? Flag(JsonObject node, string key) =>
		node[key] is JsonValue value && value.TryGetValue(out bool flag) ? flag : null;

	/// <summary>The objects of a JSON array, or none when the member is absent or not an array.</summary>
	private static IReadOnlyList<JsonObject> Objects(JsonNode? node) =>
		node is JsonArray array ? array.OfType<JsonObject>().ToList() : [];

	/// <summary>The strings of a JSON array, or none when the member is absent or not an array.</summary>
	private static IReadOnlyList<string> Strings(JsonNode? node) =>
		node is JsonArray array
			? array.OfType<JsonValue>()
				.Select(value => value.TryGetValue(out string? text) ? text : null)
				.OfType<string>()
				.ToList()
			: [];

	// The platform re-synchronizes a user-task element's parameters against its template when the design session
	// loads it (ProcessSchemaActivity.SynchronizeParameter), which the classic designer's save path never does: the
	// Process file pages force ConnectedObjectId to Lookup (the template declares Guid) and save schema lookups
	// without the template's ReferenceSchemaUId (L9 = SysSchema). The builder also re-lays out the diagram (BL3).
	// These three, in that direction only, may differ after the FIRST builder save of an element in the designer's form.
	private static IEnumerable<string> ExpectedAfterFirstSave(IReadOnlyList<string> before, IReadOnlyList<string> after) {
		if (before.Count != after.Count) {
			return before;
		}
		return before.Select((node, index) => WithFirstSaveNormalization(node, after[index]));
	}

	private static string WithFirstSaveNormalization(string beforeNode, string afterNode) {
		if (!TrySplitNode(beforeNode, out string prefix, out JsonObject? before)
				|| !TrySplitNode(afterNode, out _, out JsonObject? after)) {
			return beforeNode;
		}
		before.Remove("BL3");
		if (before["BP2"] is JsonArray parameters && after["BP2"] is JsonArray afterParameters) {
			Dictionary<string, JsonObject> afterByName = afterParameters.OfType<JsonObject>()
				.Where(parameter => parameter["A2"] is not null)
				.GroupBy(parameter => parameter["A2"]!.GetValue<string>())
				.ToDictionary(group => group.Key, group => group.First());
			foreach (JsonObject parameter in parameters.OfType<JsonObject>()) {
				string? name = parameter["A2"]?.GetValue<string>();
				if (name is null || !afterByName.TryGetValue(name, out JsonObject? afterParameter)) {
					continue;
				}
				if (name.EndsWith("EntitySchemaUId", StringComparison.Ordinal) && parameter["L9"] is null
						&& IsUId(afterParameter["L9"], SysSchemaUId)) {
					parameter["L9"] = afterParameter["L9"]!.GetValue<string>();
				}
				if (name == "ConnectedObjectId" && IsUId(parameter["L1"], LookupDataValueTypeUId)
						&& IsUId(afterParameter["L1"], GuidDataValueTypeUId)) {
					parameter["L1"] = afterParameter["L1"]!.GetValue<string>();
				}
			}
		}
		return prefix + Canonical(before).ToJsonString();
	}

	private static string WithoutLayout(string rawNode) {
		if (!TrySplitNode(rawNode, out string prefix, out JsonObject? node)) {
			return rawNode;
		}
		node.Remove("BL3");
		return prefix + Canonical(node).ToJsonString();
	}

	// A read node is "<UId>: <raw JSON>"; only the JSON part is compared structurally.
	private static bool TrySplitNode(string rawNode, out string prefix, [NotNullWhen(true)] out JsonObject? node) {
		int jsonStart = rawNode.IndexOf('{');
		prefix = jsonStart < 0 ? rawNode : rawNode[..jsonStart];
		node = jsonStart < 0 ? null : JsonNode.Parse(rawNode[jsonStart..]) as JsonObject;
		return node is not null;
	}

	// The platform writes a parameter's keys in a fixed order (an added L9 sits before L12), so both sides are compared
	// with their object keys sorted: the allow-list adds keys, and only the VALUES are what is asserted.
	[return: NotNullIfNotNull(nameof(node))]
	private static JsonNode? Canonical(JsonNode? node) => node switch {
		JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)
			.Select(pair => KeyValuePair.Create(pair.Key, Canonical(pair.Value)))),
		JsonArray array => new JsonArray(array.Select(Canonical).ToArray()),
		_ => node?.DeepClone()
	};

	private static bool IsUId(JsonNode? value, string uid) =>
		value is JsonValue && string.Equals(value.GetValue<string>(), uid, StringComparison.OrdinalIgnoreCase);

	#endregion

}

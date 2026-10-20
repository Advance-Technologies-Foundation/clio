using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using static Clio.Mcp.E2E.Support.Mcp.ProcessDesignerE2ESupport;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the File and FileCollection process parameter types (ENG-95984) over the real MCP path.
/// NOT in CI - run manually against a stand carrying CrtProcessBuilder 1.6.6.107 or later (File and FileCollection arrived in 1.6.6.99; 1.6.6.107 is the create and modify floor).
/// <para>What only this level can show is the wire: the type names, the FileCollection read-back, the dotted
/// <c>processParameter</c> member, the server's refusals and the binder notices travel as JSON members and log
/// lines that the unit tests construct in C#, so a renamed or dropped member would leave every unit suite green.
/// The two-level collection rules are asserted on what describe reads back from a real saved schema: a binding is
/// a <c>value</c> carrying the source's <c>[Parameter:{uid}]</c> segment with source <c>Script</c>.</para>
/// <para>The generic <c>userTask</c> route is deliberately NOT used for the three Process file schemas: later
/// tickets (ENG-96505, ENG-96506) refuse it, so the targets here are a process-level FileCollection and the input
/// collection of a multi-instance sub-process. Every test removes the processes it built, callers before their
/// callees, in <c>finally</c>. Strictly sequential: schema writes in a parallel burst take a .NET Framework
/// stand's app pool down.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class FileParameterToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;
	private const string DescribeToolName = DescribeProcessTool.ToolName;

	/// <summary>
	/// The create/modify floor this clio enforces (File and FileCollection arrived in 1.6.6.99; the floor rose to
	/// 1.6.6.103 with the Process file element, to 1.6.6.105 with its Generated report and Process parameter
	/// variants, to 1.6.6.106 with their final-review fixes and to 1.6.6.107 with the core-reuse fixes); named in the skip message so a developer knows what to install.
	/// </summary>
	private const string MinimumPackageVersion = "1.6.6.107";

	/// <summary>What the skip message calls these tests.</summary>
	private const string Subject = "File process parameter";

	/// <summary>The platform type name describe reports for one file.</summary>
	private const string FileLocatorType = "FileLocator";

	/// <summary>The type name describe reports for a process-level collection of the FileCollection shape.</summary>
	private const string FileCollectionType = "FileCollection";

	#region Methods: Tests

	[Test]
	[Description("create-business-process declares a File, a FileCollection and an In FileCollection in one call, and describe-business-process reads them back: File as type FileLocator, direction Variable; FileCollection as type FileCollection, direction Variable by default, with exactly one item File of type FileLocator and no tag; the explicit In direction kept.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process declares File and FileCollection parameters and describe reads them back")]
	public async Task CreateBusinessProcess_Should_DeclareFileAndFileCollection_AndReadThemBack() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Declare");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				BuildPlainDescriptor(processName, FileParametersJson));

			// Assert
			ShouldHaveBuilt(callResult, "the three file parameters are one ordinary create call");
			JsonObject graph = await DescribeGraphAsync(context, processName);
			ShouldBeASingleFile(ProcessParameter(graph, "Doc"), "Variable");
			ShouldBeAFileCollection(ProcessParameter(graph, "Docs"), "Variable");
			ShouldBeAFileCollection(ProcessParameter(graph, "InDocs"), "In");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("The described File and FileCollection parameters, projected to name, type and direction, rebuild the same parameters in a second process: describe of the second equals describe of the first on name, type, direction and the item's name and type. This is the round trip the read-back type names exist for - FileLocator and FileCollection must be accepted as declared types.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process rebuilds the described file parameters unchanged")]
	public async Task CreateBusinessProcess_Should_RebuildTheDescribedFileParameters_Unchanged() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string firstName = NewProcessName("RoundTripA");
			string secondName = NewProcessName("RoundTripB");
			await ArrangeProcessAsync(context, created, firstName,
				BuildPlainDescriptor(firstName, FileParametersJson), "the process the parameters are read from");
			IReadOnlyList<JsonObject> described = Objects((await DescribeGraphAsync(context, firstName))["parameters"]);
			string projected = new JsonArray(described
				.Select(parameter => (JsonNode?)new JsonObject {
					["name"] = Text(parameter, "name"),
					["type"] = Text(parameter, "type"),
					["direction"] = Text(parameter, "direction")
				})
				.ToArray()).ToJsonString();

			// Act
			CallToolResult callResult = await CreateAsync(context, created, secondName,
				BuildPlainDescriptor(secondName, projected));

			// Assert
			ShouldHaveBuilt(callResult, "the described type names are accepted as declared types");
			IReadOnlyList<ParameterShape> firstShapes = Shapes(described);
			IReadOnlyList<ParameterShape> secondShapes =
				Shapes(Objects((await DescribeGraphAsync(context, secondName))["parameters"]));
			firstShapes.Should().Contain(shape => shape.Type == FileCollectionType,
				because: "the round trip is only evidence if the first read-back carried the FileCollection type at all");
			secondShapes.Should().BeEquivalentTo(firstShapes,
				because: "a described File / FileCollection must rebuild the same name, type, direction and item shape");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("modify-business-process addParameter declares a File (spelled in lower case) and an In FileCollection on an existing process, and describe reads them back exactly as a create declares them: FileLocator / Variable, and FileCollection / In with one File item of type FileLocator and no tag.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process adds File and FileCollection parameters")]
	public async Task ModifyBusinessProcess_Should_AddFileAndFileCollectionParameters() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Add");
			await ArrangeProcessAsync(context, created, processName, BuildPlainDescriptor(processName, "[]"),
				"the process the parameters are added to");

			// Act
			CallToolResult callResult = await ModifyAsync(context, processName, """
				[
				  { "op": "addParameter", "parameter": { "name": "Doc", "type": "file" } },
				  { "op": "addParameter", "parameter": { "name": "Docs", "type": "FileCollection", "direction": "In" } }
				]
				""");

			// Assert
			ShouldHaveLanded(callResult, "adding a File and a FileCollection is an ordinary edit");
			JsonObject graph = await DescribeGraphAsync(context, processName);
			ShouldBeASingleFile(ProcessParameter(graph, "Doc"), "Variable");
			ShouldBeAFileCollection(ProcessParameter(graph, "Docs"), "In");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("modify-business-process addParameter refuses type Binary for good - a process parameter carries a file by reference, not its bytes - and the refusal names the two types that do carry files, File and FileCollection. The refused edit adds nothing.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses Binary and names the file types")]
	public async Task ModifyBusinessProcess_Should_RefuseBinary_AndNameTheFileTypes() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Binary");
			await ArrangeProcessAsync(context, created, processName, BuildPlainDescriptor(processName, "[]"),
				"the process the Binary parameter is offered to");

			// Act
			CallToolResult callResult = await ModifyAsync(context, processName, """
				[ { "op": "addParameter", "parameter": { "name": "Payload", "type": "Binary" } } ]
				""");

			// Assert
			ShouldBeRefused(callResult, "Binary is refused for good: a file is type File");
			string log = LogText(callResult);
			log.Should().Contain("not supported", because: "the refusal says the type is not supported");
			log.Should().Contain("Binary", because: "the refusal names the refused type");
			log.Should().Contain("type File", because: "the refusal points at the type that carries one file");
			log.Should().Contain(FileCollectionType, because: "and at the type that carries several");
			ParameterNames(await DescribeGraphAsync(context, processName)).Should().NotContain("Payload",
				because: "a refused addParameter adds nothing");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("A File and a FileCollection have no constant form: addParameter of a File with a 'value' is refused, and addMapping of a constant 'value' onto a FileCollection is refused, both saying 'no constant form'. Neither lands: describe shows no new File parameter and the FileCollection still unbound.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses a constant on a File and on a FileCollection")]
	public async Task ModifyBusinessProcess_Should_RefuseAConstant_OnAFileAndAFileCollection() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Constant");
			await ArrangeProcessAsync(context, created, processName,
				BuildPlainDescriptor(processName, """[ { "name": "Docs", "type": "FileCollection" } ]"""),
				"the process with a FileCollection");

			// Act
			CallToolResult fileWithValue = await ModifyAsync(context, processName, """
				[ { "op": "addParameter", "parameter": { "name": "Doc", "type": "File", "value": "x" } } ]
				""");
			CallToolResult constantIntoCollection = await ModifyAsync(context, processName, """
				[ { "op": "addMapping", "mapping": { "targetProcessParameter": "Docs", "value": "[]" } } ]
				""");

			// Assert
			ShouldBeRefused(fileWithValue, "a File parameter cannot carry a constant default");
			LogText(fileWithValue).Should().Contain("no constant form",
				because: "the refusal says why: a file is filled by a mapping, never by a constant");
			ShouldBeRefused(constantIntoCollection, "a FileCollection cannot take a constant");
			LogText(constantIntoCollection).Should().Contain("no constant form",
				because: "the mapping route refuses the constant with the same reason");
			JsonObject graph = await DescribeGraphAsync(context, processName);
			ParameterNames(graph).Should().NotContain("Doc", because: "the refused addParameter adds nothing");
			ShouldBeUnbound(ProcessParameter(graph, "Docs"),
				"the refused addMapping leaves the FileCollection unbound");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("P1 over the wire: a caller with a FileCollection Docs (In) runs a multi-instance sub-process whose callee takes one File F, and the build maps ONLY the item - InputRecordCollection.F from the dotted processParameter Docs.File. The binder binds the parent level as well, so describe shows InputRecordCollection bound to Docs and its item F bound to Docs.File; no 'will be empty' notice is raised, because F is mapped.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process binds the input collection when only a file item is mapped")]
	public async Task CreateBusinessProcess_Should_BindTheInputCollection_WhenOnlyAFileItemIsMapped() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string calleeName = NewProcessName("P1Callee");
			string callerName = NewProcessName("P1Caller");
			await ArrangeProcessAsync(context, created, calleeName, BuildPlainDescriptor(calleeName, FileCalleeJson),
				"the called process with a File parameter");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, callerName,
				BuildMultiInstanceCallerDescriptor(callerName, calleeName,
					"""[ { "name": "Docs", "type": "FileCollection", "direction": "In" } ]""",
					"""
					[ { "elementName": "SubProcess1", "elementParameter": "InputRecordCollection.F",
					    "processParameter": "Docs.File" } ]
					"""));

			// Assert
			ShouldHaveBuilt(callResult, "an item-only mapping of a multi-instance input is accepted and completed");
			WarningText(callResult).Should().NotContain("will be empty on every iteration",
				because: "F is mapped, so the callee's file is not reported as left empty");
			JsonObject graph = await DescribeGraphAsync(context, callerName);
			JsonObject docs = ProcessParameter(graph, "Docs");
			JsonObject input = ElementParameter(Element(graph, "SubProcess1"), "InputRecordCollection");
			ShouldBeBoundTo(input, UId(docs),
				"P1 binds the input collection to Docs: each row of a collection comes from the collection it is bound to");
			ShouldBeBoundTo(Item(input, "F"), UId(Item(docs, "File")),
				"the item keeps the mapping it was given: the dotted processParameter Docs.File crossed the wire");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("P2-MI over the wire: a callee taking a Guid ContactId and a File F, iterated over Read data rows (collection mode, columns Id and Name), is ACCEPTED - iterating over Read data rows is legal and shipped - with a notice that F will be empty on every iteration unless InputRecordCollection.F is mapped. Read data rows have no file, so the target is not file-consuming and nothing is refused; describe shows F unbound and ContactId bound.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process accepts a file callee over Read data rows with a notice")]
	public async Task CreateBusinessProcess_Should_AcceptAFileCalleeOverReadDataRows_WithANotice() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string calleeName = NewProcessName("RowsCallee");
			string callerName = NewProcessName("RowsCaller");
			await ArrangeProcessAsync(context, created, calleeName,
				BuildPlainDescriptor(calleeName, ContactAndFileCalleeJson), "the called process with Guid and File");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, callerName,
				BuildReadDataCallerDescriptor(callerName, calleeName));

			// Assert
			ShouldHaveBuilt(callResult, "a file callee iterated over Read data rows is a notice, never a refusal");
			string warnings = WarningText(callResult);
			warnings.Should().Contain("InputRecordCollection.F",
				because: "the notice names the item the caller would have to map to deliver a file");
			warnings.Should().Contain("will be empty on every iteration",
				because: "the notice says what happens to the unmapped file item at run time");
			JsonObject input = ElementParameter(Element(await DescribeGraphAsync(context, callerName), "SubProcess1"),
				"InputRecordCollection");
			ShouldBeUnbound(Item(input, "F"), "nothing in a Read data row can fill a file, so F stays unbound");
			Text(Item(input, "ContactId"), "value").Should().NotBeNullOrWhiteSpace(
				because: "the positive control: the item that WAS mapped carries its binding");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("P2 over the wire: addMapping of a FileCollection Docs onto another FileCollection Docs2 binds Docs2 to Docs and pairs the file item too, with a notice that Docs2.File now reads Docs.File. A process-level FileCollection takes files, so its item is paired by the binder rather than left to read an empty file in every row.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process pairs the file item when a FileCollection is mapped onto another")]
	public async Task ModifyBusinessProcess_Should_PairTheFileItem_WhenAFileCollectionIsMappedOntoAnother() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Pair");
			await ArrangeProcessAsync(context, created, processName,
				BuildPlainDescriptor(processName, """
					[ { "name": "Docs", "type": "FileCollection" }, { "name": "Docs2", "type": "FileCollection" } ]
					"""),
				"the process with two FileCollections");

			// Act
			CallToolResult callResult = await ModifyAsync(context, processName, """
				[ { "op": "addMapping", "mapping": { "targetProcessParameter": "Docs2", "processParameter": "Docs" } } ]
				""");

			// Assert
			ShouldHaveLanded(callResult, "a FileCollection mapped onto another is an ordinary edit");
			string warnings = WarningText(callResult);
			warnings.Should().Contain("Docs2.File", because: "the notice names the file item the binder paired");
			warnings.Should().Contain("Docs.File", because: "and the source item it was paired with");
			warnings.Should().Contain("now reads", because: "the notice says the item was bound, not merely found");
			JsonObject graph = await DescribeGraphAsync(context, processName);
			JsonObject docs = ProcessParameter(graph, "Docs");
			JsonObject docs2 = ProcessParameter(graph, "Docs2");
			ShouldBeBoundTo(docs2, UId(docs), "the mapping's own target is bound to the source collection");
			ShouldBeBoundTo(Item(docs2, "File"), UId(Item(docs, "File")),
				"P2 pairs the target's file item with the source's, so every row carries its file");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("R-M2 over the wire: addMapping of a Read data record collection (ResultCompositeObjectList) onto a FileCollection is refused - the source has no File item, so every row would reach the file item with no file. The type check alone would accept it, which is why the refusal exists; the refused edit leaves the FileCollection unbound.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses a record collection into a FileCollection")]
	public async Task ModifyBusinessProcess_Should_RefuseARecordCollectionIntoAFileCollection() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("RecordsIntoFiles");
			await ArrangeProcessAsync(context, created, processName,
				BuildReadDataDescriptor(processName, """[ { "name": "Docs", "type": "FileCollection" } ]"""),
				"the process with a FileCollection and a collection Read data");

			// Act
			CallToolResult callResult = await ModifyAsync(context, processName, """
				[ { "op": "addMapping", "mapping": { "targetProcessParameter": "Docs",
				    "sourceElement": "ReadData1", "sourceElementParameter": "ResultCompositeObjectList" } } ]
				""");

			// Assert
			ShouldBeRefused(callResult, "a collection without a File item cannot fill a collection that takes files");
			string log = LogText(callResult);
			log.Should().Contain("has no File item", because: "the refusal says what the source lacks");
			log.Should().Contain("ReadData1.ResultCompositeObjectList", because: "the refusal names the source it refused");
			ShouldBeUnbound(ProcessParameter(await DescribeGraphAsync(context, processName), "Docs"),
				"the refused addMapping leaves the FileCollection unbound");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("R-M1 over the wire: addMapping of a collection item (Docs.File) onto a single File is refused - outside a row of that collection the item reads empty at run time - and the refusal points at the two shapes that do work: a FileCollection, or a multi-instance sub-process. The single File stays unbound.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses a collection item into a single File")]
	public async Task ModifyBusinessProcess_Should_RefuseACollectionItemIntoASingleFile() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("ItemIntoFile");
			await ArrangeProcessAsync(context, created, processName,
				BuildPlainDescriptor(processName, """
					[ { "name": "Doc", "type": "File" }, { "name": "Docs", "type": "FileCollection" } ]
					"""),
				"the process with a File and a FileCollection");

			// Act
			CallToolResult callResult = await ModifyAsync(context, processName, """
				[ { "op": "addMapping", "mapping": { "targetProcessParameter": "Doc", "processParameter": "Docs.File" } } ]
				""");

			// Assert
			ShouldBeRefused(callResult, "an item of a collection read outside its row would deliver no file");
			string log = LogText(callResult);
			log.Should().Contain("single File", because: "the refusal names the target shape that cannot take an item");
			log.Should().Contain(FileCollectionType, because: "the refusal points at the collection that can");
			log.Should().Contain("multi-instance", because: "and at the multi-instance sub-process that iterates the rows");
			ShouldBeUnbound(ProcessParameter(await DescribeGraphAsync(context, processName), "Doc"),
				"the refused addMapping leaves the single File unbound");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("addParameter with a typeFromElement mirror whose source is a DOTTED path - an item of a Read data collection output - is refused naming the item and its collection, instead of silently copying the item's type into an unbound parameter. The refused edit adds no parameter.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses a dotted mirror source")]
	public async Task ModifyBusinessProcess_Should_RefuseADottedMirrorSource() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("DottedMirror");
			await ArrangeProcessAsync(context, created, processName, BuildReadDataDescriptor(processName, "[]"),
				"the process with a collection Read data");

			// Act
			CallToolResult callResult = await ModifyAsync(context, processName, """
				[ { "op": "addParameter", "parameter": { "name": "P", "typeFromElement": "ReadData1",
				    "typeFromElementParameter": "ResultCompositeObjectList.Name" } } ]
				""");

			// Assert
			ShouldBeRefused(callResult, "a mirror copies a whole parameter, never one item of a collection");
			string log = LogText(callResult);
			log.Should().Contain("is an item of collection", because: "the refusal says what the dotted source is");
			log.Should().Contain("ResultCompositeObjectList", because: "the refusal names the collection to mirror instead");
			log.Should().Contain("ReadData1", because: "the refusal names the element the collection is on");
			ParameterNames(await DescribeGraphAsync(context, processName)).Should().NotContain("P",
				because: "a refused addParameter adds nothing");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("describe-business-process decodes an element-output source into names: on the multi-instance caller iterated over Read data rows, InputRecordCollection reports sourceElement ReadData1 and sourceElementParameter ResultCompositeObjectList, and its item ContactId reports sourceElementParameter ResultCompositeObjectList.Id - the dotted path that feeds straight back into addMapping. Neither carries a sourceColumn: they read a parameter, not a column of a record.")]
	[AllureTag(DescribeToolName)]
	[AllureName("describe-business-process decodes an element output source")]
	public async Task DescribeBusinessProcess_Should_DecodeAnElementOutputSource() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string calleeName = NewProcessName("DecodeCallee");
			string callerName = NewProcessName("DecodeCaller");
			await ArrangeProcessAsync(context, created, calleeName,
				BuildPlainDescriptor(calleeName, ContactAndFileCalleeJson), "the called process with Guid and File");
			await ArrangeProcessAsync(context, created, callerName, BuildReadDataCallerDescriptor(callerName, calleeName),
				"the multi-instance caller over Read data rows");

			// Act
			JsonObject graph = await DescribeGraphAsync(context, callerName);

			// Assert
			JsonObject input = ElementParameter(Element(graph, "SubProcess1"), "InputRecordCollection");
			Text(input, "sourceElement").Should().Be("ReadData1",
				because: "describe names the element whose output the input collection reads");
			Text(input, "sourceElementParameter").Should().Be("ResultCompositeObjectList",
				because: "and the output parameter itself");
			Text(input, "sourceColumn").Should().BeNull(
				because: "a whole collection is a parameter source, not a column of a record");
			JsonObject contactId = Item(input, "ContactId");
			Text(contactId, "sourceElement").Should().Be("ReadData1",
				because: "the item's source is an item of the same element's output");
			Text(contactId, "sourceElementParameter").Should().Be("ResultCompositeObjectList.Id",
				because: "an item source is named by its dotted path, the form addMapping takes back");
			Text(contactId, "sourceColumn").Should().BeNull(
				because: "an item of a collection is a parameter source, not a column of a record");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("P3's multi-instance exemption over the wire: the caller's InputRecordCollection is already bound to its FileCollection Docs, and addMapping of InputRecordCollection.F from a single File process parameter Doc - not a collection item, so P1 does not fire - must NOT reset that binding, because P3 applies to a single-instance owner only and a multi-instance input is what the element iterates. The edit lands with no binder notice, the input collection stays bound to Docs and its item F is bound to Doc.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process binds only the item when a plain File feeds a multi-instance input")]
	public async Task ModifyBusinessProcess_Should_BindOnlyTheItem_WhenAPlainFileFeedsAMultiInstanceInput() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string calleeName = NewProcessName("PlainCallee");
			string callerName = NewProcessName("PlainCaller");
			await ArrangeProcessAsync(context, created, calleeName, BuildPlainDescriptor(calleeName, FileCalleeJson),
				"the called process with a File parameter");
			await ArrangeProcessAsync(context, created, callerName,
				BuildMultiInstanceCallerDescriptor(callerName, calleeName,
					"""[ { "name": "Doc", "type": "File" }, { "name": "Docs", "type": "FileCollection" } ]""",
					"""[ { "elementName": "SubProcess1", "elementParameter": "InputRecordCollection", "processParameter": "Docs" } ]"""),
				"the multi-instance caller whose input collection is bound to Docs");

			// Act
			CallToolResult callResult = await ModifyAsync(context, callerName, """
				[ { "op": "addMapping", "mapping": { "elementName": "SubProcess1",
				    "elementParameter": "InputRecordCollection.F", "processParameter": "Doc" } } ]
				""");

			// Assert
			ShouldHaveLanded(callResult, "a single File into one item of a multi-instance input is an ordinary edit");
			string warnings = WarningText(callResult);
			warnings.Should().NotContain("will be empty on every iteration",
				because: "this edit binds the file item, so the P2-MI notice has nothing to report");
			warnings.Should().NotContain("now reads", because: "no file item is paired: P2 applies to a target that takes files");
			warnings.Should().NotContain("was cleared because",
				because: "no parent is reset: P3 exempts a multi-instance owner, whose input is what it iterates");
			JsonObject graph = await DescribeGraphAsync(context, callerName);
			JsonObject input = ElementParameter(Element(graph, "SubProcess1"), "InputRecordCollection");
			ShouldBeBoundTo(input, UId(ProcessParameter(graph, "Docs")),
				"the input collection keeps the binding it had: a single file onto a multi-instance item resets nothing");
			ShouldBeBoundTo(Item(input, "F"), UId(ProcessParameter(graph, "Doc")),
				"the item takes the mapping it was given");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Descriptors

	// The three parameters every declaration case starts from: one File, a FileCollection left to its default
	// direction, and a FileCollection with an explicit In.
	private const string FileParametersJson = """
		[
		  { "name": "Doc", "type": "File" },
		  { "name": "Docs", "type": "FileCollection" },
		  { "name": "InDocs", "type": "FileCollection", "direction": "In" }
		]
		""";

	// A callee taking one File: converted to multi-instance, it becomes the input collection's item F.
	private const string FileCalleeJson = """[ { "name": "F", "type": "File", "direction": "In" } ]""";

	// A callee taking a Guid and a File.
	private const string ContactAndFileCalleeJson = """
		[
		  { "name": "ContactId", "type": "Guid", "direction": "In" },
		  { "name": "F", "type": "File", "direction": "In" }
		]
		""";

	// A process with only a start and an end, carrying the given process parameters.
	private static string BuildPlainDescriptor(string processName, string parametersJson) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP File parameter E2E",
		  "packageName": "Custom",
		  "parameters": {{parametersJson}},
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [ { "source": "StartEvent1", "target": "EndEvent1" } ]
		}
		""";

	// A Read data element in COLLECTION mode, unmapped: its ResultCompositeObjectList is a record collection with a
	// Name item and no File item.
	private static string BuildReadDataDescriptor(string processName, string parametersJson) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP File parameter Read data E2E",
		  "packageName": "Custom",
		  "parameters": {{parametersJson}},
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "ReadData1", "type": "readData", "caption": "Read contacts",
		      "readData": { "source": "Contact", "mode": "collection", "columns": ["Name"], "numberOfRecords": 3 } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "ReadData1" },
		    { "source": "ReadData1", "target": "EndEvent1" }
		  ]
		}
		""";

	// A caller whose multi-instance sub-process element runs the callee, with no Read data: what it iterates comes from the given
	// process parameters through the given mappings.
	private static string BuildMultiInstanceCallerDescriptor(string processName, string calleeName,
			string parametersJson, string mappingsJson) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP File parameter multi-instance caller E2E",
		  "packageName": "Custom",
		  "parameters": {{parametersJson}},
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "SubProcess1", "type": "subProcess", "caption": "Call the callee once per row",
		      "subProcess": { "processName": "{{calleeName}}", "multiInstanceOptions": { "enabled": true } } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "SubProcess1" },
		    { "source": "SubProcess1", "target": "EndEvent1" }
		  ],
		  "mappings": {{mappingsJson}}
		}
		""";

	// The multi-instance caller iterated over Read data rows: the collection itself, and ONE per-item value through
	// a dotted path on both sides. Id is listed among the columns on purpose - whether an unlisted primary column
	// becomes an item of ResultCompositeObjectList is not established.
	private static string BuildReadDataCallerDescriptor(string processName, string calleeName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP File parameter Read data caller E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "ReadData1", "type": "readData", "caption": "Read contacts",
		      "readData": { "source": "Contact", "mode": "collection", "columns": ["Id", "Name"], "numberOfRecords": 3 } },
		    { "name": "SubProcess1", "type": "subProcess", "caption": "Call the callee once per contact",
		      "subProcess": { "processName": "{{calleeName}}", "multiInstanceOptions": { "enabled": true } } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "ReadData1" },
		    { "source": "ReadData1", "target": "SubProcess1" },
		    { "source": "SubProcess1", "target": "EndEvent1" }
		  ],
		  "mappings": [
		    { "elementName": "SubProcess1", "elementParameter": "InputRecordCollection",
		      "sourceElement": "ReadData1", "sourceElementParameter": "ResultCompositeObjectList" },
		    { "elementName": "SubProcess1", "elementParameter": "InputRecordCollection.ContactId",
		      "sourceElement": "ReadData1", "sourceElementParameter": "ResultCompositeObjectList.Id" }
		  ]
		}
		""";

	#endregion

	#region Methods: Arrange and cleanup

	/// <summary>Opens a session, or ignores the test when the stand is missing, unreachable or behind the cut.</summary>
	private static Task<ProcessDesignerArrangeContext> StartAsync() =>
		ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion, TimeSpan.FromMinutes(5));

	/// <summary>A process code unique to this run, in the <c>UsrClioBpFile&lt;Case&gt;E2e</c> family.</summary>
	private static string NewProcessName(string caseName) => $"UsrClioBpFile{caseName}E2e{Guid.NewGuid():N}";

	/// <summary>
	/// Builds a process, recording its name for cleanup BEFORE the call: a build the server saved but whose answer
	/// never arrived (a cancelled session, a deadline) must still be removed, and the name is unique to this run, so
	/// deleting a name that was never built only logs "Could not delete". Ignores the test when clio refused the
	/// call because the stand's CrtProcessBuilder is behind.
	/// </summary>
	private static async Task<CallToolResult> CreateAsync(ProcessDesignerArrangeContext context,
			List<string> created, string processName, string descriptor) {
		created.Add(processName);
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = descriptor
			});
		IgnoreWhenProcessBuilderIsBehind(ToolText(result), MinimumPackageVersion);
		return result;
	}

	/// <summary>
	/// Builds a process for an ARRANGE step and fails the test on the spot unless it was created: a refused build
	/// comes back as an ordinary result with a non-zero exit code, never as <c>IsError</c>.
	/// </summary>
	private static async Task ArrangeProcessAsync(ProcessDesignerArrangeContext context, List<string> created,
			string processName, string descriptor, string what) {
		CallToolResult result = await CreateAsync(context, created, processName, descriptor);
		ShouldHaveBuilt(result, $"{what} is an arrange step, so every assertion below is about the wrong failure "
			+ "if it was not built");
	}

	private static async Task<CallToolResult> ModifyAsync(ProcessDesignerArrangeContext context, string processName,
			string operations) =>
		await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = operations
		});

	/// <summary>Reads a process back in full (<c>mode=full</c>) and returns its graph.</summary>
	private static async Task<JsonObject> DescribeGraphAsync(ProcessDesignerArrangeContext context,
			string processName) =>
		DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName));

	/// <summary>
	/// Removes every process a test built, newest first so a caller goes before its callee. Called from finally:
	/// <see cref="ProcessDesignerE2ESupport.DeleteProcessAsync"/> reports a failed delete instead of throwing, so the
	/// assertion failure the test was already reporting is not replaced.
	/// </summary>
	private static async Task DeleteProcessesAsync(ProcessDesignerArrangeContext context, List<string> created) {
		for (int index = created.Count - 1; index >= 0; index--) {
			await DeleteProcessAsync(context.EnvironmentName, created[index]);
		}
	}

	#endregion

	#region Methods: Graph reading and assertions

	/// <summary>
	/// A described File: type FileLocator - never the BLOB type the platform also calls "File" - and the
	/// <paramref name="direction"/> expected.
	/// </summary>
	private static void ShouldBeASingleFile(JsonObject parameter, string direction) {
		string name = Text(parameter, "name") ?? string.Empty;
		Text(parameter, "type").Should().Be(FileLocatorType,
			because: $"'{name}' is a File, carried by reference to its stored content");
		Text(parameter, "direction").Should().Be(direction,
			because: $"'{name}' keeps the direction it was declared with, Variable when none was given");
		Objects(parameter["itemProperties"]).Should().BeEmpty(because: $"'{name}' is one file, not a collection");
	}

	/// <summary>
	/// A described FileCollection: type FileCollection, the <paramref name="direction"/> expected, no tag and exactly
	/// one item, File, of type FileLocator.
	/// </summary>
	private static void ShouldBeAFileCollection(JsonObject parameter, string direction) {
		string name = Text(parameter, "name") ?? string.Empty;
		Text(parameter, "type").Should().Be(FileCollectionType,
			because: $"'{name}' has the FileCollection shape, and describe reports it by the type that rebuilds it");
		Text(parameter, "direction").Should().Be(direction,
			because: $"'{name}' keeps the direction it was declared with, Variable when none was given");
		Text(parameter, "tag").Should().BeNull(
			because: $"'{name}' was declared, not mirrored from an element, so it carries no provenance tag");
		IReadOnlyList<JsonObject> items = Objects(parameter["itemProperties"]);
		items.Should().HaveCount(1, because: $"'{name}' carries exactly one item, the file of each row");
		Text(items[0], "name").Should().Be("File", because: "the item of a declared FileCollection is named File");
		Text(items[0], "type").Should().Be(FileLocatorType, because: "the item is a file reference");
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

	/// <summary>A parameter with no binding in effect: source None, or no text - what the binder treats as unbound.</summary>
	private static void ShouldBeUnbound(JsonObject parameter, string because) {
		string? source = Text(parameter, "source");
		bool bound = source != null && source != "None" && !string.IsNullOrWhiteSpace(Text(parameter, "value"));
		bound.Should().BeFalse(because: Escape(because) + "; the parameter read: {0}", parameter.ToJsonString());
	}

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

	/// <summary>The described uid of a parameter or item, failing the test when describe reports none.</summary>
	private static string UId(JsonObject parameter) {
		string? uid = Text(parameter, "uid");
		Guid.TryParse(uid, out _).Should().BeTrue(
			because: $"a binding is checked by the source's UId, so '{Text(parameter, "name")}' must report one");
		return uid!;
	}

	/// <summary>The metapath segment that names the parameter with <paramref name="parameterUId"/>.</summary>
	private static string ParameterSegment(string parameterUId) =>
		"[Parameter:{" + Guid.Parse(parameterUId).ToString("D") + "}]";

	/// <summary>The objects of a JSON array, or none when the member is absent or not an array.</summary>
	private static IReadOnlyList<JsonObject> Objects(JsonNode? node) =>
		node is JsonArray array ? array.OfType<JsonObject>().ToList() : [];

	/// <summary>Name, type, direction and item shape of the described process parameters, for the round trip.</summary>
	private static IReadOnlyList<ParameterShape> Shapes(IEnumerable<JsonObject> parameters) =>
		parameters.Select(parameter => new ParameterShape(
				Text(parameter, "name"),
				Text(parameter, "type"),
				Text(parameter, "direction"),
				string.Join(",", Objects(parameter["itemProperties"])
					.Select(item => $"{Text(item, "name")}:{Text(item, "type")}"))))
			.ToList();

	/// <summary>What the round trip compares of one described parameter.</summary>
	private sealed record ParameterShape(string? Name, string? Type, string? Direction, string Items);

	#endregion

}

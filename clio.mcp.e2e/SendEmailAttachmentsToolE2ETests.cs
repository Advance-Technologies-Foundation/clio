using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
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
using static Clio.Mcp.E2E.Support.Mcp.ProcessDesignerStoredReads;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the attachments of the Send email element (<c>email.attachments</c>) over the real MCP
/// path. NOT in CI - run manually against a stand carrying CrtProcessBuilder 1.6.6.127 or later, one framework at a
/// time (<c>-f net10.0</c>, then <c>-f net8.0</c>).
/// <para>What only this level can show is the wire and the platform: the list travels as JSON members a WCF
/// contract drops in silence when they are misspelled, each entry becomes a designer attachment row the platform's
/// pre-save gate accepts, the stored bytes and resource rows are what the designer itself writes, and describe
/// reads the list back from a real saved schema. Every unit suite constructs these in C#.</para>
/// <para>Two cases need stand fixtures and are Ignored, with the build instructions, without them: an MS Word
/// printable on Invoice (read with list-printables), and the designer-built process
/// <c>UsrSeAttachmentsDesignerFixture</c>. Every other case builds its own processes and removes them, newest
/// first, in <c>finally</c>. Strictly sequential: schema writes in a parallel burst take a .NET Framework stand's app
/// pool down.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class SendEmailAttachmentsToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;
	private const string NewVersionToolName = ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName;

	/// <summary>The create/modify floor: 1.6.6.127 is the first archive that reads <c>email.attachments</c>.</summary>
	private const string MinimumPackageVersion = "1.6.6.127";

	/// <summary>What the skip message calls these tests.</summary>
	private const string Subject = "Send email attachments";

	/// <summary>The designer-built fixture of TC-I-09.</summary>
	private const string DesignerFixtureName = "UsrSeAttachmentsDesignerFixture";

	/// <summary>The object whose MS Word printable the Generated report cases use.</summary>
	private const string InvoiceObject = "Invoice";

	/// <summary>The feature the run time checks for the executing user before it attaches anything.</summary>
	private const string AttachmentsFeatureCode = "UseProcessEmailAttachments";

	/// <summary><c>CompositeObjectList</c>, the data value type of every attachment slot.</summary>
	private const string CompositeObjectListTypeUId = "651ec16f-d140-46db-b9e2-825c985a8ac2";

	/// <summary><c>FileLocator</c>, the data value type of every attachment slot's file item.</summary>
	private const string FileLocatorTypeUId = "a33c9252-d401-453e-949d-169157067ed9";

	/// <summary>The Canceled activity status, which ends the instance parked on a manual Send email.</summary>
	private const string CanceledActivityStatusId = "201cfba8-58e6-df11-971b-001d60e938c6";

	/// <summary>The notice of a single File row (<c>SendEmailAttachmentNotices</c>).</summary>
	private const string SingleFileNotice = "attaches the single file in";

	/// <summary>The phrase of every removal notice of the replace rule.</summary>
	private const string RemovalNotice = "removed attachment";

	/// <summary>The member every attachment refusal and notice names.</summary>
	private const string AttachmentsMember = "email.attachments";

	/// <summary>The DataService data value type of a Boolean parameter.</summary>
	private const int BooleanDataValueType = 12;

	/// <summary>The DataService data value type of a Lookup value, written as the referenced record's id.</summary>
	private const int LookupDataValueType = 10;

	/// <summary>The SelectQuery comparison type CONTAIN, a substring match on a text column.</summary>
	private const int ContainComparisonType = 11;

	/// <summary>An id as the stored metadata writes it: the dashed form, or 32 hex digits without separators.</summary>
	private static readonly Regex GuidPattern = new(
		@"\b(?:[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|[0-9a-fA-F]{32})\b",
		RegexOptions.CultureInvariant);

	#region Methods: Tests - create and describe

	[Test]
	[Description("TC-I-01: one create builds a Send email element (manual, custom message) with four attachment rows - an Object attachments element's ObjectFiles, a Process parameter element's ObjectFiles, the process FileCollection Docs and the process File Doc - warns about the single File, and describe reads the four entries back in the write shape with slots Attachments1..4 and bindingLevels both x3 + itemOnly; each stored slot is a CompositeObjectList owned by the process and the element with one FileLocator item, and the resource rows carry the designer's captions.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process stores every attachment in the designer's shape and describe reads it back")]
	public async Task CreateBusinessProcess_Should_StoreEveryAttachmentInTheDesignersShape_AndDescribeItBack() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Shape");
			string descriptor = LinearDescriptor(processName, FileParametersJson,
				("OF1", ObjectFilesElementJson("OF1")), ("PF1", ProcessParameterFilesElementJson("PF1")),
				("SE1", SendEmailJson("SE1", "manual", FourAttachmentsJson)));

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName, descriptor);

			// Assert
			ShouldHaveBuilt(callResult, "four attachment rows from four kinds of file source are an ordinary create");
			WarningText(callResult).Should().Contain(SingleFileNotice,
				because: "a single File row fails the element when the File is empty at run time, and the build says so");
			JsonObject graph = await DescribeGraphAsync(context, processName);
			IReadOnlyList<JsonObject> attachments = Attachments(Element(graph, "SE1"));
			attachments.Select(entry => Text(entry, "slot")).Should().Equal(
				["Attachments1", "Attachments2", "Attachments3", "Attachments4"],
				because: "each entry becomes one row, numbered from 1 in list order on a fresh element");
			attachments.Select(entry => Text(entry, "bindingLevels")).Should().Equal(["both", "both", "both", "itemOnly"],
				because: "a collection binds the slot and its file item, a single File binds the item only");
			ShouldNameElementOutput(attachments[0], "OF1", "ObjectFiles");
			ShouldNameElementOutput(attachments[1], "PF1", "ObjectFiles");
			Text(attachments[2], "processParameter").Should().Be("Docs",
				because: "a process FileCollection reads back as the parameter it names");
			Text(attachments[3], "processParameter").Should().Be("Doc",
				because: "a process File reads back as the parameter it names");
			string processUId = Text(graph, "schemaUId")!;
			string elementUId = UId(Element(graph, "SE1"));
			IReadOnlyList<JsonObject> slots = StoredSlots(
				await ReadLastElementNodeAsync(context, processName, elementUId));
			slots.Should().HaveCount(4, because: "the stored element carries exactly one parameter per attachment row");
			foreach (JsonObject slot in slots) {
				ShouldBeASlotOf(slot, processUId, elementUId);
			}
			IReadOnlyList<string> resources = await ReadElementResourceRowsAsync(context, processName, "SE1");
			resources.Should().Contain(row => row.Contains("Parameters.Attachments1.Caption") && row.EndsWith("| Attachments collection"),
				because: "the slot carries the designer's fixed caption in the active culture");
			resources.Should().Contain(row => row.Contains("Attachments1FileLocator.Caption") && row.EndsWith("| Attachment"),
				because: "the file item carries the designer's fixed caption in the active culture");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-02: a Generated report element's ReportFiles feed a Send email element: the create lands, describe reads the entry back as sourceElement RF1 + sourceElementParameter ReportFiles with both levels bound, and the stored slot has the designer's shape. Ignored without an MS Word printable on Invoice.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process attaches a generated report")]
	public async Task CreateBusinessProcess_Should_AttachAGeneratedReport() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		ListPrintableRow printable = await ReadPrintableAsync(context);
		List<string> created = [];
		try {
			string processName = NewProcessName("Report");
			string descriptor = LinearDescriptor(processName, InvoiceParametersJson,
				("RF1", ReportElementJson("RF1", printable.TemplateId)),
				("SE1", SendEmailJson("SE1", "manual",
					"""[ { "sourceElement": "RF1", "sourceElementParameter": "ReportFiles" } ]""")));

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName, descriptor);

			// Assert
			ShouldHaveBuilt(callResult, "a generated report is an ordinary attachment source");
			JsonObject graph = await DescribeGraphAsync(context, processName);
			JsonObject entry = Attachments(Element(graph, "SE1")).Should().ContainSingle(
				because: "one entry was sent").Which;
			ShouldNameElementOutput(entry, "RF1", "ReportFiles");
			Text(entry, "bindingLevels").Should().Be("both", because: "the report collection and its file item are bound");
			JsonObject slot = StoredSlots(await ReadLastElementNodeAsync(context, processName,
				UId(Element(graph, "SE1")))).Should().ContainSingle(because: "one row was built").Which;
			ShouldBeASlotOf(slot, Text(graph, "schemaUId")!, UId(Element(graph, "SE1")));
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-03: one Object attachments collection feeds four Send email elements in every mode combination - manual + custom, manual + template, auto + custom with no sender (the build does not require one), auto + template (a template the test creates) - and every element stores an equal slot, and the Process file element stores the same node as in a twin process built without the emails; both are compared with every id a save generates replaced by what it names.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process feeds four Send email elements from one collection in every mode combination")]
	public async Task CreateBusinessProcess_Should_FeedFourSendEmailElementsFromOneCollection_InEveryModeCombination() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		E2EEmailTemplate template = await EmailTemplateE2EArrange.CreateAsync(context.Session, context.EnvironmentName,
			objectSchemaName: null, context.CancellationTokenSource.Token);
		List<string> created = [];
		try {
			string processName = NewProcessName("Modes");
			string twinName = NewProcessName("Twin");
			const string attachments = """[ { "sourceElement": "OF1", "sourceElementParameter": "ObjectFiles" } ]""";
			string descriptor = LinearDescriptor(processName, FileParametersJson,
				("OF1", ObjectFilesElementJson("OF1")),
				("SE1", SendEmailJson("SE1", "manual", attachments)),
				("SE2", TemplateSendEmailJson("SE2", "manual", template.Name, attachments)),
				("SE3", SendEmailJson("SE3", "auto", attachments)),
				("SE4", TemplateSendEmailJson("SE4", "auto", template.Name, attachments)));
			await ArrangeProcessAsync(context, created, twinName,
				LinearDescriptor(twinName, FileParametersJson, ("OF1", ObjectFilesElementJson("OF1"))), "the twin");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName, descriptor);

			// Assert
			ShouldHaveBuilt(callResult, "the slot is the same in all four mode combinations, so all four build");
			JsonObject graph = await DescribeGraphAsync(context, processName);
			List<(string Json, JsonObject Graph, string OwnElementUId)> slots = [];
			foreach (string name in new[] { "SE1", "SE2", "SE3", "SE4" }) {
				string elementUId = UId(Element(graph, name));
				JsonObject slot = StoredSlots(await ReadLastElementNodeAsync(context, processName, elementUId))
					.Should().ContainSingle(because: $"{name} was sent one entry").Which;
				slots.Add((slot.ToJsonString(), graph, elementUId));
			}
			IReadOnlyList<string> shapes = Comparable([.. slots]);
			shapes.Distinct().Should().ContainSingle(
				because: "the mode and the message source do not change the slot; the shapes read: {0}",
				string.Join(" || ", shapes));
			JsonObject twin = await DescribeGraphAsync(context, twinName);
			string ownFileUId = UId(Element(graph, "OF1"));
			string twinFileUId = UId(Element(twin, "OF1"));
			IReadOnlyList<string> files = Comparable(
				(await ReadLastElementNodeAsync(context, processName, ownFileUId), graph, ownFileUId),
				(await ReadLastElementNodeAsync(context, twinName, twinFileUId), twin, twinFileUId));
			files[0].Should().Be(files[1],
				because: "feeding emails must not change the Process file element that supplies the files");
		} finally {
			try {
				await DeleteProcessesAsync(context, created);
			} finally {
				await EmailTemplateE2EArrange.DeleteAsync(context.Session, context.EnvironmentName, template);
			}
		}
	}

	// The expected fragments are the package's own wording: the planner's refusals name the entry
	// ('email.attachments[0]') and the source; the forward-reference refusal names the entry and the later element;
	// the unscoped Object attachments refusal is judged at the end of the request over the whole schema, so it names
	// the source element and the email that sends its files (Send email 'SE1' (email.attachments)), not the entry.
	[TestCase("{}", new[] { "Send email element 'SE1': 'email.attachments[0]' names no source" })]
	[TestCase("""{ "sourceElement": "Nobody", "sourceElementParameter": "ObjectFiles" }""",
		new[] { "'email.attachments[0]'", "'Nobody'" })]
	[TestCase("""{ "sourceElement": "RD", "sourceElementParameter": "ResultCompositeObjectList" }""",
		new[] { "'email.attachments[0]' 'RD.ResultCompositeObjectList'", "no File item" })]
	[TestCase("""{ "sourceElement": "OFLater", "sourceElementParameter": "ObjectFiles" }""",
		new[] { "through 'email.attachments[0]'", "'OFLater'", "declared LATER" })]
	[TestCase("""{ "sourceElement": "OFUnscoped", "sourceElementParameter": "ObjectFiles" }""",
		new[] { "Process file 'OFUnscoped'", "no record scope and no filter condition",
			"Send email 'SE1' (email.attachments) sends them outside Creatio" })]
	[Description("TC-I-04: an attachment whose source holds no files is refused and nothing is created - an entry with no source, an unknown element, a Read data collection with no file item, a source declared after the Send email element, and an Object attachments source with no record scope and no filter condition, which is judged at the end of the request; each refusal says what the package says for that case, and describe of the process name answers that it was not found.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process refuses an attachment without files and creates nothing")]
	public async Task CreateBusinessProcess_Should_RefuseAnAttachmentWithoutFiles_AndCreateNothing(string entry,
			string[] expectedFragments) {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Refuse");
			List<(string Name, string Json)> elements = [
				("OF1", ObjectFilesElementJson("OF1")),
				("RD", """{ "name": "RD", "type": "readData", "caption": "Read contacts", "readData": { "source": "Contact", "mode": "collection", "columns": ["Name"], "numberOfRecords": 3 } }"""),
				("OFUnscoped", """{ "name": "OFUnscoped", "type": "fileProcessing", "caption": "Unscoped files", "fileProcessing": { "attachments": { "object": "Contact" } } }"""),
				("SE1", SendEmailJson("SE1", "manual", $"[ {entry} ]")),
				("OFLater", ObjectFilesElementJson("OFLater"))
			];
			string descriptor = LinearDescriptor(processName, FileParametersJson, [.. elements], "[]");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName, descriptor);

			// Assert
			ShouldBeRefused(callResult, "an attachment that can hold no files is refused before anything is saved");
			string log = LogText(callResult);
			foreach (string fragment in expectedFragments) {
				log.Should().Contain(fragment,
					because: "the refusal says which entry or source it refused and why, in the package's own wording");
			}
			CallToolResult described = await ProcessDesignerE2EArrange.DescribeAsync(context, processName);
			JsonSerializer.Serialize(described).Should().Contain("was not found",
				because: "a refused build creates nothing, so describing it finds no process");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Tests - modify

	[Test]
	[Description("TC-I-05: the replace rule over the wire. A Send email element built with three entries is sent [entry 1, a new source]: slot 1 keeps its stored UIds, slots 2 and 3 are gone and their resource rows with them, the new entry is Attachments2, and the warnings carry each removed entry in the write shape. A setElement that drops a collection entry and adds a single File reuses the freed number with no stale display value. A setElement with [] leaves no Attachments parameter, and a subject-only setElement afterwards changes nothing else on the element.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process replaces and then removes the attachments of a Send email element")]
	public async Task ModifyBusinessProcess_Should_ReplaceThenRemoveAttachments() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Replace");
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, FileParametersJson,
				("OF1", ObjectFilesElementJson("OF1")), ("PF1", ProcessParameterFilesElementJson("PF1")),
				("SE1", SendEmailJson("SE1", "manual", """
					[ { "sourceElement": "OF1", "sourceElementParameter": "ObjectFiles" },
					  { "sourceElement": "PF1", "sourceElementParameter": "ObjectFiles" },
					  { "processParameter": "Docs" } ]
					"""))), "the three-row process");
			JsonObject before = await DescribeGraphAsync(context, processName);
			string elementUId = UId(Element(before, "SE1"));
			JsonObject keptBefore = StoredSlot(await ReadLastElementNodeAsync(context, processName, elementUId),
				"Attachments1");

			// Act
			CallToolResult replaced = await ModifyAsync(context, processName, SetAttachments("SE1", """
				[ { "sourceElement": "OF1", "sourceElementParameter": "ObjectFiles" }, { "processParameter": "Doc" } ]
				"""));
			JsonObject afterReplace = await DescribeGraphAsync(context, processName);
			string replaceNode = await ReadLastElementNodeAsync(context, processName, elementUId);
			IReadOnlyList<string> replaceResources = await ReadElementResourceRowsAsync(context, processName, "SE1");
			CallToolResult reused = await ModifyAsync(context, processName, SetAttachments("SE1", """
				[ { "processParameter": "Doc" }, { "processParameter": "Doc" } ]
				"""));
			JsonObject afterReuse = await DescribeGraphAsync(context, processName);
			IReadOnlyList<string> reuseResources = await ReadElementResourceRowsAsync(context, processName, "SE1");
			CallToolResult cleared = await ModifyAsync(context, processName, SetAttachments("SE1", "[]"));
			string clearedNode = await ReadLastElementNodeAsync(context, processName, elementUId);
			CallToolResult subjectOnly = await ModifyAsync(context, processName,
				"""[ { "op": "setElement", "elementName": "SE1", "elementUpdate": { "email": { "subject": "Only the subject" } } } ]""");
			string subjectNode = await ReadLastElementNodeAsync(context, processName, elementUId);

			// Assert
			ShouldHaveLanded(replaced, "a list that keeps one source and adds another is an ordinary replace");
			Attachments(Element(afterReplace, "SE1")).Select(entry => Text(entry, "slot")).Should().Equal(
				["Attachments1", "Attachments2"],
				because: "the kept row keeps its name and the new one takes the lowest free number");
			JsonObject keptAfter = StoredSlot(replaceNode, "Attachments1");
			Text(keptAfter, "UId").Should().Be(Text(keptBefore, "UId"),
				because: "a row the list still names is kept as it is, not rebuilt with new UIds");
			StoredSlots(replaceNode).Select(slot => Text(slot, "A2")).Should().BeEquivalentTo(
				["Attachments1", "Attachments2"], because: "the rows the list no longer names are removed");
			replaceResources.Should().NotContain(row => row.Contains(".Attachments3"),
				because: "a removed row's resource keys go with it");
			WarningText(replaced).Should().Contain(RemovalNotice,
				because: "the replace rule says which rows it removed");
			WarningText(replaced).Should().Contain("\"sourceElement\": \"PF1\"",
				because: "the removal notice carries the removed entry in the write shape, so one call restores it");
			ShouldHaveLanded(reused, "dropping a collection row and adding a single File reuses the freed number");
			JsonObject reusedSlot = Attachments(Element(afterReuse, "SE1")).Should().ContainSingle(
				entry => Text(entry, "slot") == "Attachments1",
				because: "the collection row that held Attachments1 was dropped, so its number is free again").Which;
			Text(reusedSlot, "processParameter").Should().Be("Doc",
				because: "the re-added single File takes the freed lowest number, Attachments1");
			Text(reusedSlot, "bindingLevels").Should().Be("itemOnly",
				because: "the slot now holds a single File, which binds its item only");
			reuseResources.Should().NotContain(row => row.Contains("Parameters.Attachments1.DisplayValue") && row.Contains("[#OF1"),
				because: "a reused single-File slot carries no stale display value of the collection it replaced");
			ShouldHaveLanded(cleared, "an empty list removes every row");
			StoredSlots(clearedNode).Should().BeEmpty(because: "[] removes every attachment row from the element");
			Attachments(Element(await DescribeGraphAsync(context, processName), "SE1"), allowAbsent: true)
				.Should().BeEmpty(because: "an element with no row reports no attachments");
			ShouldHaveLanded(subjectOnly, "a subject-only edit is an ordinary in-place update");
			WithoutSubject(subjectNode).Should().Be(WithoutSubject(clearedNode),
				because: "a subject-only setElement must leave the element's other stored bytes unchanged");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-06: describe, then send the described entries back VERBATIM in a setElement - read-back members included, which clio warns about and does not refuse - and the Send email element's stored node and its resource rows are byte-identical before and after.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process changes no stored byte when the described list is sent back")]
	public async Task ModifyBusinessProcess_Should_ChangeNoStoredByte_WhenTheDescribedListIsSentBack() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Echo");
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, FileParametersJson,
				("OF1", ObjectFilesElementJson("OF1")), ("PF1", ProcessParameterFilesElementJson("PF1")),
				("SE1", SendEmailJson("SE1", "manual", FourAttachmentsJson))), "the four-row process");
			JsonObject graph = await DescribeGraphAsync(context, processName);
			string elementUId = UId(Element(graph, "SE1"));
			string described = new JsonArray([.. Attachments(Element(graph, "SE1")).Select(entry => entry.DeepClone())])
				.ToJsonString();
			string nodeBefore = await ReadLastElementNodeAsync(context, processName, elementUId);
			IReadOnlyList<string> resourcesBefore = await ReadElementResourceRowsAsync(context, processName, "SE1");

			// Act
			CallToolResult echoed = await ModifyAsync(context, processName, SetAttachments("SE1", described));

			// Assert
			ShouldHaveLanded(echoed, "a described list fed back is a valid list");
			WarningText(echoed).Should().Contain("read-back fields",
				because: "clio names the describe-only members it ignored instead of refusing them");
			(await ReadLastElementNodeAsync(context, processName, elementUId)).Should().Be(nodeBefore,
				because: "every entry matches its own row, so the replace keeps every row as it is");
			(await ReadElementResourceRowsAsync(context, processName, "SE1")).Should().Equal(resourcesBefore,
				because: "no row was rebuilt, so no resource row changed");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-07: a raw addMapping onto an attachment slot (SE1.Attachments1) is refused naming email.attachments, the block that owns the slot, and the stored element is unchanged.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses a raw mapping onto an attachment")]
	public async Task ModifyBusinessProcess_Should_RefuseARawMappingOntoAnAttachment() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Raw");
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, FileParametersJson,
				("OF1", ObjectFilesElementJson("OF1")),
				("SE1", SendEmailJson("SE1", "manual", """[ { "sourceElement": "OF1", "sourceElementParameter": "ObjectFiles" } ]"""))),
				"the one-row process");
			string elementUId = UId(Element(await DescribeGraphAsync(context, processName), "SE1"));
			string nodeBefore = await ReadLastElementNodeAsync(context, processName, elementUId);

			// Act
			CallToolResult refused = await ModifyAsync(context, processName, """
				[ { "op": "addMapping", "mapping": { "elementName": "SE1", "elementParameter": "Attachments1",
				    "processParameter": "Docs" } } ]
				""");

			// Assert
			ShouldBeRefused(refused, "a slot is written only through its block");
			LogText(refused).Should().Contain(AttachmentsMember, because: "the refusal names the route that writes a slot");
			(await ReadLastElementNodeAsync(context, processName, elementUId)).Should().Be(nodeBefore,
				because: "a refused edit saves nothing");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-08: modify-business-process-as-new-version clones a process with attachments, and the clone's slots are owned by the NEW version on both levels (A3, A4 and GS5 carry the new schema's UId, not the source's), while describe of the new version reports the same entries. The version is deleted in finally.")]
	[AllureTag(NewVersionToolName)]
	[AllureName("modify-business-process-as-new-version keeps the attachments owned by the new version")]
	public async Task ModifyProcessAsNewVersion_Should_KeepTheAttachmentsOwnedByTheNewVersion() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Version");
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, FileParametersJson,
				("OF1", ObjectFilesElementJson("OF1")),
				("SE1", SendEmailJson("SE1", "manual", """
					[ { "sourceElement": "OF1", "sourceElementParameter": "ObjectFiles" }, { "processParameter": "Doc" } ]
					"""))), "the source process");
			IReadOnlyList<string> sourceEntries = EntryShapes(Attachments(Element(
				await DescribeGraphAsync(context, processName), "SE1")));

			// Act
			CallToolResult saved = await ProcessDesignerE2EArrange.CallToolAsync(context, NewVersionToolName,
				new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = processName,
					["package-name"] = "Custom",
					["operations"] = """[ { "op": "setElement", "elementName": "SE1", "elementUpdate": { "email": { "subject": "Version" } } } ]"""
				});
			IgnoreWhenProcessBuilderIsBehind(ToolText(saved), MinimumPackageVersion);
			// The platform composes the version's name, so it cannot be registered before the call; it is registered
			// as soon as the response names it, before any assertion on the response can throw past the cleanup.
			if (ProcessDesignerE2EArrange.TryReadCreatedVersionName(saved, out string createdVersion)) {
				created.Add(createdVersion);
			}
			ShouldHaveLanded(saved, "a version of a process with attachments is an ordinary clone");
			string versionName = ProcessDesignerE2EArrange.CreatedVersionName(saved);
			JsonObject version = await DescribeGraphAsync(context, versionName);
			string versionUId = Text(version, "schemaUId")!;
			string versionElementUId = UId(Element(version, "SE1"));
			IReadOnlyList<JsonObject> slots = StoredSlots(
				await ReadLastElementNodeAsync(context, versionName, versionElementUId));

			// Assert
			slots.Should().HaveCount(2, because: "the clone carries every row of the source");
			foreach (JsonObject slot in slots) {
				ShouldBeASlotOf(slot, versionUId, versionElementUId);
			}
			EntryShapes(Attachments(Element(version, "SE1"))).Should().Equal(sourceEntries,
				because: "describe of the version reports the entries the source had");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Tests - designer fixture and run time

	[Test]
	[Description("TC-I-09: read-only on the designer-built fixture UsrSeAttachmentsDesignerFixture: five entries decoded in the write shape across SE1 and SE2 - SE1's rows including the stale-parent row (a single File item under a collection-bound slot) with its issue - and SE2's entries in automatic mode with a template. Nothing is written. Ignored, with the build instructions, when the stand lacks the fixture.")]
	[AllureTag(DescribeProcessTool.ToolName)]
	[AllureName("describe-business-process reads the designer-built attachments fixture")]
	public async Task DescribeBusinessProcess_Should_ReadTheDesignerBuiltAttachmentsFixture() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		await IgnoreUnlessDesignerFixtureExistsAsync(context);

		// Act
		JsonObject graph = await DescribeGraphAsync(context, DesignerFixtureName);

		// Assert
		IReadOnlyList<JsonObject> first = Attachments(Element(graph, "SE1"));
		IReadOnlyList<JsonObject> second = Attachments(Element(graph, "SE2"));
		(first.Count + second.Count).Should().Be(5, because: "the fixture carries four rows on SE1 and one on SE2");
		first.Should().OnlyContain(entry => !string.IsNullOrEmpty(Text(entry, "slot")),
			because: "every described row names the slot it was read from");
		first.Should().Contain(entry => Strings(entry["issues"]).Any(),
			because: "the stale-parent row attaches one copy of its File per row of the old collection, which describe reports");
		Text(Group(Element(graph, "SE2"), "email"), "mode").Should().Be("auto",
			because: "SE2 is the automatic, template-mode element of the fixture");
		Text(Group(Element(graph, "SE2"), "email"), "messageSource").Should().Be("template",
			because: "SE2 sends a template");
	}

	[Test]
	[Description("TC-I-10: at run time a manual Send email element puts a generated report on its draft email activity: RF1 generates a Word report of an existing invoice, the run parks on SE1, and the created Activity carries one ActivityFile with a size and a .docx name. The activity and its files are found by the token the email subject carries; the activity is canceled (which ends the parked instance), and the files, the activity and the process are removed in finally. Ignored without an MS Word printable on Invoice, without an invoice, with the attachments feature off, or without McpE2E:AllowDestructiveMcpTests.")]
	[AllureTag(RunProcessTool.ToolName)]
	[AllureName("run-process puts the generated report on the manual email activity")]
	public async Task RunProcess_Should_PutTheGeneratedReportOnTheManualEmailActivity() {
		// Arrange
		if (!TestConfiguration.Load().AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run the run-process attachments case.");
		}
		await using ProcessDesignerArrangeContext context = await StartAsync();
		ListPrintableRow printable = await ReadPrintableAsync(context);
		string invoiceId = await ReadFirstRecordIdAsync(context, InvoiceObject);
		await IgnoreUnlessAttachmentsFeatureIsOnAsync(context);
		List<string> created = [];
		string token = $"ClioSeAttach{Guid.NewGuid():N}";
		try {
			string processName = NewProcessName("Run");
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, InvoiceParametersJson,
				("RF1", ReportElementJson("RF1", printable.TemplateId)),
				("SE1", SendEmailJson("SE1", "manual",
					"""[ { "sourceElement": "RF1", "sourceElementParameter": "ReportFiles" } ]""", token))),
				"the report-to-email process");

			// Act
			RunProcessEnvelope run = await RunAsync(context, processName, invoiceId);

			// Assert
			run.Error.Should().BeNull(because: "a run parked on a manual email is not a failure: {0}", run.Error);
			run.WaitingOn.Should().Contain(element => element.Type != null,
				because: "the run parks on the manual Send email until its activity is completed");
			// The activity is found by the token its subject carries, not by run.Created: the watch is a before/after
			// diff of the whole table, so it also counts rows other users or a mailbox synchronization wrote meanwhile.
			JsonElement activity = (await SelectInlineAsync(context, ActivitiesTitledWith(token),
					"the run's email activity")).EnumerateArray()
				.Should().ContainSingle(because: "manual mode creates one draft email activity, titled with the subject")
				.Which;
			string activityId = TextOf(activity, "Id");
			(run.Created ?? []).Should().Contain(record => record.Entity == "Activity"
					&& string.Equals(record.Id, activityId, StringComparison.OrdinalIgnoreCase),
				because: "the run-process watch reports the activity this run created");
			JsonElement file = (await SelectInlineAsync(context, FilesOfActivity(activityId),
					"the copied ActivityFile row")).EnumerateArray()
				.Should().ContainSingle(because: "the generated report is copied onto the draft activity").Which;
			int.Parse(TextOf(file, "Size")).Should().BePositive(because: "the generated report is not empty");
			TextOf(file, "Name").Should().EndWith(".docx", because: "an MS Word printable generates a Word file");
		} finally {
			try {
				await CleanUpRunAsync(context, token);
			} finally {
				await DeleteProcessesAsync(context, created);
			}
		}
	}

	#endregion

	#region Methods: Descriptors

	// The process parameters most cases read: a Contact by Lookup, a FileCollection and a single File.
	private const string FileParametersJson = """
		[
		  { "name": "ContactRef", "type": "Lookup", "referenceSchema": "Contact", "direction": "In" },
		  { "name": "Docs", "type": "FileCollection", "direction": "In" },
		  { "name": "Doc", "type": "File", "direction": "In" }
		]
		""";

	// The invoice a Generated report is made for.
	private const string InvoiceParametersJson = """
		[ { "name": "InvoiceRef", "type": "Lookup", "referenceSchema": "Invoice", "direction": "In" } ]
		""";

	// The four source kinds of TC-I-01, in the order their slots are numbered.
	private const string FourAttachmentsJson = """
		[ { "sourceElement": "OF1", "sourceElementParameter": "ObjectFiles" },
		  { "sourceElement": "PF1", "sourceElementParameter": "ObjectFiles" },
		  { "processParameter": "Docs" },
		  { "processParameter": "Doc" } ]
		""";

	/// <summary>An Object attachments element reading one contact's files, handed to the following elements.</summary>
	private static string ObjectFilesElementJson(string name) =>
		$$"""
		{ "name": "{{name}}", "type": "fileProcessing", "caption": "{{name}}",
		  "fileProcessing": { "attachments": { "object": "Contact", "recordId": { "processParameter": "ContactRef" } } } }
		""";

	/// <summary>A Process parameter element copying Docs onto the contact; its ObjectFiles output is the copies.</summary>
	private static string ProcessParameterFilesElementJson(string name) =>
		$$"""
		{ "name": "{{name}}", "type": "fileProcessing", "caption": "{{name}}",
		  "fileProcessing": { "files": { "processParameter": "Docs" },
		    "saveTo": { "object": "Contact", "recordId": { "processParameter": "ContactRef" } } } }
		""";

	/// <summary>A Generated report element making one Word report of the invoice InvoiceRef names, used in the process.</summary>
	private static string ReportElementJson(string name, string printableId) =>
		$$"""
		{ "name": "{{name}}", "type": "fileProcessing", "caption": "{{name}}",
		  "fileProcessing": { "report": { "printable": "{{printableId}}", "recordId": { "processParameter": "InvoiceRef" } } } }
		""";

	/// <summary>A custom-message Send email element in the given mode with the given attachments list.</summary>
	private static string SendEmailJson(string name, string mode, string attachmentsJson, string subject = "Clio e2e attachments") =>
		$$"""
		{ "name": "{{name}}", "type": "sendEmail", "caption": "{{name}}",
		  "email": { "mode": "{{mode}}", "subject": "{{subject}}", "body": "<p>Files attached.</p>",
		    "to": [ { "value": "probe@example.com" } ], "attachments": {{attachmentsJson}} } }
		""";

	/// <summary>A template-message Send email element in the given mode with the given attachments list.</summary>
	private static string TemplateSendEmailJson(string name, string mode, string templateName, string attachmentsJson) =>
		$$"""
		{ "name": "{{name}}", "type": "sendEmail", "caption": "{{name}}",
		  "email": { "mode": "{{mode}}", "messageSource": "template", "template": "{{templateName}}",
		    "to": [ { "value": "probe@example.com" } ], "attachments": {{attachmentsJson}} } }
		""";

	/// <summary>One setElement operation sending <paramref name="attachmentsJson"/> as the element's list.</summary>
	private static string SetAttachments(string elementName, string attachmentsJson) =>
		$$"""[ { "op": "setElement", "elementName": "{{elementName}}", "elementUpdate": { "email": { "attachments": {{attachmentsJson}} } } } ]""";

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
			  "caption": "Clio BP Send email attachments E2E",
			  "packageName": "Custom",
			  "parameters": {{parametersJson}},
			  "elements": [ {{elementsJson}} ],
			  "flows": [ {{flowsJson}} ],
			  "mappings": {{mappingsJson}}
			}
			""";
	}

	#endregion

	#region Methods: Arrange and cleanup

	/// <summary>Opens a session, or ignores the test when the stand is missing, unreachable or behind the cut.</summary>
	private static Task<ProcessDesignerArrangeContext> StartAsync() =>
		ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion, TimeSpan.FromMinutes(8));

	/// <summary>A process code unique to this run, in the <c>UsrClioBpSe&lt;Case&gt;E2e</c> family.</summary>
	private static string NewProcessName(string caseName) => $"UsrClioBpSe{caseName}E2e{Guid.NewGuid():N}";

	/// <summary>
	/// Builds a process, recording its name for cleanup BEFORE the call: a build the server saved but whose answer
	/// never arrived must still be removed. Ignores the test when clio refused the call because the stand's
	/// CrtProcessBuilder is behind.
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

	/// <summary>Builds a process for an ARRANGE step and fails the test on the spot unless it was created.</summary>
	private static async Task ArrangeProcessAsync(ProcessDesignerArrangeContext context, List<string> created,
			string processName, string descriptor, string what) {
		CallToolResult result = await CreateAsync(context, created, processName, descriptor);
		ShouldHaveBuilt(result, $"{what} is an arrange step, so every assertion below is about the wrong failure "
			+ "if it was not built");
	}

	private static async Task<CallToolResult> ModifyAsync(ProcessDesignerArrangeContext context, string processName,
			string operations) {
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = operations
			});
		IgnoreWhenProcessBuilderIsBehind(ToolText(result), MinimumPackageVersion);
		return result;
	}

	/// <summary>Reads a process back in full (<c>mode=full</c>) and returns its graph.</summary>
	private static async Task<JsonObject> DescribeGraphAsync(ProcessDesignerArrangeContext context,
			string processName) =>
		DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName));

	/// <summary>Removes every process a test built, newest first; a failed delete is reported, not thrown.</summary>
	private static async Task DeleteProcessesAsync(ProcessDesignerArrangeContext context, List<string> created) {
		for (int index = created.Count - 1; index >= 0; index--) {
			await DeleteProcessAsync(context.EnvironmentName, created[index]);
		}
	}

	/// <summary>The MS Word printable of Invoice the report cases use, or Ignore with the setup step.</summary>
	private static async Task<ListPrintableRow> ReadPrintableAsync(ProcessDesignerArrangeContext context) {
		CallToolResult result = await context.Session.CallToolAsync(ListPrintablesTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["entity-name"] = InvoiceObject,
					["environment-name"] = context.EnvironmentName
				}
			},
			context.CancellationTokenSource.Token);
		ListPrintablesEnvelope envelope = EntitySchemaStructuredResultParser.Extract<ListPrintablesEnvelope>(result);
		envelope.Success.Should().BeTrue(because: "list-printables must answer for the case to pick a real printable; "
			+ "it failed with: {0}", envelope.Error);
		ListPrintableRow? row = (envelope.Printables ?? []).FirstOrDefault(candidate =>
			string.Equals(string.IsNullOrEmpty(candidate.EntitySchemaName)
				? candidate.ModuleEntitySchemaName : candidate.EntitySchemaName, InvoiceObject,
				StringComparison.OrdinalIgnoreCase));
		if (row == null) {
			Assert.Ignore($"list-printables has no printable of {InvoiceObject} on '{context.EnvironmentName}'; the "
				+ "Generated report cases need an MS Word printable (System Designer -> Report setup).");
		}
		return row!;
	}

	/// <summary>
	/// Ignores the test, with the build instructions, when the stand does not carry the designer-built fixture. Read
	/// from <c>SysSchema</c> by name, so a broken describe still fails instead of reading as an absent fixture.
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
			Assert.Ignore($"Designer fixture {DesignerFixtureName} not found on '{context.EnvironmentName}'. Build it in "
				+ "the classic designer: an Object attachments element OF1, a Generated report element RF1, a Process "
				+ "parameter element PF1, the process FileCollection Docs and File Doc, a manual custom-message Send "
				+ "email SE1 with rows OF1, RF1, Docs and Doc, and an automatic template-mode SE2 with RF1; save, clear "
				+ "SE1's RF1 row and save, add an OF1 row and save, re-bind SE1's first row to Doc and save.");
		}
	}

	/// <summary>
	/// Ignores the test when no administrative unit has the attachments feature switched on. An approximation of "on
	/// for the user who runs the process": a state granted to any unit is taken as the run user's, which is what a
	/// stand configured for this case carries.
	/// </summary>
	private static async Task IgnoreUnlessAttachmentsFeatureIsOnAsync(ProcessDesignerArrangeContext context) {
		JsonElement rows = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery("AdminUnitFeatureState",
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition("Feature.Code", AttachmentsFeatureCode,
					SelectQueryHelper.TextDataValueType),
				new SelectQueryHelper.SelectQueryFilterDefinition("FeatureState", true, BooleanDataValueType)
			], rowCount: 1), "the attachments feature state");
		if (rows.GetArrayLength() == 0) {
			Assert.Ignore($"Feature {AttachmentsFeatureCode} is not on for any user on '{context.EnvironmentName}', so "
				+ "the run time would send the email without its files. Switch it on for the user who runs the process.");
		}
	}

	/// <summary>The id of one record of <paramref name="schemaName"/>, or Ignore when the stand has none.</summary>
	private static async Task<string> ReadFirstRecordIdAsync(ProcessDesignerArrangeContext context, string schemaName) {
		JsonElement rows = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery(schemaName,
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")], [], rowCount: 1), $"one {schemaName} record");
		if (rows.GetArrayLength() == 0) {
			Assert.Ignore($"{Subject} MCP E2E needs at least one {schemaName} record on '{context.EnvironmentName}'.");
		}
		return rows[0].GetProperty("Id").GetString()!;
	}

	/// <summary>
	/// Runs the process through clio-run with a watch on the activity and its files and no cleanup: run-process deletes
	/// through OData DELETE, which a stand may refuse, so this test cleans up through execute-dataservice-batch.
	/// </summary>
	private static async Task<RunProcessEnvelope> RunAsync(ProcessDesignerArrangeContext context, string processName,
			string invoiceId) {
		CallToolResult result = await context.Session.CallToolAsync(ClioRunTool.ToolName,
			new Dictionary<string, object?> {
				["command"] = RunProcessTool.ToolName,
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = processName,
					["parameters"] = new Dictionary<string, object?> { ["InvoiceRef"] = invoiceId },
					["watch"] = new[] { "Activity", "ActivityFile" },
					["wait-timeout"] = 20,
					["cleanup"] = false
				}
			},
			context.CancellationTokenSource.Token);
		return EntitySchemaStructuredResultParser.Extract<RunProcessEnvelope>(result);
	}

	/// <summary>The Activity rows whose Title carries <paramref name="token"/>, the subject this test's email sent.</summary>
	private static object ActivitiesTitledWith(string token) =>
		SelectQueryHelper.BuildSelectQuery("Activity",
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition("Title", token, SelectQueryHelper.TextDataValueType,
					ContainComparisonType)
			], rowCount: 10);

	/// <summary>The ActivityFile rows of one activity, with the size and name the run-time case asserts on.</summary>
	private static object FilesOfActivity(string activityId) =>
		SelectQueryHelper.BuildSelectQuery("ActivityFile",
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Size", "Size"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Name", "Name")
			],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition("Activity.Id", activityId,
					SelectQueryHelper.GuidDataValueType)
			], rowCount: 10);

	/// <summary>
	/// Finds the email activity this run created by <paramref name="token"/> and that activity's files, each with a query
	/// of its own, cancels the activity (which ends the parked instance), then deletes the files before the activity.
	/// Called from finally, also when the run itself threw, which is why it reads the rows rather than the run's answer,
	/// and why it runs on a cancellation token of its own: the test's may be the one that ran out. Only rows the token
	/// identifies are touched - never another user's activity, nor one a mailbox synchronization imported. A failure is
	/// reported, not thrown.
	/// <para>What remains on the stand: the generated report is a temporary file of the process instance - the
	/// package's own notice says it lives as long as the instance, so ending the instance is its cleanup - and it
	/// carries no token to find it by; the instance's process log rows stay as well.</para>
	/// </summary>
	private static async Task CleanUpRunAsync(ProcessDesignerArrangeContext context, string token) {
		using CancellationTokenSource cleanup = new(TimeSpan.FromMinutes(2));
		foreach (string activityId in await FindIdsBestEffortAsync(context, ActivitiesTitledWith(token),
				$"the activities titled with {token}", cleanup.Token)) {
			IReadOnlyList<string> fileIds = await FindIdsBestEffortAsync(context, FilesOfActivity(activityId),
				$"the files of activity {activityId}", cleanup.Token);
			await BatchBestEffortAsync(context, new Dictionary<string, object?> {
				["operation"] = "update", ["schema-name"] = "Activity", ["record-id"] = activityId,
				["values"] = new Dictionary<string, object?> {
					["Status"] = new Dictionary<string, object?> {
						["data-value-type"] = LookupDataValueType, ["value"] = CanceledActivityStatusId
					}
				}
			}, $"cancel activity {activityId}", cleanup.Token);
			foreach (string fileId in fileIds) {
				await BatchBestEffortAsync(context, new Dictionary<string, object?> {
					["operation"] = "delete", ["schema-name"] = "ActivityFile", ["record-id"] = fileId
				}, $"delete ActivityFile {fileId}", cleanup.Token);
			}
			await BatchBestEffortAsync(context, new Dictionary<string, object?> {
				["operation"] = "delete", ["schema-name"] = "Activity", ["record-id"] = activityId
			}, $"delete Activity {activityId}", cleanup.Token);
		}
	}

	/// <summary>The ids a cleanup query returns; none, reported, when the read fails.</summary>
	private static async Task<IReadOnlyList<string>> FindIdsBestEffortAsync(ProcessDesignerArrangeContext context,
			object query, string what, CancellationToken cancellationToken) {
		try {
			CallToolResult call = await context.Session.CallToolAsync(ExecuteEsqTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = context.EnvironmentName,
						["query"] = JsonSerializer.SerializeToElement(query)
					}
				}, cancellationToken);
			ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(call);
			if (!response.Success || response.Rows is not { ValueKind: JsonValueKind.Array } rows) {
				await TestContext.Error.WriteLineAsync(
					$"Could not read {what}: {response.Error}; whatever they hold stays on the stand.");
				return [];
			}
			return [.. rows.EnumerateArray().Select(row => TextOf(row, "Id")).Where(id => id.Length > 0)];
		} catch (Exception exception) when (exception is not OutOfMemoryException) {
			// Reported, not the test's verdict, for the reason BatchBestEffortAsync gives.
			PayloadDumpReader.DeleteIfPresent(exception.Message);
			await TestContext.Error.WriteLineAsync(
				$"Could not read {what}; whatever they hold stays on the stand: {exception.Message}");
			return [];
		}
	}

	private static async Task BatchBestEffortAsync(ProcessDesignerArrangeContext context,
			Dictionary<string, object?> operation, string what, CancellationToken cancellationToken) {
		try {
			CallToolResult call = await context.Session.CallToolAsync(DataServiceBatchTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = context.EnvironmentName,
						["operations"] = new[] { operation }
					}
				}, cancellationToken);
			DataServiceBatchResult result = EntitySchemaStructuredResultParser.Extract<DataServiceBatchResult>(call);
			if (!result.Success) {
				await TestContext.Error.WriteLineAsync($"Could not {what}; it stays on the stand.");
			}
		} catch (Exception exception) when (exception is not OutOfMemoryException) {
			// A cleanup failure is reported, not the test's verdict, so a parse failure's payload dump would only land in
			// the published TestResults artifact of a passing run.
			PayloadDumpReader.DeleteIfPresent(exception.Message);
			await TestContext.Error.WriteLineAsync($"Could not {what}; it stays on the stand: {exception.Message}");
		}
	}

	#endregion

	#region Methods: Graph reading and slot assertions

	/// <summary>The described attachments of a Send email element; absent fails the test unless allowed.</summary>
	private static IReadOnlyList<JsonObject> Attachments(JsonObject element, bool allowAbsent = false) {
		JsonObject email = Group(element, "email");
		if (email["attachments"] is not JsonArray list) {
			allowAbsent.Should().BeTrue(because: "the element was sent attachments, so describe must report them; what "
				+ "was read: {0}", email.ToJsonString());
			return [];
		}
		return list.OfType<JsonObject>().ToList();
	}

	/// <summary>An entry decoded as an element's output, in the write shape.</summary>
	private static void ShouldNameElementOutput(JsonObject entry, string element, string parameter) {
		Text(entry, "sourceElement").Should().Be(element, because: "the entry is decoded back to the element it reads");
		Text(entry, "sourceElementParameter").Should().Be(parameter, because: "and to that element's file output");
	}

	/// <summary>The write members of each entry, the shape a round trip compares.</summary>
	private static IReadOnlyList<string> EntryShapes(IReadOnlyList<JsonObject> entries) =>
		entries.Select(entry => string.Join("|", Text(entry, "processParameter"), Text(entry, "sourceElement"),
			Text(entry, "sourceElementParameter"), Text(entry, "expression"))).ToList();

	/// <summary>The stored attachment slots of an element node: its <c>Attachments&lt;N&gt;</c> parameters.</summary>
	private static IReadOnlyList<JsonObject> StoredSlots(string rawNode) {
		JsonObject node = (JsonNode.Parse(rawNode) as JsonObject)!;
		return node["BP2"] is JsonArray parameters
			? parameters.OfType<JsonObject>()
				.Where(parameter => (Text(parameter, "A2") ?? string.Empty).StartsWith("Attachments", StringComparison.Ordinal)
					&& parameter["L18"] is JsonArray)
				.ToList()
			: [];
	}

	/// <summary>The stored slot with that name, failing the test with what was read when absent.</summary>
	private static JsonObject StoredSlot(string rawNode, string slotName) {
		JsonObject? slot = StoredSlots(rawNode).SingleOrDefault(candidate => Text(candidate, "A2") == slotName);
		slot.Should().NotBeNull(because: $"the slot '{slotName}' must be stored; what was read: {{0}}", rawNode);
		return slot!;
	}

	/// <summary>
	/// A slot in the designer's shape: a CompositeObjectList input owned by the process and the element, with one
	/// FileLocator item <c>&lt;slot&gt;FileLocator</c>, and every bound level a Script owned by the process.
	/// </summary>
	private static void ShouldBeASlotOf(JsonObject slot, string processUId, string elementUId) {
		string name = Text(slot, "A2")!;
		IsSame(slot["L1"], CompositeObjectListTypeUId).Should().BeTrue(
			because: $"'{name}' is a collection of files, as the designer stores it: {{0}}", slot.ToJsonString());
		slot["L12"]?.ToString().Should().Be("0", because: $"'{name}' is an input of the element");
		IsSame(slot["A3"], processUId).Should().BeTrue(because: $"'{name}' is owned by this process (A3)");
		IsSame(slot["A4"], processUId).Should().BeTrue(because: $"'{name}' is created in this process (A4)");
		IsSame(slot["IL2"], elementUId).Should().BeTrue(because: $"'{name}' belongs to its Send email element (IL2)");
		JsonObject item = (slot["L18"] as JsonArray)!.OfType<JsonObject>().Should().ContainSingle(
			because: $"'{name}' carries exactly one file item").Which;
		Text(item, "A2").Should().Be($"{name}FileLocator", because: "the item is named after its slot");
		IsSame(item["L1"], FileLocatorTypeUId).Should().BeTrue(
			because: "the item is a FileLocator for every source kind, as the designer stores it");
		IsSame(item["IL2"], elementUId).Should().BeTrue(because: "the item belongs to the ELEMENT, not to the slot");
		foreach (JsonObject level in new[] { slot, item }) {
			if (level["L8"] is JsonObject value && value["GS1"] is not null) {
				value["GS1"]!.ToString().Should().Be("3", because: "a bound level is a Script, never a Mapping");
				IsSame(value["GS5"], processUId).Should().BeTrue(because: "a bound level's value is owned by this process");
			}
		}
	}

	/// <summary>
	/// Stored JSON made comparable ACROSS saves. What is compared: the stored text with its <c>UId</c> and <c>BL3</c>
	/// members dropped, and every id in it replaced by what it names - the process by <c>&lt;process&gt;</c>, the
	/// element the JSON belongs to by <c>&lt;element&gt;</c>, every other element and every process parameter by its
	/// name as describe reports it, so a <c>[#...#]</c> binding to <c>ContactRef</c> compares as the binding to
	/// <c>ContactRef</c> in both processes. Of the ids left, one that EVERY compared text carries is a constant (a
	/// data value type, the object read, a source all of them bind) and stays as it is; any other was generated by
	/// its own save - a filter item key, an id of the element's own parameters - and is replaced by its order of
	/// first appearance. Two texts then compare equal when they have the same structure, the same constants and the
	/// same pattern of generated ids. What this cannot see: one non-shared id swapped for another at the same place,
	/// which no comparison of two saves can tell from a freshly generated one.
	/// </summary>
	private static IReadOnlyList<string> Comparable(params (string Json, JsonObject Graph, string OwnElementUId)[] stored) {
		List<string> named = [.. stored.Select(item => Named(item.Json, item.Graph, item.OwnElementUId))];
		HashSet<Guid>? shared = null;
		foreach (string text in named) {
			HashSet<Guid> ids = [.. GuidPattern.Matches(text).Select(match => Guid.Parse(match.Value))];
			if (shared is null) {
				shared = ids;
			} else {
				shared.IntersectWith(ids);
			}
		}
		HashSet<Guid> constants = shared ?? [];
		return [.. named.Select(text => GeneratedByOrder(text, constants))];
	}

	/// <summary>The JSON with <c>UId</c>/<c>BL3</c> dropped and every id describe can name replaced by that name.</summary>
	private static string Named(string rawJson, JsonObject graph, string ownElementUId) {
		JsonNode node = JsonNode.Parse(rawJson)!;
		Strip(node);
		Dictionary<Guid, string> names = [];
		foreach (JsonObject parameter in (graph["parameters"] as JsonArray)?.OfType<JsonObject>() ?? []) {
			AddName(names, Text(parameter, "uid"), $"<parameter:{Text(parameter, "name")}>");
		}
		foreach (JsonObject element in (graph["elements"] as JsonArray)?.OfType<JsonObject>() ?? []) {
			AddName(names, Text(element, "uid"), $"<element:{Text(element, "name")}>");
		}
		AddName(names, Text(graph, "schemaUId"), "<process>");
		AddName(names, ownElementUId, "<element>");
		return GuidPattern.Replace(node.ToJsonString(), match =>
			names.TryGetValue(Guid.Parse(match.Value), out string? name) ? name : match.Value);
	}

	private static void AddName(Dictionary<Guid, string> names, string? uid, string name) {
		if (Guid.TryParse(uid, out Guid parsed)) {
			names[parsed] = name;
		}
	}

	/// <summary>Every id <paramref name="constants"/> does not hold replaced by its order of first appearance.</summary>
	private static string GeneratedByOrder(string text, HashSet<Guid> constants) {
		Dictionary<Guid, int> order = [];
		return GuidPattern.Replace(text, match => {
			Guid id = Guid.Parse(match.Value);
			if (constants.Contains(id)) {
				return id.ToString("D");
			}
			if (!order.TryGetValue(id, out int position)) {
				position = order.Count + 1;
				order[id] = position;
			}
			return $"<generated{position}>";
		});
	}

	private static void Strip(JsonNode? node) {
		if (node is JsonObject obj) {
			obj.Remove("UId");
			obj.Remove("BL3");
			foreach (KeyValuePair<string, JsonNode?> pair in obj.ToList()) {
				Strip(pair.Value);
			}
		} else if (node is JsonArray array) {
			foreach (JsonNode? item in array) {
				Strip(item);
			}
		}
	}

	/// <summary>An element node without its Subject parameter, to compare everything a subject edit must not touch.</summary>
	private static string WithoutSubject(string rawNode) {
		JsonObject node = (JsonNode.Parse(rawNode) as JsonObject)!;
		node.Remove("BL3");
		if (node["BP2"] is JsonArray parameters) {
			foreach (JsonObject subject in parameters.OfType<JsonObject>().Where(parameter => Text(parameter, "A2") == "Subject").ToList()) {
				parameters.Remove(subject);
			}
		}
		return node.ToJsonString();
	}

	/// <summary>The described element with that name.</summary>
	private static JsonObject Element(JsonObject graph, string name) {
		JsonObject? found = (graph["elements"] as JsonArray)?.OfType<JsonObject>()
			.SingleOrDefault(candidate => Text(candidate, "name") == name);
		found.Should().NotBeNull(because: $"the element '{name}' must be described; what was read: {{0}}",
			graph.ToJsonString());
		return found!;
	}

	/// <summary>A nested object member, failing the test with what was read when it is absent.</summary>
	private static JsonObject Group(JsonObject owner, string key) {
		JsonObject? group = owner[key] as JsonObject;
		group.Should().NotBeNull(because: $"'{key}' must be described; what was read: {{0}}", owner.ToJsonString());
		return group!;
	}

	/// <summary>The described uid of an element, failing the test when describe reports none.</summary>
	private static string UId(JsonObject node) {
		string? uid = Text(node, "uid");
		Guid.TryParse(uid, out _).Should().BeTrue(
			because: $"a stored node is found by UId, so '{Text(node, "name")}' must report one");
		return uid!;
	}

	/// <summary>The strings of a JSON array, or none when the member is absent or not an array.</summary>
	private static IReadOnlyList<string> Strings(JsonNode? node) =>
		node is JsonArray array
			? array.OfType<JsonValue>()
				.Select(value => value.TryGetValue(out string? text) ? text : null)
				.OfType<string>()
				.ToList()
			: [];

	private static bool IsSame(JsonNode? value, string uid) =>
		value is JsonValue && Guid.TryParse(value.GetValue<string>(), out Guid parsed)
			&& Guid.TryParse(uid, out Guid expected) && parsed == expected;

	#endregion

}

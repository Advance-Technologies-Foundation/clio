using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
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
/// End-to-end coverage for the Generated report and Process parameter variants of the "Process file" element
/// (ENG-96506, story 2 of ENG-92719) over the real MCP path. NOT in CI - run manually against a stand carrying
/// CrtProcessBuilder 1.6.6.127 or later.
/// <para>What only this level can show: a printable is resolved against the stand's real <c>SysModuleReport</c> rows
/// (an MS Word printable of an object, three of them through their section's object), the report scope is compiled
/// by the real filter service, the <c>files</c> source is bound at both levels by the real mapping service, and
/// describe reads all of it back from a real saved schema - plus a read-only describe of the four shipped report
/// processes, which no unit suite can construct.</para>
/// <para>The printables are read with <c>list-printables</c>, never invented: every Generated report case is Ignored
/// when the stand has no MS Word printable of the object it needs. Every test builds its own processes and removes
/// them in <c>finally</c>, except the read-only describes of the product processes and of the designer-built fixture
/// <c>UsrFpOaDesignerFixture</c>, which are never modified. Strictly sequential: schema writes in a parallel burst take
/// a .NET Framework stand's app pool down.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ProcessFileReportAndParameterToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;
	private const string DescribeToolName = DescribeProcessTool.ToolName;

	/// <summary>The create/modify floor: 1.6.6.105 registered the Generated report and Process parameter variants and
	/// 1.6.6.106 to 1.6.6.109 carry their final-review, core-reuse and review fixes, and 1.6.6.127 is the current create/modify floor; named in the skip message.</summary>
	private const string MinimumPackageVersion = "1.6.6.127";

	/// <summary>The SysFile target floor: 1.6.6.111 writes SysFile storage; named in the skip message.</summary>
	private const string SysFileMinimumPackageVersion = "1.6.6.111";

	/// <summary>
	/// The cut whose notices judge a Process file element a request only READS, and whose describe calls an item-only
	/// Files.File a run-time failure (ENG-103934); a stand behind it ignores the tests that read either.
	/// </summary>
	private const string EndOfRequestNoticesMinimumPackageVersion = "1.6.6.123";

	/// <summary>What the skip message calls these tests.</summary>
	private const string Subject = "Process file element (Generated report, Process parameter)";

	/// <summary>The build type describe reports for a Process file element (<c>ElementTypes.FileProcessing</c>).</summary>
	private const string FileProcessingBuildType = "fileprocessing";

	/// <summary>The Generated report task schema, selected by the <c>report</c> group.</summary>
	private const string ReportTaskName = "ReportFileProcessingUserTask";

	/// <summary>The Process parameter task schema, selected by the <c>files</c> group.</summary>
	private const string ProcessTaskName = "ProcessFileProcessingUserTask";

	/// <summary>The Object attachments task schema, selected by the <c>attachments</c> group.</summary>
	private const string ObjectTaskName = "ObjectFileProcessingUserTask";

	/// <summary>The object the Invoice printable is generated for - through its section's object on the dev stand.</summary>
	private const string InvoiceObject = "Invoice";

	/// <summary>A text column of Invoice, what the file-name suffix column takes.</summary>
	private const string InvoiceTextColumn = "Number";

	/// <summary>A column of Invoice that is not text, which the file-name suffix column refuses.</summary>
	private const string InvoiceNonTextColumn = "Amount";

	/// <summary>The object of the "DNS Requirements" printable (its own <c>SysEntitySchema</c>).</summary>
	private const string DnsReportObject = "DNSGuideReport";

	/// <summary>The printable TC-I-74 generates; Ignored when the stand does not carry it.</summary>
	private const string DnsRequirementsCaption = "DNS Requirements";

	/// <summary>The designer-built fixture of TC-I-46, built once by hand in the designer and never run.</summary>
	private const string DesignerFixtureName = "UsrFpOaDesignerFixture";

	/// <summary>The fragment of the refusal of a printable this environment does not carry (D17, RP-3).</summary>
	private const string ReportSetupHint = "create it in System Designer -> Report setup";

	#region Methods: Tests - Generated report

	[Test]
	[Description("TC-I-52, TC-I-60: create-business-process builds a Generated report element on an MS Word printable of Invoice (its templateId from list-printables), generated for the record report.recordId names (a Lookup process parameter) with the file-name suffix column Number, and saved to that Invoice's attachments. Describe reads it back as build type fileprocessing on ReportFileProcessingUserTask with the report group: the printable, its describe-only caption, the derived type MS Word and object Invoice, the record scope lifted out of the filter and named by its parameter, separate reports written true when omitted, the suffix column, the outputs ReportFiles and CreatedObjectFileIds, and no several-records notice, since the scope selects one record.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process builds a Word report saved to the record it is generated for")]
	public async Task CreateBusinessProcess_Should_BuildAWordReport_SavedToTheRecordItIsGeneratedFor() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		ListPrintableRow printable = await ReadPrintableAsync(context, InvoiceObject, caption: null);
		List<string> created = [];
		try {
			string processName = NewProcessName("Report");
			string block = $$"""
				{ "report": { "printable": "{{printable.TemplateId}}", "recordId": { "processParameter": "InvoiceRef" },
				    "fileNameSuffixColumn": "{{InvoiceTextColumn}}" },
				  "saveTo": { "object": "{{InvoiceObject}}", "recordId": { "processParameter": "InvoiceRef" } } }
				""";

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson, ("RF1", FileElement("RF1", block))));

			// Assert
			ShouldHaveBuilt(callResult, "an MS Word printable of the record's object, scoped to one record, is an ordinary create");
			WarningText(callResult).Should().NotContain("every file is attached to that record",
				because: "report.recordId scopes the report to one record, so no several-records notice is due");
			JsonObject element = Element(await DescribeGraphAsync(context, processName), "RF1");
			Text(element, "buildType").Should().BeEquivalentTo(FileProcessingBuildType,
				because: "the Process file handler claims the Generated report task once its variant is registered");
			Text(element, "userTaskName").Should().Be(ReportTaskName, because: "the report group selects the Generated report task");
			JsonObject describedBlock = FileProcessingBlock(element);
			Text(describedBlock, "source").Should().Be("report", because: "the group present is the variant");
			Text(describedBlock, "action").Should().Be("saveToAttachments", because: "a saveTo infers the saving action on create");
			JsonObject report = Group(describedBlock, "report");
			ShouldNameThePrintable(report, printable.TemplateId);
			Text(report, "caption").Should().NotBeNullOrWhiteSpace(because: "describe adds the printable's caption, describe-only");
			Text(report, "type").Should().Be("MS Word", because: "the printable's type is derived and read back as an identity check");
			Text(report, "object").Should().Be(InvoiceObject,
				because: "the report object is the printable's own, or its section's when it has none");
			Text(Group(report, "recordId"), "processParameter").Should().Be("InvoiceRef",
				because: "the record scope is lifted out of the filter and named by the parameter it reads");
			Flag(report, "separateReports").Should().BeTrue(
				because: "an MS Word printable always generates one file per record, so an omitted value is written true");
			Text(report, "fileNameSuffixColumn").Should().Be(InvoiceTextColumn, because: "the suffix column sent is stored");
			Text(Group(Group(describedBlock, "saveTo"), "recordId"), "processParameter").Should().Be("InvoiceRef",
				because: "the files are saved to the record the report is generated for");
			OutputNames(describedBlock).Should().Contain(["ReportFiles.File", "CreatedObjectFileIds.Id"],
				because: "the outputs name what a downstream mapping reads");
			element["filter"].Should().BeNull(
				because: "the record scope is reported once, in report.recordId, so a described element resubmits without setting it twice");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("Extends TC-I-60: an explicit report.separateReports false on an MS Word printable is kept as sent, with the notice that the designer forces it on at its next save and the file names change then; describe reads separateReports false back and names it in an issue, so a described legacy element resubmits unchanged.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process keeps an explicit separateReports false with a notice")]
	public async Task CreateBusinessProcess_Should_KeepSeparateReportsFalse_WithANotice() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		ListPrintableRow printable = await ReadPrintableAsync(context, InvoiceObject, caption: null);
		List<string> created = [];
		try {
			string processName = NewProcessName("ReportOne");
			string block = $$"""
				{ "report": { "printable": "{{printable.TemplateId}}", "recordId": { "processParameter": "InvoiceRef" },
				    "separateReports": false },
				  "saveTo": { "object": "{{InvoiceObject}}", "recordId": { "processParameter": "InvoiceRef" } } }
				""";

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson, ("RF1", FileElement("RF1", block))));

			// Assert
			ShouldHaveBuilt(callResult, "an explicit separateReports false is accepted, not refused");
			string warnings = WarningText(callResult);
			warnings.Should().Contain("false is kept as sent", because: "the binder keeps the value the caller sent");
			warnings.Should().Contain("the designer forces this setting on", because: "and says what the designer does with it");
			JsonObject describedBlock = FileProcessingBlock(Element(await DescribeGraphAsync(context, processName), "RF1"));
			Flag(Group(describedBlock, "report"), "separateReports").Should().BeFalse(because: "the stored constant is read back as sent");
			Strings(describedBlock["issues"]).Should().Contain(
				issue => issue.StartsWith("'report.separateReports' is false on an MS Word printable", StringComparison.Ordinal),
				because: "describe names an MS Word element with separate reports off as a hazard, not some other issue");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("Extends TC-I-60 with the Generated report refusals, none of which builds anything: an unknown printable is refused with the Report setup hint ('The Process file element cannot create one') and the MS Word printables of the record's object by templateId; a file-name suffix column that is not text is refused, naming the text columns the report object has; and an MS Word element with no record scope and no filter condition is refused at the end of the request, because it would generate a report for every record of its object.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process refuses an unknown printable, a non-text suffix column and an unscoped report")]
	public async Task CreateBusinessProcess_Should_RefuseAnUnknownPrintable_ANonTextSuffixColumn_AndAnUnscopedReport() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		ListPrintableRow printable = await ReadPrintableAsync(context, InvoiceObject, caption: null);
		List<string> created = [];
		try {
			string unknown = Guid.NewGuid().ToString("D");
			string unknownBlock = $$"""
				{ "report": { "printable": "{{unknown}}", "recordId": { "processParameter": "InvoiceRef" } },
				  "saveTo": { "object": "{{InvoiceObject}}", "recordId": { "processParameter": "InvoiceRef" } } }
				""";
			string nonTextBlock = $$"""
				{ "report": { "printable": "{{printable.TemplateId}}", "recordId": { "processParameter": "InvoiceRef" },
				    "fileNameSuffixColumn": "{{InvoiceNonTextColumn}}" },
				  "saveTo": { "object": "{{InvoiceObject}}", "recordId": { "processParameter": "InvoiceRef" } } }
				""";
			string unscopedBlock = $$"""
				{ "report": { "printable": "{{printable.TemplateId}}" },
				  "saveTo": { "object": "{{InvoiceObject}}", "recordId": { "processParameter": "InvoiceRef" } } }
				""";
			string unknownName = NewProcessName("RepUnknown");
			string nonTextName = NewProcessName("RepColumn");
			string unscopedName = NewProcessName("RepAll");

			// Act
			CallToolResult unknownResult = await CreateAsync(context, created, unknownName,
				LinearDescriptor(unknownName, RecordRefParametersJson, ("RF1", FileElement("RF1", unknownBlock))));
			CallToolResult nonTextResult = await CreateAsync(context, created, nonTextName,
				LinearDescriptor(nonTextName, RecordRefParametersJson, ("RF1", FileElement("RF1", nonTextBlock))));
			CallToolResult unscopedResult = await CreateAsync(context, created, unscopedName,
				LinearDescriptor(unscopedName, RecordRefParametersJson, ("RF1", FileElement("RF1", unscopedBlock))));

			// Assert
			ShouldBeRefused(unknownResult, "an element on a printable this environment does not carry fails at run time");
			string unknownLog = LogText(unknownResult);
			unknownLog.Should().Contain(ReportSetupHint, because: "the refusal says where a printable is made");
			unknownLog.Should().Contain("The Process file element cannot create one",
				because: "the refusal says nothing was created or picked in its place");
			unknownLog.Should().ContainEquivalentOf(printable.TemplateId,
				because: "the refusal lists the MS Word printables of the record's object by templateId");
			unknownLog.Should().NotContain(CreatedLine, because: "a refused build saves nothing");
			ShouldBeRefused(nonTextResult, "the run time reads the suffix column as text");
			string nonTextLog = LogText(nonTextResult);
			nonTextLog.Should().Contain($"'report.fileNameSuffixColumn' '{InvoiceNonTextColumn}' is",
				because: "the refusal names the member and the column it refused");
			nonTextLog.Should().Contain("which is not one of the text types the designer offers", because: "the refusal says why the column cannot suffix a file name");
			nonTextLog.Should().Contain("Pass the code of one of its text columns",
				because: "and points to the report object's text columns");
			nonTextLog.Should().NotContain(CreatedLine, because: "a refused build saves nothing");
			ShouldBeRefused(unscopedResult, "an unscoped MS Word element generates a report for every record of its object");
			string unscopedLog = LogText(unscopedResult);
			unscopedLog.Should().Contain("generates a report for EVERY", because: "the refusal names the consequence");
			unscopedLog.Should().NotContain(CreatedLine, because: "the check runs before the pre-save gate, so nothing is saved");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-74: Add data -> generated report -> Delete data, built and described, NOT run. Add data creates a DNSGuideReport row, a Generated report element on the 'DNS Requirements' printable (its own object DNSGuideReport) is generated for that row through report.recordId <- AddData1.RecordId and uses its files in the process, and a Delete data element removes the row again. Describe reads the record scope back as the Add data element's output. Ignored when list-printables has no 'DNS Requirements' printable of DNSGuideReport.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process generates a report for the row an Add data element creates")]
	public async Task CreateBusinessProcess_Should_GenerateAReport_ForTheRowAddDataCreates() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		ListPrintableRow printable = await ReadPrintableAsync(context, DnsReportObject, DnsRequirementsCaption);
		List<string> created = [];
		try {
			string processName = NewProcessName("AddReport");
			const string addData = $$"""
				{ "name": "AddData1", "type": "addData", "caption": "Add the report data row",
				  "addData": { "source": "{{DnsReportObject}}", "values": [ { "column": "SiteUrl", "value": "clio-e2e.example" } ] } }
				""";
			string block = $$"""
				{ "report": { "printable": "{{printable.TemplateId}}",
				    "recordId": { "sourceElement": "AddData1", "sourceElementParameter": "RecordId" } } }
				""";
			const string deleteData = $$"""
				{ "name": "DeleteData1", "type": "deleteData", "caption": "Remove the report data row",
				  "deleteData": { "source": "{{DnsReportObject}}" },
				  "filter": { "object": "{{DnsReportObject}}", "logicalOperation": "and", "conditions": [
				    { "column": "Id", "comparison": "equal",
				      "elementParameter": { "elementName": "AddData1", "parameter": "RecordId" } } ] } }
				""";

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				LinearDescriptor(processName, "[]", ("AddData1", addData), ("RF1", FileElement("RF1", block)),
					("DeleteData1", deleteData)));

			// Assert
			ShouldHaveBuilt(callResult, "a report generated for the row Add data creates is the product's own recipe");
			JsonObject graph = await DescribeGraphAsync(context, processName);
			JsonObject describedBlock = FileProcessingBlock(Element(graph, "RF1"));
			Text(describedBlock, "action").Should().Be("useInProcess", because: "no saveTo infers use in process");
			JsonObject report = Group(describedBlock, "report");
			ShouldNameThePrintable(report, printable.TemplateId);
			Text(report, "object").Should().Be(DnsReportObject, because: "the DNS printables carry their own object");
			JsonObject recordId = Group(report, "recordId");
			Text(recordId, "sourceElement").Should().Be("AddData1", because: "the scope names the element whose row it reads");
			(Text(recordId, "sourceElementParameter") ?? string.Empty).Should().StartWith("RecordId",
				because: "and the id output of that element");
			Text(Element(graph, "DeleteData1"), "buildType").Should().NotBeNullOrEmpty(
				because: "the Delete data element that removes the row was built too");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Tests - Process parameter and chains

	[Test]
	[Description("TC-I-73 (Report -> Process): a Generated report element that uses its files in the process hands ReportFiles to a Process parameter element (files <- RF1.ReportFiles), which saves them to a Contact. Describe reads the report element as useInProcess with no saveTo, and the Process parameter element as build type fileprocessing on ProcessFileProcessingUserTask with source files, the element source, bindingLevels both and its outputs ObjectFiles / ObjectFile and CreatedObjectFileIds / Id; the raw Files input is bound to ReportFiles and Files.File to ReportFiles.File.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process chains a use-in-process report into a Process parameter element")]
	public async Task CreateBusinessProcess_Should_ChainAUseInProcessReport_IntoAProcessParameterElement() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		ListPrintableRow printable = await ReadPrintableAsync(context, InvoiceObject, caption: null);
		List<string> created = [];
		try {
			string processName = NewProcessName("RepChain");
			string reportBlock = $$"""
				{ "report": { "printable": "{{printable.TemplateId}}", "recordId": { "processParameter": "InvoiceRef" } } }
				""";

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson, ("RF1", FileElement("RF1", reportBlock)),
					("PF1", FileElement("PF1", FilesBlock("""{ "sourceElement": "RF1", "sourceElementParameter": "ReportFiles" }""")))));

			// Assert
			ShouldHaveBuilt(callResult, "a report used in the process is a legal file source for a Process parameter element");
			JsonObject graph = await DescribeGraphAsync(context, processName);
			JsonObject describedReportBlock = FileProcessingBlock(Element(graph, "RF1"));
			Text(describedReportBlock, "action").Should().Be("useInProcess", because: "no saveTo infers use in process");
			describedReportBlock["saveTo"].Should().BeNull(because: "an element that uses its files in the process saves them nowhere");
			JsonObject pf1 = Element(graph, "PF1");
			ShouldBeAProcessParameterElement(pf1);
			JsonObject filesBlock = FileProcessingBlock(pf1);
			JsonObject files = Group(filesBlock, "files");
			Text(files, "sourceElement").Should().Be("RF1", because: "the files source names the report element");
			Text(files, "sourceElementParameter").Should().Be("ReportFiles", because: "and its generated files");
			Text(filesBlock, "bindingLevels").Should().Be("both", because: "a collection binds Files and Files.File");
			JsonObject reportFiles = ElementParameter(Element(graph, "RF1"), "ReportFiles");
			JsonObject filesInput = ElementParameter(pf1, "Files");
			ShouldBeBoundTo(filesInput, UId(reportFiles), "Files takes its rows from RF1.ReportFiles");
			ShouldBeBoundTo(Item(filesInput, "File"), UId(Item(reportFiles, "File")),
				"and Files.File reads each row's file, ReportFiles.File");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("QA case over the wire: a modify that adds an Out FileCollection and maps it from a use-in-process report's ReportFiles - the report itself not touched - carries the notice that those generated files are temporary.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process warns that ReportFiles handed out of the process later are temporary")]
	public async Task ModifyBusinessProcess_Should_SayTheReportFilesAreTemporary_WhenALaterMappingHandsThemOut() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartEndOfRequestAsync();
		ListPrintableRow printable = await ReadPrintableAsync(context, InvoiceObject, caption: null);
		List<string> created = [];
		try {
			string processName = NewProcessName("RepLater");
			string reportBlock = $$"""
				{ "report": { "printable": "{{printable.TemplateId}}", "recordId": { "processParameter": "InvoiceRef" } } }
				""";
			await ArrangeProcessAsync(context, created, processName,
				LinearDescriptor(processName, RecordRefParametersJson, ("RF1", FileElement("RF1", reportBlock))),
				"the process with a report that uses its files in the process");

			// Act
			CallToolResult callResult = await ModifyAsync(context, processName, """
				[ { "op": "addParameter", "parameter": { "name": "Docs", "type": "FileCollection", "direction": "Out" } },
				  { "op": "addMapping", "mapping": { "targetProcessParameter": "Docs",
				    "sourceElement": "RF1", "sourceElementParameter": "ReportFiles" } } ]
				""");

			// Assert
			ShouldHaveLanded(callResult, "handing a report's files to a process output is an ordinary edit");
			WarningText(callResult).Should().Contain("these generated files are temporary",
				because: "the files the process now hands out are deleted when the instance that generated them ends");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("Describe of a Process parameter element whose Files.File alone is bound to the item of a FileCollection (written as files.expression) reports bindingLevels itemOnly with an issue that the element fails at run time, and never that it reads one file: measured, outside its collection's rows the item reads empty.")]
	[AllureTag(DescribeToolName)]
	[AllureName("describe-business-process reports an item-only Files.File as failing at run time")]
	public async Task DescribeBusinessProcess_Should_ReportAnItemOnlyFilesFile_AsFailingAtRunTime() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartEndOfRequestAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("ItemOnly");
			string parameters = """
				[ { "name": "ContactRef", "type": "Lookup", "referenceSchema": "Contact", "direction": "In" },
				  { "name": "Docs", "type": "FileCollection" } ]
				""";
			await ArrangeProcessAsync(context, created, processName,
				LinearDescriptor(processName, parameters,
					("PF1", FileElement("PF1", FilesBlock("""{ "processParameter": "Docs" }""")))),
				"the Process parameter element bound to Docs on both levels");
			string itemUId = UId(Item(ProcessParameter(await DescribeGraphAsync(context, processName), "Docs"), "File"));
			CallToolResult rebind = await ModifyAsync(context, processName, $$"""
				[ { "op": "setElement", "elementName": "PF1", "elementUpdate": { "fileProcessing":
				    { "files": { "expression": "[#[IsOwnerSchema:false].[IsSchema:false].{{ParameterSegment(itemUId)}}#]" } } } } ]
				""");
			ShouldHaveLanded(rebind, "PRECONDITION: an expression goes onto Files.File as given");

			// Act
			JsonObject block = FileProcessingBlock(Element(await DescribeGraphAsync(context, processName), "PF1"));

			// Assert
			Text(block, "bindingLevels").Should().Be("itemOnly", because: "only Files.File is bound");
			Strings(block["issues"]).Should().Contain(issue => issue.Contains("fails at run time", StringComparison.Ordinal),
				because: "the item reads empty outside its collection's rows and the element fails on the null file");
			Strings(block["issues"]).Should().NotContain(issue => issue.Contains("one file", StringComparison.Ordinal),
				because: "the element copies no file at all, so the issue must not say it copies one");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-73 (Object -> Process): an Object attachments element that uses its files in the process hands ObjectFiles to a Process parameter element (files <- OF1.ObjectFiles), which saves them to an Account. Describe reads the Process parameter element with the element source and bindingLevels both, and the raw Files input bound to ObjectFiles at both levels.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process chains Object attachments into a Process parameter element")]
	public async Task CreateBusinessProcess_Should_ChainObjectAttachments_IntoAProcessParameterElement() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("ObjChain");
			string filesBlock = """
				{ "files": { "sourceElement": "OF1", "sourceElementParameter": "ObjectFiles" },
				  "saveTo": { "object": "Account", "recordId": { "processParameter": "AccountRef" } } }
				""";

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				LinearDescriptor(processName, ContactAndAccountRefParametersJson, ("OF1", FileElement("OF1", UseInProcessObjectBlockJson)),
					("PF1", FileElement("PF1", filesBlock))));

			// Assert
			ShouldHaveBuilt(callResult, "an Object attachments element used in the process is a legal file source");
			JsonObject graph = await DescribeGraphAsync(context, processName);
			JsonObject pf1 = Element(graph, "PF1");
			ShouldBeAProcessParameterElement(pf1);
			JsonObject block = FileProcessingBlock(pf1);
			Text(Group(block, "files"), "sourceElementParameter").Should().Be("ObjectFiles",
				because: "the files source names the Object attachments element's output");
			Text(block, "bindingLevels").Should().Be("both", because: "a collection binds Files and Files.File");
			Text(Group(block, "saveTo"), "object").Should().Be("Account", because: "the copies are saved to the Account sent");
			JsonObject objectFiles = ElementParameter(Element(graph, "OF1"), "ObjectFiles");
			JsonObject filesInput = ElementParameter(pf1, "Files");
			ShouldBeBoundTo(filesInput, UId(objectFiles), "Files takes its rows from OF1.ObjectFiles");
			ShouldBeBoundTo(Item(filesInput, "File"), UId(Item(objectFiles, "File")),
				"and Files.File reads ObjectFiles.File, the item found by its type");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-75: two consumers of one output, the FileParameterProcess shape - two Process parameter elements read the same OF1.ObjectFiles, one saving to a Contact and one to an Account. Both are built, both read back the same element source with bindingLevels both, and their targets stay apart.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process feeds one file output into two Process parameter elements")]
	public async Task CreateBusinessProcess_Should_FeedOneFileOutput_IntoTwoProcessParameterElements() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("TwoPf");
			const string toContact = """
				{ "files": { "sourceElement": "OF1", "sourceElementParameter": "ObjectFiles" },
				  "saveTo": { "object": "Contact", "recordId": { "processParameter": "ContactRef" } } }
				""";
			const string toAccount = """
				{ "files": { "sourceElement": "OF1", "sourceElementParameter": "ObjectFiles" },
				  "saveTo": { "object": "Account", "recordId": { "processParameter": "AccountRef" } } }
				""";

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				LinearDescriptor(processName, ContactAndAccountRefParametersJson, ("OF1", FileElement("OF1", UseInProcessObjectBlockJson)),
					("PF1", FileElement("PF1", toContact)), ("PF2", FileElement("PF2", toAccount))));

			// Assert
			ShouldHaveBuilt(callResult, "one output read by two consumers is two ordinary bindings");
			JsonObject graph = await DescribeGraphAsync(context, processName);
			string objectFilesUId = UId(ElementParameter(Element(graph, "OF1"), "ObjectFiles"));
			foreach ((string name, string target) in new[] { ("PF1", "Contact"), ("PF2", "Account") }) {
				JsonObject element = Element(graph, name);
				JsonObject block = FileProcessingBlock(element);
				Text(Group(block, "files"), "sourceElement").Should().Be("OF1", because: $"{name} reads OF1's files");
				Text(block, "bindingLevels").Should().Be("both", because: $"{name} binds both levels of its Files");
				Text(Group(block, "saveTo"), "object").Should().Be(target, because: $"{name} keeps its own target");
				ShouldBeBoundTo(ElementParameter(element, "Files"), objectFilesUId,
					$"{name}'s Files takes its rows from the same OF1.ObjectFiles");
			}
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[TestCase("value", """{ "files": { "value": "x" }, "saveTo": { "object": "Contact", "recordId": { "processParameter": "ContactRef" } } }""", "a file has no constant form")]
	[TestCase("useInProcess", """{ "action": "useInProcess", "files": { "processParameter": "Docs" } }""", "is not available on the Process parameter source")]
	[TestCase("noSaveTo", """{ "files": { "processParameter": "Docs" } }""", "'saveTo' is required")]
	[TestCase("noSource", """{ "files": { }, "saveTo": { "object": "Contact", "recordId": { "processParameter": "ContactRef" } } }""", "names no source")]
	[TestCase("noFileItem", """{ "files": { "sourceElement": "ReadData1", "sourceElementParameter": "ResultCompositeObjectList" }, "saveTo": { "object": "Contact", "recordId": { "processParameter": "ContactRef" } } }""", "with no File item")]
	[TestCase("notAFile", """{ "files": { "processParameter": "ContactRef" }, "saveTo": { "object": "Contact", "recordId": { "processParameter": "ContactRef" } } }""", "is neither a file nor a collection of files")]
	[Description("TC-I-66: the Process parameter refusals over the wire, none of which builds anything - a constant 'value' (a file has no constant form), 'useInProcess' (the run time throws for it), no 'saveTo' (the variant only saves), a files source naming nothing, a record collection with no File item to copy, and a source that is neither a file nor a collection of files.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process refuses an unusable Process parameter element")]
	public async Task CreateBusinessProcess_Should_RefuseAnUnusableProcessParameterElement(string caseName, string blockJson,
			string fragment) {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName($"PfNo{caseName}");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				LinearDescriptor(processName, FilesParametersJson, ("ReadData1", ReadContactsJson),
					("PF1", FileElement("PF1", blockJson))));

			// Assert
			ShouldBeRefused(callResult, $"the {caseName} case would build an element that keeps the process from starting or fails at run time");
			string log = LogText(callResult);
			log.Should().Contain(fragment, because: "the refusal says what is wrong with the block");
			log.Should().NotContain(CreatedLine, because: "a refused build saves nothing");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("Extends TC-I-66: a mapping FROM a Process parameter element's Files input is refused - Files is the files the element is given, an input its run time only reads - and the refusal points to ObjectFiles, the copies it saved; the refused edit leaves the target unbound.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses a mapping from a Process parameter element's Files")]
	public async Task ModifyBusinessProcess_Should_RefuseAMappingFromTheFilesInput() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("PfFrom");
			await ArrangeProcessAsync(context, created, processName,
				LinearDescriptor(processName, FilesParametersJson,
					("PF1", FileElement("PF1", FilesBlock("""{ "processParameter": "Docs" }""")))),
				"the process with a Process parameter element reading Docs");

			// Act
			CallToolResult callResult = await ModifyAsync(context, processName, """
				[ { "op": "addMapping", "mapping": { "targetProcessParameter": "Docs2",
				    "sourceElement": "PF1", "sourceElementParameter": "Files" } } ]
				""");

			// Assert
			ShouldBeRefused(callResult, "Files is an input the run time only reads, never an output");
			string log = LogText(callResult);
			log.Should().Contain("is the files the Process file element is given", because: "the refusal says what Files is");
			log.Should().Contain("PF1.ObjectFiles", because: "and points to the copies the element saved");
			(Text(ProcessParameter(await DescribeGraphAsync(context, processName), "Docs2"), "value") ?? string.Empty)
				.Should().BeEmpty(because: "the refused mapping leaves Docs2 unbound");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-91 (the Files mapping rules over the wire on a builder-configured Process parameter element), on one element built with files <- Docs, in order: (c) Files.File <- Doc, one File, gives the nested-only binding the designer writes - Files is cleared, describe reads files.processParameter Doc with bindingLevels itemOnly (the designer's nested-only binding copies exactly one file, so the single-file path is written); (b) Files.File <- Docs.File, an item of a FileCollection, binds both levels, Files to Docs; (a) Files <- Docs2 pairs Files.File with Docs2.File, with a notice; (d) Files <- ReadData1.ResultCompositeObjectList is refused (the source has no File item to copy) and changes nothing.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process applies the Files mapping rules to a Process parameter element")]
	public async Task ModifyBusinessProcess_Should_ApplyTheFilesMappingRules_ToAProcessParameterElement() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("PfRules");
			await ArrangeProcessAsync(context, created, processName,
				LinearDescriptor(processName, FilesParametersJson, ("ReadData1", ReadContactsJson),
					("PF1", FileElement("PF1", FilesBlock("""{ "processParameter": "Docs" }""")))),
				"the process with a Process parameter element reading Docs");

			// Act
			CallToolResult single = await ModifyAsync(context, processName,
				FilesMapping("Files.File", """ "processParameter": "Doc" """));
			JsonObject afterSingle = await DescribeGraphAsync(context, processName);
			CallToolResult item = await ModifyAsync(context, processName,
				FilesMapping("Files.File", """ "processParameter": "Docs.File" """));
			JsonObject afterItem = await DescribeGraphAsync(context, processName);
			CallToolResult outer = await ModifyAsync(context, processName,
				FilesMapping("Files", """ "processParameter": "Docs2" """));
			JsonObject afterOuter = await DescribeGraphAsync(context, processName);
			CallToolResult records = await ModifyAsync(context, processName,
				FilesMapping("Files", """ "sourceElement": "ReadData1", "sourceElementParameter": "ResultCompositeObjectList" """));
			JsonObject afterRecords = await DescribeGraphAsync(context, processName);

			// Assert
			ShouldHaveLanded(single, "(c) one File onto Files.File is the designer's nested-only binding, which copies exactly one file");
			JsonObject singleBlock = FileProcessingBlock(Element(afterSingle, "PF1"));
			Text(Group(singleBlock, "files"), "processParameter").Should().Be("Doc",
				because: "(c) the nested level alone reads back as the single File it names");
			Text(singleBlock, "bindingLevels").Should().Be("itemOnly", because: "(c) the collection bound above the item was cleared");
			ShouldHaveLanded(item, "(b) an item of a FileCollection onto Files.File is an ordinary edit");
			JsonObject itemBlock = FileProcessingBlock(Element(afterItem, "PF1"));
			Text(Group(itemBlock, "files"), "processParameter").Should().Be("Docs",
				because: "(b) an item of a collection binds Files to the collection the item belongs to, so it reads back as the collection");
			Text(itemBlock, "bindingLevels").Should().Be("both", because: "(b) both levels are now bound");
			ShouldBeBoundTo(ElementParameter(Element(afterItem, "PF1"), "Files"), UId(ProcessParameter(afterItem, "Docs")),
				"(b) the parent Files is bound to the collection Docs");
			ShouldHaveLanded(outer, "(a) a FileCollection onto Files is an ordinary edit");
			string pairing = WarningText(outer);
			pairing.Should().Contain("Files.File", because: "(a) the notice names the file item the binder paired");
			pairing.Should().Contain("Docs2.File", because: "(a) and the source item it was paired with");
			pairing.Should().Contain("now reads", because: "(a) the notice says the item was bound, not merely found");
			JsonObject outerBlock = FileProcessingBlock(Element(afterOuter, "PF1"));
			Text(Group(outerBlock, "files"), "processParameter").Should().Be("Docs2", because: "(a) Files reads Docs2 now");
			Text(outerBlock, "bindingLevels").Should().Be("both", because: "(a) the item was paired, not left unbound");
			ShouldBeRefused(records, "(d) a record collection has no File item, so every row would reach Files.File empty");
			LogText(records).Should().Contain("has no File item", because: "(d) the refusal says what the source lacks");
			FileProcessingBlock(Element(afterRecords, "PF1")).ToJsonString().Should().Be(outerBlock.ToJsonString(),
				because: "(d) the refused mapping leaves the element as it was");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Tests - refusals of the generic route (TC-I-12, extended)

	[TestCase(ReportTaskName, "report")]
	[TestCase(ProcessTaskName, "files")]
	[Description("Extends TC-I-12 to the Generated report and Process parameter variants: a generic userTask naming ReportFileProcessingUserTask or ProcessFileProcessingUserTask is refused on create and on modify's addElement, pointing to type fileProcessing with the variant's own group - that route carries no printable or files, record or target, so the element would save green and fail at run time.")]
	[AllureTag(CreateToolName, ModifyToolName)]
	[AllureName("create- and modify-business-process refuse the generic route for the Report and Process tasks")]
	public async Task GenericUserTaskRoute_Should_BeRefused_ForTheReportAndProcessTasks(string taskName, string group) {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		List<string> created = [];
		try {
			string refusedName = NewProcessName("GenericNew");
			string hostName = NewProcessName("GenericHost");
			string element = $$"""{ "name": "UT1", "type": "userTask", "userTaskName": "{{taskName}}" }""";
			await ArrangeProcessAsync(context, created, hostName, LinearDescriptor(hostName, "[]"),
				"the plain process an addElement extends");

			// Act
			CallToolResult createResult = await CreateAsync(context, created, refusedName,
				LinearDescriptor(refusedName, "[]", ("UT1", element)));
			CallToolResult addResult = await ModifyAsync(context, hostName,
				$$"""[ { "op": "addElement", "element": {{element}} } ]""");

			// Assert
			foreach ((string route, CallToolResult result) in new[] { ("create", createResult), ("addElement", addResult) }) {
				ShouldBeRefused(result, $"the generic route on {route} would build an unconfigured element that fails at run time");
				string log = LogText(result);
				log.Should().Contain("cannot be built as type", because: $"the {route} refusal says the route is the problem");
				log.Should().Contain(taskName, because: $"the {route} refusal names the task schema the name resolved to");
				log.Should().Contain("Use type 'fileProcessing'", because: $"the {route} refusal points to the element type");
				log.Should().Contain($"'{group}' group", because: $"the {route} refusal names the group that selects the variant");
			}
			LogText(createResult).Should().NotContain(CreatedLine, because: "a refused build saves nothing");
			Objects((await DescribeGraphAsync(context, hostName))["elements"])
				.Select(described => Text(described, "name")).Should().NotContain("UT1",
					because: "the refused addElement adds nothing to the host");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Tests - read-only describes (TC-I-61, TC-I-46)

	[TestCase("PrintInvoiceReport")]
	[TestCase("PrintQuotationReport")]
	[TestCase("PrintContractsReport")]
	[TestCase("GenerateDNSRecordsSpecification")]
	[Description("TC-I-61: a read-only describe (mode=full) of a shipped product report process. Every Process file element in it describes as build type fileprocessing with its block decoded - a Generated report element as source report with its printable - and its raw parameters lossless: the stored ReportId is the printable the block names. The process is never modified: its SysSchema rows read the same ModifiedOn before and after. Ignored when the stand does not carry the process.")]
	[AllureTag(DescribeToolName)]
	[AllureName("describe-business-process reads a shipped report process as fileprocessing, read-only")]
	public async Task DescribeBusinessProcess_Should_ReadAShippedReportProcess_AsFileProcessing(string processName) {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		IReadOnlyList<string> before = await ReadSchemaStampsAsync(context, processName);
		if (before.Count == 0) {
			Assert.Ignore($"The shipped process {processName} is not installed on '{context.EnvironmentName}'.");
		}

		// Act
		JsonObject graph = await DescribeGraphAsync(context, processName);

		// Assert
		List<JsonObject> fileElements = Objects(graph["elements"])
			.Where(element => IsFileTask(Text(element, "userTaskName"))).ToList();
		fileElements.Where(element => Text(element, "userTaskName") == ReportTaskName).Should().NotBeEmpty(
			because: $"{processName} generates its report through a Generated report element");
		foreach (JsonObject element in fileElements) {
			string name = Text(element, "name") ?? "?";
			Text(element, "buildType").Should().BeEquivalentTo(FileProcessingBuildType,
				because: $"{name} is a Process file element, claimed by its own handler");
			JsonObject block = FileProcessingBlock(element);
			Text(block, "source").Should().NotBeNullOrEmpty(because: $"{name}'s block names its variant");
			if (Text(element, "userTaskName") != ReportTaskName) {
				continue;
			}
			Text(block, "source").Should().Be("report", because: $"{name} is a Generated report element");
			string printable = Text(Group(block, "report"), "printable") ?? string.Empty;
			Guid.TryParse(printable, out _).Should().BeTrue(because: $"{name} names its printable by id, as stored");
			(Text(ElementParameter(element, "ReportId"), "value") ?? string.Empty).Should().ContainEquivalentOf(printable,
				because: $"{name}'s raw ReportId is reported beside the block, losslessly");
		}
		(await ReadSchemaStampsAsync(context, processName)).Should().Equal(before,
			because: "describe is read-only: the shipped process is never modified");
	}

	[Test]
	[Description("Extends TC-I-46 on the designer-built fixture UsrFpOaDesignerFixture: F2, its designer-built Generated report element, now describes as build type fileprocessing with source report and its printable, read-only - the fixture's SysSchema rows read the same ModifiedOn before and after. Ignored, with the build instructions, when the stand does not carry the fixture.")]
	[AllureTag(DescribeToolName)]
	[AllureName("the designer-built Generated report element describes as fileprocessing")]
	public async Task DesignerFixture_Should_DescribeTheReportElement_AsFileProcessing() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync();
		IReadOnlyList<string> before = await ReadSchemaStampsAsync(context, DesignerFixtureName);
		if (before.Count == 0) {
			Assert.Ignore($"Designer fixture {DesignerFixtureName} not found on '{context.EnvironmentName}'. Build it once "
				+ "in the designer, in package Custom, to run this test: a Lookup parameter PInvoice -> Invoice; F1 = Process "
				+ "file, Object attachments, Account address; F2 = Generated report, printable Invoice, filter Id = PInvoice, "
				+ "saved to the attachments of the Invoice PInvoice names; Start -> F1 -> F2 -> End.");
		}

		// Act
		JsonObject graph = await DescribeGraphAsync(context, DesignerFixtureName);

		// Assert
		List<JsonObject> reports = Objects(graph["elements"])
			.Where(element => Text(element, "userTaskName") == ReportTaskName).ToList();
		reports.Should().HaveCount(1, because: "the fixture carries exactly one Generated report element, F2; what was read: {0}",
			graph.ToJsonString());
		JsonObject f2 = reports[0];
		Text(f2, "buildType").Should().BeEquivalentTo(FileProcessingBuildType,
			because: "the Generated report element is claimed by the Process file handler since its variant was registered");
		JsonObject block = FileProcessingBlock(f2);
		Text(block, "source").Should().Be("report", because: "a designer-built Generated report element reads back as the report variant");
		Guid.TryParse(Text(Group(block, "report"), "printable"), out _).Should().BeTrue(
			because: "the designer stored a printable, read back by its id");
		(await ReadSchemaStampsAsync(context, DesignerFixtureName)).Should().Equal(before,
			because: "the describe is read-only: the fixture is never modified");
	}

	#endregion

	#region Methods: Tests - SysFile targets (ENG-103937)

	[TestCase("report")]
	[TestCase("files")]
	[Description("TC-I-81: the Generated report and the Process parameter variants save into SysFile storage - saveTo.object an object with no attachment object of its own (AccountAddress) is written, and describe reads the target back as storage sysFile, file object SysFile, link column RecordId and the record object, with no issue about the storage. Below CrtProcessBuilder 1.6.6.111 the case is Ignored; the report case is Ignored when the stand has no MS Word printable of Invoice.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process saves report and process-parameter files into SysFile storage")]
	public async Task CreateBusinessProcess_Should_SaveIntoSysFileStorage(string variant) {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartSysFileAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("SysTarget");
			const string saveTo =
				"\"saveTo\": { \"object\": \"AccountAddress\", \"recordId\": { \"processParameter\": \"AddressRef\" } }";
			string block;
			string parametersJson;
			if (variant == "report") {
				ListPrintableRow printable = await ReadPrintableAsync(context, InvoiceObject, caption: null);
				block = $$"""
					{ "report": { "printable": "{{printable.TemplateId}}", "recordId": { "processParameter": "InvoiceRef" } },
					  {{saveTo}} }
					""";
				parametersJson = SysFileReportTargetParametersJson;
			} else {
				block = $$"""{ "files": { "processParameter": "Docs" }, {{saveTo}} }""";
				parametersJson = SysFileFilesTargetParametersJson;
			}

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName,
				LinearDescriptor(processName, parametersJson, ("PF1", FileElement("PF1", block))),
				SysFileMinimumPackageVersion);

			// Assert
			ShouldHaveBuilt(callResult, $"a SysFile target of the {variant} variant is written like any other");
			JsonObject fileBlock = FileProcessingBlock(Element(await DescribeGraphAsync(context, processName), "PF1"));
			JsonObject target = Group(fileBlock, "saveTo");
			Text(target, "object").Should().Be("AccountAddress", because: "the target's record object is stored as sent");
			Text(target, "storage").Should().Be("sysFile",
				because: "Account address has no attachment object of its own, so the target derives SysFile storage");
			Text(target, "fileObject").Should().Be("SysFile", because: "the saved files are kept in SysFile");
			Text(target, "linkColumn").Should().Be("RecordId", because: "a SysFile row links to its record through RecordId");
			Strings(fileBlock["issues"]).Should().NotContain(issue => issue.Contains("SysFile", StringComparison.Ordinal),
				because: "a SysFile target is no hazard for an object without its own attachment object");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Descriptors

	// The SysFile target case of the report variant: an Invoice to report on and an Account address to save to.
	private const string SysFileReportTargetParametersJson = """
		[
		  { "name": "InvoiceRef", "type": "Lookup", "referenceSchema": "Invoice", "direction": "In" },
		  { "name": "AddressRef", "type": "Lookup", "referenceSchema": "AccountAddress", "direction": "In" }
		]
		""";

	// The SysFile target case of the files variant: a FileCollection to save and an Account address to save to. It
	// declares no Invoice reference, which a stand without the Invoice object cannot declare (see
	// ContactAndAccountRefParametersJson).
	private const string SysFileFilesTargetParametersJson = """
		[
		  { "name": "Docs", "type": "FileCollection" },
		  { "name": "AddressRef", "type": "Lookup", "referenceSchema": "AccountAddress", "direction": "In" }
		]
		""";

	// The record references the report cases read: an Invoice, a Contact and an Account by Lookup.
	private const string RecordRefParametersJson = """
		[
		  { "name": "InvoiceRef", "type": "Lookup", "referenceSchema": "Invoice", "direction": "In" },
		  { "name": "ContactRef", "type": "Lookup", "referenceSchema": "Contact", "direction": "In" },
		  { "name": "AccountRef", "type": "Lookup", "referenceSchema": "Account", "direction": "In" }
		]
		""";

	// The record references of the cases that read no Invoice: a Contact and an Account by Lookup. Kept apart from
	// RecordRefParametersJson because a Lookup to Invoice cannot even be declared where the Invoice object does not
	// exist - a Studio stand refuses the whole build with 'Item with name "Invoice" not found' before anything under
	// test runs.
	private const string ContactAndAccountRefParametersJson = """
		[
		  { "name": "ContactRef", "type": "Lookup", "referenceSchema": "Contact", "direction": "In" },
		  { "name": "AccountRef", "type": "Lookup", "referenceSchema": "Account", "direction": "In" }
		]
		""";

	// The file sources of the Process parameter cases: one File, two FileCollections, and a Contact reference.
	private const string FilesParametersJson = """
		[
		  { "name": "Doc", "type": "File" },
		  { "name": "Docs", "type": "FileCollection" },
		  { "name": "Docs2", "type": "FileCollection" },
		  { "name": "ContactRef", "type": "Lookup", "referenceSchema": "Contact", "direction": "In" }
		]
		""";

	// One contact's attachments, handed to the following elements (no saveTo infers useInProcess).
	private const string UseInProcessObjectBlockJson = """
		{ "attachments": { "object": "Contact", "recordId": { "processParameter": "ContactRef" } } }
		""";

	// A Read data element reading a few contacts as a collection: a record collection with no File item.
	private const string ReadContactsJson = """
		{ "name": "ReadData1", "type": "readData", "caption": "Read contacts",
		  "readData": { "source": "Contact", "mode": "collection", "columns": ["Name"], "numberOfRecords": 3 } }
		""";

	/// <summary>A Process parameter block taking <paramref name="filesJson"/> and saving to the Contact ContactRef.</summary>
	private static string FilesBlock(string filesJson) =>
		$$"""
		{ "files": {{filesJson}}, "saveTo": { "object": "Contact", "recordId": { "processParameter": "ContactRef" } } }
		""";

	/// <summary>A Process file element carrying <paramref name="blockJson"/> as its <c>fileProcessing</c> block.</summary>
	private static string FileElement(string name, string blockJson) =>
		$$"""{ "name": "{{name}}", "type": "fileProcessing", "caption": "Process file {{name}}", "fileProcessing": {{blockJson}} }""";

	/// <summary>One addMapping onto PF1's <paramref name="elementParameter"/> from the given source members.</summary>
	private static string FilesMapping(string elementParameter, string sourceMembers) =>
		$$"""
		[ { "op": "addMapping", "mapping": { "elementName": "PF1", "elementParameter": "{{elementParameter}}", {{sourceMembers}} } } ]
		""";

	/// <summary>Start -> the elements in the order given -> End, with no mappings.</summary>
	private static string LinearDescriptor(string processName, string parametersJson,
			params (string Name, string Json)[] elements) {
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
			  "caption": "Clio BP Process file report and parameter E2E",
			  "packageName": "Custom",
			  "parameters": {{parametersJson}},
			  "elements": [ {{elementsJson}} ],
			  "flows": [ {{flowsJson}} ],
			  "mappings": []
			}
			""";
	}

	#endregion

	#region Methods: Arrange and cleanup

	/// <summary>Opens a session, or ignores the test when the stand is missing, unreachable or behind the cut.</summary>
	private static Task<ProcessDesignerArrangeContext> StartAsync() =>
		ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion, TimeSpan.FromMinutes(8));

	/// <summary>Opens a session, or ignores the test when the stand is behind the end-of-request notices cut.</summary>
	private static Task<ProcessDesignerArrangeContext> StartEndOfRequestAsync() =>
		ProcessDesignerE2EArrange.StartAsync(Subject, EndOfRequestNoticesMinimumPackageVersion, TimeSpan.FromMinutes(8));

	/// <summary>Opens a session, or ignores the test when the stand is behind the SysFile cut (1.6.6.111).</summary>
	private static Task<ProcessDesignerArrangeContext> StartSysFileAsync() =>
		ProcessDesignerE2EArrange.StartAsync("Process file element (SysFile targets)", SysFileMinimumPackageVersion,
			TimeSpan.FromMinutes(8));

	/// <summary>A process code unique to this run, in the <c>UsrClioBpFr&lt;Case&gt;E2e</c> family.</summary>
	private static string NewProcessName(string caseName) => $"UsrClioBpFr{caseName}E2e{Guid.NewGuid():N}";

	/// <summary>
	/// Builds a process, recording its name for cleanup BEFORE the call: a build the server saved but whose answer
	/// never arrived must still be removed. Ignores the test when clio refused the call because the stand's
	/// CrtProcessBuilder is behind.
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

	/// <summary>
	/// Removes every process a test built, newest first. Reports a failed delete instead of throwing, so the
	/// assertion failure the test was already reporting is not replaced.
	/// </summary>
	private static async Task DeleteProcessesAsync(ProcessDesignerArrangeContext context, List<string> created) {
		for (int index = created.Count - 1; index >= 0; index--) {
			await DeleteProcessAsync(context.EnvironmentName, created[index]);
		}
	}

	/// <summary>
	/// The printable of <paramref name="objectName"/> a case builds on, read with <c>list-printables</c> - never
	/// invented. Ignores the test when the stand lists no printable at all, or none of that object (with
	/// <paramref name="caption"/>, when given).
	/// </summary>
	private static async Task<ListPrintableRow> ReadPrintableAsync(ProcessDesignerArrangeContext context,
			string objectName, string? caption) {
		CallToolResult result = await context.Session.CallToolAsync(ListPrintablesTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["entity-name"] = objectName,
					["environment-name"] = context.EnvironmentName
				}
			},
			context.CancellationTokenSource.Token);
		ListPrintablesEnvelope envelope = EntitySchemaStructuredResultParser.Extract<ListPrintablesEnvelope>(result);
		envelope.Success.Should().BeTrue(because: "list-printables must answer for the case to pick a real printable; "
			+ "it failed with: {0}", envelope.Error);
		IReadOnlyList<ListPrintableRow> rows = envelope.Printables ?? [];
		if (envelope.Count == 0 || rows.Count == 0) {
			Assert.Ignore($"list-printables returned 0 printables of {objectName} on '{context.EnvironmentName}'; the "
				+ "Generated report cases need an MS Word printable (System Designer -> Report setup).");
		}
		ListPrintableRow? row = rows.FirstOrDefault(candidate =>
			string.Equals(ObjectOf(candidate), objectName, StringComparison.OrdinalIgnoreCase)
			&& (caption == null || string.Equals(candidate.PrintableCaption, caption, StringComparison.OrdinalIgnoreCase)));
		if (row == null) {
			Assert.Ignore($"list-printables has no printable {(caption == null ? string.Empty : $"'{caption}' ")}of "
				+ $"{objectName} on '{context.EnvironmentName}'.");
		}
		return row!;
	}

	/// <summary>The object a printable is generated for: its own, else its section's - the run time's fallback.</summary>
	private static string? ObjectOf(ListPrintableRow row) =>
		string.IsNullOrEmpty(row.EntitySchemaName) ? row.ModuleEntitySchemaName : row.EntitySchemaName;

	/// <summary>
	/// The <c>Id | ModifiedOn</c> of every stored <c>SysSchema</c> row of a process, ordered by id - empty when the
	/// process is not installed. Equal before and after is the evidence a read did not write.
	/// </summary>
	private static async Task<IReadOnlyList<string>> ReadSchemaStampsAsync(ProcessDesignerArrangeContext context,
			string processName) {
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, ExecuteEsqTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["query"] = JsonSerializer.SerializeToElement(SelectQueryHelper.BuildSelectQuery("SysSchema",
					[
						new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id", OrderDirection: 1, OrderPosition: 0),
						new SelectQueryHelper.SelectQueryColumnDefinition("ModifiedOn", "ModifiedOn")
					],
					[
						new SelectQueryHelper.SelectQueryFilterDefinition("Name", processName,
							SelectQueryHelper.TextDataValueType),
						new SelectQueryHelper.SelectQueryFilterDefinition("ManagerName", "ProcessSchemaManager",
							SelectQueryHelper.TextDataValueType)
					]))
			});
		ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(result);
		response.Success.Should().BeTrue(because: $"the SysSchema rows of '{processName}' must be readable for this case to "
			+ "measure anything; the read failed with: {0}", response.Error);
		JsonElement rows = response.Rows.GetValueOrDefault();
		rows.ValueKind.Should().Be(JsonValueKind.Array, because: "a SelectQuery answers with a rows array");
		return rows.EnumerateArray()
			.Select(row => $"{row.GetProperty("Id")} | {row.GetProperty("ModifiedOn")}")
			.ToList();
	}

	#endregion

	#region Methods: Graph reading and assertions

	/// <summary>Whether a user task is one of the three Process file tasks.</summary>
	private static bool IsFileTask(string? userTaskName) =>
		userTaskName is ObjectTaskName or ReportTaskName or ProcessTaskName;

	/// <summary>A Process parameter element as describe reads it: its build type, task, source, action and outputs.</summary>
	private static void ShouldBeAProcessParameterElement(JsonObject element) {
		string name = Text(element, "name") ?? "?";
		Text(element, "buildType").Should().BeEquivalentTo(FileProcessingBuildType,
			because: $"{name} is claimed by the Process file handler once the Process parameter variant is registered");
		Text(element, "userTaskName").Should().Be(ProcessTaskName, because: "the files group selects the Process parameter task");
		JsonObject block = FileProcessingBlock(element);
		Text(block, "source").Should().Be("files", because: "the group present is the variant");
		Text(block, "action").Should().Be("saveToAttachments", because: "the Process parameter variant only saves");
		OutputNames(block).Should().Contain(["ObjectFiles.ObjectFile", "CreatedObjectFileIds.Id"],
			because: "its own ObjectFiles holds the copies, item ObjectFile");
	}

	/// <summary>The described printable, compared as a Guid because the server may spell it in another case.</summary>
	private static void ShouldNameThePrintable(JsonObject report, string templateId) {
		string? printable = Text(report, "printable");
		Guid.TryParse(printable, out Guid described).Should().BeTrue(because: "describe names the printable by its id");
		described.Should().Be(Guid.Parse(templateId), because: "the printable stored is the templateId list-printables reported");
	}

	/// <summary>The element's described <c>fileProcessing</c> block, failing the test when describe reports none.</summary>
	private static JsonObject FileProcessingBlock(JsonObject element) => Group(element, "fileProcessing");

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

	/// <summary>The described outputs as <c>parameter.item</c>.</summary>
	private static IReadOnlyList<string> OutputNames(JsonObject block) =>
		Objects(block["outputs"]).Select(output => $"{Text(output, "parameter")}.{Text(output, "item")}").ToList();

	/// <summary>The described process parameter with that name.</summary>
	private static JsonObject ProcessParameter(JsonObject graph, string name) =>
		Named(Objects(graph["parameters"]), name, "process parameter", graph);

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
			because: $"a binding is found by UId, so '{Text(node, "name")}' must report one");
		return uid!;
	}

	/// <summary>The metapath segment that names the parameter with <paramref name="parameterUId"/>.</summary>
	private static string ParameterSegment(string parameterUId) =>
		"[Parameter:{" + Guid.Parse(parameterUId).ToString("D") + "}]";

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
				.Where(text => text != null)
				.Select(text => text!)
				.ToList()
			: [];

	#endregion

}

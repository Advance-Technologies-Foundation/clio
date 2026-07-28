using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Web;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Pins the wire contract of the chunked <c>upload-report-template</c> call: the query string that
/// <c>PrintableSupport.BuildUploadQuery</c> composes and the design-service response that
/// <c>PrintableSupport.ParseUploadResponse</c> interprets. Both mirror Creatio's own
/// MSWordReportDesigner service client, so a silent drift here uploads a template that never
/// lands — with a success-looking tool result. The e2e suite cannot cover this without a writable
/// sandbox and a real .docx fixture, which is why the mapping is pinned at the unit level.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PrintableTemplateUploadMappingTests {

	private const string ReportId = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";

	private static Dictionary<string, string> ParseQuery(string query) {
		var parsed = HttpUtility.ParseQueryString(query);
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (string? key in parsed.AllKeys) {
			if (key is not null) {
				result[key] = parsed[key] ?? string.Empty;
			}
		}
		return result;
	}

	[Test]
	[Description("Every parameter the design service requires is present and carries the value the service expects.")]
	public void BuildUploadQuery_ShouldMapEveryServiceParameter() {
		Guid fileId = Guid.Parse("6f1f6a4f-1d31-4a2e-9a08-2c6d0f4c1e77");

		string query = PrintableSupport.BuildUploadQuery(ReportId, fileId, 4096, "Invoice.docx");

		Dictionary<string, string> parameters = ParseQuery(query);
		parameters["columnName"].Should().Be(PrintableSupport.TemplateColumnName,
			because: "the template bytes land in the File stream column");
		parameters["fileId"].Should().Be(fileId.ToString(),
			because: "the chunked upload is keyed by the fresh file id the caller generated");
		parameters["mimeType"].Should().Be(PrintableSupport.WordMimeType,
			because: "the service validates the .docx MIME type");
		parameters["parentColumnName"].Should().Be("Id",
			because: "the template row is linked through its Id column");
		parameters["reportId"].Should().Be(ReportId,
			because: "the service needs the printable the template belongs to");
		// Pins current behavior: parentColumnValue carries the upload's own fileId, not the report id.
		// Not independently verified against Creatio's MSWordReportDesigner client — if that contract
		// says otherwise, fix the implementation and this assertion together.
		parameters["parentColumnValue"].Should().Be(fileId.ToString(),
			because: "this pins the current, not-yet-verified mapping so a silent drift is caught");
		parameters["totalFileLength"].Should().Be("4096",
			because: "the service reassembles the chunks against the declared total size");
		parameters["entitySchemaName"].Should().Be(PrintableSupport.TemplateUploadEntitySchema,
			because: "the upload writes through the report-template store schema");
		parameters["fileName"].Should().Be("Invoice.docx",
			because: "the stored template keeps the original file name");
		parameters["maxFileSizeSysSettingsName"].Should().Be(PrintableSupport.MaxFileSizeSettingName,
			because: "the service enforces the size limit from this SysSetting");
		JsonSerializer.Deserialize<Dictionary<string, string>>(parameters["additionalParams"])
			.Should().Contain(new KeyValuePair<string, string>("ReportId", ReportId),
				because: "additionalParams is what actually carries the report link to the service");
	}

	[Test]
	[Description("A file name with spaces and non-ASCII characters survives the round trip through the query string.")]
	public void BuildUploadQuery_ShouldEscapeFileName() {
		string query = PrintableSupport.BuildUploadQuery(ReportId, Guid.NewGuid(), 1, "Рахунок для клієнта.docx");

		query.Should().NotContain("Рахунок для клієнта.docx",
			because: "the raw name must be percent-encoded, not embedded verbatim");
		ParseQuery(query)["fileName"].Should().Be("Рахунок для клієнта.docx",
			because: "the encoding must round-trip the exact name the service will store");
	}

	[Test]
	[Description("A zero-length file still reports its size explicitly rather than omitting the parameter.")]
	public void BuildUploadQuery_ShouldReportZeroLengthExplicitly() {
		string query = PrintableSupport.BuildUploadQuery(ReportId, Guid.NewGuid(), 0, "Empty.docx");

		ParseQuery(query)["totalFileLength"].Should().Be("0",
			because: "omitting the size would let the service fall back to an unbounded read");
	}

	[Test]
	[Description("The design service's errorInfo message is surfaced as the failure reason.")]
	public void ParseUploadResponse_ShouldSurfaceServiceErrorMessage() {
		string json = """{"success":false,"errorInfo":{"message":"Report template exceeds the allowed size."}}""";

		PrintableTemplateUploadResponse response = PrintableSupport.ParseUploadResponse(json, ReportId, "Invoice.docx");

		response.Success.Should().BeFalse(because: "the service reported an explicit failure");
		response.Error.Should().Be("Report template exceeds the allowed size.",
			because: "the caller needs the service's own reason, not a generic one");
	}

	[Test]
	[Description("A failure without a usable errorInfo still fails, with a generic reason instead of a silent success.")]
	public void ParseUploadResponse_ShouldFailClosed_WhenErrorInfoIsMissing() {
		PrintableTemplateUploadResponse response =
			PrintableSupport.ParseUploadResponse("""{"success":false}""", ReportId, "Invoice.docx");

		response.Success.Should().BeFalse(because: "an explicit success:false is a failure regardless of errorInfo");
		response.Error.Should().Be("Template upload failed.",
			because: "a missing errorInfo must still produce a reason instead of an empty error");
	}

	[Test]
	[Description("An explicit success is the only body that reports the template as stored.")]
	public void ParseUploadResponse_ShouldReportSuccess_OnExplicitSuccess() {
		PrintableTemplateUploadResponse response =
			PrintableSupport.ParseUploadResponse("""{"success":true}""", ReportId, "Invoice.docx");

		response.Success.Should().BeTrue(because: "the service explicitly confirmed the upload");
		response.Error.Should().BeNull(because: "a confirmed upload carries no error");
		response.Id.Should().Be(ReportId, because: "the response echoes the report the template belongs to");
		response.FileName.Should().Be("Invoice.docx", because: "the response echoes the uploaded file name");
	}

	[TestCase("", TestName = "ParseUploadResponse fails closed on an empty body")]
	[TestCase("   ", TestName = "ParseUploadResponse fails closed on a whitespace-only body")]
	[TestCase("OK", TestName = "ParseUploadResponse fails closed on a plain-text body")]
	[TestCase("<html><body>Sign in</body></html>", TestName = "ParseUploadResponse fails closed on an HTML login page")]
	[TestCase("""{"rowsAffected":1}""", TestName = "ParseUploadResponse fails closed on JSON without a success flag")]
	[TestCase("""{"success":"true"}""", TestName = "ParseUploadResponse fails closed on a non-boolean success flag")]
	[Description("Anything short of an explicit success must fail closed: a 200 with an empty, plain-text, HTML or unrecognized JSON body means the template may never have landed, and a false success would also patch FileName so get-printable corroborates the wrong answer.")]
	public void ParseUploadResponse_ShouldFailClosed_WhenSuccessIsNotConfirmed(string json) {
		PrintableTemplateUploadResponse response = PrintableSupport.ParseUploadResponse(json, ReportId, "Invoice.docx");

		response.Success.Should().BeFalse(
			because: "the design service never confirmed the upload, so the tool must not claim it landed");
		response.Error.Should().Contain("not confirmed",
			because: "the caller needs to know the outcome is unknown rather than failed outright");
		response.Error.Should().Contain("get-printable",
			because: "the message must point at the tool that verifies whether the template actually attached");
	}
}

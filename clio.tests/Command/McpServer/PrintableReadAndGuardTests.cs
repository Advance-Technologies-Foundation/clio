using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Covers the printable surfaces the mapping fixtures leave untested: the <c>get-printable</c> read
/// projection, the MS Word type lookup's HIT path, the <c>update-printable</c> empty-change-set guard,
/// and the upload's post-success <c>FileName</c> patch when that patch fails.
/// </summary>
/// <remarks>
/// Each of these is a case where a defect would be invisible to the existing tests (PR #651 review):
/// the projection must exclude the <c>File</c> stream column or every read ships the whole .docx; the
/// type lookup only had its constant-fallback path exercised, so an implementation that ALWAYS returned
/// the constant would have passed; the empty-change-set guard is what stops a fieldless PATCH reaching
/// Creatio; and a swallowed <c>FileName</c> patch must not leave the response claiming a name the
/// database never received.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PrintableReadAndGuardTests {

	private const string RecordId = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";
	private const string LookupTypeId = "3fa2c1d0-77b8-4e19-9c5a-0e1d2b3c4d5e";

	private IApplicationClient _client;
	private IServiceUrlBuilder _urlBuilder;
	private IToolCommandResolver _resolver;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IApplicationClient>();
		_urlBuilder = Substitute.For<IServiceUrlBuilder>();
		_resolver = Substitute.For<IToolCommandResolver>();
		_resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(_client);
		_resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(_urlBuilder);
		_urlBuilder.Build(Arg.Any<string>()).Returns(call => $"https://stand.creatio.com/0/{call.Arg<string>()}");
	}

	/// <summary>Returns the single URL the given read/write method was called with.</summary>
	private string CapturedUrl(string methodName) {
		string url = _client.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == methodName)
			.Select(call => call.GetArguments()[0] as string)
			.SingleOrDefault();
		url.Should().NotBeNull(
			because: $"the tool must have made exactly one {methodName} call for its URL to be assertable");
		return url;
	}

	[Test]
	[Description("get-printable returns the record and asks Creatio for the metadata projection only — the File stream column is never selected, so a report with a large .docx attached does not ship its bytes through the MCP response.")]
	public void GetPrintable_Should_Return_Record_And_Project_Metadata_Without_The_File_Stream() {
		// Arrange
		_client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>()).Returns(
			$"{{\"Id\":\"{RecordId}\",\"Caption\":\"Contact card\",\"FileName\":\"Invoice.docx\"," +
			"\"Type\":{\"Name\":\"MS Word\"},\"SysEntitySchema\":{\"Name\":\"Contact\"}}");
		PrintableGetTool tool = new(_resolver);

		// Act
		ODataReadResponse response = tool.Get(new PrintableGetArgs { EnvironmentName = "dev", Id = RecordId });

		// Assert
		response.Success.Should().BeTrue(because: "a well-formed single-entity body is a successful read");
		response.Value!.Value.GetProperty("FileName").GetString().Should().Be("Invoice.docx",
			because: "FileName is the metadata that tells a caller a template is attached, so it must survive");
		response.Value!.Value.GetProperty("Type").GetProperty("Name").GetString().Should().Be("MS Word",
			because: "the expanded Type name is part of what makes the read useful without a second call");

		string url = Uri.UnescapeDataString(CapturedUrl("ExecuteGetRequest"));
		url.Should().Contain($"$select={PrintableSupport.MetadataSelect}",
			because: "the projection must be the explicit metadata list, not Creatio's default all-columns read");
		url.Should().NotContain($"$select={PrintableSupport.MetadataSelect},{PrintableSupport.TemplateColumnName}",
			because: "the stream column must not be appended to the projection");
		PrintableSupport.MetadataSelect.Split(',').Should().NotContain(PrintableSupport.TemplateColumnName,
			because: "selecting the File column would ship the whole .docx payload on every read — this is the " +
				"assertion that fails if the stream column is ever added to the projection");
		url.Should().Contain("Type($select=Name)",
			because: "the type is expanded by name so the caller does not have to resolve the lookup id");
		url.Should().Contain("SysEntitySchema($select=Name)",
			because: "the bound object is expanded by name for the same reason");
		url.Should().Contain("SysModule($select=Caption)",
			because: "the section binding is expanded by caption");
	}

	[Test]
	[Description("ResolveMsWordTypeId returns the id the runtime lookup found, NOT the well-known constant — the hit path was previously unexercised, so an implementation that always returned the constant would have passed the suite while breaking localized or customized installations.")]
	public void ResolveMsWordTypeId_Should_Return_The_LookedUp_Id_When_The_Lookup_Succeeds() {
		// Arrange
		_client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>())
			.Returns($"{{\"value\":[{{\"Id\":\"{LookupTypeId}\"}}]}}");

		// Act
		string typeId = PrintableSupport.ResolveMsWordTypeId(_client, _urlBuilder);

		// Assert
		typeId.Should().Be(LookupTypeId,
			because: "the runtime lookup is primary; the constant is only a fallback for when it fails");
		typeId.Should().NotBe(PrintableSupport.MsWordTypeId,
			because: "returning the constant on a successful lookup would make the lookup dead code and break " +
				"installations whose MS Word type record carries a different id");
		Uri.UnescapeDataString(CapturedUrl("ExecuteGetRequest")).Should()
			.Contain($"Name eq '{PrintableSupport.MsWordTypeName}'",
				because: "the lookup must filter by the report type's display name");
	}

	[Test]
	[Description("ResolveMsWordTypeId falls back to the well-known constant when the lookup transport fails, so a create does not fail over an optional optimisation.")]
	public void ResolveMsWordTypeId_Should_FallBack_To_The_Constant_When_The_Lookup_Fails() {
		// Arrange
		_client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>()).Throws(new HttpRequestException("no route"));

		// Act
		string typeId = PrintableSupport.ResolveMsWordTypeId(_client, _urlBuilder);

		// Assert
		typeId.Should().Be(PrintableSupport.MsWordTypeId,
			because: "the lookup is best-effort, so a transport failure must not fail the whole create");
	}

	[Test]
	[Description("update-printable refuses a payload that supplies no updatable field, and does so BEFORE any remote call, so a fieldless PATCH never reaches Creatio.")]
	public void UpdatePrintable_Should_Refuse_An_Empty_ChangeSet_Before_Any_Remote_Call() {
		// Arrange
		PrintableUpdateTool tool = new(_resolver);

		// Act
		ODataWriteResponse response = tool.Update(new PrintableUpdateArgs {
			EnvironmentName = "dev",
			Id = RecordId,
			Confirm = true
		});

		// Assert
		response.Success.Should().BeFalse(because: "there is nothing to write, so the call cannot succeed");
		response.Error.Should().Contain("No fields to update",
			because: "the caller must be told the payload was empty rather than see an opaque OData error");
		response.Error.Should().Contain("caption",
			because: "the message must enumerate the fields the caller can actually supply");
		_client.ReceivedCalls().Should().BeEmpty(
			because: "the guard runs before the client is used at all — an empty PATCH must never be sent");
	}

	[Test]
	[Description("A failing FileName patch after a confirmed upload leaves the upload reported as successful but clears FileName, so the tool never claims a value the database did not receive while get-printable would say otherwise.")]
	public void UploadReportTemplate_Should_Keep_Success_But_Clear_FileName_When_The_Patch_Fails() {
		// Arrange — a real temp .docx, because the tool checks the extension and reads the file length.
		string filePath = Path.Combine(Path.GetTempPath(), $"clio-printable-{Guid.NewGuid():N}.docx");
		File.WriteAllText(filePath, "docx-bytes");
		try {
			_client.UploadAlmFileByChunk(Arg.Any<string>(), Arg.Any<string>()).Returns("{\"success\":true}");
			_client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
				.Throws(new HttpRequestException("patch rejected"));
			PrintableTemplateUploadTool tool = new(_resolver);

			// Act
			PrintableTemplateUploadResponse response = tool.Upload(new PrintableTemplateUploadArgs {
				EnvironmentName = "dev",
				Id = RecordId,
				FilePath = filePath,
				Confirm = true
			});

			// Assert
			response.Success.Should().BeTrue(
				because: "the template bytes are already stored — a failed convenience patch must not turn a " +
					"confirmed upload into a failure");
			response.Id.Should().Be(RecordId, because: "the response still identifies the report it uploaded to");
			response.FileName.Should().BeNull(
				because: "the FileName column was never written, so reporting the name would have the upload tool " +
					"and get-printable disagree about whether the report has a template attached");
		} finally {
			File.Delete(filePath);
		}
	}

	[Test]
	[Description("A confirmed upload whose FileName patch succeeds reports the file name, so the cleared-FileName behavior above is specific to the failure path and not a blanket regression.")]
	public void UploadReportTemplate_Should_Report_FileName_When_The_Patch_Succeeds() {
		// Arrange
		string filePath = Path.Combine(Path.GetTempPath(), $"clio-printable-{Guid.NewGuid():N}.docx");
		File.WriteAllText(filePath, "docx-bytes");
		try {
			_client.UploadAlmFileByChunk(Arg.Any<string>(), Arg.Any<string>()).Returns("{\"success\":true}");
			_client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>()).Returns("{}");
			PrintableTemplateUploadTool tool = new(_resolver);

			// Act
			PrintableTemplateUploadResponse response = tool.Upload(new PrintableTemplateUploadArgs {
				EnvironmentName = "dev",
				Id = RecordId,
				FilePath = filePath,
				Confirm = true
			});

			// Assert
			response.Success.Should().BeTrue(because: "the service confirmed the upload");
			response.FileName.Should().Be(Path.GetFileName(filePath),
				because: "a written FileName is what makes get-printable report the template as attached");
			JsonDocument.Parse(_client.ReceivedCalls()
					.Where(call => call.GetMethodInfo().Name == "ExecutePatchRequest")
					.Select(call => (string)call.GetArguments()[1])
					.Single())
				.RootElement.GetProperty("FileName").GetString().Should().Be(Path.GetFileName(filePath),
					because: "the patch must write the name onto the FileName column, not some other field");
		} finally {
			File.Delete(filePath);
		}
	}
}

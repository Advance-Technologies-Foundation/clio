using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.RuntimeVerification;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using Clio.Tests.Command.McpServer;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Requirement (b) of the site-text redaction plan: the paths wired in this change carry the text a Creatio SITE
/// wrote to a caller only through <see cref="SensitiveErrorTextRedactor.RedactSiteText"/>, clio's own text does
/// not take it, and every reader of a site's error text that is NOT wired yet is listed by file and member.
/// </summary>
/// <remarks>
/// Each wired path is driven with one marker that only RedactSiteText removes - a sign-in name in SQL Server's own
/// phrase - and the test asserts the marker never reaches what the caller reads. Once rule 9 (PR B) lands, a SQL
/// fragment can serve as a second marker. <see cref="SourceGuard_ShouldKnowEverySiteTextExtractorCallSite"/>
/// fails when a new call site of a site-text extractor appears without a case here, and
/// <see cref="SourceGuard_ShouldKnowEveryDirectReadOfASitesErrorText"/> when a new direct read of a site's error
/// text appears that is neither wired nor in the known-unwired list (follow-up, for the remaining envelope
/// readers). The write-tool and dynamic-case paths (A2) are wired; their cases are in
/// <c>SiteTextWritePathWiringTests</c>. That scan is by NAME: it sees the site-error JSON names as
/// literals, members named ErrorInfo, ResponseStatus or ExceptionMessage or decorated with a site-error JSON name,
/// those names in a property pattern, and constants that hold them (see <see cref="DirectSiteErrorReads"/>). It
/// does not see a site's text read through a member of any other name that no clio source decorates, a property
/// name built at run time, a JSON path, reflection or <c>dynamic</c>.
/// </remarks>
[TestFixture]
[Property("Module", "Common")]
public sealed class SiteTextWiringTests {

	internal const string Marker = "wiring_marker_login";

	internal const string SiteMessage = "Login failed for user '" + Marker + "'.";

	private static readonly string RepositoryRoot =
		Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

	private static string ODataError => JsonSerializer.Serialize(new { error = new { code = "", message = SiteMessage } });

	[Test]
	[Category("Unit")]
	[Description("The marker is one only RedactSiteText removes, so every wiring case below proves the site entry "
		+ "point was used rather than Redact.")]
	public void Marker_ShouldBeRemovedByRedactSiteTextOnly() {
		// Arrange

		// Act
		string redact = SensitiveErrorTextRedactor.Redact(SiteMessage);
		string site = SensitiveErrorTextRedactor.RedactSiteText(SiteMessage);

		// Assert
		redact.Should().Contain(Marker, because: "Redact keeps a sign-in phrase, which clio's own CLI text may hold");
		site.Should().NotContain(Marker, because: "RedactSiteText removes the login the database named");
		site.Should().Contain("Login failed for user", because: "the site's reason stays");
	}

	[Test]
	[Category("Unit")]
	[Description("site-response.message and site-response.code (SiteResponse.Carry) go through RedactSiteText.")]
	public void SiteResponse_ShouldCarryMessageAndCode_ThroughRedactSiteText() {
		// Arrange
		string body = JsonSerializer.Serialize(new { errorInfo = new { message = SiteMessage, errorCode = SiteMessage } });

		// Act
		SiteResponse response = SiteResponse.FromRefusal(new SiteHttpResponse(500, body));

		// Assert
		response.Message.Should().Be("Login failed for user '[redacted]'.", because: "the message is the site's text");
		response.Code.Should().NotContain(Marker, because: "the code is the site's text too");
	}

	[Test]
	[Category("Unit")]
	[Description("A site message longer than the cap is clamped before the redaction and still capped after it, so a "
		+ "page-sized message neither times out the chain nor exceeds the 2,000-character cap.")]
	public void SiteResponse_ShouldKeepTheCap_WhenTheSiteMessageIsPageSized() {
		// Arrange
		string body = JsonSerializer.Serialize(new {
			errorInfo = new { message = SiteMessage + " " + string.Join(' ', Enumerable.Repeat("word", 190_000)) }
		});

		// Act
		SiteResponse response = SiteResponse.FromRefusal(new SiteHttpResponse(500, body));

		// Assert
		response.Message.Should().StartWith("Login failed for user '[redacted]'.",
			because: "the reason at the start survives; the chain did not fail closed");
		response.Message.Length.Should().Be(SiteResponse.MaxMessageLength, because: "the existing cap is kept");
	}

	[Test]
	[Category("Unit")]
	[Description("The OData server error of a keyed write (odata-update, and odata-delete through the record deletion "
		+ "service) goes through RedactSiteText, the FK hint appender included.")]
	public void KeyedWrite_ShouldRedactTheODataServerError_AsSiteText() {
		// Arrange

		// Act
		string error = ODataKeyedWrite.ValidateWriteResponse(ODataError);

		// Assert
		error.Should().NotContain(Marker, because: "CreatioResponseError's extracted message is the site's text");
		error.Should().Contain("Login failed for user", because: "the reason stays");
	}

	[Test]
	[Category("Unit")]
	[Description("The up-to-500-character preview of a non-JSON body (DescribeNonJsonResponse) goes through "
		+ "RedactSiteText: the sign-in name goes, and so does a stack trace after the reason.")]
	public void NonJsonPreview_ShouldRedactTheBody_AsSiteText() {
		// Arrange
		string body = SiteMessage + "\r\n   at Terrasoft.Core.Entities.Entity.Save()";

		// Act
		string error = ODataKeyedWrite.ValidateWriteResponse(body);

		// Assert
		error.Should().NotContain(Marker).And.NotContain("Terrasoft.Core",
			because: "the preview is the site's text, cut before its stack trace");
		error.Should().Contain("Response: Login failed for user '[redacted]'.", because: "the reason stays in the preview");
	}

	[TestCase("rejected eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJpbnZlbnRlZCJ9.aW52ZW50ZWRzaWduYXR1cmU", "eyJ",
		TestName = "NonJsonPreview_JwtAcrossTheCut")]
	[TestCase("Login failed for user 'zq_invented_login'.", "zq_", TestName = "NonJsonPreview_SignInNameAcrossTheCut")]
	[Category("Unit")]
	[Description("The non-JSON preview is redacted BEFORE it is cut to 500 characters: a JWT or a quoted sign-in name "
		+ "that crosses the cut goes whole instead of leaving the half before the cut, which matched no rule.")]
	public void NonJsonPreview_ShouldRedactBeforeTheCut_WhenASecretCrossesIt(string secretText, string fragment) {
		// Arrange
		string body = new string('x', 494 - secretText.IndexOf(fragment, StringComparison.Ordinal)) + " " + secretText;

		// Act
		string error = CreatioResponseError.DescribeNonJsonResponse(body);

		// Assert
		error.Should().NotContain(fragment, because: "no part of the secret may survive the preview's cut");
	}

	[Test]
	[Category("Unit")]
	[Description("A refused delete's inner message is not cut before the redaction either: a sign-in name after a long "
		+ "URI, crossing the old 500-character cut, goes whole.")]
	public void RecordDeletionInnerMessage_ShouldRedactBeforeAnyCut_WhenASecretCrossesTheOldCut() {
		// Arrange
		string message = "See https://" + new string('a', 450) + ".invalid/x Login failed for user 'zq_invented_login'.";
		string body = JsonSerializer.Serialize(new {
			error = new { message = "An error has occurred.", innererror = new { message } }
		});

		// Act
		string inner = RecordDeletionService.ReadInnerMessage(body);

		// Assert
		inner.Should().NotContain("zq_", because: "the name crossed the old cut and must still go");
		inner.Should().Contain("Login failed for user '[redacted]'", because: "the reason stays");
	}

	[Test]
	[Category("Unit")]
	[Description("odata-create's per-row error, its site-response and its diagnostic message all go through the site "
		+ "entry point, for a JSON OData error and for a non-JSON body.")]
	public void ODataCreate_ShouldKeepTheMarkerOutOfTheSerializedResult(
		[Values(400, 502)] int status, [Values(true, false)] bool json) {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient, IStatusPreservingApplicationClient>();
		((IStatusPreservingApplicationClient)client)
			.SendKeepingStatus(default, default, default, default, default, default, default)
			.ReturnsForAnyArgs(new SiteHttpResponse(status, json ? ODataError : SiteMessage));
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		resolver.ResolvePair<IApplicationClient, IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>())
			.Returns((client, urlBuilder));
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio.invalid/odata/Account");
		MockFileSystem fileSystem = new();
		ODataCreateTool tool = new(resolver, new OperationCorrelationIdProvider(),
			new ODataFileContract(fileSystem, new MockConfinedFileAccess(fileSystem)));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = JsonDocument.Parse("[{\"Name\":\"Acme\"}]").RootElement
		});

		// Assert
		response.Failed.Should().Be(1, because: "the site refused the row");
		JsonSerializer.Serialize(response).Should().NotContain(Marker,
			because: "no field of the result may carry the site's text past RedactSiteText");
	}

	[Test]
	[Category("Unit")]
	[Description("The inner message of a refused delete (odata-delete with ids, trigger smoke) goes through the fenced "
		+ "site rendering.")]
	public void RecordDeletionInnerMessage_ShouldBeFencedSiteText() {
		// Arrange
		string body = JsonSerializer.Serialize(new {
			error = new { message = "An error has occurred.", innererror = new { message = SiteMessage } }
		});

		// Act
		string inner = RecordDeletionService.ReadInnerMessage(body);

		// Assert
		inner.Should().Be("[untrusted-source-text begin] Login failed for user '[redacted]'. [untrusted-source-text end]",
			because: "the inner exception is the site's text");
	}

	[Test]
	[Category("Unit")]
	[Description("DataWriteDiagnostic.message goes through the fenced site rendering.")]
	public void DataWriteDiagnostic_ShouldCarryTheMessage_AsFencedSiteText() {
		// Arrange

		// Act
		DataWriteDiagnostic diagnostic = DataWriteDiagnostic.Create("insert", "Account", null, true, true, false, SiteMessage);

		// Assert
		diagnostic.Message.Should().NotContain(Marker, because: "the write's error text carries the site's words");
		diagnostic.Message.Should().Contain("Login failed for user", because: "the reason stays");
	}

	[Test]
	[Category("Unit")]
	[Description("The DataService envelope's errorInfo.message (DataServiceResponse) goes through RedactSiteText before "
		+ "it becomes the exception message every DataService write tool reports.")]
	public void DataServiceResponse_ShouldRedactTheServiceMessage_AsSiteText() {
		// Arrange
		string body = JsonSerializer.Serialize(new { success = false, errorInfo = new { message = SiteMessage } });

		// Act
		Action act = () => DataServiceResponse.ThrowIfUnsuccessful(body, "Write");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.Which.Message.Should().Be("Write failed: Login failed for user '[redacted]'.",
				because: "the service's message is the site's text");
	}

	[Test]
	[Category("Unit")]
	[Description("The designer's save error (PageSchemaMetadataHelper: create-page, update-page, sync-pages, the schema "
		+ "designers) goes through RedactSiteText; clio's default sentence is returned as it is.")]
	public void DesignerSaveError_ShouldBeRedactedAsSiteText() {
		// Arrange
		JObject failed = JObject.Parse(JsonSerializer.Serialize(new { success = false, errorInfo = new { message = SiteMessage } }));
		JObject empty = JObject.Parse("{\"success\":false}");

		// Act
		string error = PageSchemaMetadataHelper.ParseSaveErrorMessage(failed, "Failed to save page schema");
		string fallback = PageSchemaMetadataHelper.ParseSaveErrorMessage(empty, "Failed to save page schema");

		// Assert
		error.Should().Be("Login failed for user '[redacted]'.", because: "the designer's message is the site's text");
		fallback.Should().Be("Failed to save page schema", because: "clio's own sentence is not site text");
	}

	[Test]
	[Category("Unit")]
	[Description("compile-status: the compiler's diagnostics in the history and the message tail of a tracked compile "
		+ "go through the site entry point.")]
	public void CompileStatus_ShouldRedactCompilerTextAndMessageTail_AsSiteText() {
		// Arrange
		PackageBuildDiagnostic diagnostic = new("CS0103", SiteMessage, "C:\\inetpub\\x.cs", 1, 2, false);
		CompileOperationRegistry registry = new();
		CompileOperationRecord begun = registry.Begin("tenant", "dev", "UsrPkg");

		// Act
		string rendered = CompileStatusTool.RenderError(diagnostic);
		CompileOperationRecord finished = registry.Finish(begun.OperationId, 1, [new ErrorMessage(SiteMessage)]);

		// Assert
		rendered.Should().NotContain(Marker, because: "the compiler's text is the environment's");
		finished.MessageTail.Should().ContainSingle().Which.Should().NotContain(Marker,
			because: "the compile output tail quotes the site");
	}

	[Test]
	[Category("Unit")]
	[Description("The inverse: clio-run's serialized envelope keeps using Redact, so the site-only shapes, a version "
		+ "number and a JSON escape survive there.")]
	public void ClioRunEnvelope_ShouldKeepUsingRedact() {
		// Arrange
		string text = JsonSerializer.Serialize(new {
			success = false, error = SiteMessage + " clio 8.1.0.5 wrote ./MyPkg/descriptor.json\nPassword=hunter2"
		});
		CallToolResult result = new() { IsError = true, Content = [new TextContentBlock { Text = text }] };

		// Act
		ClioRunExecutor.RedactFailureContent(result);

		// Assert
		string error = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text)
			.RootElement.GetProperty("error").GetString();
		error.Should().Be(SiteMessage + " clio 8.1.0.5 wrote ./MyPkg/descriptor.json\nPassword=[redacted]",
			because: "the envelope is clio's own: Redact removes the credential and nothing only a site's text may lose");
	}

	[Test]
	[Category("Unit")]
	[Description("The inverse: clio's own log lines on a passthrough request keep using Redact.")]
	public void PassthroughLogLines_ShouldKeepUsingRedact() {
		// Arrange
		List<LogMessage> messages = [new InfoMessage("Wrote ./MyPkg/descriptor.json for user 'operator_login' token=abc123")];

		// Act
		McpPassthroughRedaction.RedactForPassthrough(messages, ToolCommandResolver.PassthroughKeyPrefix + "tenant");

		// Assert
		messages.Single().Value.Should().Be("Wrote ./MyPkg/descriptor.json for user 'operator_login' token=[redacted]",
			because: "a clio log line is not site text");
	}

	#region Source guard

	// Every call of an extractor that hands a caller text the SITE wrote, and how that call is wired. A call site
	// that appears in clio without an entry here fails SourceGuard_ShouldKnowEverySiteTextExtractorCallSite: add
	// it with a wiring case in this fixture (or in the fixture of the tool it belongs to) before adding it here.
	//   "central": the extractor itself applies the site entry point (asserted by CentralExtractors below);
	//   "call site": the caller must pass what it extracted to RedactSiteText and nowhere else (asserted by
	//                SourceGuard_ShouldSeeTheExtractedTextFlowOnlyIntoRedactSiteText_AtEveryCallSite);
	//   "discarded": the extracted text is thrown away (an "out _" discard) and reaches no caller;
	//   "follow-up": KNOWN-UNWIRED - the text reaches a caller through Redact only, and the follow-up after A2 wires
	//                it (see KnownSiteErrorReads for what that means).
	private static readonly Dictionary<string, (int Count, string Wiring)> KnownCallSites = new(StringComparer.Ordinal) {
		["SiteResponse.FromRefusal|clio/Command/McpServer/Tools/ODataCreateTool.cs"] = (2, "central"),
		["SiteResponse.FromRefusal|clio/Command/McpServer/Tools/ODataUpdateTool.cs"] = (1, "central"),
		["SiteResponse.FromRefusal|clio/Command/RuntimeVerification/RecordDeletionService.cs"] = (1, "central"),
		["SiteResponse.FromRefusal|clio/Common/SiteResponseCapture.cs"] = (1, "central"),
		["CreatioResponseError.TryDetect|clio/Command/Branding/BrandingBackgroundService.cs"] = (1, "call site"),
		["CreatioResponseError.TryDetect|clio/Command/McpServer/Tools/EmailTemplateTool.cs"] = (2, "call site"),
		["CreatioResponseError.TryDetect|clio/Command/McpServer/Tools/ODataCreateTool.cs"] = (1, "call site"),
		["CreatioResponseError.TryDetect|clio/Command/McpServer/Tools/ODataFieldValidation.cs"] = (2, "call site"),
		["CreatioResponseError.TryDetect|clio/Command/McpServer/Tools/ODataKeyedWrite.cs"] = (1, "call site"),
		["CreatioResponseError.TryDetect|clio/Command/RuntimeVerification/TriggerSmokeRunner.cs"] = (1, "call site"),
		["CreatioResponseError.TryDetect|clio/Query/DataServiceQuery.cs"] = (1, "discarded"),
		// TryClassify's server detail goes to odata-read's debug channel through UntrustedText.Fenced (Redact).
		["CreatioResponseError.TryClassify|clio/Command/McpServer/Tools/ODataReadTool.cs"] = (1, "follow-up"),
		["CreatioResponseError.TryClassify|clio/Command/McpServer/Tools/ODataFileContract.cs"] = (1, "discarded"),
		// The overload that returns no text at all, only whether the body is an error.
		["CreatioResponseError.TryClassify|clio/Command/CompileBusinessProcessCommand.cs"] = (1, "discarded"),
		["CreatioResponseError.DescribeNonJsonResponse|clio/Command/McpServer/Tools/ODataCreateTool.cs"] = (1, "central"),
		["CreatioResponseError.DescribeNonJsonResponse|clio/Command/McpServer/Tools/ODataFieldValidation.cs"] = (3, "central"),
		["CreatioResponseError.DescribeNonJsonResponse|clio/Command/McpServer/Tools/ODataKeyedWrite.cs"] = (1, "central"),
		["RecordDeletionService.ReadInnerMessage|clio/Command/RuntimeVerification/RecordDeletionService.cs"] = (1, "central"),
		["RecordDeletionService.ReadInnerMessage|clio/Command/RuntimeVerification/TriggerSmokeRunner.cs"] = (1, "central"),
		["PageSchemaMetadataHelper.ParseSaveErrorMessage|clio/Command/ClientUnitSchemaCreate.cs"] = (1, "central"),
		["PageSchemaMetadataHelper.ParseSaveErrorMessage|clio/Command/LocalizePageCommand.cs"] = (1, "central"),
		["PageSchemaMetadataHelper.ParseSaveErrorMessage|clio/Command/PageCreateOptions.cs"] = (1, "central"),
		["PageSchemaMetadataHelper.ParseSaveErrorMessage|clio/Command/PageUpdateOptions.cs"] = (1, "central"),
		["PageSchemaMetadataHelper.ParseSaveErrorMessage|clio/Command/SchemaDesignerHelper.cs"] = (1, "central"),
		["DataServiceResponse.ThrowIfUnsuccessful|clio/Command/ApplicationSectionLocalization.cs"] = (4, "central"),
		["DataServiceResponse.ThrowIfUnsuccessful|clio/Command/DataBindingDbCommand.cs"] = (5, "central"),
		["DataServiceResponse.ThrowIfUnsuccessful|clio/Command/DataBindingDbLocalizer.cs"] = (2, "central"),
		["DataServiceResponse.ThrowIfUnsuccessful|clio/Command/PackageDataBindingWriter.cs"] = (2, "central"),
		// The write-tool paths (A2): update-page, sync-pages, create-page, SelectQuery, package installation and the
		// business-process tools.
		["PageSchemaMetadataHelper.DescribeRefusal|clio/Command/PageDesignerHierarchyClient.cs"] = (2, "central"),
		["PageSchemaMetadataHelper.DescribeRefusal|clio/Command/SchemaTemplateCatalog.cs"] = (1, "central"),
		["SelectQueryHelper.DescribeServerFailure|clio/Command/ApplicationInfoService.cs"] = (1, "central"),
		["SelectQueryHelper.DescribeServerFailure|clio/Package/SelectQueryHelper.cs"] = (1, "central"),
		["InstallLogAnalyzer.DescribeFailure|clio/Package/BasePackageInstaller.cs"] = (1, "central"),
		["ProcessDesignServerFault.ReadServerText|clio/Command/BuildCaseClient.cs"] = (1, "central"),
		["ProcessDesignServerFault.ReadServerText|clio/Command/CreateBusinessProcessCommand.cs"] = (1, "central"),
		["ProcessDesignServerFault.ReadServerText|clio/Command/ModifyBusinessProcessCommand.cs"] = (1, "central"),
		["ProcessDesignServerFault.ReadServerText|clio/Command/ModifyProcessAsNewVersionCommand.cs"] = (1, "central"),
	};

	// The extractors and the call shape of each, as found in clio's CODE (never in a comment or a string).
	private static readonly (string Name, Regex Call)[] Extractors = [
		("SiteResponse.FromRefusal", new Regex(@"\bSiteResponse\.FromRefusal\(")),
		("CreatioResponseError.TryDetect", new Regex(@"\bCreatioResponseError\.TryDetect\(")),
		("CreatioResponseError.TryClassify", new Regex(@"\bCreatioResponseError\.TryClassify\(")),
		("CreatioResponseError.DescribeNonJsonResponse", new Regex(@"(?<!static string )\bDescribeNonJsonResponse\(")),
		("RecordDeletionService.ReadInnerMessage", new Regex(@"(?<!static string )\bReadInnerMessage\(")),
		("PageSchemaMetadataHelper.ParseSaveErrorMessage", new Regex(@"(?<!static string )\bParseSaveErrorMessage\(")),
		("DataServiceResponse.ThrowIfUnsuccessful", new Regex(@"\bDataServiceResponse\.ThrowIfUnsuccessful\(")),
		("PageSchemaMetadataHelper.DescribeRefusal", new Regex(@"(?<!static string )\bDescribeRefusal\(")),
		("SelectQueryHelper.DescribeServerFailure", new Regex(@"(?<!static string )\bDescribeServerFailure\(")),
		("InstallLogAnalyzer.DescribeFailure", new Regex(@"\bInstallLogAnalyzer\.DescribeFailure\(")),
		("ProcessDesignServerFault.ReadServerText", new Regex(@"\bProcessDesignServerFault\.ReadServerText\(")),
	];

	// Where each "central" extractor - and the two fenced item paths - applies the site entry point: the file, the
	// member that must hold it, and the call that must appear inside that member.
	private static readonly (string File, string Member, string Call)[] CentralExtractors = [
		("clio/Common/SiteResponse.cs", "private static string Carry(", "SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Common/CreatioResponseError.cs", "internal static string DescribeNonJsonResponse(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Command/RuntimeVerification/RecordDeletionService.cs", "internal static string ReadInnerMessage(",
			"UntrustedText.FencedSiteText("),
		("clio/Command/PageSchemaMetadataHelper.cs", "internal static string ParseSaveErrorMessage(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Common/DataServiceResponse.cs", "internal static void ThrowIfUnsuccessful(string response",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Command/DataServiceBatchCommand.cs", "private static string ReadError(",
			"SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull("),
		("clio/Common/DataWriteDiagnostic.cs", "public static DataWriteDiagnostic Create(",
			"SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(message)"),
		("clio/Command/McpServer/Tools/CompileStatusTool.cs", "internal static string RenderError(",
			"UntrustedText.FencedSiteText("),
		("clio/Command/McpServer/Tools/CompileOperationRegistry.cs", "private static IReadOnlyList<string> BuildMessageTail(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		// The write-tool paths (A2). A member with several site-text parts is listed once per part, so reverting any
		// one of them fails here.
		("clio/Command/PageSchemaMetadataHelper.cs", "internal static string DescribeRefusal(",
			"SiteValue(errorInfo["),
		("clio/Command/PageSchemaMetadataHelper.cs", "private static string SiteValue(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Command/PageSchemaMetadataHelper.cs", "private static string AddonErrorText(",
			"SiteValue(error["),
		("clio/Command/PageUpdateOptions.cs", "private bool TryGetSchema(", "SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Command/PageCreateOptions.cs", "private bool TryLoadTemplateLocalizableStrings(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Command/SchemaTemplateCatalog.cs", "private static string BuildNonJsonErrorMessage(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Command/EntitySchemaDesigner/RemoteEntitySchemaDesignerClient.cs", "private static TResponse EnsureSuccess<TResponse>(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Package/SelectQueryHelper.cs", "internal static string DescribeServerFailure(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Package/SelectQueryHelper.cs", "internal static string DescribeServerFailure(", "UntrustedText.FencedSiteText("),
		("clio/Package/ServiceResponseJsonGuard.cs", "private static string BuildResponsePreview(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Command/CompileConfigurationCommand.cs", "protected override void ProceedResponse(", "SiteText(errorInfo)"),
		("clio/Command/CompileConfigurationCommand.cs", "protected override void ProceedResponse(",
			"diagnostic.WithSiteTextRedacted()"),
		("clio/Command/CompileConfigurationCommand.cs", "private void ApplyEnvironmentVerdict(",
			"SiteText(diagnostic.errorText)"),
		("clio/Command/CompileConfigurationCommand.cs", "internal static string FormatHistoryDiagnostics(",
			"diagnostic.WithSiteTextRedacted()"),
		("clio/Command/CompileConfigurationCommand.cs", "internal static string FormatHistoryDiagnostics(",
			"SiteText(errorsWarnings)"),
		("clio/Command/CompileConfigurationCommand.cs", "private static string SiteText(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Package/PackageBuildResult.cs", "private static string Site(", "SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Package/PackageBuildResult.cs", "internal PackageBuildDiagnostic WithSiteTextRedacted(",
			"Site(FileNameOnly(FileName))"),
		("clio/Command/CompileConfigurationCommand.cs", "private void ApplyEnvironmentVerdict(",
			"SiteText(PackageBuildDiagnostic.FileNameOnly(diagnostic.fileName))"),
		("clio/Package/PackageBuilder.cs", "private void Fail(", "diagnostic.WithSiteTextRedacted()"),
		("clio/Package/PackageBuilder.cs", "private void Fail(", "SensitiveErrorTextRedactor.RedactSiteText(rawHistoryErrors)"),
		("clio/Package/PackageBuilder.cs", "private void Fail(", "SensitiveErrorTextRedactor.RedactSiteText(errorMessage)"),
		("clio/Package/InstallLogAnalyzer.cs", "public static string DescribeFailure(",
			"SiteText(CompilerLineWithFileNameOnly(compilationErrorLine))"),
		("clio/Package/InstallLogAnalyzer.cs", "public static string DescribeFailure(", "SiteText(message)"),
		("clio/Package/InstallLogAnalyzer.cs", "public static string DescribeFailure(", "SiteText(errorCode)"),
		("clio/Package/InstallLogAnalyzer.cs", "public static string DescribeFailure(", "SiteTail(MeaningfulTailLines("),
		("clio/Package/InstallLogAnalyzer.cs", "private static string SiteTail(", "SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Package/InstallLogAnalyzer.cs", "private static string SiteText(", "SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Package/BasePackageInstaller.cs", "private static string GetInvalidGZipArchiveMessage(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Command/ProcessModel/ProcessDesignServerFault.cs", "internal static string? ReadServerText(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Command/ProcessModel/ProcessDesignServerFault.cs", "internal static string? ReadServerText(",
			"SensitiveErrorTextRedactor.ClampAtBoundary("),
		("clio/Command/ProcessModel/ProcessDesignServerFault.cs", "internal static string SiteText(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Command/ProcessModel/ProcessDesignServerFault.cs", "internal static string SiteMessageOr(",
			"SiteText(errorMessage)"),
		("clio/Command/ProcessModel/ProcessDesignServerFault.cs", "internal static string SiteWarnings(",
			".Select(SiteText)"),
		("clio/Command/ProcessModel/ProcessDesignServerFault.cs", "internal static IReadOnlyList<string>? SiteWarningList(",
			".Select(SiteText)"),
		("clio/Command/CreateBusinessProcessCommand.cs", "private static CreateBusinessProcessResult ReadBuildProcessAnswer(",
			"ProcessDesignServerFault.SiteWarningList(result.Warnings)"),
		("clio/Command/ModifyBusinessProcessCommand.cs", "public ModifyBusinessProcessResult ModifyProcess(",
			"ProcessDesignServerFault.SiteWarningList(result.Warnings)"),
		("clio/Command/ModifyProcessAsNewVersionCommand.cs", "public ModifyProcessAsNewVersionResult ModifyAsNewVersion(",
			"ProcessDesignServerFault.SiteWarningList(result.Warnings)"),
		("clio/Command/SetActiveProcessVersionCommand.cs", "public SetActiveProcessVersionResult SetActiveVersion(",
			"ProcessDesignServerFault.SiteWarningList(result.Warnings)"),
		("clio/Command/ProcessModel/LayoutChangeRelay.cs", "public string ElementsClause(",
			".Select(ProcessDesignServerFault.SiteText)"),
		("clio/Command/CreateBusinessProcessCommand.cs", "private static CreateBusinessProcessResult ReadBuildProcessAnswer(",
			"ProcessDesignServerFault.SiteMessageOr(result.ErrorMessage,"),
		("clio/Command/CreateBusinessProcessCommand.cs", "private static CreateBusinessProcessResult ReadBuildProcessAnswer(",
			"ProcessDesignServerFault.SiteWarnings(result.Warnings)"),
		("clio/Command/ModifyBusinessProcessCommand.cs", "private static string BuildFailureMessage(",
			"ProcessDesignServerFault.SiteMessageOr(result.ErrorMessage,"),
		("clio/Command/ModifyBusinessProcessCommand.cs", "private static string BuildFailureMessage(",
			"ProcessDesignServerFault.SiteWarnings(result.Warnings)"),
		("clio/Command/ModifyProcessAsNewVersionCommand.cs", "private static string BuildFailureMessage(",
			"ProcessDesignServerFault.SiteMessageOr(result.ErrorMessage,"),
		("clio/Command/ModifyProcessAsNewVersionCommand.cs", "private static string BuildFailureMessage(",
			"ProcessDesignServerFault.SiteText(result.VersionName)"),
		("clio/Command/ModifyProcessAsNewVersionCommand.cs", "private static string BuildFailureMessage(",
			"ProcessDesignServerFault.SiteWarnings(result.Warnings)"),
		("clio/Command/SetActiveProcessVersionCommand.cs", "private static string BuildFailureMessage(",
			"ProcessDesignServerFault.SiteMessageOr(result.ErrorMessage,"),
		("clio/Command/SetActiveProcessVersionCommand.cs", "private static string BuildFailureMessage(",
			"ProcessDesignServerFault.SiteText(result.ActiveVersionName)"),
		("clio/Command/SetActiveProcessVersionCommand.cs", "private static string BuildFailureMessage(",
			"ProcessDesignServerFault.SiteWarnings(result.Warnings)"),
		("clio/Command/BuildCaseClient.cs", "private static string SiteDetail(", "SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Command/BuildCaseClient.cs", "private static string SiteDetail(", "SensitiveErrorTextRedactor.ClampAtBoundary("),
		("clio/Command/DynamicCaseService.cs", "private static string SiteText(", "SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Command/DynamicCaseService.cs", "public DescribeDynamicCaseResult Describe(", "SiteText(response.ErrorMessage)"),
		("clio/Command/DynamicCaseService.cs", "public ModifyDynamicCaseResult Modify(", "SiteText(response.ErrorMessage)"),
		("clio/Command/DynamicCaseService.cs", "private DynamicCaseResult FromServer(", "SiteText(response.ErrorMessage)"),
		("clio/Command/SchemaTemplateCatalog.cs", "private static string BuildNonJsonErrorMessage(",
			"SensitiveErrorTextRedactor.ClampAtBoundary("),
		("clio/Package/ServiceResponseJsonGuard.cs", "private static string BuildResponsePreview(",
			"SensitiveErrorTextRedactor.ClampAtBoundary("),
		("clio/Command/CompileBusinessProcessCommand.cs", "private void ReportFailure(", "UntrustedText.FencedSiteText("),
		("clio/Command/CompileBusinessProcessCommand.cs", "private static string ServerName(", "UntrustedText.FencedSiteText("),
	];

	// Members that read only site text: the general entry points (Redact, the plain fence, the console renderings)
	// must not come back into them, because a stdio log is not redacted again and only RedactSiteText takes the
	// site-only shapes.
	private static readonly (string File, string Member)[] SiteOnlyMembers = [
		("clio/Command/PageSchemaMetadataHelper.cs", "internal static string DescribeRefusal("),
		("clio/Command/PageSchemaMetadataHelper.cs", "private static string SiteValue("),
		("clio/Command/PageSchemaMetadataHelper.cs", "internal static string ParseSaveErrorMessage("),
		("clio/Command/SchemaTemplateCatalog.cs", "private static string BuildNonJsonErrorMessage("),
		("clio/Package/SelectQueryHelper.cs", "internal static string DescribeServerFailure("),
		("clio/Package/ServiceResponseJsonGuard.cs", "private static string BuildResponsePreview("),
		("clio/Package/PackageBuilder.cs", "private void Fail("),
		("clio/Package/InstallLogAnalyzer.cs", "public static string DescribeFailure("),
		("clio/Command/ProcessModel/ProcessDesignServerFault.cs", "internal static string? ReadServerText("),
		("clio/Command/CompileBusinessProcessCommand.cs", "private void ReportFailure("),
		("clio/Command/CompileBusinessProcessCommand.cs", "private static string ServerName("),
		("clio/Command/ProcessModel/ProcessDesignServerFault.cs", "internal static string SiteText("),
		("clio/Command/ProcessModel/ProcessDesignServerFault.cs", "internal static string SiteWarnings("),
		("clio/Command/ProcessModel/ProcessDesignServerFault.cs", "internal static IReadOnlyList<string>? SiteWarningList("),
		("clio/Command/ProcessModel/LayoutChangeRelay.cs", "public string ElementsClause("),
		("clio/Command/ModifyProcessAsNewVersionCommand.cs", "private static string BuildFailureMessage("),
		("clio/Command/SetActiveProcessVersionCommand.cs", "private static string BuildFailureMessage("),
		("clio/Command/ModifyBusinessProcessCommand.cs", "private static string BuildFailureMessage("),
		("clio/Command/BuildCaseClient.cs", "private static string SiteDetail("),
		("clio/Command/DynamicCaseService.cs", "private static string SiteText("),
		("clio/Package/InstallLogAnalyzer.cs", "private static string SiteTail("),
	];

	// The general entry points a site-only member must not call.
	private static readonly string[] GeneralRedactionCalls = [
		"SensitiveErrorTextRedactor.Redact(", "UntrustedText.Fenced(", "RedactUntrustedOrNull(", "UntrustedText.Scrub(",
		"UntrustedText.ForConsole(", "RedactForConsoleOrNull("
	];

	// The calls that apply the site entry point; a "call site" extraction must reach a caller only inside one.
	private static readonly Regex SiteEntryCall = new(
		@"\b(?:SensitiveErrorTextRedactor\.(?:RedactSiteText|RedactUntrustedSiteTextOrNull)|UntrustedText\.FencedSiteText)\(");

	// The variable an extractor call writes its text to: the last "out" argument of the call.
	private static readonly Regex OutArgument = new(@"\bout\s+(?:[\w.?<>\[\]]+\s+)?(?<name>[A-Za-z_]\w*)\s*$");

	// DIRECT READS OF A SITE'S ERROR TEXT, outside the extractors above: the JSON names a Creatio site writes its
	// error text under (errorInfo, responseStatus, ExceptionMessage, errorMessage, OData's innererror) read as a
	// literal, the deserialized members that hold it (.ErrorInfo, .ResponseStatus, .ExceptionMessage), and the two
	// helpers that put a site's text into a message (ProcessDesignServerFault.ServerSaid,
	// ServerReportedFailureText.Describe). A [JsonPropertyName]/[JsonProperty]/[DataMember] declaration is not a read.
	private static readonly Regex SiteErrorNameLiteral = new(
		@"""(?:errorInfo|ErrorInfo|responseStatus|ResponseStatus|exceptionMessage|ExceptionMessage|errorMessage|ErrorMessage"
		+ @"|innererror|internalexception)""");

	private static readonly Regex SiteErrorMemberRead = new(@"(?<=[\w)\]?!])\.(?:ErrorInfo|ResponseStatus|ExceptionMessage)\b");

	private static readonly Regex SiteTextHelperCall = new(
		@"(?<!\bstring\s+)\bServerSaid\(|\bServerReportedFailureText\.Describe\(");

	private static readonly Regex PropertyNameDeclaration = new(@"\b(?:JsonPropertyName|JsonProperty|DataMember)\(");

	// A [JsonPropertyName]/[JsonProperty] decoration with a site-error name, and the C# member it decorates.
	private static readonly Regex SiteErrorNameDecoration = new(
		@"\bJson(?:PropertyName|Property)\(""(?:errorInfo|ErrorInfo|responseStatus|ResponseStatus|exceptionMessage|ExceptionMessage"
		+ @"|errorMessage|ErrorMessage|innererror|internalexception)""\)\]");

	private static readonly Regex DecoratedMemberName = new(
		@"\G(?:\s*\[[^\]]*\])*\s*(?:(?:public|internal|private|protected|required|virtual|override|static|readonly|new)\s+)*"
		+ @"[\w.<>\[\]?, ]+?\s+(?<name>[A-Za-z_]\w*)\s*(?:\{|=|,|\)|;)");

	// The declaration of a constant or a read-only field, read from the member's start to the site-error name in it.
	private static readonly Regex NameConstantDeclaration = new(@"\b(?:const|readonly)\b[^;{(]*=");

	// Every member of clio that reads a site's error text DIRECTLY, how many reads it holds, and how that text is
	// wired. A read in a member that is not listed here - or one more read in a listed member - fails
	// SourceGuard_ShouldKnowEveryDirectReadOfASitesErrorText: wire it through RedactSiteText and list it as wired,
	// or, only if the change that wires it is already agreed, list it as known-unwired with that change.
	//   "wired: <extractor>": the text reaches a caller only through an extractor this fixture already checks -
	//                         a CentralExtractors member or a KnownCallSites extractor named after the colon;
	//   "follow-up": KNOWN-UNWIRED, an envelope reader the follow-up after A2 wires. Until then its text passes
	//                Redact (or Fenced/ForConsole, which use Redact) only - the site-only rules do not run on it;
	//   "not text: <why>": the name only recognizes a shape - a set of field names, a marker - and no site text
	//                      is extracted.
	// The known-unwired entries, with the follow-up TryClassify call site in KnownCallSites, are the exact list of
	// the readers of these shapes that still skip RedactSiteText. The write-tool files A2 wired (writePathFiles in
	// SourceGuard_ShouldClassifyEveryDirectReadAsWiredOrKnownUnwired) hold no follow-up entry; each of their reads has a
	// behaviour case in SiteTextWritePathWiringTests.
	private static readonly Dictionary<string, (int Count, string Wiring)> KnownSiteErrorReads = new(StringComparer.Ordinal) {
		// Wired: the text reaches a caller only through an extractor this fixture checks.
		["clio/Command/DataServiceBatchCommand.cs|ReadError"] = (2, "wired: DataServiceBatchCommand.ReadError"),
		["clio/Command/PageSchemaMetadataHelper.cs|ParseSaveErrorMessage"] = (1, "wired: PageSchemaMetadataHelper.ParseSaveErrorMessage"),
		["clio/Command/RuntimeVerification/RecordDeletionService.cs|ReadInnerMessage"] = (2, "wired: RecordDeletionService.ReadInnerMessage"),
		["clio/Common/CreatioResponseError.cs|TryDetectBaseResponse"] = (6, "wired: CreatioResponseError.TryDetect"),
		["clio/Common/CreatioResponseError.cs|TryDetectAspNetException"] = (2, "wired: CreatioResponseError.TryDetect"),
		["clio/Common/DataServiceResponse.cs|ThrowIfUnsuccessful"] = (2, "wired: DataServiceResponse.ThrowIfUnsuccessful"),
		["clio/Common/SiteResponse.cs|FindEnvelopeMessage"] = (4, "wired: SiteResponse.Carry"),
		["clio/Common/SiteResponse.cs|FindEnvelopeCode"] = (2, "wired: SiteResponse.Carry"),
		// Not text: a site error name used only to recognize a shape; no site text is extracted.
		["clio/Command/McpServer/Tools/ClioRunTool.cs|FailureFieldNames"] = (1, "not text: the failure field names of clio's own envelope"),
		["clio/Common/ServerReadinessWaiter.cs|DataServiceAnswerMarkers"] = (1, "not text: markers that tell a DataService answer apart"),
		["clio/Command/AddCustomLoggingCommand.cs|Execute"] = (1, "not text: clio's own logging configurator result"),
		["clio/Command/AddItemCommand.cs|Execute"] = (1, "not text: a validation failure of clio's own options"),
		["clio/Command/Command.cs|PrintErrors"] = (2, "not text: a validation failure of clio's own options"),
		["clio/Command/CreateUiProjectCommand.cs|Execute"] = (1, "not text: a validation failure of clio's own options"),
		["clio/Command/CreatioInstallCommand/CreatioInstallerService.cs|RestoreToLocalDb"] = (1, "not text: clio's own local database result"),
		["clio/Command/CreatioInstallCommand/CreatioInstallerService.cs|TryDisableForcedPasswordReset"] = (1, "not text: clio's own local database result"),
		["clio/Command/CreatioInstallCommand/CreatioInstallerService.cs|UpdateConnectionString"] = (2, "not text: clio's own local database result"),
		["clio/Command/EnvironmentRuntimeDetectionService.cs|Describe"] = (1, "not text: clio's own probe result, an exception message"),
		["clio/Command/ExternalLink.cs|PrintError"] = (2, "not text: a validation failure of clio's own options"),
		["clio/Command/LinkCoreSrcCommand.cs|Execute"] = (1, "not text: a validation failure of clio's own options"),
		["clio/Command/McpServer/Tools/ClioRunTool.cs|IsErrorFieldName"] = (1, "not text: reads the failure field-name set of clio's own envelope"),
		["clio/Command/OpenAppCommand.cs|Execute"] = (1, "not text: clio's own browser launch result"),
		["clio/Command/RegAppCommand.cs|Execute"] = (2, "not text: a validation failure of clio's own options"),
		["clio/Command/RestoreDb.cs|RestoreToLocalServer"] = (1, "not text: clio's own local database result"),
		["clio/Command/RingCommand.cs|Launch"] = (1, "not text: clio's own process launch result"),
		["clio/Command/TurnFarmModeCommand.cs|PrintErrors"] = (2, "not text: a validation failure of clio's own options"),
		["clio/Common/Assertions/LocalRedisAssertion.cs|ExecuteAsync"] = (2, "not text: clio's own local Redis discovery result"),
		["clio/Common/BrowserSession/AuthenticatedBrowserLauncher.cs|LaunchAsync"] = (1, "not text: clio's own browser launch result"),
		["clio/Common/ConsoleLogger.cs|PrintValidationFailureErrors"] = (2, "not text: a validation failure of clio's own options"),
		["clio/Common/CreatioHostService.cs|StartInBackground"] = (1, "not text: clio's own process launch result"),
		["clio/Common/Kubernetes/K8RedisAssertion.cs|ExecuteAsync"] = (1, "not text: clio's own Redis discovery result"),
		["clio/Common/ServerReadinessWaiter.cs|IsGenuineAuthenticatedJsonAnswer"] = (1, "not text: reads the markers that tell a DataService answer apart"),
		// Wired by A2: the write-tool and dynamic-case paths.
		["clio/Command/CompileBusinessProcessCommand.cs|Compile"] = (1, "wired: CompileBusinessProcessCommand.ReportFailure"),
		["clio/Command/CompileBusinessProcessCommand.cs|ReportFailure"] = (1, "wired: CompileBusinessProcessCommand.ReportFailure"),
		["clio/Command/CompileConfigurationCommand.cs|ProceedResponse"] = (1, "wired: CompileConfigurationCommand.ProceedResponse"),
		["clio/Command/CreateDynamicCaseCommand.cs|SaysNothingWasWritten"] = (1, "wired: DynamicCaseService.SiteText"),
		["clio/Command/CreateDynamicCaseCommand.cs|Verdict"] = (4, "wired: DynamicCaseService.SiteText"),
		["clio/Command/DescribeDynamicCaseCommand.cs|Verdict"] = (1, "wired: DynamicCaseService.SiteText"),
		["clio/Command/DynamicCaseService.cs|Describe"] = (1, "wired: DynamicCaseService.SiteText"),
		["clio/Command/DynamicCaseService.cs|FromServer"] = (1, "wired: DynamicCaseService.SiteText"),
		["clio/Command/DynamicCaseService.cs|Modify"] = (1, "wired: DynamicCaseService.SiteText"),
		["clio/Command/ModifyBusinessProcessCommand.cs|BuildFailureMessage"] = (1, "wired: ProcessDesignServerFault.SiteMessageOr"),
		["clio/Command/ModifyDynamicCaseCommand.cs|SaysNothingWasWritten"] = (1, "wired: DynamicCaseService.SiteText"),
		["clio/Command/ModifyDynamicCaseCommand.cs|Verdict"] = (4, "wired: DynamicCaseService.SiteText"),
		["clio/Package/PackageBuilder.cs|ApplyVerdict"] = (1, "wired: PackageBuilder.Fail"),
		// ServerSaid quotes ReadServerText's redacted fault; the answer's errorMessage goes through SiteMessageOr.
		["clio/Command/CreateBusinessProcessCommand.cs|ReadBuildProcessAnswer"] = (3, "wired: ProcessDesignServerFault.SiteMessageOr"),
		["clio/Command/EntitySchemaDesigner/RemoteEntitySchemaDesignerClient.cs|EnsureSuccess"] = (2, "wired: RemoteEntitySchemaDesignerClient.EnsureSuccess"),
		["clio/Command/ModifyBusinessProcessCommand.cs|ModifyProcess"] = (2, "wired: ProcessDesignServerFault.ReadServerText"),
		["clio/Command/ModifyProcessAsNewVersionCommand.cs|ModifyAsNewVersion"] = (2, "wired: ProcessDesignServerFault.ReadServerText"),
		["clio/Command/ModifyProcessAsNewVersionCommand.cs|BuildFailureMessage"] = (1, "wired: ProcessDesignServerFault.SiteMessageOr"),
		["clio/Command/SetActiveProcessVersionCommand.cs|BuildFailureMessage"] = (1, "wired: ProcessDesignServerFault.SiteMessageOr"),
		["clio/Command/PageCreateOptions.cs|TryLoadTemplateLocalizableStrings"] = (1, "wired: PageCreateOptions.TryLoadTemplateLocalizableStrings"),
		["clio/Command/PageSchemaMetadataHelper.cs|DescribeRefusal"] = (1, "wired: PageSchemaMetadataHelper.DescribeRefusal"),
		["clio/Command/PageSchemaMetadataHelper.cs|AddonErrorText"] = (2, "wired: PageSchemaMetadataHelper.AddonErrorText"),
		["clio/Command/PageUpdateOptions.cs|TryGetSchema"] = (1, "wired: PageUpdateOptions.TryGetSchema"),
		["clio/Package/BasePackageInstaller.cs|GetInvalidGZipArchiveMessage"] = (1, "wired: BasePackageInstaller.GetInvalidGZipArchiveMessage"),
		["clio/Package/InstallLogAnalyzer.cs|DescribeFailure"] = (2, "wired: InstallLogAnalyzer.DescribeFailure"),
		["clio/Package/PackageBuildResult.cs|TryParseResponse"] = (1, "wired: PackageBuilder.Fail"),
		["clio/Package/SelectQueryHelper.cs|ExecuteSelectQuery"] = (2, "wired: SelectQueryHelper.DescribeServerFailure"),
		// Not text, write-tool files: the site's error fields only decide what the failure is, or the message is clio's
		// own sentence; no site text of theirs is extracted.
		["clio/Package/BasePackageInstaller.cs|IsInvalidGZipArchiveFailure"] = (3, "not text: looks for the invalid-archive type name"),
		["clio/Package/InstallLogAnalyzer.cs|IsGenericInstallationFailure"] = (1, "not text: compares with the generic failure message"),
		["clio/Command/DynamicCasePageStep.cs|Plan"] = (2, "not text: the record page resolver's own sentence"),
		["clio/Command/DynamicCaseService.cs|PageInfo"] = (1, "not text: the page step's own plan, whose page reads are wired in update-page"),
		["clio/Command/DynamicCaseService.cs|PageRefusal"] = (1, "not text: the page step's own plan, whose page reads are wired in update-page"),
		// KNOWN-UNWIRED, follow-up: the envelope readers the follow-up after A2 wires. Their text passes Redact
		// (directly, or through UntrustedText.Fenced / ForConsole / ServerReportedFailureText) and no site-only rule.
		["clio/Command/FeatureStateService.cs|ThrowIfSaveFailed"] = (1, "follow-up"),
		// Read tools of the process design service (list-user-tasks, describe-business-process): they write nothing.
		["clio/Command/ListUserTasksCommand.cs|GetUserTasks"] = (1, "follow-up"),
		["clio/Command/ProcessModel/IProcessDescriber.cs|Describe"] = (1, "follow-up"),
		["clio/Command/SetFsmConfigCommand.cs|EnsurePlatformSupport"] = (1, "follow-up"),
		["clio/Common/ClassifyingDataProvider.cs|BatchExecute"] = (1, "follow-up"),
		["clio/Common/ClassifyingDataProvider.cs|ExecuteProcess"] = (1, "follow-up"),
		["clio/Common/ClassifyingDataProvider.cs|GetDefaultValues"] = (1, "follow-up"),
		["clio/Common/ClassifyingDataProvider.cs|GetItems"] = (1, "follow-up"),
		["clio/Common/CreatioResponseError.cs|AppendInnerMessages"] = (1, "follow-up"),
		["clio/Command/AddonSchemaDesigner/AddonSchemaDesignerClient.cs|GetSchema"] = (1, "follow-up"),
		["clio/Command/AddonSchemaDesigner/AddonSchemaDesignerClient.cs|SaveSchema"] = (1, "follow-up"),
		["clio/Command/AddonSchemaDesigner/AddonSchemaDesignerClient.cs|BuildConfiguration"] = (1, "follow-up"),
		["clio/Command/ApplicationCreateService.cs|ResolveRandomIconId"] = (1, "follow-up"),
		["clio/Command/ApplicationCreateService.cs|BuildFailureMessage"] = (2, "follow-up"),
		["clio/Command/ApplicationInfoService.cs|GetPrimaryPackage"] = (1, "follow-up"),
		["clio/Command/ApplicationInfoService.cs|LoadEntityInfo"] = (2, "follow-up"),
		["clio/Command/ApplicationListService.cs|GetApplicationsCore"] = (1, "follow-up"),
		["clio/Command/ApplicationSectionCreateCommand.cs|ClassifyInsertResponse"] = (3, "follow-up"),
		["clio/Command/ApplicationSectionCreateCommand.cs|SetIconBackground"] = (1, "follow-up"),
		["clio/Command/ApplicationSectionCreateCommand.cs|ResolveRandomIconId"] = (1, "follow-up"),
		["clio/Command/ApplicationSectionDeleteCommand.cs|GetSectionRecord"] = (1, "follow-up"),
		["clio/Command/ApplicationSectionDeleteCommand.cs|DeleteWorkspaceSchema"] = (1, "follow-up"),
		["clio/Command/ApplicationSectionDeleteCommand.cs|ExecuteDeleteQuery"] = (1, "follow-up"),
		["clio/Command/ApplicationSectionGetListCommand.cs|GetAllSectionRecords"] = (1, "follow-up"),
		["clio/Command/ApplicationSectionLocalization.cs|ReadLocalizations"] = (1, "follow-up"),
		["clio/Command/ApplicationSectionLocalization.cs|FindBindingUId"] = (1, "follow-up"),
		["clio/Command/ApplicationSectionUpdateCommand.cs|ExecuteSectionUpdate"] = (1, "follow-up"),
		["clio/Command/ApplicationSectionUpdateCommand.cs|GetSectionRecord"] = (1, "follow-up"),
		["clio/Command/CompileWorspaceCommand.cs|ApplyCreatioModel"] = (2, "follow-up"),
		["clio/Command/CreateUserTaskCommand.cs|EnsureResponseSucceeded"] = (2, "follow-up"),
		["clio/Command/DataServiceSelectResponse.cs|TryGetFailure"] = (3, "follow-up"),
		["clio/Command/DeletePackageCommand.cs|ProceedResponse"] = (1, "follow-up"),
		["clio/Command/DeleteSchemaCommand.cs|ReadFileDesignMode"] = (1, "follow-up"),
		["clio/Command/DeleteSchemaCommand.cs|TryDeleteRemote"] = (1, "follow-up"),
		["clio/Command/DeleteSchemaCommand.cs|EnsureDeleteSucceeded"] = (2, "follow-up"),
		["clio/Command/EntityConnections/EntityConnectionsReader.cs|TryReadFromService"] = (1, "follow-up"),
		["clio/Command/EnvironmentRuntimeDetectionService.cs|ValidateSelectQueryResponse"] = (1, "follow-up"),
		["clio/Command/GenerateSourceCodeCommand.cs|ProceedResponse"] = (2, "follow-up"),
		["clio/Command/IdentityProviderManagementCommand.cs|Post"] = (1, "follow-up"),
		["clio/Command/LocalizePageCommand.cs|TryGetSchema"] = (1, "follow-up"),
		["clio/Command/McpServer/Tools/ExecuteEsqTool.cs|TryDataServiceFailure"] = (3, "follow-up"),
		["clio/Command/McpServer/Tools/ExecuteEsqTool.cs|ExtractErrorMessage"] = (3, "follow-up"),
		["clio/Command/NavigationCacheResetter.cs|ReadFailure"] = (1, "follow-up"),
		["clio/Command/PackageCommand/LicenseResponseParser.cs|EnsureSuccess"] = (1, "follow-up"),
		["clio/Command/PageListOptions.cs|ResolvePrimaryPackageName"] = (1, "follow-up"),
		["clio/Command/RestoreFromPackageBackupCommand.cs|GetErrorMessage"] = (1, "follow-up"),
		["clio/Command/RunProcessCommand.cs|AddLoggedError"] = (1, "follow-up"),
		["clio/Command/RunProcessCommand.cs|BuildResponse"] = (1, "follow-up"),
		["clio/Command/SchemaDesignerHelper.cs|LoadSchema"] = (1, "follow-up"),
		["clio/Command/SchemaDesignerHelper.cs|CreateNewSchema"] = (1, "follow-up"),
		["clio/Command/SchemaTransfer/SchemaTransferClient.cs|FindLayers"] = (1, "follow-up"),
		["clio/Command/SchemaTransfer/SchemaTransferClient.cs|Export"] = (1, "follow-up"),
		["clio/Command/SchemaTransfer/SchemaTransferClient.cs|Import"] = (1, "follow-up"),
		["clio/Command/SequenceEnrollmentCommand.cs|ReadErrors"] = (1, "follow-up"),
		["clio/Command/SetFileContentStorageConnectionStringCommand.cs|HandleResponse"] = (1, "follow-up"),
		["clio/Command/SqlSchemaCreate.cs|ResolveDatabaseEngine"] = (1, "follow-up"),
		["clio/Command/SqlSchemaInstall.cs|TryInstall"] = (1, "follow-up"),
		["clio/Command/SysSettingsCommand.cs|TryCreateSysSetting"] = (1, "follow-up"),
		["clio/Command/SysSettingsCommand.cs|ReportProviderFailure"] = (1, "follow-up"),
		["clio/Command/Theming/ThemeServiceResponse.cs|TryGetFailure"] = (1, "follow-up"),
		["clio/Command/Theming/UserThemeApplier.cs|TryUpdateProfileTheme"] = (1, "follow-up"),
		["clio/Command/WebServiceDesigner/WebServiceDesignerClient.cs|Read"] = (1, "follow-up"),
		["clio/Command/WebServiceDesigner/WebServiceDesignerClient.cs|CallMethod"] = (1, "follow-up"),
		["clio/Command/WebServiceDesigner/WebServiceDesignerClient.cs|EnsureSucceeded"] = (1, "follow-up"),
		["clio/Common/ClassifyingDataProvider.cs|GenericMessage"] = (1, "follow-up"),
		["clio/Common/ClassifyingDataProvider.cs|ConsoleGenericMessage"] = (1, "follow-up"),
		["clio/Common/CreatioResponseError.cs|InnerErrorMemberNames"] = (2, "follow-up"),
		["clio/Common/DataForge/DataForgeReadClient.cs|FindSimilarTables"] = (1, "follow-up"),
		["clio/Common/DataForge/DataForgeReadClient.cs|FindSimilarLookups"] = (1, "follow-up"),
		["clio/Common/DataForge/DataForgeReadClient.cs|GetTableRelationships"] = (1, "follow-up"),
		["clio/Common/EntitySchema/RuntimeEntitySchemaReader.cs|GetByName"] = (1, "follow-up"),
		["clio/Common/ISysSettingsManager.cs|UpdateSysSetting"] = (1, "follow-up"),
		["clio/Common/ObjectRights/RightManagementServiceClient.cs|SendSave"] = (1, "follow-up"),
		["clio/Common/ObjectRights/RightManagementServiceClient.cs|TryFetchNode"] = (1, "follow-up"),
		["clio/Common/RecordFileUploader.cs|ReadUploadResponse"] = (1, "follow-up"),
		["clio/Common/SysImageUploader.cs|TryReadUploadError"] = (1, "follow-up"),
		["clio/Package/BasePackageOperation.cs|ThrowsErrorIfUnsuccessfulResponseReceived"] = (1, "follow-up"),
		["clio/Package/FileDesignModePackages.cs|TryGetIsFileDesignMode"] = (1, "follow-up"),
		["clio/Package/FileDesignModePackages.cs|LoadPackagesToStorage"] = (4, "follow-up"),
		["clio/Package/FileDesignModePackages.cs|SetFileDesignMode"] = (2, "follow-up"),
		["clio/Package/PackageDependencyManager.cs|LoadPackageProperties"] = (1, "follow-up"),
		["clio/Package/PackageDependencyManager.cs|BuildSaveErrorMessage"] = (1, "follow-up"),
		["clio/Package/RemotePackageCreator.cs|SendCreateRequest"] = (1, "follow-up"),
		["clio/Package/RemotePackageCreator.cs|ReadBack"] = (1, "follow-up"),
	};

	[Test]
	[Category("Unit")]
	[Description("A scan of clio's code finds exactly the known call sites of the site-text extractors: a new one "
		+ "fails here until it has a wiring case and an entry in KnownCallSites.")]
	public void SourceGuard_ShouldKnowEverySiteTextExtractorCallSite() {
		// Arrange
		Dictionary<string, int> found = new(StringComparer.Ordinal);

		// Act
		foreach (string file in ClioSources()) {
			CSharpSource source = CSharpSource.Read(Path.Combine(RepositoryRoot, file));
			foreach ((string name, Regex call) in Extractors) {
				int count = call.Matches(source.Code).Count;
				if (count > 0) {
					found[$"{name}|{file}"] = count;
				}
			}
		}

		// Assert
		found.Should().BeEquivalentTo(KnownCallSites.ToDictionary(pair => pair.Key, pair => pair.Value.Count),
			because: "every call site of a site-text extractor must be listed with its wiring - a new one needs a "
				+ "wiring case proving its text reaches the caller only through RedactSiteText");
	}

	[Test]
	[Category("Unit")]
	[Description("At every 'call site' extractor use, the variable the call writes the site's text to is read only "
		+ "inside a RedactSiteText (or fenced site rendering) argument, and at least once; every 'discarded' use writes "
		+ "to a discard.")]
	public void SourceGuard_ShouldSeeTheExtractedTextFlowOnlyIntoRedactSiteText_AtEveryCallSite() {
		// Arrange
		List<string> unwired = [];

		// Act
		foreach ((string key, (int _, string wiring)) in KnownCallSites
				.Where(pair => pair.Value.Wiring is "call site" or "discarded")) {
			string[] parts = key.Split('|');
			Regex call = Extractors.Single(extractor => extractor.Name == parts[0]).Call;
			CSharpSource source = CSharpSource.Read(Path.Combine(RepositoryRoot, parts[1]));
			foreach (Match match in call.Matches(source.Code)) {
				string problem = CheckCallSite(source, match, wiring);
				if (problem is not null) {
					unwired.Add($"{parts[1]}:{source.LineOf(match.Index)} ({wiring}): {problem}");
				}
			}
		}

		// Assert
		unwired.Should().BeEmpty(because: "the server text CreatioResponseError.TryDetect extracts is the site's own, "
			+ "so the variable it lands in may reach the caller only through RedactSiteText");
	}

	[Test]
	[Category("Unit")]
	[Description("Every centrally wired extractor applies the site entry point inside its own body.")]
	public void SourceGuard_ShouldFindTheSiteEntryPointInsideEveryCentralExtractor() {
		// Arrange
		List<string> unwired = [];

		// Act
		foreach ((string file, string member, string call) in CentralExtractors) {
			CSharpSource source = CSharpSource.Read(Path.Combine(RepositoryRoot, file));
			(string Name, int Start, int End)? span = source.MemberDeclaredBy(member);
			string body = span is null ? string.Empty : source.Code[span.Value.Start..(span.Value.End + 1)];
			if (!body.Contains(call, StringComparison.Ordinal)) {
				unwired.Add($"{file}: {member} lacks {call}");
			}
		}

		// Assert
		unwired.Should().BeEmpty(because: "a central extractor is the only place its callers' site text is redacted");
	}

	[Test]
	[Category("Unit")]
	[Description("A member that reads only site text never falls back to a general entry point (Redact, the plain "
		+ "fence, the console renderings): only RedactSiteText takes the sign-in names, server instances and stack "
		+ "traces a site writes.")]
	public void SourceGuard_ShouldFindNoGeneralRedactionInsideASiteOnlyMember() {
		// Arrange
		List<string> regressed = [];

		// Act
		foreach ((string file, string member) in SiteOnlyMembers) {
			CSharpSource source = CSharpSource.Read(Path.Combine(RepositoryRoot, file));
			(string Name, int Start, int End)? span = source.MemberDeclaredBy(member);
			if (span is null) {
				regressed.Add($"{file}: {member} not found");
				continue;
			}
			string body = source.Code[span.Value.Start..(span.Value.End + 1)];
			regressed.AddRange(GeneralRedactionCalls.Where(call => body.Contains(call, StringComparison.Ordinal))
				.Select(call => $"{file}: {member} calls {call}"));
		}

		// Assert
		regressed.Should().BeEmpty(because: "the site's text takes the site-text redaction, once, where it is read");
	}

	[Test]
	[Category("Unit")]
	[Description("A scan of clio's code finds exactly the known DIRECT reads of a site's error text - errorInfo, "
		+ "responseStatus, ExceptionMessage, errorMessage and innererror reads, ServerSaid and "
		+ "ServerReportedFailureText.Describe - each listed as wired, as not text, or as known-unwired follow-up: a new read "
		+ "fails here until it is wired through RedactSiteText or listed.")]
	public void SourceGuard_ShouldKnowEveryDirectReadOfASitesErrorText() {
		// Arrange
		Dictionary<string, int> found = FindDirectSiteErrorReads();

		// Act
		string[] missing = found.Where(pair => !KnownSiteErrorReads.TryGetValue(pair.Key, out (int Count, string _) known)
				|| known.Count != pair.Value)
			.Select(pair => $"[\"{pair.Key}\"] = ({pair.Value}, \"...\"),").ToArray();
		string[] stale = KnownSiteErrorReads.Keys.Where(key => !found.ContainsKey(key)).ToArray();

		// Assert
		missing.Should().BeEmpty(because: "a direct read of a site's error text must be wired through RedactSiteText "
			+ "or listed in KnownSiteErrorReads with the change that wires it; found:\n" + string.Join('\n', missing));
		stale.Should().BeEmpty(because: "an entry whose reads are gone must leave the list, so it stays exact");
	}

	[Test]
	[Category("Unit")]
	[Description("Every entry of the direct-read list names a known wiring - an extractor this fixture checks, "
		+ "follow-up, or a reason it holds no text - and a read in a write-tool file A2 wired is never follow-up.")]
	public void SourceGuard_ShouldClassifyEveryDirectReadAsWiredOrKnownUnwired() {
		// Arrange
		HashSet<string> extractors = Extractors.Select(extractor => extractor.Name)
			.Concat(CentralExtractors.Select(central => $"{Path.GetFileNameWithoutExtension(central.File)}.{MemberName(central.Member)}"))
			.ToHashSet(StringComparer.Ordinal);
		string[] writePathFiles = [
			"PageDesignerHierarchyClient", "PageUpdateOptions", "PageCreateOptions", "SchemaTemplateCatalog",
			"RemoteEntitySchemaDesignerClient", "SelectQueryHelper", "CompileConfigurationCommand", "PackageBuildResult",
			"PackageBuilder", "InstallLogAnalyzer", "BasePackageInstaller", "CreateBusinessProcessCommand",
			"ModifyBusinessProcessCommand", "ProcessDesignServerFault", "CompileBusinessProcessCommand",
			// Dynamic case is a write path too; A2 wired its readers.
			"DynamicCaseService", "CreateDynamicCaseCommand", "ModifyDynamicCaseCommand", "DescribeDynamicCaseCommand",
			"DynamicCasePageStep", "ModifyProcessAsNewVersionCommand", "SetActiveProcessVersionCommand", "BuildCaseClient"
		];

		// Act
		List<string> wrong = [];
		foreach ((string key, (int _, string wiring)) in KnownSiteErrorReads) {
			string file = Path.GetFileNameWithoutExtension(key.Split('|')[0]);
			bool isWritePathFile = writePathFiles.Contains(file, StringComparer.Ordinal);
			bool ok = wiring switch {
				"follow-up" => !isWritePathFile,
				_ when wiring.StartsWith("not text: ", StringComparison.Ordinal) => wiring.Length > "not text: ".Length,
				_ => wiring.StartsWith("wired: ", StringComparison.Ordinal) && extractors.Contains(wiring["wired: ".Length..])
			};
			if (!ok) {
				wrong.Add($"{key}: {wiring}");
			}
		}

		// Assert
		wrong.Should().BeEmpty(because: "each read is either wired through a checked extractor or known-unwired with "
			+ "the change that wires it");
	}

	[TestCase("\n", TestName = "SourceScan_LfLineEndings")]
	[TestCase("\r\n", TestName = "SourceScan_CrlfLineEndings")]
	[Category("Unit")]
	[Description("The guards' source reader ends an expression-bodied member at its semicolon and a block member at its "
		+ "brace on any line ending, attributes a read to the member it is written in, and never sees a call in a "
		+ "comment or a string.")]
	public void SourceScan_ShouldReadMembersWhole_OnAnyLineEnding(string lineEnding) {
		// Arrange
		string text = string.Join(lineEnding,
			"namespace X;",
			"internal static class C {",
			"	internal static string A(string x) =>",
			"		Wrap(x);",
			"	// SensitiveErrorTextRedactor.RedactSiteText( in a comment",
			"	internal static string B() => \"SensitiveErrorTextRedactor.RedactSiteText( in a string { \";",
			"	private static (int A, string B) D(string s) { return (1, $\"{s.Length}\"); }",
			"	private static readonly string[] E = [\"errorInfo\"];",
			"}",
			"");

		// Act
		CSharpSource source = CSharpSource.Parse(text);
		(string Name, int Start, int End)? a = source.MemberDeclaredBy("internal static string A(");

		// Assert
		a.Should().NotBeNull();
		source.Code[a.Value.Start..(a.Value.End + 1)].Should().Contain("Wrap(x);").And.NotContain("B()",
			because: "an expression-bodied member ends at its own semicolon, not at the end of the file");
		source.MemberAt(source.Code.IndexOf("return", StringComparison.Ordinal)).Should().Be("D",
			because: "a tuple return type is not the member's name");
		source.MemberAt(source.CodeWithLiterals.IndexOf("errorInfo", StringComparison.Ordinal)).Should().Be("E");
		source.Code.Should().NotContain("RedactSiteText(", because: "a comment and a string are masked");
	}

	private static Dictionary<string, int> FindDirectSiteErrorReads() =>
		DirectSiteErrorReads(ClioSources().Select(file => (file, CSharpSource.Read(Path.Combine(RepositoryRoot, file)))).ToList());

	// The direct reads of a site's error text in the given sources, counted by "file|member":
	//   - a site-error JSON name read as a literal (not on a [JsonPropertyName]/[JsonProperty]/[DataMember] line);
	//   - a read of a member named ErrorInfo, ResponseStatus or ExceptionMessage, or of ANY member a source decorates
	//     with one of the site-error JSON names, whatever its C# name ("[JsonPropertyName("errorMessage")] public
	//     string ErrorMessage" makes every ".ErrorMessage" a read);
	//   - one of those member names in a property pattern ("is { ErrorInfo: { } }"), not a named argument;
	//   - a reference to a constant or a read-only field that holds a site-error name ("InnerErrorMemberNames");
	//   - ProcessDesignServerFault.ServerSaid and ServerReportedFailureText.Describe.
	internal static Dictionary<string, int> DirectSiteErrorReads(IReadOnlyList<(string File, CSharpSource Source)> sources) {
		HashSet<string> memberNames = new(StringComparer.Ordinal) { "ErrorInfo", "ResponseStatus", "ExceptionMessage" };
		foreach ((string _, CSharpSource source) in sources) {
			foreach (Match decoration in SiteErrorNameDecoration.Matches(source.CodeWithLiterals)) {
				Match declared = DecoratedMemberName.Match(source.Code, decoration.Index + decoration.Length);
				if (declared.Success) {
					memberNames.Add(declared.Groups["name"].Value);
				}
			}
		}
		string names = string.Join('|', memberNames.OrderBy(name => name, StringComparer.Ordinal).Select(Regex.Escape));
		// A member access that is no call ("new Clio.Common.ErrorMessage(…)" names a type), and a member name inside a
		// property pattern - after "is", "and", "or", "not" or "case" and its "{" - never a named argument.
		Regex memberRead = new($@"(?<=[\w)\]?!])\.(?:{names})\b(?!\s*\()");
		Regex propertyPattern = new($@"(?<=\b(?:is|and|or|not|case)\s+(?:[\w.?<>]+\s*)?\{{(?:[^{{}};]*,)?\s*)(?:{names})\s*:(?!:)");
		Dictionary<string, int> found = new(StringComparer.Ordinal);
		List<(string File, string Name, int Start, int End)> constants = [];
		foreach ((string file, CSharpSource source) in sources) {
			List<int> literals = SiteErrorNameLiteral.Matches(source.CodeWithLiterals)
				.Where(match => !PropertyNameDeclaration.IsMatch(LineAt(source.Code, match.Index)))
				.Select(match => match.Index).ToList();
			foreach (int index in literals) {
				(string name, int start, int end) = source.MemberSpanAt(index);
				if (NameConstantDeclaration.IsMatch(source.Code[start..index]) && !constants.Any(entry => entry.File == file && entry.Name == name)) {
					constants.Add((file, name, start, end));
				}
			}
			IEnumerable<int> reads = literals
				.Concat(memberRead.Matches(source.Code).Select(match => match.Index))
				.Concat(propertyPattern.Matches(source.Code).Select(match => match.Index))
				.Concat(SiteTextHelperCall.Matches(source.Code).Select(match => match.Index));
			foreach (int index in reads) {
				string key = $"{file}|{source.MemberAt(index)}";
				found[key] = found.GetValueOrDefault(key) + 1;
			}
		}
		foreach ((string declaringFile, string constant, int start, int end) in constants) {
			Regex reference = new($@"(?<![\w.""])(?:\w+\.)?{Regex.Escape(constant)}\b");
			foreach ((string file, CSharpSource source) in sources) {
				foreach (Match use in reference.Matches(source.Code)) {
					if (file == declaringFile && use.Index >= start && use.Index <= end) {
						continue;
					}
					string key = $"{file}|{source.MemberAt(use.Index)}";
					found[key] = found.GetValueOrDefault(key) + 1;
				}
			}
		}
		return found;
	}

	[Test]
	[Category("Unit")]
	[Description("The direct-read scan sees the three shapes the round-2 review found it blind to: a site-error name "
		+ "held in a constant and read through it, a property pattern, and a member decorated with a site-error JSON "
		+ "name whatever its C# name - and a decorated ErrorMessage member's reads.")]
	public void SourceScan_ShouldSeeReadsThroughConstantsPatternsAndDecoratedMembers() {
		// Arrange
		string dto = string.Join("\n",
			"internal sealed class Answer {",
			"	[JsonPropertyName(\"errorInfo\")] public Info Failure { get; set; }",
			"	[JsonPropertyName(\"errorMessage\")] public string ErrorMessage { get; set; }",
			"}");
		string reader = string.Join("\n",
			"internal static class Reader {",
			"	private static readonly string[] Names = [\"innererror\", \"internalexception\"];",
			"	internal static string Walk(JsonElement node) {",
			"		foreach (string name in Names) { node = node.GetProperty(name); }",
			"		return node.ToString();",
			"	}",
			"	internal static bool Failed(object answer) => answer is { ErrorInfo: { } };",
			"	internal static string Why(Answer answer) => answer.Failure.Message;",
			"	internal static string Said(Answer answer) => answer.ErrorMessage;",
			"}");

		// Act
		Dictionary<string, int> found = DirectSiteErrorReads([
			("Dto.cs", CSharpSource.Parse(dto)), ("Reader.cs", CSharpSource.Parse(reader))
		]);

		// Assert
		found.Should().ContainKeys(["Reader.cs|Names", "Reader.cs|Walk", "Reader.cs|Failed", "Reader.cs|Why", "Reader.cs|Said"],
			because: "a read through a constant, a pattern or a decorated member is a read of a site's error text");
		found.Keys.Should().NotContain(key => key.StartsWith("Dto.cs", StringComparison.Ordinal),
			because: "a declaration is not a read");
	}

	// Null when the call site is wired as its entry says, or what is wrong with it.
	private static string CheckCallSite(CSharpSource source, Match match, string wiring) {
		int open = match.Index + match.Length - 1;
		int close = ClosingParenthesis(source.Code, open);
		string arguments = source.Code[(open + 1)..close];
		string lastArgument = arguments[(arguments.LastIndexOf(',') + 1)..];
		Match output = OutArgument.Match(lastArgument);
		if (!output.Success) {
			return "the call writes its text to no out variable the guard can follow";
		}
		string name = output.Groups["name"].Value;
		if (wiring == "discarded") {
			return name == "_" ? null : $"the text lands in '{name}', not in a discard";
		}
		if (name == "_") {
			return "the text is discarded, so the entry should say 'discarded'";
		}
		(string _, int _, int end) = source.MemberSpanAt(match.Index);
		List<(int Start, int End)> siteArguments = SiteEntryCall.Matches(source.Code[..(end + 1)])
			.Where(entry => entry.Index > close)
			.Select(entry => (entry.Index + entry.Length, ClosingParenthesis(source.Code, entry.Index + entry.Length - 1)))
			.ToList();
		Regex reference = new($@"(?<!\bout\s+(?:(?:string\??|var)\s+)?)(?<![\w.]){Regex.Escape(name)}\b");
		List<int> uses = reference.Matches(source.Code[..(end + 1)]).Select(use => use.Index).Where(at => at > close).ToList();
		if (uses.Count == 0) {
			return $"'{name}' is never read after the call";
		}
		int[] outside = uses.Where(at => !siteArguments.Any(span => span.Start <= at && at < span.End)).ToArray();
		return outside.Length == 0
			? null
			: $"'{name}' is read outside RedactSiteText at line(s) {string.Join(", ", outside.Select(source.LineOf))}";
	}

	private static int ClosingParenthesis(string code, int open) {
		int depth = 0;
		for (int index = open; index < code.Length; index++) {
			depth += code[index] switch { '(' => 1, ')' => -1, _ => 0 };
			if (depth == 0) {
				return index;
			}
		}
		return code.Length - 1;
	}

	private static string LineAt(string text, int index) {
		int start = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
		int end = text.IndexOf('\n', index);
		return text[start..(end < 0 ? text.Length : end)];
	}

	private static string MemberName(string signature) =>
		Regex.Match(signature, @"([A-Za-z_]\w*)\s*(?:<[^(]*>)?\s*\(").Groups[1].Value;

	private static IEnumerable<string> ClioSources() =>
		Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "clio"), "*.cs", SearchOption.AllDirectories)
			.Select(path => Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/'))
			.Where(path => !path.Contains("/obj/", StringComparison.Ordinal) && !path.Contains("/bin/", StringComparison.Ordinal))
			.OrderBy(path => path, StringComparer.Ordinal);

	#endregion
}

/// <summary>Requirement (b) for the DataService batch path: a batch item's error is the site's text.</summary>
[TestFixture]
[Property("Module", "Common")]
public sealed class SiteTextBatchWiringTests : BaseCommandTests<DataServiceBatchOptions> {

	private IApplicationClient _client;
	private DataServiceBatchCommand _command;

	protected override void AdditionalRegistrations(Microsoft.Extensions.DependencyInjection.IServiceCollection services) {
		_client = Substitute.For<IApplicationClient>();
		Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, _client);
		Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services,
			Substitute.For<System.IO.Abstractions.IFileSystem>());
	}

	public override void Setup() {
		base.Setup();
		_command = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
			.GetRequiredService<DataServiceBatchCommand>(Container);
	}

	[Test]
	[Category("Unit")]
	[Description("A batch item's responseStatus message (batch item errors of the DataService write tools) goes "
		+ "through the fenced site rendering, in the item's error and in its diagnostic.")]
	public void BatchItemError_ShouldBeFencedSiteText() {
		// Arrange
		DataServiceBatchOperation operation = new("update", "Contact", Guid.NewGuid(),
			new() { ["Name"] = new(1, JsonSerializer.SerializeToElement("Synthetic")) });
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30000, 1, 1).Returns(call => {
			using JsonDocument sent = JsonDocument.Parse(call.ArgAt<string>(1));
			string queryId = sent.RootElement.GetProperty("items")[0].GetProperty("queryId").GetString();
			return JsonSerializer.Serialize(new {
				queryResults = new[] {
					new { queryId, success = false, responseStatus = new { Message = SiteTextWiringTests.SiteMessage } }
				}
			});
		});

		// Act
		DataServiceBatchResult result = _command.ExecuteBatch([operation]);

		// Assert
		result.Items.Should().ContainSingle().Which.Error.Should().Contain("Login failed for user '[redacted]'.",
			because: "the item error is the site's text, and its reason stays");
		JsonSerializer.Serialize(result).Should().NotContain(SiteTextWiringTests.Marker,
			because: "neither the item error nor its diagnostic may carry the login");
	}
}

/// <summary>
/// A small reader of clio's C# sources for the site-text source guards: line endings normalized, comments and the
/// contents of string and char literals masked, and the member that holds a position found by brace structure.
/// </summary>
/// <remarks>
/// Not a compiler: it knows comments, preprocessor lines, char literals, regular, verbatim and raw strings with
/// their interpolation holes, and braces. That is what the guards need - a call or a read is found only in code,
/// never in a comment or a string, a member body is read whole on any line ending, and a read is attributed to the
/// member it is written in.
/// </remarks>
internal sealed class CSharpSource {

	private static readonly Regex TypeKeyword = new(@"\b(?:class|struct|interface|record|enum|namespace|extension)\b");
	private static readonly Regex TrailingIdentifier = new(@"([A-Za-z_][A-Za-z0-9_]*)\s*$");

	private readonly List<(string Name, int Start, int End)> members = [];

	// Where each member's body brace opens, by member index: its header is the text before it.
	private readonly List<int> bodyOpens = [];

	private CSharpSource(string text, string code, string codeWithLiterals) {
		Text = text;
		Code = code;
		CodeWithLiterals = codeWithLiterals;
		FindMembers();
	}

	/// <summary>The file's text with every line ending written as "\n".</summary>
	public string Text { get; }

	/// <summary><see cref="Text"/> with comments, preprocessor lines and literal contents replaced by spaces; the
	/// delimiters and the code inside interpolation holes stay.</summary>
	public string Code { get; }

	/// <summary><see cref="Text"/> with comments and preprocessor lines replaced by spaces; literals stay.</summary>
	public string CodeWithLiterals { get; }

	public static CSharpSource Read(string path) => Parse(File.ReadAllText(path));

	public static CSharpSource Parse(string raw) {
		string text = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
		char[] code = text.ToCharArray();
		char[] literals = text.ToCharArray();
		int index = 0;
		ScanCode(text, code, literals, ref index, inHole: false);
		return new CSharpSource(text, new string(code), new string(literals));
	}

	/// <summary>The 1-based line of <paramref name="index"/>.</summary>
	public int LineOf(int index) => Text.AsSpan(0, index).Count('\n') + 1;

	/// <summary>The member that holds <paramref name="index"/>, or "(type level)".</summary>
	public string MemberAt(int index) => MemberSpanAt(index).Name;

	/// <summary>The member that holds <paramref name="index"/>: its name, and its text from the declaration to the
	/// closing brace or semicolon.</summary>
	public (string Name, int Start, int End) MemberSpanAt(int index) {
		foreach ((string name, int start, int end) in members) {
			if (start <= index && index <= end) {
				return (name, start, end);
			}
		}
		return ("(type level)", index, index);
	}

	/// <summary>The span of the first member whose declaration contains <paramref name="signature"/>.</summary>
	public (string Name, int Start, int End)? MemberDeclaredBy(string signature) {
		int at = Code.IndexOf(signature, StringComparison.Ordinal);
		return at < 0 ? null : MemberSpanAt(at);
	}

	private static char Peek(string text, int index) => index < text.Length ? text[index] : '\0';

	private static void Blank(char[] view, int index) {
		if (view[index] != '\n') {
			view[index] = ' ';
		}
	}

	// Code until the end, or - inside an interpolation hole - until the brace that closes the hole.
	private static void ScanCode(string text, char[] code, char[] literals, ref int index, bool inHole) {
		int depth = 0;
		bool lineStart = true;
		while (index < text.Length) {
			char character = text[index];
			if (character == '\n') {
				lineStart = true;
				index++;
				continue;
			}
			if (lineStart && character == '#' && !inHole) {
				for (; index < text.Length && text[index] != '\n'; index++) {
					Blank(code, index);
					Blank(literals, index);
				}
				continue;
			}
			if (!char.IsWhiteSpace(character)) {
				lineStart = false;
			}
			if (character == '/' && Peek(text, index + 1) == '/') {
				for (; index < text.Length && text[index] != '\n'; index++) {
					Blank(code, index);
					Blank(literals, index);
				}
				continue;
			}
			if (character == '/' && Peek(text, index + 1) == '*') {
				int end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
				end = end < 0 ? text.Length : end + 2;
				for (; index < end; index++) {
					Blank(code, index);
					Blank(literals, index);
				}
				continue;
			}
			if (character == '\'') {
				ScanChar(text, code, ref index);
				continue;
			}
			if (TryScanString(text, code, literals, ref index)) {
				continue;
			}
			if (inHole && character == '{') {
				depth++;
			}
			else if (inHole && character == '}') {
				if (depth == 0) {
					return;
				}
				depth--;
			}
			index++;
		}
	}

	private static void ScanChar(string text, char[] code, ref int index) {
		int end = index + 1;
		if (Peek(text, end) == '\\') {
			end += 2;
		}
		else {
			end++;
		}
		while (end < text.Length && text[end] != '\'' && text[end] != '\n') {
			end++;
		}
		for (int content = index + 1; content < end && content < text.Length; content++) {
			Blank(code, content);
		}
		index = Math.Min(end + 1, text.Length);
	}

	private static bool TryScanString(string text, char[] code, char[] literals, ref int index) {
		int cursor = index;
		int dollars = 0;
		bool verbatim = false;
		while (Peek(text, cursor) == '$') {
			dollars++;
			cursor++;
		}
		if (Peek(text, cursor) == '@') {
			verbatim = true;
			cursor++;
			while (Peek(text, cursor) == '$') {
				dollars++;
				cursor++;
			}
		}
		if (Peek(text, cursor) != '"') {
			return false;
		}
		int quotes = 0;
		while (Peek(text, cursor + quotes) == '"') {
			quotes++;
		}
		bool raw = !verbatim && quotes >= 3;
		int open = raw ? quotes : 1;
		index = cursor + open;
		if (raw) {
			ScanRaw(text, code, literals, ref index, open, dollars);
		}
		else {
			ScanQuoted(text, code, literals, ref index, verbatim, dollars > 0);
		}
		return true;
	}

	private static void ScanQuoted(string text, char[] code, char[] literals, ref int index, bool verbatim, bool interpolated) {
		while (index < text.Length) {
			char character = text[index];
			if (!verbatim && character == '\\') {
				Blank(code, index);
				if (index + 1 < text.Length) {
					Blank(code, index + 1);
				}
				index += 2;
				continue;
			}
			if (character == '"') {
				if (verbatim && Peek(text, index + 1) == '"') {
					Blank(code, index);
					Blank(code, index + 1);
					index += 2;
					continue;
				}
				index++;
				return;
			}
			if (!verbatim && character == '\n') {
				return;
			}
			if (interpolated && (character == '{' || character == '}') && Peek(text, index + 1) == character) {
				Blank(code, index);
				Blank(code, index + 1);
				index += 2;
				continue;
			}
			if (interpolated && character == '{') {
				index++;
				ScanCode(text, code, literals, ref index, inHole: true);
				index++;
				continue;
			}
			Blank(code, index);
			index++;
		}
	}

	private static void ScanRaw(string text, char[] code, char[] literals, ref int index, int quotes, int dollars) {
		string closing = new('"', quotes);
		while (index < text.Length) {
			if (string.CompareOrdinal(text, index, closing, 0, quotes) == 0) {
				index += quotes;
				while (Peek(text, index) == '"') {
					index++;
				}
				return;
			}
			if (dollars > 0 && text[index] == '{') {
				int run = 0;
				while (Peek(text, index + run) == '{') {
					run++;
				}
				if (run >= dollars) {
					for (int content = 0; content < run - dollars; content++) {
						Blank(code, index + content);
					}
					index += run;
					ScanCode(text, code, literals, ref index, inHole: true);
					for (int brace = 0; brace < dollars && Peek(text, index) == '}'; brace++) {
						index++;
					}
					continue;
				}
			}
			Blank(code, index);
			index++;
		}
	}

	private enum ScopeKind { Type, Member, Block }

	// Scopes by brace structure over the masked code: a brace whose parent is a type opens a type (when its header
	// names one) or a member; any other brace is a block of the member around it. A member whose header assigns or
	// has an arrow ("X => new() { … };", "X = new() { … };") ends at the semicolon after its braces.
	private void FindMembers() {
		Stack<(ScopeKind Kind, int Member)> scopes = new();
		int statementStart = 0;
		int continuing = -1;
		for (int index = 0; index < Code.Length; index++) {
			char character = Code[index];
			bool typeLevel = scopes.Count == 0 || scopes.Peek().Kind == ScopeKind.Type;
			if (character == '{') {
				if (!typeLevel) {
					scopes.Push((ScopeKind.Block, scopes.Peek().Member));
				}
				else if (continuing >= 0) {
					scopes.Push((ScopeKind.Block, continuing));
				}
				else if (IsTypeHeader(Code[statementStart..index])) {
					scopes.Push((ScopeKind.Type, -1));
					statementStart = index + 1;
				}
				else {
					members.Add((MemberName(Code[statementStart..index]), statementStart, int.MaxValue));
					bodyOpens.Add(index);
					scopes.Push((ScopeKind.Member, members.Count - 1));
				}
			}
			else if (character == '}' && scopes.Count > 0) {
				(ScopeKind kind, int member) = scopes.Pop();
				if (kind == ScopeKind.Type) {
					statementStart = index + 1;
					continuing = -1;
				}
				else if (kind == ScopeKind.Member) {
					(string name, int start, int _) = members[member];
					if (AssignsOrArrows(Code[start..bodyOpens[member]])) {
						continuing = member;
					}
					else {
						members[member] = (name, start, index);
						statementStart = index + 1;
					}
				}
			}
			else if (character == ';' && typeLevel) {
				if (continuing >= 0) {
					members[continuing] = (members[continuing].Name, members[continuing].Start, index);
					continuing = -1;
				}
				else {
					string header = Code[statementStart..index];
					string name = MemberName(header);
					if (IsTypeHeader(header) || header.TrimStart().StartsWith("using ", StringComparison.Ordinal)) {
						// a file-scoped namespace, a using directive or a positional record
					}
					else if (name.Length > 0) {
						members.Add((name, statementStart, index));
						bodyOpens.Add(index);
					}
					else if (members.Count > 0 && members[^1].End < index) {
						members[^1] = (members[^1].Name, members[^1].Start, index);
					}
				}
				statementStart = index + 1;
			}
		}
	}

	private static string WithoutAttributes(string header) {
		string rest = header.TrimStart();
		while (rest.StartsWith('[')) {
			int depth = 0;
			int end = 0;
			for (; end < rest.Length; end++) {
				depth += rest[end] switch { '[' => 1, ']' => -1, _ => 0 };
				if (depth == 0) {
					break;
				}
			}
			rest = rest[Math.Min(end + 1, rest.Length)..].TrimStart();
		}
		return rest;
	}

	private static bool IsTypeHeader(string header) {
		string declaration = WithoutAttributes(header);
		int paren = declaration.IndexOf('(');
		return TypeKeyword.IsMatch(paren < 0 ? declaration : declaration[..paren]);
	}

	// The first parameter-list "(", "=>" or lone "=" outside brackets, or -1. A parameter list's "(" follows the
	// name (or its generic arguments) directly; a tuple return type's "(" follows a space and is skipped.
	private static int FirstTopLevelCut(string declaration, bool parenthesisToo) {
		int depth = 0;
		for (int index = 0; index < declaration.Length; index++) {
			char character = declaration[index];
			if (character == '(' && depth == 0 && parenthesisToo && index > 0
				&& (char.IsLetterOrDigit(declaration[index - 1]) || declaration[index - 1] is '_' or '>')) {
				return index;
			}
			depth += character switch { '(' or '[' => 1, ')' or ']' => -1, _ => 0 };
			if (depth != 0 || character != '=') {
				continue;
			}
			char before = index > 0 ? declaration[index - 1] : ' ';
			char after = Peek(declaration, index + 1);
			if (after == '>' || (after != '=' && before is not ('=' or '!' or '<' or '>'))) {
				return index;
			}
			index++;
		}
		return -1;
	}

	private static bool AssignsOrArrows(string header) => FirstTopLevelCut(WithoutAttributes(header), parenthesisToo: false) >= 0;

	private static string MemberName(string header) {
		string declaration = WithoutAttributes(header);
		int cut = FirstTopLevelCut(declaration, parenthesisToo: true);
		string before = (cut < 0 ? declaration : declaration[..cut]).TrimEnd();
		if (before.EndsWith('>')) {
			int depth = 0;
			for (int index = before.Length - 1; index >= 0; index--) {
				depth += before[index] switch { '>' => 1, '<' => -1, _ => 0 };
				if (depth == 0) {
					before = before[..index].TrimEnd();
					break;
				}
			}
		}
		Match name = TrailingIdentifier.Match(before);
		return name.Success ? name.Groups[1].Value : string.Empty;
	}
}

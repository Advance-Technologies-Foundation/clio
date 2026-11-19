using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Package;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using SiteErrorInfo = Clio.Common.Responses.ErrorInfo;

namespace Clio.Tests.Common;

/// <summary>
/// The write-tool half of requirement (b) of the site-text redaction plan: on update-page, sync-pages, create-page,
/// the entity-schema designer tools, SelectQuery, the package installer and the business-process tools, text a
/// Creatio SITE wrote passes through <see cref="SensitiveErrorTextRedactor.RedactSiteText"/> where it is read - into
/// the tool's error AND into every log line built from it, which a stdio caller receives unredacted.
/// </summary>
/// <remarks>
/// Each path is driven with the marker only RedactSiteText removes (<see cref="SiteTextWiringTests.Marker"/>, a
/// sign-in name in SQL Server's own phrase), and each test asserts the marker reaches neither the result nor the log
/// while the site's reason stays. The compile and installer paths, whose fixtures already drive those commands, have
/// their cases next to them: <c>CompileConfigurationCommandTestCase</c>, <c>PackageBuilderVerdictTests</c> and
/// <c>InstallerFailureClassificationTests</c>.
/// </remarks>
[TestFixture]
[Property("Module", "Common")]
public sealed class SiteTextWritePathWiringTests {

	private const string Marker = SiteTextWiringTests.Marker;
	private const string SiteMessage = SiteTextWiringTests.SiteMessage;
	private const string Reason = "Login failed for user";
	private const string RedactedReason = "Login failed for user '[redacted]'.";

	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string DesignPackageUrl = "http://test/ServiceModel/ApplicationPackagesService.svc/GetDesignPackageUId";
	private const string ParentSchemasUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetParentSchemas";
	private const string GetSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
	private const string SchemaName = "UsrFixture_FormPage";
	private const string SchemaUId = "11111111-1111-1111-1111-111111111111";
	private const string DesignPackage = "22222222-2222-2222-2222-222222222222";

	private const string ValidBody =
		"define(\"UsrFixture_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ "
		+ "{ return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, "
		+ "viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, "
		+ "modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, "
		+ "handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, "
		+ "converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, "
		+ "validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private static readonly string RepositoryRoot =
		Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

	private static string Refusal(string message, string errorCode = null) =>
		JsonSerializer.Serialize(new { success = false, errorInfo = new { errorCode, message } });

	private static string HierarchyOf(string packageUId) =>
		JsonSerializer.Serialize(new {
			success = true,
			values = new[] {
				new {
					uId = SchemaUId, name = SchemaName, schemaVersion = 1, schemaType = 9, body = ValidBody,
					package = new { uId = packageUId, name = "UsrFixturePackage" }
				}
			}
		});

	private static string DesignPackageOf(string packageUId) => JsonSerializer.Serialize(new { success = true, uId = packageUId });

	// Every string a logger was handed, whichever Write method took it: the stdio surface returns all of them.
	private static List<string> Logged(ILogger logger) =>
		logger.ReceivedCalls().SelectMany(call => call.GetArguments()).OfType<string>().ToList();

	#region update-page and sync-pages (PageDesignerHierarchyClient, PageUpdateCommand)

	private static IApplicationClient UpdatePageSite(string designPackage, string hierarchy, string getSchema) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => call.ArgAt<string>(0) switch {
				SelectQueryUrl => $$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}", "ExtendParent": false}]}""",
				DesignPackageUrl => designPackage,
				ParentSchemasUrl => hierarchy,
				GetSchemaUrl => getSchema,
				SaveSchemaUrl => """{"success": true}""",
				_ => """{"success": false}"""
			});
		return client;
	}

	private static PageUpdateCommand UpdatePageCommand(IApplicationClient client, ILogger logger,
		IPageLayerInspector inspector = null) {
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		urls.Build("/ServiceModel/ApplicationPackagesService.svc/GetDesignPackageUId").Returns(DesignPackageUrl);
		urls.Build(ServiceUrlBuilder.KnownRoute.GetClientUnitDesignerParentSchemas).Returns(ParentSchemasUrl);
		urls.Build(ServiceUrlBuilder.KnownRoute.GetClientUnitDesignerSchema).Returns(GetSchemaUrl);
		urls.Build(ServiceUrlBuilder.KnownRoute.SaveClientUnitDesignerSchema).Returns(SaveSchemaUrl);
		return new PageUpdateCommand(client, urls, logger, Substitute.For<IPageBaselineGuard>(),
			new PersistedResourceKeyReader(), new PageDesignerHierarchyClient(client, urls),
			viewConfigApplierFactory: () => Substitute.For<IJsonDiffApplier>(), pageLayerInspector: inspector);
	}

	private static PageUpdateOptions UpdatePageOptions(bool dryRun = false, string targetPackageUId = null) =>
		new() {
			SchemaName = SchemaName, Body = ValidBody, DryRun = dryRun, TargetPackageUId = targetPackageUId,
			EnforcePackageChoice = targetPackageUId is not null
		};

	// Drives the write through TryUpdatePage (the update-page and sync-pages tools) and through Execute (the CLI,
	// which logs the response), and returns the error and every logged string.
	private static (string Error, List<string> Logged) RunUpdatePage(IApplicationClient client,
		Func<PageUpdateOptions> options, IPageLayerInspector inspector = null) {
		ILogger logger = Substitute.For<ILogger>();
		PageUpdateCommand command = UpdatePageCommand(client, logger, inspector);
		command.TryUpdatePage(options(), out PageUpdateResponse response);
		command.Execute(options());
		return (response.Error, Logged(logger));
	}

	[Test]
	[Category("Unit")]
	[Description("update-page (and its dry run) on a designer rights refusal, from the 19 Nov capture's shape with "
		+ "invented values: the error keeps the site's code and its reason with the operation name, and drops the stack "
		+ "trace, the server path and the host the whole errorInfo used to carry - in the result and in the log.")]
	public void UpdatePage_ShouldKeepTheReasonAndDropTheStackTrace_WhenTheHierarchyReadIsRefused(
		[Values(false, true)] bool dryRun) {
		// Arrange
		string refusal = File.ReadAllText(
			Path.Combine(RepositoryRoot, "clio.tests", "Common", "Fixtures", "update-page-rights-refusal.json"));
		IApplicationClient client = UpdatePageSite(DesignPackageOf(DesignPackage), refusal, Refusal("unused"));

		// Act
		(string error, List<string> logged) = RunUpdatePage(client, () => UpdatePageOptions(dryRun));

		// Assert
		error.Should().Be($"Failed to load hierarchy for '{SchemaName}': Failed to load page schema hierarchy: "
			+ "SecurityException: Current user does not have sufficient permissions to run \"CanManageSolution\"",
			because: "the site's code and its human reason, operation name included, are the whole answer");
		foreach (string text in logged.Append(error)) {
			text.Should().NotContainAny(["stackTrace", "Terrasoft.Fixture", "fixture-build", "fixture-site.invalid",
					"[redacted-path]"],
				because: "neither the result nor the log may carry the server's frames, its path or its host, nor lose "
					+ "the operation name to a path rule");
		}
		logged.Should().Contain(text => text.Contains("CanManageSolution", StringComparison.Ordinal)
				&& text.Contains("SecurityException", StringComparison.Ordinal),
			because: "the CLI logs the response, and the reason stays in it");
	}

	[Test]
	[Category("Unit")]
	[Description("update-page: the hierarchy read's refusal message goes through RedactSiteText before it becomes the "
		+ "error (PageDesignerHierarchyClient.GetParentSchemas, then PageUpdateCommand.TryGetHierarchy).")]
	public void UpdatePage_ShouldRedactTheHierarchyRefusal_AsSiteText() {
		// Arrange
		IApplicationClient client = UpdatePageSite(DesignPackageOf(DesignPackage), Refusal(SiteMessage, "DbException"),
			Refusal("unused"));

		// Act
		(string error, List<string> logged) = RunUpdatePage(client, () => UpdatePageOptions());

		// Assert
		error.Should().Contain("DbException: " + RedactedReason, because: "the code and the reason stay");
		logged.Append(error).Should().NotContain(text => text.Contains(Marker, StringComparison.Ordinal),
			because: "the login the database named reaches neither the error nor the log");
	}

	[Test]
	[Category("Unit")]
	[Description("update-page: the design-package read's refusal goes through RedactSiteText "
		+ "(PageDesignerHierarchyClient.GetDesignPackageUId, then TryGetDesignPackageUId).")]
	public void UpdatePage_ShouldRedactTheDesignPackageRefusal_AsSiteText() {
		// Arrange
		IApplicationClient client = UpdatePageSite(Refusal(SiteMessage), HierarchyOf(DesignPackage), Refusal("unused"));

		// Act
		(string error, List<string> logged) = RunUpdatePage(client, () => UpdatePageOptions());

		// Assert
		error.Should().Be($"Failed to resolve design package for '{SchemaName}': Failed to resolve design package: "
			+ RedactedReason, because: "the reason stays and the login goes");
		logged.Should().NotContain(text => text.Contains(Marker, StringComparison.Ordinal),
			because: "the logged response carries the same redacted reason");
	}

	[Test]
	[Category("Unit")]
	[Description("update-page: the replace refusal that embeds the default design package's read failure "
		+ "(TryAllowChosenTargetPackage) carries the site's reason redacted as site text.")]
	public void UpdatePage_ShouldRedactTheSiteReason_InTheReplaceRefusal() {
		// Arrange
		const string otherPackage = "33333333-3333-3333-3333-333333333333";
		IApplicationClient client = UpdatePageSite(Refusal(SiteMessage), HierarchyOf(otherPackage), Refusal("unused"));
		IPageLayerInspector inspector = Substitute.For<IPageLayerInspector>();

		// Act
		(string error, List<string> logged) = RunUpdatePage(client,
			() => UpdatePageOptions(targetPackageUId: otherPackage), inspector);

		// Assert
		error.Should().Contain("the default design package could not be resolved (Failed to resolve design package: "
			+ RedactedReason + ")", because: "the refusal quotes the site's reason, redacted");
		logged.Append(error).Should().NotContain(text => text.Contains(Marker, StringComparison.Ordinal),
			because: "the login reaches neither the refusal nor the log");
	}

	[Test]
	[Category("Unit")]
	[Description("update-page: the designer's GetSchema refusal message (TryGetSchema's errorInfo.message) goes "
		+ "through RedactSiteText before it becomes the error.")]
	public void UpdatePage_ShouldRedactTheSchemaReadRefusal_AsSiteText() {
		// Arrange
		IApplicationClient client = UpdatePageSite(DesignPackageOf(DesignPackage), HierarchyOf(DesignPackage),
			Refusal(SiteMessage));

		// Act
		(string error, List<string> logged) = RunUpdatePage(client, () => UpdatePageOptions());

		// Assert
		error.Should().Contain(RedactedReason, because: "the designer's reason stays");
		logged.Append(error).Should().NotContain(text => text.Contains(Marker, StringComparison.Ordinal),
			because: "the login reaches neither the error nor the log");
	}

	#endregion

	#region create-page (PageCreateCommand, SchemaTemplateCatalog)

	private const string TemplateName = "BlankPageTemplate";

	private static PageCreateOptions CreatePageOptions() =>
		new() { SchemaName = "UsrFixture_BlankPage", Template = TemplateName, PackageName = "Custom", Caption = "Fixture" };

	private static IServiceUrlBuilder CreatePageUrls() {
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		urls.Build(ServiceUrlBuilder.KnownRoute.GetClientUnitDesignerSchema).Returns(GetSchemaUrl);
		urls.Build(ServiceUrlBuilder.KnownRoute.SaveClientUnitDesignerSchema).Returns(SaveSchemaUrl);
		urls.Build(Arg.Is<string>(url => url.StartsWith("/rest/schema.template.api/templates", StringComparison.Ordinal)))
			.Returns("http://test/rest/schema.template.api/templates");
		return urls;
	}

	[Test]
	[Category("Unit")]
	[Description("create-page: the designer's refusal to read the template's resources "
		+ "(TryLoadTemplateLocalizableStrings) goes through RedactSiteText, in the error and in the failure log line.")]
	public void CreatePage_ShouldRedactTheTemplateResourcesRefusal_AsSiteText() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		Queue<string> selects = new([$$"""{"success": true, "rows": [{"UId": "{{DesignPackage}}"}]}""",
			"""{"success": true, "rows": []}"""]);
		client.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => selects.Dequeue());
		client.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(Refusal(SiteMessage));
		ISchemaTemplateCatalog catalog = Substitute.For<ISchemaTemplateCatalog>();
		catalog.FindTemplate(TemplateName).Returns(new PageTemplateInfo {
			UId = "44444444-4444-4444-4444-444444444444", Name = TemplateName, SchemaType = 9
		});
		ILogger logger = Substitute.For<ILogger>();
		PageCreateCommand command = new(client, CreatePageUrls(), catalog, logger,
			Substitute.For<ICaptionCultureResolver>());

		// Act
		command.TryCreatePage(CreatePageOptions(), out PageCreateResponse response);

		// Assert
		response.Error.Should().Be(RedactedReason, because: "the designer's reason is the error, redacted");
		Logged(logger).Should().Contain(text => text.Contains(RedactedReason, StringComparison.Ordinal),
			because: "the failure is logged with the reason")
			.And.NotContain(text => text.Contains(Marker, StringComparison.Ordinal),
				because: "the login reaches no log line");
	}

	[Test]
	[Category("Unit")]
	[Description("create-page: the template catalog's refusal (SchemaTemplateCatalog.LoadForSchemaType) carries the "
		+ "site's code and reason redacted as site text, and never its errorInfo's stack trace.")]
	public void CreatePage_ShouldRedactTheTemplateCatalogRefusal_AsSiteText() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>()).Returns(JsonSerializer.Serialize(new {
			success = false,
			errorInfo = new {
				errorCode = "SecurityException", message = SiteMessage, stackTrace = "   at Terrasoft.Fixture.Catalog.Read()"
			}
		}));
		IServiceUrlBuilder urls = CreatePageUrls();
		ILogger logger = Substitute.For<ILogger>();
		PageCreateCommand command = new(client, urls, new SchemaTemplateCatalog(client, urls), logger,
			Substitute.For<ICaptionCultureResolver>());

		// Act
		command.TryCreatePage(CreatePageOptions(), out PageCreateResponse response);

		// Assert
		response.Error.Should().Be("Failed to resolve template catalog: SecurityException: " + RedactedReason,
			because: "the site's code and reason stay");
		foreach (string text in Logged(logger).Append(response.Error)) {
			text.Should().NotContainAny([Marker, "Terrasoft.Fixture", "stackTrace"],
				because: "neither the login nor the stack trace reaches the error or the log");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("create-page and list-page-templates: the preview of a template catalog body that is not JSON "
		+ "(SchemaTemplateCatalog.BuildNonJsonErrorMessage) goes through RedactSiteText, cut before its stack trace.")]
	public void TemplateCatalog_ShouldRedactTheNonJsonPreview_AsSiteText() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>()).Returns(SiteMessage + "\r\n   at Terrasoft.Fixture.Catalog.Read()");
		SchemaTemplateCatalog catalog = new(client, CreatePageUrls());

		// Act
		Action act = () => catalog.GetTemplates(PageSchemaType.Web);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.Which.Message.Should().Contain("Response preview: " + RedactedReason, because: "the reason stays")
			.And.NotContainAny([Marker, "Terrasoft.Fixture"], because: "the login and the stack trace go");
	}

	#endregion

	#region Entity-schema designer (RemoteEntitySchemaDesignerClient.EnsureSuccess)

	[Test]
	[Category("Unit")]
	[Description("The entity-schema designer's refusal message (RemoteEntitySchemaDesignerClient.EnsureSuccess: "
		+ "entity-schema tools, create-lookup, sync-schemas, the data-binding-db tools) goes through RedactSiteText: "
		+ "driven through create-entity-schema, it reaches neither the exception nor the command's error line.")]
	public void EntitySchemaDesigner_ShouldRedactTheRefusal_InTheExceptionAndTheLog() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(Refusal(SiteMessage, "SecurityException"));
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>())
			.Returns("http://test/ServiceModel/EntitySchemaDesignerService.svc/GetSchemaDesignItem");
		RemoteEntitySchemaDesignerClient designer = new(client, new JsonConverter(), urls);
		Exception thrown = null;
		IRemoteEntitySchemaCreator creator = Substitute.For<IRemoteEntitySchemaCreator>();
		creator.When(value => value.Create(Arg.Any<CreateEntitySchemaOptions>())).Do(_ => {
			try {
				designer.GetSchemaDesignItem(new GetSchemaDesignItemRequestDto { Name = "UsrFixture" },
					new RemoteCommandOptions());
			} catch (Exception exception) {
				thrown = exception;
				throw;
			}
		});
		ILogger logger = Substitute.For<ILogger>();
		CreateEntitySchemaCommand command = new(creator, logger);

		// Act
		int exitCode = command.Execute(new CreateEntitySchemaOptions {
			Package = "Custom", SchemaName = "UsrFixture", Title = "Fixture"
		});

		// Assert
		exitCode.Should().Be(1, because: "the designer refused");
		thrown.Should().NotBeNull(because: "the designer client threw the refusal");
		thrown.Message.Should().Be(RedactedReason, because: "the designer's reason is the message, redacted");
		logger.Received(1).WriteError(RedactedReason);
		Logged(logger).Should().NotContain(text => text.Contains(Marker, StringComparison.Ordinal),
			because: "the command's error line is the stdio caller's result");
	}

	#endregion

	#region SelectQuery (SelectQueryHelper, ServiceResponseJsonGuard)

	private sealed class SelectRows : SelectQueryHelper.SelectQueryResponseBaseDto;

	private static Exception RunSelectQuery(string body) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(body);
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectQueryUrl);
		using IDisposable noWait = SelectQueryHelper.OverrideRetryWait(_ => { });
		try {
			SelectQueryHelper.ExecuteSelectQuery<SelectRows>(client, urls, new { rootSchemaName = "SysPackage" });
			return null;
		} catch (Exception exception) {
			return exception;
		}
	}

	[Test]
	[Category("Unit")]
	[Description("SelectQuery's server failure (SelectQueryHelper.DescribeServerFailure) goes through the site entry "
		+ "point: a non-blank errorInfo.message is redacted, and a blank one's raw body is fenced as site text.")]
	public void SelectQuery_ShouldRedactTheServerFailure_AsSiteText([Values(true, false)] bool withMessage) {
		// Arrange
		// Written by hand, not serialized: the serializer escapes the quotes around the login as \u0027, and the raw body
		// is what the blank-message branch quotes.
		string body = withMessage
			? Refusal(SiteMessage)
			: $$"""{"success": false, "errorInfo": {"message": ""}, "detail": "{{SiteMessage}}"}""";

		// Act
		Exception exception = RunSelectQuery(body);

		// Assert
		exception.Should().BeOfType<InvalidOperationException>(because: "the server reported a failure");
		exception.Message.Should().StartWith("SelectQuery failed: ", because: "clio's own wording stays as it is");
		exception.Message.Should().Contain(withMessage ? RedactedReason : "Login failed for user '[redacted]'",
			because: "the reason stays");
		exception.Message.Should().NotContain(Marker, because: "every SelectQuery caller logs this message");
	}

	[Test]
	[Category("Unit")]
	[Description("The preview of a SelectQuery (and designer) body that is not JSON (ServiceResponseJsonGuard) goes "
		+ "through RedactSiteText, cut before the stack trace that follows the reason.")]
	public void SelectQuery_ShouldRedactTheNonJsonPreview_AsSiteText() {
		// Arrange
		string body = SiteMessage + "\r\n   at Terrasoft.Fixture.DataService.Select()";

		// Act
		Exception exception = RunSelectQuery(body);

		// Assert
		exception.Should().BeAssignableTo<InvalidOperationException>(because: "the body is not JSON");
		exception.Message.Should().Contain("Response preview: " + RedactedReason, because: "the reason stays")
			.And.NotContainAny([Marker, "Terrasoft.Fixture"], because: "the login and the stack trace go");
	}

	#endregion

	#region Package installation (InstallLogAnalyzer.DescribeFailure)

	[Test]
	[Category("Unit")]
	[Description("The installation failure line (InstallLogAnalyzer.DescribeFailure, logged by the installer as "
		+ "\"Package installation failed: ...\") takes every site-written part through RedactSiteText: the service's "
		+ "code and message, the compiler's log line and the log tail.")]
	public void InstallFailure_ShouldRedactEverySitePart_AsSiteText([Values("message", "compiler", "tail")] string part) {
		// Arrange
		BaseResponse response = part == "message"
			? new BaseResponse { Success = false, ErrorInfo = new SiteErrorInfo { ErrorCode = "Exception", Message = SiteMessage } }
			: new BaseResponse {
				Success = false,
				ErrorInfo = new SiteErrorInfo { ErrorCode = "Exception", Message = "Packages installation failed" }
			};
		string log = part switch {
			"compiler" => "Autogenerated\\Src\\UsrFixture.cs(1,79) error CS1519: " + SiteMessage + "\r\n",
			"tail" => "Start package installation\r\n" + SiteMessage + "\r\n",
			_ => string.Empty
		};
		if (part == "tail") {
			response = new BaseResponse { Success = false };
		}

		// Act
		string description = InstallLogAnalyzer.DescribeFailure(response, log);

		// Assert
		description.Should().Contain(RedactedReason, because: "the site's reason stays");
		description.Should().NotContain(Marker, because: "the description is the installer's error line");
	}

	#endregion

	#region Business processes (ProcessDesignServerFault, create/modify/compile-business-process)

	private const string Env = "sandbox";
	private const string ProcessDescriptor = "{\"name\":\"UsrFixtureProcess\",\"elements\":[],\"flows\":[]}";

	private static (EnvironmentSettings Env, ISettingsRepository Settings, IApplicationClientFactory Factory)
		ProcessEnvironment(IApplicationClient client) {
		EnvironmentSettings environment = new() { Uri = "http://sandbox" };
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.FindEnvironment(Env).Returns(environment);
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateEnvironmentClient(environment).Returns(client);
		return (environment, settings, factory);
	}

	[Test]
	[Category("Unit")]
	[Description("create-business-process: the build's errorMessage (ReadBuildProcessAnswer) and a bare fault string "
		+ "(ProcessDesignServerFault.ReadServerText, quoted by ServerSaid) go through RedactSiteText, in the "
		+ "exception and in the command's error line.")]
	public void CreateBusinessProcess_ShouldRedactTheServerText_InTheExceptionAndTheLog(
		[Values(true, false)] bool envelope) {
		// Arrange
		string body = envelope
			? JsonSerializer.Serialize(new { BuildProcessResult = new { success = false, errorMessage = SiteMessage } })
			: JsonSerializer.Serialize(SiteMessage);
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient, IStatusPreservingApplicationClient>();
		((IStatusPreservingApplicationClient)client)
			.SendKeepingStatus(default, default, default, default, default, default, default)
			.ReturnsForAnyArgs(new SiteHttpResponse(200, body));
		(EnvironmentSettings environment, ISettingsRepository settings, IApplicationClientFactory factory) =
			ProcessEnvironment(client);
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(ServiceUrlBuilder.KnownRoute.BuildProcess, environment)
			.Returns("http://sandbox/0/rest/ProcessDesignService/BuildProcess");
		ILogger logger = Substitute.For<ILogger>();
		CreateBusinessProcessService service = new(settings, factory, urls, Substitute.For<IProcessPageFactsChecker>(),
			Substitute.For<ILogger>());
		CreateBusinessProcessCommand command = new(service, Substitute.For<IProcessDescriber>(), logger);

		// Act
		int exitCode = command.Execute(new CreateBusinessProcessOptions {
			Environment = Env, DescriptorJson = ProcessDescriptor
		});

		// Assert
		exitCode.Should().Be(1, because: "the server refused the build");
		List<string> logged = Logged(logger);
		logged.Should().Contain(text => text.Contains(RedactedReason, StringComparison.Ordinal),
			because: "the server's reason reaches the error line, redacted");
		logged.Should().NotContain(text => text.Contains(Marker, StringComparison.Ordinal),
			because: "the error line is the exception's message, which carries no login");
	}

	[Test]
	[Category("Unit")]
	[Description("modify-business-process: the edit's errorMessage (BuildFailureMessage) and a bare fault string "
		+ "(ServerSaid) go through RedactSiteText, in the exception and in the command's error line.")]
	public void ModifyBusinessProcess_ShouldRedactTheServerText_InTheExceptionAndTheLog(
		[Values(true, false)] bool envelope) {
		// Arrange
		string body = envelope
			? JsonSerializer.Serialize(new { ModifyProcessResult = new { success = false, errorMessage = SiteMessage } })
			: JsonSerializer.Serialize(SiteMessage);
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(body);
		(EnvironmentSettings environment, ISettingsRepository settings, IApplicationClientFactory factory) =
			ProcessEnvironment(client);
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(ServiceUrlBuilder.KnownRoute.ModifyProcess, environment)
			.Returns("http://sandbox/0/rest/ProcessDesignService/ModifyProcess");
		ILogger logger = Substitute.For<ILogger>();
		ModifyBusinessProcessService service = new(settings, factory, urls, Substitute.For<IProcessPageFactsChecker>(),
			Substitute.For<ILogger>());
		ModifyBusinessProcessCommand command = new(service, Substitute.For<IProcessDescriber>(), logger);

		// Act
		int exitCode = command.Execute(new ModifyBusinessProcessOptions {
			Environment = Env, ProcessName = "UsrFixtureProcess",
			OperationsJson = "[{\"op\":\"addParameter\",\"parameter\":{\"name\":\"Amount\",\"type\":\"Integer\"}}]"
		});

		// Assert
		exitCode.Should().Be(1, because: "the server refused the edit");
		List<string> logged = Logged(logger);
		logged.Should().Contain(text => text.Contains(RedactedReason, StringComparison.Ordinal),
			because: "the server's reason reaches the error line, redacted");
		logged.Should().NotContain(text => text.Contains(Marker, StringComparison.Ordinal),
			because: "the error line is the exception's message, which carries no login");
	}

	[Test]
	[Category("Unit")]
	[Description("compile-business-process: the compile's message, each error's location and message, and a server "
		+ "name that is not a code are fenced as SITE text (UntrustedText.FencedSiteText) in the command's log lines.")]
	public void CompileBusinessProcess_ShouldFenceTheServerText_AsSiteText() {
		// Arrange
		ICompileBusinessProcessService service = Substitute.For<ICompileBusinessProcessService>();
		service.Compile(Env, Arg.Any<CompileBusinessProcessRequest>()).Returns(new CompileBusinessProcessResult(false,
			SiteMessage, "UsrFixtureProcess", "Custom " + SiteMessage, "general", true, true, 1000,
			[new CompileBusinessProcessError("UsrFixture.Custom.cs " + SiteMessage, 3, 4, "CS0103", SiteMessage, true)],
			1));
		ILogger logger = Substitute.For<ILogger>();
		CompileBusinessProcessCommand command = new(service, logger);

		// Act
		int exitCode = command.Execute(new CompileBusinessProcessOptions { Environment = Env, ProcessName = "UsrFixtureProcess" });

		// Assert
		exitCode.Should().Be(1, because: "the compile failed");
		List<string> logged = Logged(logger);
		logged.Should().Contain(text => text.Contains("CompileProcess failed: [untrusted-source-text begin] "
				+ RedactedReason, StringComparison.Ordinal),
			because: "the server's summary stays, fenced and redacted");
		logged.Should().NotContain(text => text.Contains(Marker, StringComparison.Ordinal),
			because: "no fenced segment of the log carries the login");
	}

	#endregion
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Command.RelatedPages;
using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Package;
using Clio.Package.Responses;
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

	private static IApplicationClient UpdatePageSite(string designPackage, string hierarchy, string getSchema,
		string save = """{"success": true}""") {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => call.ArgAt<string>(0) switch {
				SelectQueryUrl => $$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}", "ExtendParent": false}]}""",
				DesignPackageUrl => designPackage,
				ParentSchemasUrl => hierarchy,
				GetSchemaUrl => getSchema,
				SaveSchemaUrl => save,
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

	[Test]
	[Category("Unit")]
	[Description("update-page: a design-package refusal whose message is only a stack trace (DescribeRefusal redacts it "
		+ "to nothing) reads as clio's own sentence, not as a dangling 'Failed to resolve design package: '.")]
	public void UpdatePage_ShouldSayItsOwnSentence_WhenTheRefusalRedactsToNothing() {
		// Arrange
		IApplicationClient client = UpdatePageSite(Refusal("\n   at Terrasoft.Fixture.Designer.Read()"),
			HierarchyOf(DesignPackage), Refusal("unused"));

		// Act
		(string error, List<string> _) = RunUpdatePage(client, () => UpdatePageOptions());

		// Assert
		error.Should().Be($"Failed to resolve design package for '{SchemaName}': Failed to resolve design package",
			because: "a reason that redacts to nothing is no reason");
	}

	[Test]
	[Category("Unit")]
	[Description("update-page: an addonsErrors item of the designer's save refusal (ParseSaveErrorMessage) gives its "
		+ "code and message only, redacted as site text - never its stackTrace - in the error and in the logged response.")]
	public void UpdatePage_ShouldReadOnlyTheMessageOfAnAddonError_NeverItsStackTrace() {
		// Arrange
		string save = JsonSerializer.Serialize(new {
			success = false,
			addonsErrors = new[] {
				new { errorCode = "AddonException", message = SiteMessage, stackTrace = "   at Terrasoft.Fixture.Addon.Save()" }
			}
		});
		IApplicationClient client = UpdatePageSite(DesignPackageOf(DesignPackage), HierarchyOf(DesignPackage),
			JsonSerializer.Serialize(new { success = true, schema = new { body = "old", name = SchemaName } }), save);

		// Act
		(string error, List<string> logged) = RunUpdatePage(client, () => UpdatePageOptions());

		// Assert
		error.Should().Contain("AddonException: " + RedactedReason, because: "the item's code and reason stay");
		foreach (string text in logged.Append(error)) {
			text.Should().NotContainAny([Marker, "Terrasoft.Fixture", "stackTrace"],
				because: "neither the login nor the item's stack trace reaches the error or the log");
		}
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
		IServiceUrlBuilder urls = CreatePageUrls();
		ILogger logger = Substitute.For<ILogger>();
		PageCreateCommand command = new(client, urls, new SchemaTemplateCatalog(client, urls), logger,
			Substitute.For<ICaptionCultureResolver>());

		// Act
		command.TryCreatePage(CreatePageOptions(), out PageCreateResponse response);

		// Assert
		response.Error.Should().Contain("Response preview: " + RedactedReason, because: "the reason stays");
		foreach (string text in Logged(logger).Append(response.Error)) {
			text.Should().NotContainAny([Marker, "Terrasoft.Fixture"],
				because: "the login and the stack trace reach neither the error nor create-page's failure line");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("create-page: a template catalog refusal whose message redacts to nothing (DescribeRefusal) reads as "
		+ "clio's own sentence instead of an empty one.")]
	public void CreatePage_ShouldSayItsOwnSentence_WhenTheCatalogRefusalRedactsToNothing() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>()).Returns(Refusal("\n   at Terrasoft.Fixture.Catalog.Read()"));
		IServiceUrlBuilder urls = CreatePageUrls();
		PageCreateCommand command = new(client, urls, new SchemaTemplateCatalog(client, urls), Substitute.For<ILogger>(),
			Substitute.For<ICaptionCultureResolver>());

		// Act
		command.TryCreatePage(CreatePageOptions(), out PageCreateResponse response);

		// Assert
		response.Error.Should().Be("Failed to resolve template catalog: Failed to load page template catalog",
			because: "a reason that redacts to nothing is no reason");
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

	// SelectQueryHelper writes no log line of its own: it throws, and the message it builds is what each caller puts
	// into its error and its log line verbatim. The exception's message is therefore both halves of these two cases.

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

	// The head of a JSON Web Token: header and part of the payload, with no signature - the shape the redactor does
	// not recognize on its own (it needs all three segments).
	private const string TokenHead = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJmaXh0dXJlLXVzZXIiLCJyb2xlIjoi";

	[Test]
	[Category("Unit")]
	[Description("The non-JSON preview's redaction input is clamped (8x the 200-character preview) without cutting a "
		+ "token in two: a token that straddles the clamp is dropped, not shown as a head the redactor cannot "
		+ "recognize.")]
	public void SelectQuery_ShouldNotShowATokenHead_WhenTheClampCutsTheToken() {
		// Arrange - a secret value the redaction shortens to a placeholder, so what follows it reaches the preview, and
		// a token that starts just before the 1,600-character clamp.
		string lead = "password=" + new string('k', 1600 - 9 - 1 - 30) + " ";
		string body = lead + TokenHead + ".dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk tail";

		// Act
		Exception exception = RunSelectQuery(body);

		// Assert
		exception.Message.Should().Contain("Response preview: password=[redacted]",
			because: "the secret before the token is redacted");
		exception.Message.Should().NotContain("eyJhbGciOiJIUzI1NiJ9", because: "no head of the cut token is shown");
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
		if (part == "compiler") {
			description.Should().Contain("UsrFixture.cs(1,79) error CS1519",
				because: "the line still names the file to fix; only its directories are the environment's layout");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("The installation failure line names the file of a compiler line whatever path the environment wrote "
		+ "it under: an absolute Windows or POSIX path, or a relative Autogenerated\\Src one.")]
	public void InstallFailure_ShouldNameTheFile_OfACompilerLine(
		[Values(@"C:\inetpub\wwwroot\creatio\Terrasoft.Configuration\Autogenerated\Src\UsrFixture.cs",
			"/app/Terrasoft.Configuration/Autogenerated/Src/UsrFixture.cs", @"Autogenerated\Src\UsrFixture.cs")]
		string path) {
		// Arrange
		BaseResponse response = new() {
			Success = false, ErrorInfo = new SiteErrorInfo { ErrorCode = "Exception", Message = "Packages installation failed" }
		};

		// Act
		string description = InstallLogAnalyzer.DescribeFailure(response,
			path + "(1,79) error CS1519: Invalid token '!=' in a member declaration\r\n");

		// Assert
		description.Should().Be("the configuration failed to compile. UsrFixture.cs(1,79) error CS1519: Invalid token "
			+ "'!=' in a member declaration", because: "the file name stays and the directories go");
	}

	[Test]
	[Category("Unit")]
	[Description("The installation failure line's log tail is redacted with its lines still apart, so the site-text "
		+ "cut drops the stack frames of an exception the log ended with.")]
	public void InstallFailure_ShouldCutTheStackFrames_OfTheLogTail() {
		// Arrange
		string log = "Install failed\r\nSystem.NullReferenceException: Object reference not set to an instance of an "
			+ "object.\r\n   at Terrasoft.Core.Packages.PackageInstaller.Install(String name)\r\n";

		// Act
		string description = InstallLogAnalyzer.DescribeFailure(new BaseResponse { Success = false }, log);

		// Assert
		description.Should().Be("the installation service reported a failure without an error message. Last log lines: "
			+ "Install failed | System.NullReferenceException: Object reference not set to an instance of an object.",
			because: "the reason stays and the frames after it go");
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
	[Description("compile-business-process: the compile's message and each error's location and message are fenced as "
		+ "SITE text (UntrustedText.FencedSiteText) in the command's log lines. The command writes no result field of "
		+ "its own: its log lines are what a caller reads.")]
	public void CompileBusinessProcess_ShouldFenceTheServerText_AsSiteText() {
		// Arrange
		ICompileBusinessProcessService service = Substitute.For<ICompileBusinessProcessService>();
		service.Compile(Env, Arg.Any<CompileBusinessProcessRequest>()).Returns(new CompileBusinessProcessResult(false,
			SiteMessage, "UsrFixtureProcess", "Custom", "general", true, true, 1000,
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

	[Test]
	[Category("Unit")]
	[Description("compile-business-process: a process or package name the server echoes back that is not a code is "
		+ "fenced as SITE text in the success line (ServerName runs only on success).")]
	public void CompileBusinessProcess_ShouldFenceAServerName_AsSiteText_OnSuccess() {
		// Arrange
		ICompileBusinessProcessService service = Substitute.For<ICompileBusinessProcessService>();
		service.Compile(Env, Arg.Any<CompileBusinessProcessRequest>()).Returns(new CompileBusinessProcessResult(true,
			null, "UsrFixtureProcess " + SiteMessage, "Custom " + SiteMessage, "general", true, true, 1000, [], 0));
		ILogger logger = Substitute.For<ILogger>();
		CompileBusinessProcessCommand command = new(service, logger);

		// Act
		int exitCode = command.Execute(new CompileBusinessProcessOptions { Environment = Env, ProcessName = "UsrFixtureProcess" });

		// Assert
		exitCode.Should().Be(0, because: "the compile succeeded; only how its names are shown is under test");
		List<string> logged = Logged(logger);
		logged.Should().Contain(text => text.Contains("Compiled package '[untrusted-source-text begin] Custom "
				+ RedactedReason, StringComparison.Ordinal),
			because: "the package name stays, fenced and redacted");
		logged.Should().NotContain(text => text.Contains(Marker, StringComparison.Ordinal),
			because: "the success line is the caller's whole result");
	}

	private static (ILogger Logger, int ExitCode) RunModifyAsNewVersion(string body) {
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(body);
		(EnvironmentSettings environment, ISettingsRepository settings, IApplicationClientFactory factory) =
			ProcessEnvironment(client);
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(ServiceUrlBuilder.KnownRoute.ModifyProcessAsNewVersion, environment)
			.Returns("http://sandbox/0/rest/ProcessDesignService/ModifyProcessAsNewVersion");
		ILogger logger = Substitute.For<ILogger>();
		ModifyProcessAsNewVersionService service = new(settings, factory, urls,
			Substitute.For<IProcessPageFactsChecker>(), Substitute.For<ILogger>());
		int exitCode = new ModifyProcessAsNewVersionCommand(service, Substitute.For<IProcessDescriber>(), logger).Execute(
			new ModifyProcessAsNewVersionOptions {
				Environment = Env, ProcessName = "UsrFixtureProcess",
				OperationsJson = "[{\"op\":\"addParameter\",\"parameter\":{\"name\":\"Amount\",\"type\":\"Integer\"}}]"
			});
		return (logger, exitCode);
	}

	[Test]
	[Category("Unit")]
	[Description("modify-business-process-as-new-version: the refusal's errorMessage, the created version's name, the "
		+ "layout elements and every warning (BuildFailureMessage) go through RedactSiteText, in the exception and in "
		+ "the command's error line, and each warning is redacted on its own.")]
	public void ModifyAsNewVersion_ShouldRedactTheServerText_InTheExceptionAndTheLog() {
		// Arrange
		string body = JsonSerializer.Serialize(new {
			ModifyProcessAsNewVersionResult = new {
				success = false, errorMessage = SiteMessage, versionName = "UsrFixtureProcess_v2 " + SiteMessage,
				versionSchemaUId = "55555555-5555-5555-5555-555555555555",
				layoutChange = new { elements = new[] { "UsrStart " + SiteMessage } },
				warnings = new[] { "Warning " + SiteMessage + "\n   at Terrasoft.Fixture.Process.Save()", OwedCompileWarning }
			}
		});

		// Act
		(ILogger logger, int exitCode) = RunModifyAsNewVersion(body);

		// Assert
		exitCode.Should().Be(1, because: "the server refused the new version");
		string line = Logged(logger).Single(text => text.StartsWith(RedactedReason, StringComparison.Ordinal));
		line.Should().Contain("The version 'UsrFixtureProcess_v2 " + RedactedReason + "'",
			because: "the version name stays, redacted");
		line.Should().Contain("Affected elements: UsrStart " + RedactedReason, because: "the element name stays");
		line.Should().Contain(OwedCompileWarning, because: "a frame in the first warning may not cut the second");
		line.Should().NotContainAny([Marker, "Terrasoft.Fixture"],
			because: "the error line is the exception's message, which carries no login and no frame");
	}

	[Test]
	[Category("Unit")]
	[Description("set-active-process-version: the refusal's errorMessage, the active version's name and the warnings "
		+ "(BuildFailureMessage) go through RedactSiteText, in the exception and in the command's error line.")]
	public void SetActiveProcessVersion_ShouldRedactTheServerText_InTheExceptionAndTheLog() {
		// Arrange
		string body = JsonSerializer.Serialize(new {
			SetActiveProcessVersionResult = new {
				success = false, errorMessage = SiteMessage, activeVersionName = "UsrFixtureProcess_v1 " + SiteMessage,
				activeVersionSchemaUId = "66666666-6666-6666-6666-666666666666",
				warnings = new[] { "Warning " + SiteMessage }
			}
		});
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(body);
		(EnvironmentSettings environment, ISettingsRepository settings, IApplicationClientFactory factory) =
			ProcessEnvironment(client);
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(ServiceUrlBuilder.KnownRoute.SetActiveProcessVersion, environment)
			.Returns("http://sandbox/0/rest/ProcessDesignService/SetActiveProcessVersion");
		ILogger logger = Substitute.For<ILogger>();
		SetActiveProcessVersionService service = new(settings, factory, urls, Substitute.For<ILogger>());

		// Act
		int exitCode = new SetActiveProcessVersionCommand(service, Substitute.For<IProcessDescriber>(), logger).Execute(
			new SetActiveProcessVersionOptions { Environment = Env, VersionName = "UsrFixtureProcess_v2" });

		// Assert
		exitCode.Should().Be(1, because: "the server refused the activation");
		string line = Logged(logger).Single(text => text.StartsWith(RedactedReason, StringComparison.Ordinal));
		line.Should().Contain("'UsrFixtureProcess_v1 " + RedactedReason + "'", because: "the version name stays");
		line.Should().Contain("Warning " + RedactedReason, because: "the warning stays");
		Logged(logger).Should().NotContain(text => text.Contains(Marker, StringComparison.Ordinal),
			because: "the error line is the exception's message, which carries no login");
	}

	private const string OwedCompileWarning =
		"The process has a script task: compile it with compile-business-process before it runs.";

	[Test]
	[Category("Unit")]
	[Description("create-business-process: a failure after the save keeps every warning, each redacted on its own, so "
		+ "a stack frame in one warning cannot cut the owed-compile warning after it.")]
	public void CreateBusinessProcess_ShouldKeepTheOwedCompileWarning_WhenAnEarlierWarningCarriesAFrame() {
		// Arrange
		string body = JsonSerializer.Serialize(new {
			BuildProcessResult = new {
				success = false, errorMessage = "Tracing could not be switched on.",
				schemaUId = "77777777-7777-7777-7777-777777777777",
				warnings = new[] { "Warning " + SiteMessage + "\n   at Terrasoft.Fixture.Process.Save()", OwedCompileWarning }
			}
		});
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

		// Act
		int exitCode = new CreateBusinessProcessCommand(service, Substitute.For<IProcessDescriber>(), logger).Execute(
			new CreateBusinessProcessOptions { Environment = Env, DescriptorJson = ProcessDescriptor });

		// Assert
		exitCode.Should().Be(1, because: "the build reported a failure");
		string line = Logged(logger).Single(text => text.StartsWith("Tracing could not be switched on.", StringComparison.Ordinal));
		line.Should().Contain(OwedCompileWarning, because: "the compile the script task owes must not vanish");
		line.Should().NotContainAny([Marker, "Terrasoft.Fixture"], because: "each warning is redacted as site text");
	}

	[Test]
	[Category("Unit")]
	[Description("modify-business-process: a saved edit's failure keeps every warning, each redacted on its own, and "
		+ "a layout refusal's element names are redacted as site text.")]
	public void ModifyBusinessProcess_ShouldRedactTheElementNamesAndEachWarning([Values(true, false)] bool saved) {
		// Arrange
		string body = JsonSerializer.Serialize(new {
			ModifyProcessResult = new {
				success = false, errorMessage = "The edit was refused.",
				schemaUId = saved ? "88888888-8888-8888-8888-888888888888" : null,
				layoutChange = new { elements = new[] { "UsrStart " + SiteMessage } },
				warnings = new[] { "Warning " + SiteMessage + "\n   at Terrasoft.Fixture.Process.Save()", OwedCompileWarning }
			}
		});
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

		// Act
		int exitCode = new ModifyBusinessProcessCommand(service, Substitute.For<IProcessDescriber>(), logger).Execute(
			new ModifyBusinessProcessOptions {
				Environment = Env, ProcessName = "UsrFixtureProcess",
				OperationsJson = "[{\"op\":\"addParameter\",\"parameter\":{\"name\":\"Amount\",\"type\":\"Integer\"}}]"
			});

		// Assert
		exitCode.Should().Be(1, because: "the server refused the edit");
		string line = Logged(logger).Single(text => text.StartsWith("The edit was refused.", StringComparison.Ordinal));
		line.Should().Contain("Affected elements: UsrStart " + RedactedReason, because: "the element name stays");
		if (saved) {
			line.Should().Contain(OwedCompileWarning, because: "a frame in the first warning may not cut the second");
		}
		line.Should().NotContainAny([Marker, "Terrasoft.Fixture"], because: "the server's text is redacted as site text");
	}

	[TestCase("create", TestName = "ProcessWriteSuccess_ShouldLogEachWarning_AsSiteText(create)")]
	[TestCase("modify", TestName = "ProcessWriteSuccess_ShouldLogEachWarning_AsSiteText(modify)")]
	[TestCase("new-version", TestName = "ProcessWriteSuccess_ShouldLogEachWarning_AsSiteText(new-version)")]
	[TestCase("activate", TestName = "ProcessWriteSuccess_ShouldLogEachWarning_AsSiteText(activate)")]
	[Category("Unit")]
	[Description("A SUCCESSFUL process write (create, modify in place, as a new version, activate a version) carries the "
		+ "server's warnings in its result and logs each as a warning line: each is redacted as site text on its own, "
		+ "so a frame in one cannot cut the owed-compile warning after it.")]
	public void ProcessWriteSuccess_ShouldLogEachWarning_AsSiteText(string write) {
		// Arrange
		string[] warnings = ["Warning " + SiteMessage + "\n   at Terrasoft.Fixture.Process.Save()", OwedCompileWarning];
		const string uid = "99999999-9999-9999-9999-999999999999";
		string body = write switch {
			"create" => JsonSerializer.Serialize(new {
				BuildProcessResult = new { success = true, schemaName = "UsrFixtureProcess", schemaUId = uid, warnings }
			}),
			"modify" => JsonSerializer.Serialize(new {
				ModifyProcessResult = new {
					success = true, schemaName = "UsrFixtureProcess", schemaUId = uid, appliedOperations = 1, warnings
				}
			}),
			"new-version" => JsonSerializer.Serialize(new {
				ModifyProcessAsNewVersionResult = new {
					success = true, versionName = "UsrFixtureProcess_v2", versionSchemaUId = uid, appliedOperations = 1,
					warnings
				}
			}),
			_ => JsonSerializer.Serialize(new {
				SetActiveProcessVersionResult = new {
					success = true, activeVersionName = "UsrFixtureProcess_v2", activeVersionSchemaUId = uid, warnings
				}
			})
		};
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient, IStatusPreservingApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(body);
		((IStatusPreservingApplicationClient)client)
			.SendKeepingStatus(default, default, default, default, default, default, default)
			.ReturnsForAnyArgs(new SiteHttpResponse(200, body));
		(EnvironmentSettings environment, ISettingsRepository settings, IApplicationClientFactory factory) =
			ProcessEnvironment(client);
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>(), environment)
			.Returns("http://sandbox/0/rest/ProcessDesignService/Write");
		ILogger logger = Substitute.For<ILogger>();
		IProcessDescriber describer = Substitute.For<IProcessDescriber>();
		const string operations = "[{\"op\":\"addParameter\",\"parameter\":{\"name\":\"Amount\",\"type\":\"Integer\"}}]";

		// Act
		int exitCode = write switch {
			"create" => new CreateBusinessProcessCommand(new CreateBusinessProcessService(settings, factory, urls,
					Substitute.For<IProcessPageFactsChecker>(), Substitute.For<ILogger>()), describer, logger)
				.Execute(new CreateBusinessProcessOptions { Environment = Env, DescriptorJson = ProcessDescriptor }),
			"modify" => new ModifyBusinessProcessCommand(new ModifyBusinessProcessService(settings, factory, urls,
					Substitute.For<IProcessPageFactsChecker>(), Substitute.For<ILogger>()), describer, logger)
				.Execute(new ModifyBusinessProcessOptions {
					Environment = Env, ProcessName = "UsrFixtureProcess", OperationsJson = operations
				}),
			"new-version" => new ModifyProcessAsNewVersionCommand(new ModifyProcessAsNewVersionService(settings, factory,
					urls, Substitute.For<IProcessPageFactsChecker>(), Substitute.For<ILogger>()), describer, logger)
				.Execute(new ModifyProcessAsNewVersionOptions {
					Environment = Env, ProcessName = "UsrFixtureProcess", OperationsJson = operations
				}),
			_ => new SetActiveProcessVersionCommand(new SetActiveProcessVersionService(settings, factory, urls,
					Substitute.For<ILogger>()), describer, logger)
				.Execute(new SetActiveProcessVersionOptions { Environment = Env, VersionName = "UsrFixtureProcess_v2" })
		};

		// Assert
		exitCode.Should().Be(0, because: "the server accepted the write");
		logger.Received().WriteWarning("Warning " + RedactedReason);
		logger.Received().WriteWarning(OwedCompileWarning);
		foreach (string text in Logged(logger)) {
			text.Should().NotContainAny([Marker, "Terrasoft.Fixture"],
				because: "the warning lines are the site's text, and a stdio log is not redacted again");
		}
	}

	#endregion

	#region Dynamic cases (DynamicCaseService, BuildCaseClient)

	private static (IBuildCaseClient Client, IDynamicCaseSession Session, DynamicCaseService Service) DynamicCaseSite() {
		IBuildCaseClient client = Substitute.For<IBuildCaseClient>();
		IDynamicCaseSession session = Substitute.For<IDynamicCaseSession>();
		client.OpenSession(Env).Returns(session);
		IDynamicCasePageStep pageStep = Substitute.For<IDynamicCasePageStep>();
		pageStep.Plan(Env, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
			.Returns(new DynamicCasePagePlan {
				Status = DynamicCasePageStep.StatusPlanned, PageSchemaName = "UsrOrders_FormPage",
				ResolvedBy = DynamicCaseRecordPageResolver.ResolvedByRelatedPageAddon, DesignPackageName = "UsrOrders",
				Template = "PageWithTabsFreedomTemplate", SaveOnChange = false,
				Fragment = ProgressBarFragment.BuildBody("UsrOrders_FormPage", "UsrOrder", string.Empty)
			});
		IPackageDependencyManager dependencies = Substitute.For<IPackageDependencyManager>();
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		EnvironmentSettings environment = new() { Uri = "http://sandbox" };
		settings.FindEnvironment(Env).Returns(environment);
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(Arg.Any<string>(), environment).Returns(call => "http://sandbox/0/" + call.ArgAt<string>(0));
		return (client, session, new DynamicCaseService(client, pageStep, dependencies, settings, urls));
	}

	[Test]
	[Category("Unit")]
	[Description("create-dynamic-case: a refused build's errorMessage and errorCode (DynamicCaseService.FromServer) go "
		+ "through RedactSiteText, in the result and in the command's verdict and result log lines.")]
	public void CreateDynamicCase_ShouldRedactTheBuildRefusal_InTheResultAndTheLog() {
		// Arrange
		(IBuildCaseClient _, IDynamicCaseSession session, DynamicCaseService service) = DynamicCaseSite();
		session.Build(Arg.Any<BuildCaseRequest>()).Returns(new DynamicCaseReply<BuildCaseResponse>(
			DynamicCaseReplyKind.Envelope,
			new BuildCaseResponse { Success = false, ErrorCode = "StageColumnInvalid " + SiteMessage, ErrorMessage = SiteMessage },
			null));
		ILogger logger = Substitute.For<ILogger>();

		// Act
		int exitCode = new CreateDynamicCaseCommand(service, logger).Execute(new CreateDynamicCaseOptions {
			Environment = Env, PackageName = "UsrOrders", EntitySchemaName = "UsrOrder", StageColumnName = "UsrStatus"
		});
		DynamicCaseResult result = service.Create(new CreateDynamicCaseOptions {
			Environment = Env, PackageName = "UsrOrders", EntitySchemaName = "UsrOrder", StageColumnName = "UsrStatus"
		});

		// Assert
		exitCode.Should().Be(1, because: "the build was refused");
		result.ErrorMessage.Should().Be(RedactedReason, because: "the server's reason is the result's message, redacted");
		Logged(logger).Should().Contain(text => text.Contains("Refused [StageColumnInvalid " + RedactedReason + "]: " + RedactedReason,
				StringComparison.Ordinal), because: "the verdict quotes the redacted reason")
			.And.NotContain(text => text.Contains(Marker, StringComparison.Ordinal),
				because: "neither the verdict nor the result line may carry the login");
	}

	[Test]
	[Category("Unit")]
	[Description("modify-dynamic-case: a refused change's errorMessage (DynamicCaseService.Modify) goes through "
		+ "RedactSiteText, in the result and in the command's log lines.")]
	public void ModifyDynamicCase_ShouldRedactTheRefusal_InTheResultAndTheLog() {
		// Arrange
		(IBuildCaseClient _, IDynamicCaseSession session, DynamicCaseService service) = DynamicCaseSite();
		session.Modify(Arg.Any<ModifyCaseRequest>()).Returns(new DynamicCaseReply<ModifyCaseResponse>(
			DynamicCaseReplyKind.Envelope,
			new ModifyCaseResponse { Success = false, ErrorCode = "CaseNotFound " + SiteMessage, ErrorMessage = SiteMessage }, null));
		ILogger logger = Substitute.For<ILogger>();
		ModifyDynamicCaseOptions options = new() { Environment = Env, CaseName = "UsrOrderCase", Caption = "Orders" };

		// Act
		int exitCode = new ModifyDynamicCaseCommand(service, logger).Execute(options);
		ModifyDynamicCaseResult result = service.Modify(options);

		// Assert
		exitCode.Should().Be(1, because: "the change was refused");
		result.ErrorMessage.Should().Be(RedactedReason, because: "the server's reason is the result's message, redacted");
		Logged(logger).Should().Contain(text => text.Contains(RedactedReason, StringComparison.Ordinal),
				because: "the verdict quotes the redacted reason")
			.And.NotContain(text => text.Contains(Marker, StringComparison.Ordinal), because: "no log line carries the login");
	}

	[Test]
	[Category("Unit")]
	[Description("describe-dynamic-case: a refused read's errorMessage (DynamicCaseService.Describe) goes through "
		+ "RedactSiteText, in the result and in the command's log lines.")]
	public void DescribeDynamicCase_ShouldRedactTheRefusal_InTheResultAndTheLog() {
		// Arrange
		(IBuildCaseClient _, IDynamicCaseSession session, DynamicCaseService service) = DynamicCaseSite();
		session.Describe(Arg.Any<DescribeCaseRequest>()).Returns(new DynamicCaseReply<DescribeCaseResponse>(
			DynamicCaseReplyKind.Envelope,
			new DescribeCaseResponse { Success = false, ErrorCode = "CaseNotFound " + SiteMessage, ErrorMessage = SiteMessage },
			null));
		ILogger logger = Substitute.For<ILogger>();
		DescribeDynamicCaseOptions options = new() { Environment = Env, CaseName = "UsrOrderCase" };

		// Act
		int exitCode = new DescribeDynamicCaseCommand(service, logger).Execute(options);
		DescribeDynamicCaseResult result = service.Describe(options);

		// Assert
		exitCode.Should().Be(1, because: "the read was refused");
		result.ErrorMessage.Should().Be(RedactedReason, because: "the server's reason is the result's message, redacted");
		Logged(logger).Should().Contain(text => text.Contains("Refused [CaseNotFound " + RedactedReason + "]: " + RedactedReason,
				StringComparison.Ordinal), because: "the verdict quotes the redacted reason")
			.And.NotContain(text => text.Contains(Marker, StringComparison.Ordinal), because: "no log line carries the login");
	}

	[Test]
	[Category("Unit")]
	[Description("Every dynamic-case tool: a body the BuildCase/DescribeCase/ModifyCase route answers that is not the "
		+ "envelope (BuildCaseClient's quoted detail) goes through RedactSiteText before its cap, in the result and in "
		+ "the command's log lines - a non-JSON body, a JSON body that is not the envelope, and a bare fault string.")]
	public void BuildCaseClient_ShouldRedactTheQuotedBody_InTheResultAndTheLog(
		[Values("text", "json", "fault")] string shape) {
		// Arrange
		string body = shape switch {
			"text" => SiteMessage + "\r\n   at Terrasoft.Fixture.Case.Describe()",
			"json" => $$"""{"unexpected": "{{SiteMessage}}"}""",
			_ => JsonSerializer.Serialize(SiteMessage)
		};
		IOwnedApplicationClient applicationClient = Substitute.For<IOwnedApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(body);
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		EnvironmentSettings environment = new() { Uri = "http://sandbox" };
		settings.FindEnvironment(Env).Returns(environment);
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateEnvironmentClient(environment).Returns(applicationClient);
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>(), environment).Returns("http://sandbox/0/rest/ProcessDesignService/X");
		urls.Build(Arg.Any<string>(), environment).Returns(call => "http://sandbox/0/" + call.ArgAt<string>(0));
		DynamicCaseService service = new(new BuildCaseClient(settings, factory, urls), Substitute.For<IDynamicCasePageStep>(),
			Substitute.For<IPackageDependencyManager>(), settings, urls);
		ILogger logger = Substitute.For<ILogger>();
		DescribeDynamicCaseOptions options = new() { Environment = Env, CaseName = "UsrOrderCase" };

		// Act
		int exitCode = new DescribeDynamicCaseCommand(service, logger).Execute(options);
		DescribeDynamicCaseResult result = service.Describe(options);

		// Assert
		exitCode.Should().Be(1, because: "the answer was not the envelope");
		result.ErrorMessage.Should().Contain("Login failed for user", because: "the reason stays")
			.And.NotContainAny([Marker, "Terrasoft.Fixture"], because: "the quoted body is the site's text");
		Logged(logger).Should().NotContain(text => text.Contains(Marker, StringComparison.Ordinal),
			because: "the verdict and the result line quote the same redacted detail");
	}

	#endregion

	#region Round 4: follow-up text at the write tools' sinks, lists and odd shapes

	private static (IDynamicCaseSession Session, IBuildCaseClient Client) CaseSession(BuildCaseResponse validate,
		BuildCaseResponse build = null) {
		IBuildCaseClient client = Substitute.For<IBuildCaseClient>();
		IDynamicCaseSession session = Substitute.For<IDynamicCaseSession>();
		client.OpenSession(Env).Returns(session);
		session.Build(Arg.Is<BuildCaseRequest>(request => request.ValidateOnly))
			.Returns(new DynamicCaseReply<BuildCaseResponse>(DynamicCaseReplyKind.Envelope, validate, null));
		if (build is not null) {
			session.Build(Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly))
				.Returns(new DynamicCaseReply<BuildCaseResponse>(DynamicCaseReplyKind.Envelope, build, null));
		}
		return (session, client);
	}

	private static (ISettingsRepository Settings, IServiceUrlBuilder Urls) CaseEnvironment() {
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		EnvironmentSettings environment = new() { Uri = "http://sandbox" };
		settings.FindEnvironment(Env).Returns(environment);
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(Arg.Any<string>(), environment).Returns(call => "http://sandbox/0/" + call.ArgAt<string>(0));
		urls.Build(Arg.Any<string>()).Returns(call => "http://sandbox/0/" + call.ArgAt<string>(0));
		return (settings, urls);
	}

	private static CreateDynamicCaseOptions CaseOptions(bool addProgressBar = true) => new() {
		Environment = Env, PackageName = "UsrOrders", EntitySchemaName = "UsrOrder", StageColumnName = "UsrStatus",
		AddProgressBar = addProgressBar
	};

	[Test]
	[Category("Unit")]
	[Description("create-dynamic-case END TO END: the RelatedPage add-on read (a follow-up reader) refuses with the "
		+ "site's text, and the real record page resolver and page step put it into the page refusal - which "
		+ "DynamicCaseService redacts where it enters the result and the command's verdict line.")]
	public void CreateDynamicCase_ShouldRedactThePageRefusal_EndToEnd() {
		// Arrange
		IApplicationClient addonClient = Substitute.For<IApplicationClient>();
		addonClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(Refusal(SiteMessage + "\r\n   at Terrasoft.Fixture.Addon.GetSchema()"));
		(ISettingsRepository settings, IServiceUrlBuilder urls) = CaseEnvironment();
		IEntityAddonTargetResolver targets = Substitute.For<IEntityAddonTargetResolver>();
		targets.Resolve(Arg.Any<string>(), Arg.Any<string>()).Returns(new EntityAddonTarget("pkg-uid", Guid.NewGuid(),
			new EntityDesignSchemaDto { UId = Guid.NewGuid(), Name = "UsrOrder" }));
		RelatedPageAddonService addons = new(addonClient, urls,
			new AddonSchemaDesignerClient(addonClient, new JsonConverter(), urls, Substitute.For<ILogger>()), targets,
			Substitute.For<IPageSchemaResolver>(), Substitute.For<ILogger>());
		DynamicCasePageStep pageStep = new(new DynamicCaseRecordPageResolver(addons, Substitute.For<IEntityClientSchemaReader>()),
			Substitute.For<IProcessPageReader>(), Substitute.For<IPageBodyWriter>(),
			Substitute.For<IPageDataSourceReferenceValidator>());
		(IDynamicCaseSession _, IBuildCaseClient client) = CaseSession(new BuildCaseResponse { Success = true });
		DynamicCaseService service = new(client, pageStep, Substitute.For<IPackageDependencyManager>(), settings, urls);
		ILogger logger = Substitute.For<ILogger>();

		// Act
		int exitCode = new CreateDynamicCaseCommand(service, logger).Execute(CaseOptions());
		DynamicCaseResult result = service.Create(CaseOptions());

		// Assert
		exitCode.Should().Be(1, because: "the record page could not be resolved");
		result.ErrorMessage.Should().Contain(RedactedReason, because: "the add-on read's reason stays")
			.And.NotContainAny([Marker, "Terrasoft.Fixture"], because: "the result carries no login and no frame");
		JsonSerializer.Serialize(result).Should().NotContain(Marker, because: "no field of the result carries the login");
		foreach (string text in Logged(logger)) {
			text.Should().NotContainAny([Marker, "Terrasoft.Fixture"], because: "the verdict and the result line are "
				+ "the stdio caller's whole answer");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("create-dynamic-case END TO END: adding the missing package dependencies fails in the real "
		+ "PackageDependencyManager (a follow-up reader) with the site's text, which DynamicCaseService redacts where it "
		+ "enters the result and the command's verdict line.")]
	public void CreateDynamicCase_ShouldRedactTheDependencyFailure_EndToEnd() {
		// Arrange
		IApplicationPackageListProvider packages = Substitute.For<IApplicationPackageListProvider>();
		packages.GetPackages("{}", Arg.Any<int>()).Returns([
			new PackageInfo(new PackageDescriptor { Name = "UsrOrders", UId = Guid.NewGuid(), PackageVersion = "1.0.0.0" },
				string.Empty, []),
			new PackageInfo(new PackageDescriptor { Name = "CrtBase", UId = Guid.NewGuid(), PackageVersion = "8.0.0.0" },
				string.Empty, [])
		]);
		IApplicationClient packageClient = Substitute.For<IApplicationClient>();
		packageClient.ExecutePostRequest<PackagePropertiesResponse>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
				Arg.Any<int>(), Arg.Any<int>())
			.Returns(new PackagePropertiesResponse { Success = false, ErrorInfo = new SiteErrorInfo { Message = SiteMessage } });
		(ISettingsRepository settings, IServiceUrlBuilder urls) = CaseEnvironment();
		PackageDependencyManager dependencies = new(packages, packageClient, urls, Substitute.For<ILogger>());
		(IDynamicCaseSession _, IBuildCaseClient client) = CaseSession(new BuildCaseResponse {
			Success = true, MissingDependencies = ["CrtBase"]
		});
		DynamicCaseService service = new(client, Substitute.For<IDynamicCasePageStep>(), dependencies, settings, urls);
		ILogger logger = Substitute.For<ILogger>();

		// Act
		int exitCode = new CreateDynamicCaseCommand(service, logger).Execute(CaseOptions(addProgressBar: false));
		DynamicCaseResult result = service.Create(CaseOptions(addProgressBar: false));

		// Assert
		exitCode.Should().Be(1, because: "the dependencies could not be added");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.DependencyAddFailed, because: "the add was refused");
		result.ErrorMessage.Should().Contain(RedactedReason, because: "the storage's reason stays")
			.And.NotContain(Marker, because: "the result carries no login");
		Logged(logger).Should().NotContain(text => text.Contains(Marker, StringComparison.Ordinal),
			because: "the verdict and the result line carry no login");
	}

	[Test]
	[Category("Unit")]
	[Description("create-dynamic-case: the server's notes, warnings and leftovers of a build that failed and left "
		+ "something behind are redacted item by item, in the result and in the command's verdict, note and warning "
		+ "lines; a frame in one item cannot cut the next.")]
	public void CreateDynamicCase_ShouldRedactTheNotesWarningsAndLeftovers_ItemByItem() {
		// Arrange
		string framed = "Item " + SiteMessage + "\n   at Terrasoft.Fixture.Case.Build()";
		(IDynamicCaseSession _, IBuildCaseClient client) = CaseSession(new BuildCaseResponse { Success = true },
			new BuildCaseResponse {
				Success = false, Outcome = DynamicCaseOutcomes.FailedLeftover, ErrorCode = "BuildFailed",
				ErrorMessage = "The build failed.", Leftovers = [framed, "UsrOrderCase schema"],
				Notes = [framed, "Second note"], Warnings = [framed, "Second warning"]
			});
		(ISettingsRepository settings, IServiceUrlBuilder urls) = CaseEnvironment();
		DynamicCaseService service = new(client, Substitute.For<IDynamicCasePageStep>(),
			Substitute.For<IPackageDependencyManager>(), settings, urls);
		ILogger logger = Substitute.For<ILogger>();

		// Act
		int exitCode = new CreateDynamicCaseCommand(service, logger).Execute(CaseOptions(addProgressBar: false));
		DynamicCaseResult result = service.Create(CaseOptions(addProgressBar: false));

		// Assert
		exitCode.Should().Be(1, because: "the build failed");
		result.Leftovers.Should().Equal(["Item " + RedactedReason, "UsrOrderCase schema"],
			because: "each leftover is redacted on its own and the second survives the first one's frame");
		result.Notes.Should().Contain("Second note", because: "a frame in one note does not cut the next");
		result.Warnings.Should().Contain("Second warning", because: "a frame in one warning does not cut the next");
		foreach (string text in Logged(logger).Append(JsonSerializer.Serialize(result))) {
			text.Should().NotContainAny([Marker, "Terrasoft.Fixture"], because: "no line or field carries the login");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("modify-dynamic-case and describe-dynamic-case: the server's notes, warnings and leftovers are "
		+ "redacted item by item, in the result and in the command's log lines.")]
	public void ChangeAndDescribeDynamicCase_ShouldRedactTheNotesWarningsAndLeftovers([Values(true, false)] bool modify) {
		// Arrange
		string framed = "Item " + SiteMessage + "\n   at Terrasoft.Fixture.Case.Change()";
		IBuildCaseClient client = Substitute.For<IBuildCaseClient>();
		IDynamicCaseSession session = Substitute.For<IDynamicCaseSession>();
		client.OpenSession(Env).Returns(session);
		session.Modify(Arg.Any<ModifyCaseRequest>()).Returns(new DynamicCaseReply<ModifyCaseResponse>(
			DynamicCaseReplyKind.Envelope, new ModifyCaseResponse {
				Success = false, Outcome = DynamicCaseModifyOutcomes.FailedLeftover, ErrorCode = "ModifyFailed",
				ErrorMessage = "The change failed.", Leftovers = [framed, "Second leftover"], Notes = [framed],
				Warnings = [framed, "Second warning"]
			}, null));
		session.Describe(Arg.Any<DescribeCaseRequest>()).Returns(new DynamicCaseReply<DescribeCaseResponse>(
			DynamicCaseReplyKind.Envelope, new DescribeCaseResponse {
				Success = false, ErrorCode = "CaseNotFound", ErrorMessage = "No case.", Notes = [framed],
				Warnings = [framed, "Second warning"]
			}, null));
		(ISettingsRepository settings, IServiceUrlBuilder urls) = CaseEnvironment();
		DynamicCaseService service = new(client, Substitute.For<IDynamicCasePageStep>(),
			Substitute.For<IPackageDependencyManager>(), settings, urls);
		ILogger logger = Substitute.For<ILogger>();

		// Act
		object result;
		if (modify) {
			ModifyDynamicCaseOptions options = new() { Environment = Env, CaseName = "UsrOrderCase", Caption = "Orders" };
			new ModifyDynamicCaseCommand(service, logger).Execute(options);
			result = service.Modify(options);
		} else {
			DescribeDynamicCaseOptions options = new() { Environment = Env, CaseName = "UsrOrderCase" };
			new DescribeDynamicCaseCommand(service, logger).Execute(options);
			result = service.Describe(options);
		}

		// Assert
		string serialized = JsonSerializer.Serialize(result);
		serialized.Should().Contain("Second warning", because: "a frame in one warning does not cut the next");
		foreach (string text in Logged(logger).Append(serialized)) {
			text.Should().NotContainAny([Marker, "Terrasoft.Fixture"], because: "no line or field carries the login");
		}
		Logged(logger).Should().Contain(text => text.Contains("Item " + RedactedReason, StringComparison.Ordinal),
			because: "each item is logged, redacted");
	}

	[TestCase("{\"success\":false,\"addonsErrors\":[{\"message\":\"m1\",\"errorInfo\":null}]}", "m1",
		TestName = "SaveError_ShouldReadAnAddonItem_WhoseErrorInfoIsNull")]
	[TestCase("{\"success\":false,\"addonsErrors\":[{\"addonName\":\"X\",\"errorInfo\":\"boom\"}]}", "boom",
		TestName = "SaveError_ShouldReadAnAddonItem_WhoseErrorInfoIsAString")]
	[TestCase("{\"success\":false,\"addonsErrors\":[{\"addonName\":\"X\",\"errorInfo\":{\"errorCode\":\"AddonError\","
		+ "\"message\":\"Login failed for user 'wiring_marker_login'.\",\"stackTrace\":\"   at Terrasoft.Fixture.Addon.Save()\"}}]}",
		"AddonError: Login failed for user '[redacted]'.",
		TestName = "SaveError_ShouldReadANestedErrorInfo_OfAnAddonItem")]
	[Category("Unit")]
	[Description("update-page and create-page: an addonsErrors item whose errorInfo is null, a string or an object "
		+ "gives its code and message as the site's reason (redacted), never Newtonsoft's sentence and never its "
		+ "stack trace.")]
	public void SaveError_ShouldReadEveryAddonItemShape(string answer, string expected) {
		// Arrange
		Newtonsoft.Json.Linq.JObject response = Newtonsoft.Json.Linq.JObject.Parse(answer);

		// Act
		string error = PageSchemaMetadataHelper.ParseSaveErrorMessage(response, "Failed to save page schema");

		// Assert
		error.Should().Be(expected, because: "the item's reason is the error");
		error.Should().NotContainAny([Marker, "Terrasoft.Fixture"], because: "the login and the frame go");
	}

	[Test]
	[Category("Unit")]
	[Description("The installation failure line's tail of a log that ENDS in stack frames - an exception usually does "
		+ "- leaves every frame out, the first one included, which no line break precedes in the tail.")]
	public void InstallFailure_ShouldDropEveryFrame_WhenTheLogEndsInFrames() {
		// Arrange
		string log = "Start\r\nSystem.Exception: " + SiteMessage + "\r\n"
			+ "   at Terrasoft.Core.Packages.PackageInstaller.Install(String name) in C:\\b\\x.cs:line 4\r\n"
			+ "   at Terrasoft.Core.Packages.PackageInstaller.Run()\r\n   at Terrasoft.Web.Install.Service.Go()\r\n";

		// Act
		string description = InstallLogAnalyzer.DescribeFailure(new BaseResponse { Success = false }, log);

		// Assert
		description.Should().Be("the installation service reported a failure without an error message. Last log lines: "
			+ "Start | System.Exception: " + RedactedReason, because: "the reason stays and every frame goes");
	}

	[Test]
	[Category("Unit")]
	[Description("The installation failure line names the file of a compiler line written with a colon after the "
		+ "position ('(1,79): error CS1519') as well as without one.")]
	public void InstallFailure_ShouldNameTheFile_OfACompilerLineWithAColon([Values("", ":")] string colon) {
		// Arrange
		BaseResponse response = new() {
			Success = false, ErrorInfo = new SiteErrorInfo { ErrorCode = "Exception", Message = "Packages installation failed" }
		};

		// Act
		string description = InstallLogAnalyzer.DescribeFailure(response,
			@"C:\inetpub\Autogenerated\Src\UsrFixture.cs(1,79)" + colon + " error CS1519: Invalid token\r\n");

		// Assert
		description.Should().Be($"the configuration failed to compile. UsrFixture.cs(1,79){colon} error CS1519: Invalid token",
			because: "the file name stays and the directories go");
	}

	[TestCase(@"C:\a\b\", "[redacted-path]", TestName = "SiteFileName_ShouldRedact_APathEndingInASeparator")]
	[TestCase(@"\\fs01", "[redacted-path]", TestName = "SiteFileName_ShouldNeverShow_ABareHost")]
	[TestCase("/app/Password=ab/cd", "[redacted-path]", TestName = "SiteFileName_ShouldNeverShow_AFragmentOfASecret")]
	[TestCase("https://svc:S3cret@build-host", "[redacted-path]", TestName = "SiteFileName_ShouldRedact_AUri")]
	[TestCase("/app/password=S3cret1/UsrClass.cs", "UsrClass.cs", TestName = "SiteFileName_ShouldKeep_TheFileNameOnly")]
	[TestCase("UsrClass.cs", "UsrClass.cs", TestName = "SiteFileName_ShouldKeep_ABareFileName")]
	[Category("Unit")]
	[Description("A file the environment reports is named by its last segment only when that is a plain file name "
		+ "the site-text redaction leaves whole; otherwise it is [redacted-path] - never a host or a secret's fragment.")]
	public void SiteFileName_ShouldNameOnlyAPlainFileName(string path, string expected) {
		// Arrange
		PackageBuildDiagnostic diagnostic = new("CS0103", "The name 'x' does not exist", path, 12, 3, false);

		// Act
		string rendered = diagnostic.WithSiteTextRedacted().ToString();

		// Assert
		rendered.Should().Be($"(CS0103) in {expected} at (12,3): The name 'x' does not exist",
			because: "only a plain file name may reach the line");
	}

	[Test]
	[Category("Unit")]
	[Description("compile-creatio names an environment verdict diagnostic whose path ends in a separator as "
		+ "[redacted-path], not as an empty name ('in  at (12,3)').")]
	public void ApplyEnvironmentVerdict_ShouldNotPrintAnEmptyFileName() {
		// Arrange
		PackageBuildDiagnostic diagnostic = new("CS0103", "x", @"C:\a\b\", 12, 3, false);

		// Act
		string rendered = diagnostic.WithSiteTextRedacted().ToString();

		// Assert
		rendered.Should().NotContain("in  at", because: "an empty name is not a file name");
	}

	[Test]
	[Category("Unit")]
	[Description("A layout refusal whose element names all redact to nothing gets no 'Affected elements' clause, "
		+ "rather than one that lists nothing.")]
	public void ElementsClause_ShouldBeOmitted_WhenEveryNameRedactsToNothing() {
		// Arrange
		LayoutChangeRelay relay = new() { Elements = ["\n   at Terrasoft.Fixture.Layout.Move()"] };

		// Act
		string clause = relay.ElementsClause();

		// Assert
		clause.Should().BeEmpty(because: "a clause that names nothing reads as a truncated message");
	}

	[TestCase("create", TestName = "ProcessWriteSuccess_ShouldRedactTheNames(create)")]
	[TestCase("modify", TestName = "ProcessWriteSuccess_ShouldRedactTheNames(modify)")]
	[TestCase("new-version", TestName = "ProcessWriteSuccess_ShouldRedactTheNames(new-version)")]
	[TestCase("activate", TestName = "ProcessWriteSuccess_ShouldRedactTheNames(activate)")]
	[Category("Unit")]
	[Description("A SUCCESSFUL process write redacts the schema or version name the server returned, in the result "
		+ "and in the command's success line.")]
	public void ProcessWriteSuccess_ShouldRedactTheNames(string write) {
		// Arrange
		string name = "UsrFixtureProcess " + SiteMessage;
		const string uid = "99999999-9999-9999-9999-999999999999";
		string body = write switch {
			"create" => JsonSerializer.Serialize(new { BuildProcessResult = new { success = true, schemaName = name, schemaUId = uid } }),
			"modify" => JsonSerializer.Serialize(new {
				ModifyProcessResult = new { success = true, schemaName = name, schemaUId = uid, appliedOperations = 1 }
			}),
			"new-version" => JsonSerializer.Serialize(new {
				ModifyProcessAsNewVersionResult = new { success = true, versionName = name, versionSchemaUId = uid, appliedOperations = 1 }
			}),
			_ => JsonSerializer.Serialize(new {
				SetActiveProcessVersionResult = new { success = true, activeVersionName = name, activeVersionSchemaUId = uid }
			})
		};
		ILogger logger = RunProcessWrite(write, body);

		// Assert
		Logged(logger).Should().Contain(text => text.Contains("UsrFixtureProcess " + RedactedReason, StringComparison.Ordinal),
			because: "the name stays, redacted")
			.And.NotContain(text => text.Contains(Marker, StringComparison.Ordinal), because: "no line carries the login");
	}

	[Test]
	[Category("Unit")]
	[Description("A process write whose read-back fails (describe-business-process, a follow-up reader) logs the "
		+ "describe failure's text redacted as site text.")]
	public void ProcessWriteSuccess_ShouldRedactTheReadBackFailure() {
		// Arrange
		string body = JsonSerializer.Serialize(new {
			BuildProcessResult = new {
				success = true, schemaName = "UsrFixtureProcess", schemaUId = "99999999-9999-9999-9999-999999999999"
			}
		});

		// Act
		ILogger logger = RunProcessWrite("create", body, describeFailure: SiteMessage);

		// Assert
		Logged(logger).Should().Contain(text => text.Contains(RedactedReason, StringComparison.Ordinal),
			because: "the read-back's reason is logged, redacted")
			.And.NotContain(text => text.Contains(Marker, StringComparison.Ordinal), because: "no line carries the login");
	}

	[Test]
	[Category("Unit")]
	[Description("modify-business-process-as-new-version: a bare fault string (ProcessDesignServerFault.ReadServerText, "
		+ "quoted by ServerSaid) is redacted in the exception and the command's error line.")]
	public void ModifyAsNewVersion_ShouldRedactAFaultString_InTheExceptionAndTheLog() {
		// Act
		(ILogger logger, int exitCode) = RunModifyAsNewVersion(JsonSerializer.Serialize(SiteMessage));

		// Assert
		exitCode.Should().Be(1, because: "the answer was not the envelope");
		Logged(logger).Should().Contain(text => text.Contains("The server said: " + RedactedReason, StringComparison.Ordinal),
			because: "the fault is quoted, redacted")
			.And.NotContain(text => text.Contains(Marker, StringComparison.Ordinal), because: "no line carries the login");
	}

	[Test]
	[Category("Unit")]
	[Description("compile-business-process END TO END: the CompileProcess answer's errorMessage and errors, read by the "
		+ "real service, are fenced as site text in the command's log lines.")]
	public void CompileBusinessProcess_ShouldFenceTheServiceAnswer_EndToEnd() {
		// Arrange
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>()).Returns(JsonSerializer.Serialize(new {
			CompileProcessResult = new {
				success = false, errorMessage = SiteMessage, processName = "UsrFixtureProcess", packageName = "Custom",
				packageType = "general", compileRequired = true, compiled = false, durationMs = 1000, errorCount = 1,
				errors = new[] {
					new { fileName = "UsrFixture.Custom.cs", line = 3, column = 4, code = "CS0103", message = SiteMessage,
						inThisProcess = true }
				}
			}
		}));
		(EnvironmentSettings environment, ISettingsRepository settings, IApplicationClientFactory factory) =
			ProcessEnvironment(client);
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(ServiceUrlBuilder.KnownRoute.CompileProcess, environment)
			.Returns("http://sandbox/0/rest/ProcessDesignService/CompileProcess");
		ILogger logger = Substitute.For<ILogger>();
		CompileBusinessProcessCommand command = new(new CompileBusinessProcessService(settings, factory, urls,
			Substitute.For<ILogger>()), logger);

		// Act
		int exitCode = command.Execute(new CompileBusinessProcessOptions { Environment = Env, ProcessName = "UsrFixtureProcess" });

		// Assert
		exitCode.Should().Be(1, because: "the compile failed");
		Logged(logger).Should().Contain(text => text.Contains(RedactedReason, StringComparison.Ordinal),
			because: "the reason stays, fenced")
			.And.NotContain(text => text.Contains(Marker, StringComparison.Ordinal), because: "no line carries the login");
	}

	private static ILogger RunProcessWrite(string write, string body, string describeFailure = null) {
		IOwnedApplicationClient client = Substitute.For<IOwnedApplicationClient, IStatusPreservingApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(body);
		((IStatusPreservingApplicationClient)client)
			.SendKeepingStatus(default, default, default, default, default, default, default)
			.ReturnsForAnyArgs(new SiteHttpResponse(200, body));
		(EnvironmentSettings environment, ISettingsRepository settings, IApplicationClientFactory factory) =
			ProcessEnvironment(client);
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>(), environment)
			.Returns("http://sandbox/0/rest/ProcessDesignService/Write");
		ILogger logger = Substitute.For<ILogger>();
		IProcessDescriber describer = Substitute.For<IProcessDescriber>();
		if (describeFailure is not null) {
			describer.Describe(default!, default!).ReturnsForAnyArgs(ErrorOr.Error.Failure("DescribeFailed", describeFailure));
		}
		const string operations = "[{\"op\":\"addParameter\",\"parameter\":{\"name\":\"Amount\",\"type\":\"Integer\"}}]";
		_ = write switch {
			"create" => new CreateBusinessProcessCommand(new CreateBusinessProcessService(settings, factory, urls,
					Substitute.For<IProcessPageFactsChecker>(), Substitute.For<ILogger>()), describer, logger)
				.Execute(new CreateBusinessProcessOptions { Environment = Env, DescriptorJson = ProcessDescriptor }),
			"modify" => new ModifyBusinessProcessCommand(new ModifyBusinessProcessService(settings, factory, urls,
					Substitute.For<IProcessPageFactsChecker>(), Substitute.For<ILogger>()), describer, logger)
				.Execute(new ModifyBusinessProcessOptions {
					Environment = Env, ProcessName = "UsrFixtureProcess", OperationsJson = operations
				}),
			"new-version" => new ModifyProcessAsNewVersionCommand(new ModifyProcessAsNewVersionService(settings, factory,
					urls, Substitute.For<IProcessPageFactsChecker>(), Substitute.For<ILogger>()), describer, logger)
				.Execute(new ModifyProcessAsNewVersionOptions {
					Environment = Env, ProcessName = "UsrFixtureProcess", OperationsJson = operations
				}),
			_ => new SetActiveProcessVersionCommand(new SetActiveProcessVersionService(settings, factory, urls,
					Substitute.For<ILogger>()), describer, logger)
				.Execute(new SetActiveProcessVersionOptions { Environment = Env, VersionName = "UsrFixtureProcess_v2" })
		};
		return logger;
	}

	#endregion
}

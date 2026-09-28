using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.Localization;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit tests for <see cref="LocalizePageCommand"/> (ENG-90576, story 1, TC-U-01..TC-U-14). The remote
/// environment is a URL-routed fake behind <see cref="IApplicationClient"/>; the SysCulture read, the
/// schema resolution and the GetSchema/SaveSchema round trips go through the production code paths.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public sealed class LocalizePageCommandTests : BaseCommandTests<LocalizePageOptions> {

	private const string SchemaName = "UsrLab_FormPage";
	private const string SchemaUId = "777339c8-8631-46c7-8ba3-e83b1743749c";
	private const string AncestorUId = "1a29b42d-0000-0000-0000-000000000001";
	private const string DesignPackageUId = "ede220e4-352b-4a5f-9e75-8a0530bf5925";
	private const string PackageName = "UsrLab";
	private const string OwnKey = "UsrLabLabel_caption";

	private static readonly string[] TwentyEightCultures = [
		"en-US", "ru-RU", "ar-SA", "he-IL", "es-ES", "de-DE", "uk-UA", "fr-FR", "it-IT", "pt-BR",
		"pt-PT", "nl-NL", "pl-PL", "cs-CZ", "tr-TR", "ja-JP", "zh-CN", "ko-KR", "sv-SE", "da-DK",
		"nb-NO", "hu-HU", "ro-RO", "bg-BG", "el-GR", "sk-SK", "lt-LT", "lv-LV"
	];

	private IApplicationClient _applicationClient;
	private IPageDesignerHierarchyClient _hierarchyClient;
	private IPageBaselineGuard _baselineGuard;
	private ILocalizePageService _command;
	private readonly List<(string Url, string Body)> _calls = [];
	private string _cultureRows;
	private JObject _schema;
	private JObject _readbackOverride;
	private string _savedBody;
	private string _existingInPackageRows;
	private bool _readbackThrows;

	private const string PreSaveChecksum = "checksum-before-save";
	private const string PostSaveChecksum = "checksum-after-save";

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		_baselineGuard = Substitute.For<IPageBaselineGuard>();
		containerBuilder.AddTransient(_ => _applicationClient);
		containerBuilder.AddTransient(_ => _hierarchyClient);
		containerBuilder.AddTransient(_ => _baselineGuard);
		containerBuilder.AddTransient(_ => Substitute.For<ILogger>());
	}

	public override void Setup() {
		base.Setup();
		EnvironmentSettings.IsNetCore = false;
		_calls.Clear();
		_cultureRows = """[{"Name":"en-US","Active":true},{"Name":"es-ES","Active":true},{"Name":"de-DE","Active":false}]""";
		_schema = null;
		_readbackOverride = null;
		_savedBody = null;
		_existingInPackageRows = "[]";
		_readbackThrows = false;
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => Route(call.ArgAt<string>(0), call.ArgAt<string>(1)));
		_hierarchyClient.GetDesignPackageUId(SchemaUId).Returns(DesignPackageUId);
		_hierarchyClient.GetParentSchemas(SchemaUId, DesignPackageUId).Returns([
			new PageDesignerHierarchySchema {
				UId = SchemaUId, Name = SchemaName, PackageUId = DesignPackageUId, PackageName = PackageName
			}
		]);
		_command = Container.GetRequiredService<ILocalizePageService>();
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_hierarchyClient.ClearReceivedCalls();
		_baselineGuard.ClearReceivedCalls();
		base.TearDown();
	}

	#region Fake environment

	private string Route(string url, string body) {
		_calls.Add((url, body));
		if (url.EndsWith("/0/DataService/json/SyncReply/SelectQuery", StringComparison.Ordinal)) {
			if (body.Contains("\"SysCulture\"", StringComparison.Ordinal)) {
				return $$"""{"success":true,"rows":{{_cultureRows}}}""";
			}
			if (body.Contains("SysPackage.UId", StringComparison.Ordinal)) {
				return $$"""{"success":true,"rows":{{_existingInPackageRows}}}""";
			}
			if (body.Contains("\"Checksum\"", StringComparison.Ordinal)) {
				string checksum = _savedBody is null ? PreSaveChecksum : PostSaveChecksum;
				return $$"""{"success":true,"rows":[{"Checksum":"{{checksum}}","ModifiedOn":"2026-09-26T10:00:00"}]}""";
			}
			return $$"""{"success":true,"rows":[{"UId":"{{SchemaUId}}"}]}""";
		}
		if (url.EndsWith("/0/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema", StringComparison.Ordinal)) {
			if (_savedBody is not null && _readbackThrows) {
				throw new InvalidOperationException("connection reset during readback");
			}
			JObject schema = _savedBody is null
				? _schema
				: _readbackOverride ?? JObject.Parse(_savedBody);
			return new JObject { ["success"] = true, ["schema"] = schema.DeepClone() }.ToString();
		}
		if (url.EndsWith("/0/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema", StringComparison.Ordinal)) {
			_savedBody = body;
			return """{"success":true}""";
		}
		if (url.EndsWith("/0/rest/WorkplaceService/ResetScriptCache", StringComparison.Ordinal)) {
			return string.Empty;
		}
		throw new InvalidOperationException($"Unexpected request to {url}");
	}

	private int CountCalls(string endpointSuffix) =>
		_calls.Count(call => call.Url.EndsWith(endpointSuffix, StringComparison.Ordinal));

	private int SaveCount => CountCalls("ClientUnitSchemaDesignerService.svc/SaveSchema");

	private int GetSchemaCount => CountCalls("ClientUnitSchemaDesignerService.svc/GetSchema");

	private JObject SavedSchema => JObject.Parse(_savedBody);

	private static JObject SavedEntry(JObject schema, string key) =>
		schema["localizableStrings"].Children<JObject>().Single(entry => entry["name"].ToString() == key);

	private static Dictionary<string, string> ValuesOf(JObject entry) =>
		((JArray)entry["values"]).Children<JObject>()
			.ToDictionary(v => v["cultureName"].ToString(), v => v["value"].ToString());

	private static JObject Culture(string culture, string value) =>
		new() { ["cultureName"] = culture, ["value"] = value };

	private static JObject Entry(string name, string parentSchemaUId, params (string Culture, string Value)[] values) =>
		new() {
			["uId"] = $"uid-{name}",
			["name"] = name,
			["parentSchemaUId"] = parentSchemaUId,
			["values"] = new JArray(values.Select(v => Culture(v.Culture, v.Value)))
		};

	private static JObject Schema(string body, JArray caption, params JObject[] entries) =>
		new() {
			["uId"] = SchemaUId,
			["name"] = SchemaName,
			["body"] = body,
			["package"] = new JObject { ["uId"] = DesignPackageUId, ["name"] = PackageName },
			["caption"] = caption,
			["localizableStrings"] = new JArray(entries.Cast<object>().ToArray())
		};

	private static JObject Schema(params JObject[] entries) =>
		Schema(" ", new JArray(Culture("en-US", "Lab form page")), entries);

	private static LocalizePageOptions Options(string culture = "es-ES", string resources = null, string caption = null) =>
		new() { SchemaName = SchemaName, Culture = culture, Resources = resources, Caption = caption, Environment = "dev" };

	#endregion

	[Test]
	[Description("TC-U-01: writing es-ES into a page-owned key sends the complete value list back, with en-US and de-DE unchanged and the entry's uId and parentSchemaUId kept (F3: an omitted culture of an own key is deleted).")]
	public void Execute_ShouldWriteTargetCultureAndKeepOtherCultures_WhenKeyIsOwnedByPage() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label"), ("de-DE", "Laboretikett")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(resources: """{"UsrLabLabel_caption":"Etiqueta"}"""));

		// Assert
		response.Success.Should().BeTrue(because: "the value was saved and read back");
		response.Written.Should().Equal([OwnKey], because: "the key's es-ES value changed");
		SaveCount.Should().Be(1, because: "one schema write carries every change");
		JObject entry = SavedEntry(SavedSchema, OwnKey);
		ValuesOf(entry).Should().Equal(new Dictionary<string, string> {
			["en-US"] = "Lab label", ["de-DE"] = "Laboretikett", ["es-ES"] = "Etiqueta"
		}, because: "every culture read from GetSchema is sent back and only es-ES is added");
		entry["uId"].ToString().Should().Be($"uid-{OwnKey}", because: "the entry identity is preserved");
		entry["parentSchemaUId"].ToString().Should().Be(SchemaUId, because: "the ownership marker is preserved");
	}

	[Test]
	[Description("TC-U-02: writing es-ES into an inherited key keeps the parent marker and sends all 28 values, of which only es-ES differs (F1/F2).")]
	public void Execute_ShouldKeepParentMarkerAndAllCultures_WhenKeyIsInherited() {
		// Arrange
		_schema = Schema(Entry("SaveButton", AncestorUId,
			TwentyEightCultures.Select(c => (c, $"Save {c}")).ToArray()));

		// Act
		LocalizePageResponse response = _command.Localize(Options(resources: """{"SaveButton":"Guardar ya"}"""));

		// Assert
		response.Success.Should().BeTrue(because: "the override was saved and read back");
		JObject entry = SavedEntry(SavedSchema, "SaveButton");
		Dictionary<string, string> values = ValuesOf(entry);
		values.Should().HaveCount(28, because: "the complete inherited list is sent back");
		values.Where(v => v.Key != "es-ES").Should().OnlyContain(v => v.Value == $"Save {v.Key}",
			because: "no culture other than the target one changes");
		values["es-ES"].Should().Be("Guardar ya", because: "the target culture carries the new value");
		entry["parentSchemaUId"].ToString().Should().Be(AncestorUId, because: "the inherited marker must be kept");
	}

	[Test]
	[Description("TC-U-03: an entry that has no value in the target culture gets a new {cultureName, value} object appended.")]
	public void Execute_ShouldAddCultureValue_WhenEntryHasNoValueInThatCulture() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(resources: """{"UsrLabLabel_caption":"Etiqueta"}"""));

		// Assert
		response.Success.Should().BeTrue(because: "the new culture value was saved and read back");
		JArray values = (JArray)SavedEntry(SavedSchema, OwnKey)["values"];
		values.Should().HaveCount(2, because: "exactly one culture object is appended");
		values[1]["cultureName"].ToString().Should().Be("es-ES", because: "the appended object carries the target culture");
		values[1]["value"].ToString().Should().Be("Etiqueta", because: "the appended object carries the supplied value");
	}

	[Test]
	[Description("TC-U-04: a re-run whose every value equals the stored one sends no SaveSchema and reports the keys as unchanged.")]
	public void Execute_ShouldNotSave_WhenEverySuppliedValueIsUnchanged() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label"), ("es-ES", "Etiqueta")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(resources: """{"UsrLabLabel_caption":"Etiqueta"}"""));

		// Assert
		response.Success.Should().BeTrue(because: "an idempotent re-run is a success");
		response.Saved.Should().BeFalse(because: "nothing changed, so nothing is written");
		response.Unchanged.Should().Equal([OwnKey], because: "the stored value already equals the supplied one");
		response.Written.Should().BeEmpty(because: "no value changed");
		SaveCount.Should().Be(0, because: "a no-op save would still move the schema checksum");
		_baselineGuard.DidNotReceiveWithAnyArgs().RefreshAfterSave(default, default, default, default, default, default);
	}

	[Test]
	[Description("TC-U-05: with neither resources nor caption the command only reads, returns coverage and saves nothing.")]
	public void Execute_ShouldOnlyReportCoverage_WhenNoResourcesAndNoCaption() {
		// Arrange
		_schema = Schema(
			Entry(OwnKey, SchemaUId, ("en-US", "Lab label")),
			Entry("SaveButton", AncestorUId, ("en-US", "Save"), ("es-ES", "Guardar")));

		// Act
		LocalizePageResponse response = _command.Localize(Options());

		// Assert
		response.Success.Should().BeTrue(because: "report-only is a successful read");
		response.Saved.Should().BeFalse(because: "report-only never writes");
		SaveCount.Should().Be(0, because: "report-only never writes");
		GetSchemaCount.Should().Be(1, because: "report-only reads the schema once and has nothing to read back");
		response.Coverage.Keys.Should().Be(2, because: "own and inherited keys are both counted");
		response.Coverage.Translated.Should().Be(1, because: "only SaveButton has an es-ES value");
		response.Coverage.Missing.Should().Equal([OwnKey], because: "the own key has no es-ES value");
		response.CaptionOutcome.Should().BeNull(because: "no caption was supplied");
	}

	[Test]
	[Description("TC-U-06: a key that is not in localizableStrings fails the call before any save; the error lists the unknown key and the available keys, own first.")]
	public void Execute_ShouldFailWithoutSaving_WhenKeyIsUnknown() {
		// Arrange
		_schema = Schema(
			Entry("SaveButton", AncestorUId, ("en-US", "Save")),
			Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(
			resources: """{"UsrLabLabel_caption":"Etiqueta","UsrMissing_caption":"Falta"}"""));

		// Assert
		response.Success.Should().BeFalse(because: "one unknown key fails the whole call");
		response.Error.Should().Contain("UsrMissing_caption", because: "the unknown key is named")
			.And.Contain($"Available keys: {OwnKey}, SaveButton", because: "candidate keys are listed with own keys first");
		SaveCount.Should().Be(0, because: "nothing is saved when any key is unknown");
	}

	[Test]
	[Description("TC-U-07: an unknown key that the body references as $Resources.Strings.<Key> and binds to a data-source attribute points the caller to the entity column caption tools.")]
	public void Execute_ShouldPointToEntityColumnCaption_WhenUnknownKeyIsDataSourceBound() {
		// Arrange
		const string body = """
			define("UsrLab_FormPage", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
				return {
					viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{"operation":"insert","name":"UsrName","values":{"type":"crt.Input","label":"$Resources.Strings.UsrName","control":"$UsrName"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/,
					viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{"operation":"merge","path":[],"values":{"attributes":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"}}}}}]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
					modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
					handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
					converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
					validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
				};
			});
			""";
		_schema = Schema(body, new JArray(Culture("en-US", "Lab form page")),
			Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(resources: """{"UsrName":"Nombre"}"""));

		// Assert
		response.Success.Should().BeFalse(because: "UsrName is not a page resource key");
		response.Error.Should().Contain("title-localizations",
				because: "a data-source-bound caption is translated on the entity column")
			.And.Contain("update-entity-schema", because: "the error names the tool that translates it");
		SaveCount.Should().Be(0, because: "nothing is saved when any key is unknown");
	}

	[Test]
	[Description("TC-U-08: a culture that is not a SysCulture row fails before the schema is read; the message names the Languages section and lists the available cultures.")]
	public void Execute_ShouldFailBeforeReadingSchema_WhenCultureIsAbsent() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(culture: "fi-FI",
			resources: """{"UsrLabLabel_caption":"Nimi"}"""));

		// Assert
		response.Success.Should().BeFalse(because: "the platform would drop the value and still answer success");
		response.Error.Should().Be(
			"Culture 'fi-FI' is not available in this environment. Add it in the Languages section "
			+ "(System Designer → Languages) first. Available: en-US, es-ES, de-DE.",
			because: "the message tells the caller where to add the culture and which ones exist");
		GetSchemaCount.Should().Be(0, because: "the culture check runs before any schema read");
		SaveCount.Should().Be(0, because: "nothing is saved for an absent culture");
	}

	[Test]
	[Description("TC-U-09: an inactive culture is written (the platform stores it) and the response carries cultureActive:false and the inactive warning.")]
	public void Execute_ShouldSaveAndWarn_WhenCultureIsInactive() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(culture: "de-DE",
			resources: """{"UsrLabLabel_caption":"Laboretikett"}"""));

		// Assert
		response.Success.Should().BeTrue(because: "an inactive culture is stored and read back");
		response.Saved.Should().BeTrue(because: "the value changed");
		response.CultureActive.Should().BeFalse(because: "de-DE is inactive in the fake SysCulture");
		response.Warnings.Should().Contain(CultureMessages.FormatCultureInactive("de-DE"),
			because: "the caller must learn that users cannot select the culture yet");
		response.Warnings.Should().Contain(LocalizePageCommand.WorkspaceCaptureWarning,
			because: "a server save does not update the workspace source, exactly as for update-page");
	}

	[Test]
	[Description("TC-U-10: a culture name that differs only in case is canonicalized to the SysCulture.Name spelling before it is written and reported.")]
	public void Execute_ShouldCanonicalizeCultureName_WhenCaseDiffers() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(culture: "es-es",
			resources: """{"UsrLabLabel_caption":"Etiqueta"}"""));

		// Assert
		response.Culture.Should().Be("es-ES", because: "the response reports the canonical name");
		((JArray)SavedEntry(SavedSchema, OwnKey)["values"]).Children<JObject>()
			.Select(v => v["cultureName"].ToString())
			.Should().Equal(["en-US", "es-ES"], because: "the value is written under the canonical culture name");
	}

	[Test]
	[Description("TC-U-11: the caption is written in the target culture only; every other caption culture is sent back unchanged.")]
	public void Execute_ShouldWriteCaptionInTargetCultureOnly_WhenCaptionSupplied() {
		// Arrange
		var caption = new JArray(TwentyEightCultures.Select(c => Culture(c, "Lab form page")));
		_schema = Schema(" ", caption, Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(caption: "Página de laboratorio"));

		// Assert
		response.Success.Should().BeTrue(because: "the caption was saved and read back");
		response.CaptionOutcome.Should().Be(LocalizePageResponse.CaptionWritten, because: "the es-ES caption changed");
		Dictionary<string, string> saved = ((JArray)SavedSchema["caption"]).Children<JObject>()
			.ToDictionary(v => v["cultureName"].ToString(), v => v["value"].ToString());
		saved.Should().HaveCount(28, because: "the complete caption list is sent back");
		saved.Where(v => v.Key != "es-ES").Should().OnlyContain(v => v.Value == "Lab form page",
			because: "no other caption culture changes");
		saved["es-ES"].Should().Be("Página de laboratorio", because: "the target culture carries the new title");
	}

	[Test]
	[Description("TC-U-12: when the readback does not contain the written value (the platform can answer success while dropping it), the call fails naming the key.")]
	public void Execute_ShouldFail_WhenReadbackDoesNotContainWrittenValue() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));
		_readbackOverride = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(resources: """{"UsrLabLabel_caption":"Etiqueta"}"""));

		// Assert
		response.Success.Should().BeFalse(because: "success means the value is stored");
		response.Saved.Should().BeTrue(because: "the save itself was sent and accepted");
		response.Error.Should().Contain(OwnKey, because: "the key whose value did not persist is named");
		GetSchemaCount.Should().Be(2, because: "one read before the save and one readback after it");
	}

	[Test]
	[Description("TC-U-13: when neither the head schema nor a same-name schema lives in the design package, the call fails naming the page and the design package and never creates a replacing schema.")]
	public void Execute_ShouldFailWithoutCreatingSchema_WhenNoEditableSchemaInDesignPackage() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));
		_hierarchyClient.GetParentSchemas(SchemaUId, DesignPackageUId).Returns([
			new PageDesignerHierarchySchema {
				UId = SchemaUId, Name = SchemaName, PackageUId = "other-package", PackageName = "CrtBase"
			}
		]);

		// Act
		LocalizePageResponse response = _command.Localize(Options(resources: """{"UsrLabLabel_caption":"Etiqueta"}"""));

		// Assert
		response.Success.Should().BeFalse(because: "there is no schema the caller may edit");
		response.Error.Should().Contain(SchemaName, because: "the page is named")
			.And.Contain(DesignPackageUId, because: "the design package is named");
		SaveCount.Should().Be(0, because: "localize-page never creates a replacing schema");
		GetSchemaCount.Should().Be(0, because: "nothing is read once resolution failed");
	}

	[Test]
	[Description("TC-U-14: coverage reports missing (no value in the culture) and sameAsDefault (value equals en-US) keys and the caption state from the stored schema, and the on-disk baseline refresh is requested once after the save.")]
	public void Execute_ShouldReportMissingAndSameAsDefault_WhenComputingCoverage() {
		// Arrange
		var caption = new JArray(Culture("en-US", "Lab form page"), Culture("es-ES", "Lab form page"));
		_schema = Schema(" ", caption,
			Entry(OwnKey, SchemaUId, ("en-US", "Lab label")),
			Entry("UsrOther_caption", SchemaUId, ("en-US", "Other")),
			Entry("EmailLabel", AncestorUId, ("en-US", "Email"), ("es-ES", "Email")),
			Entry("SaveButton", AncestorUId, ("en-US", "Save"), ("es-ES", "Guardar")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(resources: """{"UsrLabLabel_caption":"Etiqueta"}"""));

		// Assert
		response.Success.Should().BeTrue(because: "the value was saved and read back");
		response.Coverage.Keys.Should().Be(4, because: "every key of localizableStrings is counted");
		response.Coverage.Translated.Should().Be(3, because: "three keys have an es-ES value after the write");
		response.Coverage.Missing.Should().Equal(["UsrOther_caption"], because: "only that key has no es-ES value");
		response.Coverage.SameAsDefault.Should().Equal(["EmailLabel"],
			because: "a value equal to en-US is reported, not treated as missing");
		response.Coverage.CaptionSameAsDefault.Should().BeTrue(
			because: "right after create-page the caption holds the English text in every culture (F8)");
		_baselineGuard.Received(1).RefreshAfterSave(
			Arg.Any<EnvironmentOptions>(), SchemaName, SchemaUId, null, PreSaveChecksum, Arg.Any<Func<(string, string)>>());
	}

	private void StubHierarchyWithAncestorStrings(params JObject[] ancestorEntries) =>
		_hierarchyClient.GetParentSchemas(SchemaUId, DesignPackageUId).Returns([
			new PageDesignerHierarchySchema {
				UId = SchemaUId, Name = SchemaName, PackageUId = DesignPackageUId, PackageName = PackageName
			},
			new PageDesignerHierarchySchema {
				UId = AncestorUId, Name = "PageWithTabsFreedomTemplate", PackageUId = "crt-package",
				PackageName = "CrtUIPlatform", LocalizableStrings = new JArray(ancestorEntries.Cast<object>().ToArray())
			}
		]);

	[Test]
	[Description("OQ-2: a key that an ancestor declares but the page's own localizableStrings does not carry is written as a NEW page entry holding only the target culture (the platform stores just that override; en-US keeps resolving from the parent).")]
	public void Execute_ShouldAddEntryWithOnlyTargetCulture_WhenKeyExistsOnlyInHierarchy() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));
		StubHierarchyWithAncestorStrings(
			Entry("PostponeQueueItemButton_caption", AncestorUId, ("en-US", "Postpone till"), ("es-ES", "Posponer hasta")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(
			resources: """{"PostponeQueueItemButton_caption":"Aplazar hasta"}"""));

		// Assert
		response.Success.Should().BeTrue(because: "a hierarchy key is a known key and the override was read back");
		response.Written.Should().Equal(["PostponeQueueItemButton_caption"], because: "the es-ES value changed");
		JObject entry = SavedEntry(SavedSchema, "PostponeQueueItemButton_caption");
		ValuesOf(entry).Should().Equal(new Dictionary<string, string> { ["es-ES"] = "Aplazar hasta" },
			because: "the new entry must hold only the target culture, never a copy of the inherited en-US");
		SavedSchema["localizableStrings"].Children<JObject>().Should().HaveCount(2,
			because: "exactly one entry is added and the existing own entry is kept");
	}

	[Test]
	[Description("OQ-2: a hierarchy-only key whose resolved value in the culture already equals the supplied one is unchanged and nothing is saved.")]
	public void Execute_ShouldNotSave_WhenHierarchyOnlyKeyAlreadyHasTheValue() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));
		StubHierarchyWithAncestorStrings(
			Entry("PostponeQueueItemButton_caption", AncestorUId, ("en-US", "Postpone till"), ("es-ES", "Posponer hasta")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(
			resources: """{"PostponeQueueItemButton_caption":"Posponer hasta"}"""));

		// Assert
		response.Unchanged.Should().Equal(["PostponeQueueItemButton_caption"],
			because: "the value resolved from the hierarchy already equals the supplied one");
		SaveCount.Should().Be(0, because: "an unchanged hierarchy key must not add an entry or move the checksum");
	}

	[Test]
	[Description("OQ-2: coverage is computed over the same key set get-page shows (hierarchy merged with the page), and sameAsDefault compares with the en-US value resolved from the hierarchy.")]
	public void Execute_ShouldComputeCoverageOverHierarchyKeys_WhenAncestorsDeclareMoreKeys() {
		// Arrange
		_schema = Schema(
			Entry(OwnKey, SchemaUId, ("en-US", "Lab label")),
			Entry("SaveButton", AncestorUId, ("en-US", "Save"), ("es-ES", "Guardar")),
			Entry("EmailLabel", AncestorUId, ("es-ES", "Email")));
		StubHierarchyWithAncestorStrings(
			Entry("SaveButton", AncestorUId, ("en-US", "Save"), ("es-ES", "Guardar")),
			Entry("EmailLabel", AncestorUId, ("en-US", "Email")),
			Entry("PostponeQueueItemButton_caption", AncestorUId, ("en-US", "Postpone till"), ("es-ES", "Posponer hasta")),
			Entry("RequeueQueueItemButton_caption", AncestorUId, ("en-US", "Re-queue")));

		// Act
		LocalizePageResponse response = _command.Localize(Options());

		// Assert
		response.Coverage.Keys.Should().Be(5, because: "three page keys plus two keys only the hierarchy declares");
		response.Coverage.Missing.Should().BeEquivalentTo([OwnKey, "RequeueQueueItemButton_caption"],
			because: "keys with no es-ES value anywhere in the hierarchy are missing");
		response.Coverage.SameAsDefault.Should().Equal(["EmailLabel"],
			because: "the page stores only es-ES for EmailLabel; its en-US resolves from the ancestor and is equal");
		response.Coverage.Translated.Should().Be(3, because: "keys minus missing");
	}

	[Test]
	[Description("Fix 1/2: the schema checksum is read BEFORE SaveSchema and handed to the baseline guard together with output-directory, so a baseline written by get-page --output-directory is found and refreshed only when it is current.")]
	public void Execute_ShouldPassPreSaveChecksumAndOutputDirectory_WhenSaving() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));
		LocalizePageOptions options = Options(resources: """{"UsrLabLabel_caption":"Etiqueta"}""");
		options.OutputDirectory = "/work/project";

		// Act
		LocalizePageResponse response = _command.Localize(options);

		// Assert
		response.Success.Should().BeTrue(because: "the value was saved and read back");
		int checksumRead = _calls.FindIndex(call => call.Body != null && call.Body.Contains("\"Checksum\"", StringComparison.Ordinal));
		int save = _calls.FindIndex(call => call.Url.EndsWith("SaveSchema", StringComparison.Ordinal));
		checksumRead.Should().BeGreaterThanOrEqualTo(0, because: "the pre-save checksum must be read");
		checksumRead.Should().BeLessThan(save, because: "the checksum that proves the baseline current is the one before the save");
		_baselineGuard.Received(1).RefreshAfterSave(
			Arg.Any<EnvironmentOptions>(), SchemaName, SchemaUId, "/work/project", PreSaveChecksum,
			Arg.Any<Func<(string, string)>>());
	}

	[Test]
	[Description("Fix 1: a stale-baseline warning returned by the guard reaches the response warnings of a successful save.")]
	public void Execute_ShouldSurfaceBaselineWarning_WhenGuardReportsStaleBaseline() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));
		const string staleWarning = "The .clio-pages baseline of 'UsrLab_FormPage' was stale";
		_baselineGuard.RefreshAfterSave(default, default, default, default, default, default)
			.ReturnsForAnyArgs(staleWarning);

		// Act
		LocalizePageResponse response = _command.Localize(Options(resources: """{"UsrLabLabel_caption":"Etiqueta"}"""));

		// Assert
		response.Success.Should().BeTrue(because: "a stale baseline does not undo a save that landed");
		response.Warnings.Should().Contain(staleWarning, because: "the caller must learn that update-page will report a conflict");
	}

	[Test]
	[Description("Fix 3: the default culture en-US is refused in any letter case before anything is read, pointing to update-page resources.")]
	public void Execute_ShouldRefuseDefaultCulture_WhenCultureIsEnUs() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(culture: "EN-us", resources: """{"UsrLabLabel_caption":"Label"}"""));

		// Assert
		response.Success.Should().BeFalse(because: "localize-page never writes the default culture");
		response.Error.Should().Contain("update-page", because: "the message points to the tool that owns en-US values");
		_calls.Should().BeEmpty(because: "the refusal needs no request to the environment");
	}

	[Test]
	[Description("Fix 4: a resource value carrying a control character Creatio cannot store as an XML attribute is refused before any save, naming the key.")]
	public void Execute_ShouldRefuseValue_WhenItContainsControlCharacter() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(resources: """{"UsrLabLabel_caption":"Eti\u0001queta"}"""));

		// Assert
		response.Success.Should().BeFalse(because: "the save would succeed and a later package export would throw");
		response.Error.Should().Contain(OwnKey, because: "the caller must know which value to fix");
		SaveCount.Should().Be(0, because: "nothing is saved when a value cannot be stored");
	}

	[Test]
	[Description("Fix 4: tab, LF, CR and a surrogate pair are storable; a lone surrogate half in the caption is refused.")]
	public void Execute_ShouldRefuseCaption_WhenItContainsLoneSurrogate() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(caption: "P\uD83Dgina"));

		// Assert
		response.Success.Should().BeFalse(because: "a lone surrogate half is not valid XML");
		response.Error.Should().Contain("caption", because: "the message names the caption as the offending value");
		SaveCount.Should().Be(0, because: "nothing is saved when the caption cannot be stored");
		XmlAttributeText.ContainsUnstorable("a\tb\nc\rd \uD83D\uDE00").Should().BeFalse(
			because: "tab, LF, CR and a surrogate pair are legal in an XML attribute");
	}

	[Test]
	[Description("Fix 5: a resource key mapped to JSON null is refused instead of being written as null and reported as written.")]
	public void Execute_ShouldRefuseNullValue_WhenResourceValueIsNull() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));

		// Act
		LocalizePageResponse response = _command.Localize(Options(resources: """{"UsrLabLabel_caption":null}"""));

		// Assert
		response.Success.Should().BeFalse(because: "a null value is not a translation");
		response.Error.Should().Contain(OwnKey, because: "the key with the null value is named");
		SaveCount.Should().Be(0, because: "nothing is saved for an invalid resources map");
	}

	[Test]
	[Description("Fix 6: when the readback after a successful SaveSchema throws, the result still says saved:true with success:false and the error.")]
	public void Execute_ShouldReportSavedTrue_WhenReadbackThrows() {
		// Arrange
		_schema = Schema(Entry(OwnKey, SchemaUId, ("en-US", "Lab label")));
		_readbackThrows = true;

		// Act
		LocalizePageResponse response = _command.Localize(Options(resources: """{"UsrLabLabel_caption":"Etiqueta"}"""));

		// Assert
		response.Saved.Should().BeTrue(because: "the save landed before the readback failed");
		response.Success.Should().BeFalse(because: "the stored value could not be verified");
		response.Error.Should().Contain("connection reset during readback", because: "the readback failure is reported");
		SaveCount.Should().Be(1, because: "the save was sent once");
	}
}

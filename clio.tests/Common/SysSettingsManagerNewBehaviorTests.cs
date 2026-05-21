using System;
using System.Collections.Generic;
using ATF.Repository.Mock;
using ATF.Repository.Providers;
using Clio.Common;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using mockFs = System.IO.Abstractions;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class SysSettingsManagerNewBehaviorTests {

	#region Helpers

	private static readonly Guid AllUsersAdminUnitId = new("a29a3ba5-4b0d-de11-9a51-005056c00008");

	private static readonly mockFs.IFileSystem FileSystem
		= TestFileSystem.MockExamplesFolder("deployments-manifest");

	private static EnvironmentSettings EnvironmentSettings => new() {
		Uri = "https://localhost",
		Login = "Supervisor",
		Password = "Supervisor",
		IsNetCore = false
	};

	private static ISysSettingsManager BuildSut(IDataProvider dataProvider,
		IApplicationClient applicationClient = null) {
		BindingsModule bm = new(FileSystem);
		IServiceProvider container = bm.Register(EnvironmentSettings);
		return new SysSettingsManager(
			applicationClient ?? Substitute.For<IApplicationClient>(),
			container.GetRequiredService<IServiceUrlBuilder>(),
			dataProvider,
			container.GetRequiredService<IWorkingDirectoriesProvider>(),
			container.GetRequiredService<IFileSystem>(),
			FileSystem,
			Substitute.For<ILogger>());
	}

	private static DataProviderMock SetupSysSettingsMock(
		Guid settingId, string code, string valueTypeName,
		Dictionary<string, object> valueRow = null) {
		DataProviderMock providerMock = new();
		providerMock.MockItems("SysSettings").Returns(new List<Dictionary<string, object>> {
			new() {
				{ "Id", settingId },
				{ "Code", code },
				{ "Name", code },
				{ "ValueTypeName", valueTypeName },
				{ "Description", "" },
				{ "IsCacheable", true },
				{ "IsPersonal", false },
				{ "IsSSPAvailable", false }
			}
		});
		List<Dictionary<string, object>> values = [];
		if (valueRow is not null) {
			Dictionary<string, object> defaults = new() {
				{ "Id", Guid.NewGuid() },
				{ "SysSettings", settingId },
				{ "SysAdminUnit", AllUsersAdminUnitId },
				{ "IsDef", true },
				{ "TextValue", string.Empty },
				{ "IntegerValue", 0 },
				{ "FloatValue", 0m },
				{ "BooleanValue", false },
				{ "DateTimeValue", new DateTime(1900, 1, 1) },
				{ "GuidValue", Guid.Empty }
			};
			foreach (KeyValuePair<string, object> kv in valueRow) {
				defaults[kv.Key] = kv.Value;
			}
			values.Add(defaults);
		}
		providerMock.MockItems("SysSettingsValue").Returns(values);
		return providerMock;
	}

	#endregion

	#region GetSysSettingValueByCode — provider-first / model fallback

	[Test]
	[Description("Provider-first ordering: when the data provider already exposes a non-empty value, it is returned without consulting the typed model fallback.")]
	public void GetSysSettingValueByCode_PrefersProviderValue_WhenProviderReturnsNonEmpty() {
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		dataProvider.GetSysSettingValue<string>("MyText").Returns("provider-value");
		ISysSettingsManager sut = BuildSut(dataProvider);

		sut.GetSysSettingValueByCode("MyText").Should().Be("provider-value",
			because: "provider-first ordering preserves legacy behavior for text/personal settings");
	}

	[Test]
	[Description("Typed model fallback: Boolean settings round-trip through SysSettingsValue.BooleanValue formatted as lower-case 'true' / 'false'.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForBoolean() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyBool", "Boolean",
			new() { { "BooleanValue", true } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyBool").Should().Be("true",
			because: "Boolean values must be returned as invariant lowercase string");
	}

	[Test]
	[Description("Typed model fallback: Integer settings round-trip through SysSettingsValue.IntegerValue formatted with InvariantCulture.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForInteger() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyInt", "Integer",
			new() { { "IntegerValue", 42 } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyInt").Should().Be("42",
			because: "Integer values are emitted via InvariantCulture, with no thousands separator or culture-specific suffix");
	}

	[Test]
	[Description("Typed model fallback: Float / Money / Decimal / Currency settings reuse the FloatValue column and InvariantCulture formatting.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForFloat() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyFloat", "Float",
			new() { { "FloatValue", 3.14m } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyFloat").Should().Be("3.14",
			because: "Float/Money/Decimal/Currency must use invariant culture (period, not comma)");
	}

	[Test]
	[Description("Typed model fallback: Money is the canonical Creatio alias for Currency and must reuse the FloatValue formatting path.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForMoney() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyMoney", "Money",
			new() { { "FloatValue", 1500.5m } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyMoney").Should().Be("1500.5",
			because: "Money is treated as Float on the read side and InvariantCulture renders the decimal separator as a period");
	}

	[Test]
	[Description("Typed model fallback: Date settings format DateTimeValue as 'yyyy-MM-dd' under InvariantCulture.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForDate() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyDate", "Date",
			new() { { "DateTimeValue", new DateTime(2026, 1, 15) } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyDate").Should().Be("2026-01-15",
			because: "Date formatting uses 'yyyy-MM-dd' under InvariantCulture so the wire representation is stable across locales");
	}

	[Test]
	[Description("Typed model fallback: Time settings format DateTimeValue as 'HH:mm:ss' under InvariantCulture.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForTime() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyTime", "Time",
			new() { { "DateTimeValue", new DateTime(1900, 1, 1, 14, 30, 0) } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyTime").Should().Be("14:30:00",
			because: "Time formatting uses 'HH:mm:ss' under InvariantCulture so the wire representation is stable across locales");
	}

	[Test]
	[Description("Typed model fallback: DateTime settings format DateTimeValue with the round-trip 'o' specifier so Kind information is preserved.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForDateTime() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyDt", "DateTime",
			new() { { "DateTimeValue", new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc) } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyDt").Should().Contain("2026-02-01",
			because: "DateTime should be formatted as ISO 8601 round-trip");
	}

	[Test]
	[Description("Typed model fallback: Lookup settings expose the GUID stored in SysSettingsValue.GuidValue.")]
	public void GetSysSettingValueByCode_FallsBackToModel_ForLookup() {
		Guid id = Guid.NewGuid();
		Guid guidValue = new("2cfdcf5d-744b-4e0a-b6d0-fbd905fea8ed");
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyLookup", "Lookup",
			new() { { "GuidValue", guidValue } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyLookup").Should().Be(guidValue.ToString(),
			because: "Lookup values surface as the underlying GUID; the platform stores the foreign-key on SysSettingsValue.GuidValue");
	}

	[Test]
	[Description("When a setting exists but has no SysSettingsValue rows, the manager returns an empty string rather than throwing.")]
	public void GetSysSettingValueByCode_ReturnsEmpty_WhenNoValueRowExists() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "EmptyInt", "Integer", valueRow: null);
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("EmptyInt").Should().BeEmpty(
			because: "missing SysSettingsValue rows should produce an empty result");
	}

	#endregion

	#region FindSchemaUIdByName

	[Test]
	[Description("FindSchemaUIdByName resolves a schema name to its UId via the data provider's SysSchema model.")]
	public void FindSchemaUIdByName_ReturnsUId_WhenSchemaExists() {
		DataProviderMock providerMock = new();
		Guid expectedUId = Guid.NewGuid();
		providerMock.MockItems("SysSchema").Returns(new List<Dictionary<string, object>> {
			new() {
				{ "Id", Guid.NewGuid() },
				{ "UId", expectedUId },
				{ "Name", "UsrPhoneFormat" }
			}
		});
		ISysSettingsManager sut = BuildSut(providerMock);

		Guid? actual = sut.FindSchemaUIdByName("UsrPhoneFormat");

		actual.Should().Be(expectedUId,
			because: "FindSchemaUIdByName resolves the schema UId via SysSchema model and must return the value stored in UId");
	}

	[Test]
	[Description("FindSchemaUIdByName returns null (rather than throwing) when no SysSchema row matches the requested name.")]
	public void FindSchemaUIdByName_ReturnsNull_WhenSchemaMissing() {
		DataProviderMock providerMock = new();
		providerMock.MockItems("SysSchema").Returns(new List<Dictionary<string, object>>());
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.FindSchemaUIdByName("Nonexistent").Should().BeNull(
			because: "the lookup helper must return null (not throw) for codes that resolve to no SysSchema row");
	}

	[Test]
	[Description("FindSchemaUIdByName treats an empty / whitespace name as a missing lookup and returns null without contacting the provider.")]
	public void FindSchemaUIdByName_ReturnsNull_ForBlankInput() {
		ISysSettingsManager sut = BuildSut(new DataProviderMock());

		sut.FindSchemaUIdByName(null).Should().BeNull(
			because: "a null name is invalid input and the helper short-circuits without contacting the provider");
		sut.FindSchemaUIdByName(string.Empty).Should().BeNull(
			because: "an empty name is invalid input and the helper short-circuits without contacting the provider");
		sut.FindSchemaUIdByName("   ").Should().BeNull(
			because: "a whitespace-only name is invalid input and the helper short-circuits without contacting the provider");
	}

	#endregion

	#region InsertSysSetting — referenceSchemaUId + new type aliases

	private const string InsertSuccessJson =
		"""{"responseStatus":{"ErrorCode":"","Message":"","Errors":[]},"id":"acf40078-ba48-4285-9f3b-44ebafa28cac","rowsAffected":1,"nextPrcElReady":false,"success":true}""";

	[Test]
	[Description("Insert serializes the supplied referenceSchemaUId into the JSON payload so the platform creates a Lookup setting bound to the chosen entity schema.")]
	public void InsertSysSetting_SerializesReferenceSchemaUId_WhenProvidedForLookup() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		string capturedBody = null;
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => capturedBody = b))
			.Returns(InsertSuccessJson);
		Guid refUId = new("b80eb7bb-193c-4bb2-ad51-e0beb1670278");
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.InsertSysSetting("Lookup Setting", "UsrLookupSetting", "Lookup",
			referenceSchemaUId: refUId);

		capturedBody.Should().NotBeNull(
			because: "the platform request must be issued and its body captured for inspection");
		capturedBody.Should().Contain("\"referenceSchemaUId\":\"b80eb7bb-193c-4bb2-ad51-e0beb1670278\"",
			because: "Lookup sys-settings must carry the reference schema UId so the picker can render");
		capturedBody.Should().Contain("\"valueTypeName\":\"Lookup\"",
			because: "the platform expects the Creatio internal type name 'Lookup' on the wire");
	}

	[Test]
	[Description("Insert omits the referenceSchemaUId from the JSON payload when null or Guid.Empty so non-Lookup settings do not declare an unintended reference.")]
	public void InsertSysSetting_OmitsReferenceSchemaUId_WhenNotProvided() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		string capturedBody = null;
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => capturedBody = b))
			.Returns(InsertSuccessJson);
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.InsertSysSetting("Plain", "UsrPlain", "Text");

		capturedBody.Should().NotContain("referenceSchemaUId",
			because: "null reference schema UId is skipped by the serializer to preserve legacy payload shape");
	}

	[TestCase("Money", "Money")]
	[TestCase("Float", "Float")]
	[TestCase("Binary", "Binary")]
	[TestCase("Currency", "Money")]
	[TestCase("Decimal", "Float")]
	[Description("Insert accepts the legacy aliases Currency and Decimal and maps them to the canonical Creatio internal names Money and Float on the wire.")]
	public void InsertSysSetting_MapsTypeAliasesToCreatioInternalNames(string input, string expected) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		string capturedBody = null;
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => capturedBody = b))
			.Returns(InsertSuccessJson);
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.InsertSysSetting("N", "UsrCode", input);

		capturedBody.Should().Contain($"\"valueTypeName\":\"{expected}\"",
			because: "the serialized type must use the Creatio internal name regardless of caller alias");
	}

	#endregion

	#region UpdateSysSetting — saveResult parsing

	[Test]
	[Description("Update parses the saveResult dictionary by code instead of relying on the unreliable top-level success flag — a per-code true means the value landed.")]
	public void UpdateSysSetting_ReturnsTrue_WhenSaveResultReportsSuccessForCode() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(
				"""{"saveResult":{"UsrAny":true},"rowsAffected":-1,"nextPrcElReady":false,"success":false}""");
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.UpdateSysSetting("UsrAny", "value").Should().BeTrue(
			because: "saveResult[code] is the authoritative per-setting result; top-level success is unreliable");
	}

	[Test]
	[Description("Update returns false when the platform reports saveResult[code] = false, surfacing the platform's error message when available.")]
	public void UpdateSysSetting_ReturnsFalse_WhenSaveResultReportsFailureForCode() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(
				"""{"saveResult":{"UsrAny":false},"rowsAffected":-1,"nextPrcElReady":false,"success":false,"responseStatus":{"ErrorCode":"","Message":"denied","Errors":[]}}""");
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.UpdateSysSetting("UsrAny", "value").Should().BeFalse(
			because: "a per-code saveResult of false means the platform actively rejected the value");
	}

	[Test]
	[Description("Update returns false when the saveResult payload does not contain the requested code — the platform did not acknowledge the per-code outcome.")]
	public void UpdateSysSetting_ReturnsFalse_WhenSaveResultMissingForCode() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(
				"""{"saveResult":{"OtherCode":true},"rowsAffected":-1,"nextPrcElReady":false,"success":false}""");
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.UpdateSysSetting("UsrAny", "value").Should().BeFalse(
			because: "a saveResult that does not include the requested code is treated as failure");
	}

	[Test]
	[Description("Update returns false when the platform returns an empty response body so the caller does not infer success from a missing acknowledgement.")]
	public void UpdateSysSetting_ReturnsFalse_WhenResponseIsEmpty() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(string.Empty);
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.UpdateSysSetting("UsrAny", "value").Should().BeFalse(
			because: "an empty response body means the platform did not acknowledge the request and the caller must not infer success");
	}

	#endregion

	#region CBinary sanity

	[Test]
	[Description("CBinary subclass should report 'Binary' as its value-type-name for serialization parity with other typed settings.")]
	public void CBinary_ExposesBinaryValueTypeName() {
		CBinary sut = new("Name", "Code", value: null, isCacheable: true,
			description: "", isPersonal: false);
		sut.ValueTypeName.Should().Be("Binary",
			because: "platform-side InsertSysSettingRequest expects the Creatio internal type name 'Binary' for binary settings");
	}

	#endregion

	#region UpdateSysSetting — code validation & safe JSON encoding

	[Test]
	[Description("UpdateSysSetting must reject codes containing non-identifier characters before contacting the platform.")]
	public void UpdateSysSetting_RejectsCode_WithInvalidIdentifierCharacters() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		bool result = sut.UpdateSysSetting("Usr\"Inject", "value");

		result.Should().BeFalse(
			because: "an agent-supplied code with a quote character could otherwise break the request JSON payload");
		applicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("UpdateSysSetting must encode string values via JsonSerializer so quotes and control characters cannot corrupt the request body.")]
	public void UpdateSysSetting_EscapesQuotesInValuePayload() {
		DataProviderMock providerMock = SetupSysSettingsMock(Guid.NewGuid(), "UsrEscapeCode", "Text");
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		string capturedBody = null;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => capturedBody = b))
			.Returns("""{"saveResult":{"UsrEscapeCode":true},"success":false}""");
		ISysSettingsManager sut = BuildSut(providerMock, applicationClient);

		sut.UpdateSysSetting("UsrEscapeCode", "with \"quote\" and \\ slash").Should().BeTrue();

		capturedBody.Should().NotBeNull();
		capturedBody.Should().NotContain("\"quote\"",
			because: "embedded quotes must be encoded by the serializer; leaving them literal would close the JSON value early");
		capturedBody.Should().MatchRegex(@"(\\u0022|\\"")quote(\\u0022|\\"")",
			because: "the serializer escapes inner quotes either as \\u0022 or \\\" depending on its encoder settings");
		capturedBody.Should().Contain("\\\\ slash",
			because: "backslashes must be JSON-escaped through JsonSerializer to avoid request corruption");
	}

	#endregion

	#region UpdateSysSetting — Money / Float numeric branches

	[Test]
	[Description("Money is the new Creatio internal alias for Currency and must accept decimal values on the update path.")]
	public void UpdateSysSetting_MoneyType_SerializesDecimalValue() {
		DataProviderMock providerMock = SetupSysSettingsMock(Guid.NewGuid(), "UsrMoneyCode", "Money");
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		string capturedBody = null;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => capturedBody = b))
			.Returns("""{"saveResult":{"UsrMoneyCode":true},"success":false}""");
		ISysSettingsManager sut = BuildSut(providerMock, applicationClient);

		sut.UpdateSysSetting("UsrMoneyCode", "19.95").Should().BeTrue(
			because: "Money settings reuse the decimal serialization branch alongside Currency/Decimal/Float");
		capturedBody.Should().Contain("\"UsrMoneyCode\":19.95",
			because: "decimal payloads are emitted as JSON numbers, not strings");
	}

	[Test]
	[Description("Float is the new Creatio internal alias for Decimal and must accept decimal values on the update path.")]
	public void UpdateSysSetting_FloatType_SerializesDecimalValue() {
		DataProviderMock providerMock = SetupSysSettingsMock(Guid.NewGuid(), "UsrFloatCode", "Float");
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		string capturedBody = null;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(b => capturedBody = b))
			.Returns("""{"saveResult":{"UsrFloatCode":true},"success":false}""");
		ISysSettingsManager sut = BuildSut(providerMock, applicationClient);

		sut.UpdateSysSetting("UsrFloatCode", "3.14").Should().BeTrue(
			because: "Float settings reuse the decimal serialization branch alongside Currency/Decimal/Money");
		capturedBody.Should().Contain("\"UsrFloatCode\":3.14",
			because: "decimal payloads are emitted as JSON numbers, not strings");
	}

	#endregion

	#region GetSysSettingValueByCode — All-Users-only fallback

	[Test]
	[Description("GetSysSettingValueByCode must return empty when only personal-user values exist; falling back to a non-All-Users row would mislead callers expecting the global default.")]
	public void GetSysSettingValueByCode_ReturnsEmpty_WhenOnlyPersonalValuesExist() {
		Guid settingId = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(settingId, "UsrPersonalOnly", "Text",
			valueRow: new Dictionary<string, object> {
				{ "SysAdminUnit", Guid.NewGuid() },
				{ "TextValue", "personal-value" }
			});
		ISysSettingsManager sut = BuildSut(providerMock);

		string value = sut.GetSysSettingValueByCode("UsrPersonalOnly");

		value.Should().BeEmpty(
			because: "the MCP get-sys-setting flow advertises the All-Users default; falling back to a personal row would leak another user's value");
	}

	#endregion
}

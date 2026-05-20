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
	public void GetSysSettingValueByCode_PrefersProviderValue_WhenProviderReturnsNonEmpty() {
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		dataProvider.GetSysSettingValue<string>("MyText").Returns("provider-value");
		ISysSettingsManager sut = BuildSut(dataProvider);

		sut.GetSysSettingValueByCode("MyText").Should().Be("provider-value",
			because: "provider-first ordering preserves legacy behavior for text/personal settings");
	}

	[Test]
	public void GetSysSettingValueByCode_FallsBackToModel_ForBoolean() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyBool", "Boolean",
			new() { { "BooleanValue", true } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyBool").Should().Be("true",
			because: "Boolean values must be returned as invariant lowercase string");
	}

	[Test]
	public void GetSysSettingValueByCode_FallsBackToModel_ForInteger() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyInt", "Integer",
			new() { { "IntegerValue", 42 } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyInt").Should().Be("42");
	}

	[Test]
	public void GetSysSettingValueByCode_FallsBackToModel_ForFloat() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyFloat", "Float",
			new() { { "FloatValue", 3.14m } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyFloat").Should().Be("3.14",
			because: "Float/Money/Decimal/Currency must use invariant culture (period, not comma)");
	}

	[Test]
	public void GetSysSettingValueByCode_FallsBackToModel_ForMoney() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyMoney", "Money",
			new() { { "FloatValue", 1500.5m } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyMoney").Should().Be("1500.5");
	}

	[Test]
	public void GetSysSettingValueByCode_FallsBackToModel_ForDate() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyDate", "Date",
			new() { { "DateTimeValue", new DateTime(2026, 1, 15) } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyDate").Should().Be("2026-01-15");
	}

	[Test]
	public void GetSysSettingValueByCode_FallsBackToModel_ForTime() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyTime", "Time",
			new() { { "DateTimeValue", new DateTime(1900, 1, 1, 14, 30, 0) } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyTime").Should().Be("14:30:00");
	}

	[Test]
	public void GetSysSettingValueByCode_FallsBackToModel_ForDateTime() {
		Guid id = Guid.NewGuid();
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyDt", "DateTime",
			new() { { "DateTimeValue", new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc) } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyDt").Should().Contain("2026-02-01",
			because: "DateTime should be formatted as ISO 8601 round-trip");
	}

	[Test]
	public void GetSysSettingValueByCode_FallsBackToModel_ForLookup() {
		Guid id = Guid.NewGuid();
		Guid guidValue = new("2cfdcf5d-744b-4e0a-b6d0-fbd905fea8ed");
		DataProviderMock providerMock = SetupSysSettingsMock(id, "MyLookup", "Lookup",
			new() { { "GuidValue", guidValue } });
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.GetSysSettingValueByCode("MyLookup").Should().Be(guidValue.ToString());
	}

	[Test]
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

		actual.Should().Be(expectedUId);
	}

	[Test]
	public void FindSchemaUIdByName_ReturnsNull_WhenSchemaMissing() {
		DataProviderMock providerMock = new();
		providerMock.MockItems("SysSchema").Returns(new List<Dictionary<string, object>>());
		ISysSettingsManager sut = BuildSut(providerMock);

		sut.FindSchemaUIdByName("Nonexistent").Should().BeNull();
	}

	[Test]
	public void FindSchemaUIdByName_ReturnsNull_ForBlankInput() {
		ISysSettingsManager sut = BuildSut(new DataProviderMock());

		sut.FindSchemaUIdByName(null).Should().BeNull();
		sut.FindSchemaUIdByName(string.Empty).Should().BeNull();
		sut.FindSchemaUIdByName("   ").Should().BeNull();
	}

	#endregion

	#region InsertSysSetting — referenceSchemaUId + new type aliases

	private const string InsertSuccessJson =
		"""{"responseStatus":{"ErrorCode":"","Message":"","Errors":[]},"id":"acf40078-ba48-4285-9f3b-44ebafa28cac","rowsAffected":1,"nextPrcElReady":false,"success":true}""";

	[Test]
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

		capturedBody.Should().NotBeNull();
		capturedBody.Should().Contain("\"referenceSchemaUId\":\"b80eb7bb-193c-4bb2-ad51-e0beb1670278\"",
			because: "Lookup sys-settings must carry the reference schema UId so the picker can render");
		capturedBody.Should().Contain("\"valueTypeName\":\"Lookup\"");
	}

	[Test]
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
	public void UpdateSysSetting_ReturnsFalse_WhenSaveResultReportsFailureForCode() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(
				"""{"saveResult":{"UsrAny":false},"rowsAffected":-1,"nextPrcElReady":false,"success":false,"responseStatus":{"ErrorCode":"","Message":"denied","Errors":[]}}""");
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.UpdateSysSetting("UsrAny", "value").Should().BeFalse();
	}

	[Test]
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
	public void UpdateSysSetting_ReturnsFalse_WhenResponseIsEmpty() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(string.Empty);
		ISysSettingsManager sut = BuildSut(new DataProviderMock(), applicationClient);

		sut.UpdateSysSetting("UsrAny", "value").Should().BeFalse();
	}

	#endregion

	#region CBinary sanity

	[Test]
	public void CBinary_ExposesBinaryValueTypeName() {
		CBinary sut = new("Name", "Code", value: null, isCacheable: true,
			description: "", isPersonal: false);
		sut.ValueTypeName.Should().Be("Binary");
	}

	#endregion
}

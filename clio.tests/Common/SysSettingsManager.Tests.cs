using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
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
public class SysSettingsManagerTests
{

	#region Fields: Private

	private readonly IServiceProvider _container;
	private readonly mockFs.IFileSystem _fileSystem = TestFileSystem.MockExamplesFolder("deployments-manifest");

	#endregion

	#region Constructors: Public

	public SysSettingsManagerTests(){
		BindingsModule bm = new(_fileSystem);
		_container = bm.Register(EnvironmentSettings);
	}

	#endregion

	#region Properties: Private

	private static EnvironmentSettings EnvironmentSettings =>
		new() {
			Uri = "https://localhost",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = false
		};

	#endregion

	#region Method : GetSysSettingValueByCode

	[TestCase("true")]
	[TestCase("True")]
	[TestCase("false")]
	[TestCase("False")]
	public void GetSysSettingValueByCode_Returns_CorrectBooleanValue(string value){
		const string sysSettingCode = "nonExistingCode";
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.GetRequiredService<IServiceUrlBuilder>();
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = _container.GetRequiredService<IWorkingDirectoriesProvider>();
		IFileSystem filesystem = _container.GetRequiredService<IFileSystem>();
		ILogger logger = Substitute.For<ILogger>();
		dataProvider.GetSysSettingValue<string>(sysSettingCode).Returns(value);
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder, dataProvider, workingDirectoriesProvider, filesystem, logger);

		bool actual = sut.GetSysSettingValueByCode<bool>(sysSettingCode);

		if (value.ToLower() == "true") {
			actual.Should().BeTrue();
		} else {
			actual.Should().BeFalse();
		}
	}

	[TestCase("10/29/2013 4:42:51 PM", "MM/dd/yyyy h:mm:ss tt")]
	[TestCase("10/29/2013 4:42:51 AM", "MM/dd/yyyy h:mm:ss tt")]
	public void GetSysSettingValueByCode_Returns_CorrectDateTimeValue(string dateValue, string format){
		DateTime.TryParseExact(dateValue, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dtValue);
		string stringDateTimeValue = dtValue.ToString(CultureInfo.InvariantCulture);
		const string sysSettingCode = "nonExistingCode";
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.GetRequiredService<IServiceUrlBuilder>();
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = _container.GetRequiredService<IWorkingDirectoriesProvider>();
		IFileSystem filesystem = _container.GetRequiredService<IFileSystem>();
		ILogger logger = Substitute.For<ILogger>();
		dataProvider.GetSysSettingValue<string>(sysSettingCode).Returns(stringDateTimeValue);
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder, dataProvider, workingDirectoriesProvider, filesystem, logger);

		DateTime actual = sut.GetSysSettingValueByCode<DateTime>(sysSettingCode);

		actual.Should().Be(dtValue);
	}

	[TestCase("0C5715C6-D067-45F5-ABC6-B2BDAE909393")]
	[TestCase("00000000-0000-0000-0000-000000000000")]
	public void GetSysSettingValueByCode_Returns_CorrectGuidValue(string value){
		const string sysSettingCode = "nonExistingCode";
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.GetRequiredService<IServiceUrlBuilder>();
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = _container.GetRequiredService<IWorkingDirectoriesProvider>();
		IFileSystem filesystem = _container.GetRequiredService<IFileSystem>();
		ILogger logger = Substitute.For<ILogger>();
		dataProvider.GetSysSettingValue<string>(sysSettingCode).Returns(value);
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder, dataProvider, workingDirectoriesProvider, filesystem, logger);

		Guid actual = sut.GetSysSettingValueByCode<Guid>(sysSettingCode);

		Guid.TryParse(value, out Guid guidValue);
		actual.Should().Be(guidValue);
	}

	[TestCase("123")]
	[TestCase("123.00")]
	[TestCase("123.00000")]
	[TestCase("-123.00000")]
	[TestCase("-123.00")]
	[TestCase("-123")]
	[TestCase("0")]
	[TestCase("1,1234.00")]
	public void GetSysSettingValueByCode_Returns_CorrectDecimalValue(string value){
		const string sysSettingCode = "nonExistingCode";
		decimal sysSettingValue = decimal.Parse(value, CultureInfo.InvariantCulture);
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.GetRequiredService<IServiceUrlBuilder>();
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = _container.GetRequiredService<IWorkingDirectoriesProvider>();
		IFileSystem filesystem = _container.GetRequiredService<IFileSystem>();
		ILogger logger = Substitute.For<ILogger>();
		dataProvider.GetSysSettingValue<string>(sysSettingCode).Returns(sysSettingValue.ToString(CultureInfo.InvariantCulture));
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder, dataProvider, workingDirectoriesProvider, filesystem, logger);

		decimal actual = sut.GetSysSettingValueByCode<decimal>(sysSettingCode);
		actual.Should().BeOfType(typeof(decimal));
		actual.Should().Be(sysSettingValue);
	}

	[TestCase("0")]
	[TestCase("123")]
	[TestCase("-123")]
	[TestCase("1,230")]
	[TestCase("-1,230")]
	public void GetSysSettingValueByCode_Returns_CorrectIntValue(string value){
		const string sysSettingCode = "nonExistingCode";
		CultureInfo provider = new("en-US");
		int sysSettingValue = (int)decimal.Parse(value, provider);

		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.GetRequiredService<IServiceUrlBuilder>();
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = _container.GetRequiredService<IWorkingDirectoriesProvider>();
		IFileSystem filesystem = _container.GetRequiredService<IFileSystem>();
		ILogger logger = Substitute.For<ILogger>();
		dataProvider.GetSysSettingValue<string>(sysSettingCode).Returns(value);
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder, dataProvider, workingDirectoriesProvider, filesystem, logger);

		int actual = sut.GetSysSettingValueByCode<int>(sysSettingCode);
		actual.Should().BeOfType(typeof(int));
		actual.Should().Be(sysSettingValue);
	}

	[TestCase("-1,230.5")]
	[TestCase("1,230.5")]
	public void GetSysSettingValueByCode_Throws(string value){
		const string sysSettingCode = "nonExistingCode";
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.GetRequiredService<IServiceUrlBuilder>();
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = _container.GetRequiredService<IWorkingDirectoriesProvider>();
		IFileSystem filesystem = _container.GetRequiredService<IFileSystem>();
		ILogger logger = Substitute.For<ILogger>();
		dataProvider.GetSysSettingValue<string>(sysSettingCode).Returns(value);
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder, dataProvider, workingDirectoriesProvider, filesystem, logger);

		Action act = () => sut.GetSysSettingValueByCode<int>(sysSettingCode);
		act.Should()
			.Throw<InvalidCastException>()
			.WithMessage($"Could not convert {value} to to {nameof(Int32)}");
	}

	[Test]
	public void GetSysSettingValueByCode_Returns_Value(){
		const string sysSettingCode = "nonExistingCode";
		const string sysSettingValue = "123";

		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.GetRequiredService<IServiceUrlBuilder>();
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = _container.GetRequiredService<IWorkingDirectoriesProvider>();
		IFileSystem filesystem = _container.GetRequiredService<IFileSystem>();
		ILogger logger = Substitute.For<ILogger>();
		dataProvider.GetSysSettingValue<string>(sysSettingCode).Returns(sysSettingValue);
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder, dataProvider, workingDirectoriesProvider, filesystem, logger);

		string actual = sut.GetSysSettingValueByCode(sysSettingCode);

		actual.Should().Be(sysSettingValue);
		dataProvider.Received(1).GetSysSettingValue<string>(sysSettingCode);
	}

	#endregion

	#region Method : InsertSysSetting

	[TestCase("Text")]
	[TestCase("ShortText")]
	[TestCase("MediumText")]
	[TestCase("LongText")]
	[TestCase("SecureText")]
	[TestCase("MaxSizeText")]
	public void CreatioCanCreateSetting(string valueTypeName){
		const string sysSettingCode = "nonExistingCode";
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.GetRequiredService<IServiceUrlBuilder>();
		IDataProvider dataProvider = _container.GetRequiredService<IDataProvider>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = _container.GetRequiredService<IWorkingDirectoriesProvider>();
		IFileSystem filesystem = _container.GetRequiredService<IFileSystem>();
		ILogger logger = Substitute.For<ILogger>();
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder, dataProvider, workingDirectoriesProvider, filesystem, logger);

		string segment = EnvironmentSettings.IsNetCore switch {
			true => "/DataService/json/SyncReply/InsertSysSettingRequest",
			false => "/0/DataService/json/SyncReply/InsertSysSettingRequest"
		};
		string expectedUrl = EnvironmentSettings.Uri + segment;

		applicationClient
			.ExecutePostRequest(Arg.Is(expectedUrl), Arg.Is<string>(s => EvalValueTypeName(s, valueTypeName)))
			.Returns(
				"""
					{
						"id": "acf40078-ba48-4285-9f3b-44ebafa28cac",
						"rowsAffected": 1,
						"nextPrcElReady": false,
						"success": true
					}
				"""
			);

		SysSettingsManager.InsertSysSettingResponse actual
			= sut.InsertSysSetting(sysSettingCode, sysSettingCode, valueTypeName);

		actual.Id.Should().Be("acf40078-ba48-4285-9f3b-44ebafa28cac");
	}

	private static bool EvalValueTypeName(string json, string valueTypeName){
		Dictionary<string, object> sysSetting = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
		return sysSetting["valueTypeName"].ToString() == valueTypeName;
	}

	[Test]
	public void CreatioCannotCanCreateSetting(){
		const string sysSettingCode = "nonExistingCode";
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.GetRequiredService<IServiceUrlBuilder>();
		IDataProvider dataProvider = _container.GetRequiredService<IDataProvider>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = _container.GetRequiredService<IWorkingDirectoriesProvider>();
		IFileSystem filesystem = _container.GetRequiredService<IFileSystem>();
		ILogger logger = Substitute.For<ILogger>();
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder, dataProvider, workingDirectoriesProvider, filesystem, logger);

		string segment = EnvironmentSettings.IsNetCore switch {
			true => "/DataService/json/SyncReply/InsertSysSettingRequest",
			false => "/0/DataService/json/SyncReply/InsertSysSettingRequest"
		};
		string expectedUrl = EnvironmentSettings.Uri + segment;
		applicationClient
			.ExecutePostRequest(Arg.Is(expectedUrl), Arg.Any<string>())
			.Returns(
				"""
					{
				               "responseStatus": {
				                 "ErrorCode": "DbOperationException",
				                 "Message": "Violation of PRIMARY KEY constraint 'PKO0XjBowul8kHVr5gXrx2yS4A0Lc'. Cannot insert duplicate key in object 'dbo.SysSettings'. The duplicate key value is (c7363e33-f8cc-4059-9761-a7c379088489).\r\nThe statement has been terminated.",
				                 "Errors": []
				               },
				               "rowsAffected": -1,
				               "nextPrcElReady": false,
				               "success": false
				             }
				"""
			);

		SysSettingsManager.InsertSysSettingResponse actual
			= sut.InsertSysSetting(sysSettingCode, sysSettingCode, "Text");

		actual.Id.Should().Be(Guid.Empty);
		actual.Success.Should().BeFalse();
		actual.ResponseStatus.ErrorCode.Should().Be("DbOperationException");
		actual.ResponseStatus.Message.Should().StartWith("Violation of PRIMARY KEY constraint");
	}

	#endregion

	#region Method : UpdateSysSetting

	[Test]
	[Ignore("Changed update logic, deprecated test")]
	public void UpdateSysSetting_ReturnsTrue_WhenApiResponseSuccessIsTrue(){
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.GetRequiredService<IServiceUrlBuilder>();
		IDataProvider dataProvider = _container.GetRequiredService<IDataProvider>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = _container.GetRequiredService<IWorkingDirectoriesProvider>();
		IFileSystem filesystem = _container.GetRequiredService<IFileSystem>();
		ILogger logger = Substitute.For<ILogger>();
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder, dataProvider,
			workingDirectoriesProvider, filesystem, logger);

		string segment = EnvironmentSettings.IsNetCore switch {
			true => "/DataService/json/SyncReply/PostSysSettingsValues",
			false => "/0/DataService/json/SyncReply/PostSysSettingsValues"
		};
		string expectedUrl = EnvironmentSettings.Uri + segment;

		applicationClient.ExecutePostRequest(Arg.Is(expectedUrl), Arg.Any<string>())
			.Returns(
				"""
				{
				  "responseStatus": {
				    "ErrorCode": "",
				    "Message": "",
				    "Errors": []
				  },
				  "rowsAffected": 1,
				  "nextPrcElReady": false,
				  "success": true
				}
				"""
			);

		bool actual = sut.UpdateSysSetting("Maintainer", "Creatio");

		actual.Should().BeTrue();
	}

	[Test]
	[Ignore("Changed update logic, deprecated test")]
	public void UpdateSysSetting_ReturnsFalse_WhenApiResponseSuccessIsFalse(){
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.GetRequiredService<IServiceUrlBuilder>();
		IDataProvider dataProvider = _container.GetRequiredService<IDataProvider>();
		IWorkingDirectoriesProvider workingDirectoriesProvider = _container.GetRequiredService<IWorkingDirectoriesProvider>();
		IFileSystem filesystem = _container.GetRequiredService<IFileSystem>();
		ILogger logger = Substitute.For<ILogger>();
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder, dataProvider,
			workingDirectoriesProvider, filesystem, logger);

		string segment = EnvironmentSettings.IsNetCore switch {
			true => "/DataService/json/SyncReply/PostSysSettingsValues",
			false => "/0/DataService/json/SyncReply/PostSysSettingsValues"
		};
		string expectedUrl = EnvironmentSettings.Uri + segment;

		applicationClient.ExecutePostRequest(Arg.Is(expectedUrl), Arg.Any<string>())
			.Returns(
				"""
				{
				  "responseStatus": {
				    "ErrorCode": "DbOperationException",
				    "Message": "Could not update setting",
				    "Errors": []
				  },
				  "rowsAffected": -1,
				  "nextPrcElReady": false,
				  "success": false
				}
				"""
			);

		bool actual = sut.UpdateSysSetting("Maintainer", "Creatio");

		actual.Should().BeFalse();
	}

	#endregion

}

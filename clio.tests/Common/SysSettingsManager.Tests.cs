using System;
using System.Globalization;
using System.Text.Json;
using Autofac;
using Clio.Common;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using mockFs = System.IO.Abstractions;

namespace Clio.Tests.Common;

[TestFixture]
[Category("UnitTests")]
public class SysSettingsManagerTests
{

	#region Fields: Private

	private readonly IContainer _container;
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

	#region Methods: Public

	#region Method : GetSysSettingValueByCode 
	[TestCase("true")]
	[TestCase("True")]
	[TestCase("false")]
	[TestCase("False")]
	public void GetSysSettingValueByCode_Returns_CorrectBooleanValue(string value){
		//Arrange
		const string sysSettingCode = "nonExistingCode";
		string sysSettingValue = value;
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.Resolve<IServiceUrlBuilder>();
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder);

		string segment = EnvironmentSettings.IsNetCore switch {
			true => "/rest/CreatioApiGateway/GetSysSettingValueByCode",
			false => "/0/rest/CreatioApiGateway/GetSysSettingValueByCode"
		};

		string expectedUrl = EnvironmentSettings.Uri + segment;
		string expectedRequestData = JsonSerializer
			.Serialize(new SysSettingsManager.GetSettingRequestData(sysSettingCode),
				new JsonSerializerOptions {
					WriteIndented = false,
					AllowTrailingCommas = false,
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				});

		applicationClient.ExecutePostRequest(
				Arg.Is(expectedUrl),
				Arg.Is(expectedRequestData))
			.Returns(sysSettingValue);

		//Act
		bool actual = sut.GetSysSettingValueByCode<bool>(sysSettingCode);

		if (value.ToLower() == "true") {
			actual.Should().BeTrue();
		} else {
			actual.Should().BeFalse();
		}
	}

	[TestCase("10/29/2013 4:42:51 PM")]
	[TestCase("10/29/2013 4:42:51 AM")]
	public void GetSysSettingValueByCode_Returns_CorrectDateTimeValue(string value){
		//Arrange
		const string sysSettingCode = "nonExistingCode";
		string sysSettingValue = value;
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.Resolve<IServiceUrlBuilder>();
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder);
		string segment = EnvironmentSettings.IsNetCore switch {
			true => "/rest/CreatioApiGateway/GetSysSettingValueByCode",
			false => "/0/rest/CreatioApiGateway/GetSysSettingValueByCode"
		};
		string expectedUrl = EnvironmentSettings.Uri + segment;
		string expectedRequestData = JsonSerializer
			.Serialize(new SysSettingsManager.GetSettingRequestData(sysSettingCode),
				new JsonSerializerOptions {
					WriteIndented = false,
					AllowTrailingCommas = false,
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				});

		applicationClient.ExecutePostRequest(
				Arg.Is(expectedUrl),
				Arg.Is(expectedRequestData))
			.Returns(sysSettingValue);

		//Act
		DateTime actual = sut.GetSysSettingValueByCode<DateTime>(sysSettingCode);
		
		//Assert
		DateTime.TryParse(value, out DateTime dtValue);
		actual.Should().Be(dtValue);
	}
	
	
	[TestCase("0C5715C6-D067-45F5-ABC6-B2BDAE909393")]
	[TestCase("00000000-0000-0000-0000-000000000000")]
	public void GetSysSettingValueByCode_Returns_CorrectGuidValue(string value){
		//Arrange
		const string sysSettingCode = "nonExistingCode";
		string sysSettingValue = value;
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.Resolve<IServiceUrlBuilder>();
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder);
		string segment = EnvironmentSettings.IsNetCore switch {
			true => "/rest/CreatioApiGateway/GetSysSettingValueByCode",
			false => "/0/rest/CreatioApiGateway/GetSysSettingValueByCode"
		};
		string expectedUrl = EnvironmentSettings.Uri + segment;
		string expectedRequestData = JsonSerializer
			.Serialize(new SysSettingsManager.GetSettingRequestData(sysSettingCode),
				new JsonSerializerOptions {
					WriteIndented = false,
					AllowTrailingCommas = false,
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				});

		applicationClient.ExecutePostRequest(
				Arg.Is(expectedUrl),
				Arg.Is(expectedRequestData))
			.Returns(sysSettingValue);

		//Act
		Guid actual = sut.GetSysSettingValueByCode<Guid>(sysSettingCode);
		
		//Assert
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
		//Arrange
		const string sysSettingCode = "nonExistingCode";
		decimal sysSettingValue = decimal.Parse(value);
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.Resolve<IServiceUrlBuilder>();
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder);

		string segment = EnvironmentSettings.IsNetCore switch {
			true => "/rest/CreatioApiGateway/GetSysSettingValueByCode",
			false => "/0/rest/CreatioApiGateway/GetSysSettingValueByCode"
		};

		string expectedUrl = EnvironmentSettings.Uri + segment;
		string expectedRequestData = JsonSerializer
			.Serialize(new SysSettingsManager.GetSettingRequestData(sysSettingCode),
				new JsonSerializerOptions {
					WriteIndented = false,
					AllowTrailingCommas = false,
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				});

		applicationClient.ExecutePostRequest(
				Arg.Is(expectedUrl),
				Arg.Is(expectedRequestData))
			.Returns(sysSettingValue.ToString(CultureInfo.InvariantCulture));

		//Act
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
		//Arrange
		const string sysSettingCode = "nonExistingCode";
		CultureInfo provider = new CultureInfo("en-US");
		int sysSettingValue = (int)decimal.Parse(value, provider);

		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.Resolve<IServiceUrlBuilder>();
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder);

		string segment = EnvironmentSettings.IsNetCore switch {
			true => "/rest/CreatioApiGateway/GetSysSettingValueByCode",
			false => "/0/rest/CreatioApiGateway/GetSysSettingValueByCode"
		};

		string expectedUrl = EnvironmentSettings.Uri + segment;
		string expectedRequestData = JsonSerializer
			.Serialize(new SysSettingsManager.GetSettingRequestData(sysSettingCode),
				new JsonSerializerOptions {
					WriteIndented = false,
					AllowTrailingCommas = false,
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				});

		applicationClient.ExecutePostRequest(
				Arg.Is(expectedUrl),
				Arg.Is(expectedRequestData))
			.Returns(value);

		//Act
		int actual = sut.GetSysSettingValueByCode<int>(sysSettingCode);
		actual.Should().BeOfType(typeof(int));
		actual.Should().Be(sysSettingValue);
	}

	[TestCase("-1,230.5")]
	[TestCase("1,230.5")]
	public void GetSysSettingValueByCode_Throws(string value){
		//Arrange
		const string sysSettingCode = "nonExistingCode";
		CultureInfo provider = new CultureInfo("en-US");
		int sysSettingValue = (int)decimal.Parse(value, provider);

		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.Resolve<IServiceUrlBuilder>();
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder);

		string segment = EnvironmentSettings.IsNetCore switch {
			true => "/rest/CreatioApiGateway/GetSysSettingValueByCode",
			false => "/0/rest/CreatioApiGateway/GetSysSettingValueByCode"
		};

		string expectedUrl = EnvironmentSettings.Uri + segment;
		string expectedRequestData = JsonSerializer
			.Serialize(new SysSettingsManager.GetSettingRequestData(sysSettingCode),
				new JsonSerializerOptions {
					WriteIndented = false,
					AllowTrailingCommas = false,
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				});

		applicationClient.ExecutePostRequest(
				Arg.Is(expectedUrl),
				Arg.Is(expectedRequestData))
			.Returns(value);

		//Act
		Action act = () => sut.GetSysSettingValueByCode<int>(sysSettingCode);
		act.Should()
			.Throw<InvalidCastException>()
			.WithMessage($"Could not convert {value} to to {nameof(Int32)}");
	}


	[Test]
	public void GetSysSettingValueByCode_Returns_Value(){
		//Arrange
		const string sysSettingCode = "nonExistingCode";
		const string sysSettingValue = "123";

		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = _container.Resolve<IServiceUrlBuilder>();
		ISysSettingsManager sut = new SysSettingsManager(applicationClient, urlBuilder);

		string segment = EnvironmentSettings.IsNetCore switch {
			true => "/rest/CreatioApiGateway/GetSysSettingValueByCode",
			false => "/0/rest/CreatioApiGateway/GetSysSettingValueByCode"
		};

		string expectedUrl = EnvironmentSettings.Uri + segment;
		string expectedRequestData = JsonSerializer
			.Serialize(new SysSettingsManager.GetSettingRequestData(sysSettingCode),
				new JsonSerializerOptions {
					WriteIndented = false,
					AllowTrailingCommas = false,
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				});

		applicationClient.ExecutePostRequest(
				Arg.Is(expectedUrl),
				Arg.Is(expectedRequestData))
			.Returns(sysSettingValue);

		//Act
		string actual = sut.GetSysSettingValueByCode(sysSettingCode);

		//Assert
		actual.Should().Be(sysSettingValue);
		applicationClient.Received(1)
			.ExecutePostRequest(Arg.Is(expectedUrl), Arg.Any<string>());

		applicationClient.Received(1)
			.ExecutePostRequest(Arg.Any<string>(), Arg.Is(expectedRequestData));
	}

	#endregion
	
	#endregion
}
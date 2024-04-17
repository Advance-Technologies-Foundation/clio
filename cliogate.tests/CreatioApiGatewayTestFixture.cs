using System;
using System.Collections.Generic;
using cliogate.Files.cs;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Terrasoft.Configuration.Tests;
using Terrasoft.Core;
using Terrasoft.Core.Entities;
using Terrasoft.Core.Factories;
using Terrasoft.TestFramework;

namespace cliogate.tests
{
	[System.ComponentModel.Category("UnitTest")]
	[Author("Kirill Krylov")]
	[MockSettings(RequireMock.All)]
	public class CreatioApiGatewayTestFixture : BaseMarketplaceTestFixture
	{

		private Entity _sysSettingsEntity;
		private Guid _sysSettingId = Guid.NewGuid();
		#region Constants: Private

		private const string SysSettingCode = "SysSettingOne_Code";
		private const string SysSettingValue = "SysSettingOne_Value";

		#endregion

		
		private void MockSysSettingsEntity(string code, string valueTypeName) {
			const string schemaName = "SysSettings";
			MockEntitySchemaWithColumns(schemaName, new Dictionary<string, DataValueType> {
				{"Code", DataValueType.Text},
				{"ValueTypeName", DataValueType.Text}
			});

			SetUpTestData(schemaName, new Dictionary<string, object> {
				{"Code", code},
				{"ValueTypeName", valueTypeName}
			});
		}
		
		private void MockSysSetting(string code, object value){
			UserConnection.SettingsValues.Add(code, value);
            GlobalAppSettings.FeatureUseSysSettingsEngine = true;
            FakeSysSettings settings = new FakeSysSettings {
            	Code = code
            };
            FakeSysSettings.Setup(new[] {settings});
            FakeSysSettingsEngine engine = Substitute.For<FakeSysSettingsEngine>();
            FakeSysSettingsEngine.Setup(engine);
            engine.TryGetSettingsValue(Arg.Is(code), Arg.Any<Guid>(),
            		out object vv)
            	.Returns(x => {
            		x[2] = value;
            		return true;
            	});
		}
		
		#region Methods: Protected

		protected override void SetUp(){
			base.SetUp();
			ClassFactory.RebindWithFactoryMethod(() => (UserConnection)UserConnection);
		}

		protected override void SetupSysSettings(){
			base.SetupSysSettings();
			MockSysSetting(SysSettingCode, SysSettingValue);
		}

		#endregion

		[Test]
		public void GetSysSettingValueByCode_Returns_EmptyString_When_CodeDoesNotExist(){
			//Arrange
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSysSettings")
				.Returns(true);

			CreatioApiGateway sut = new CreatioApiGateway();
			const string sysSettingCode = "fake_code";
			MockSysSettingsEntity(sysSettingCode, "Text");
			
			//Act
			string actual = sut.GetSysSettingValueByCode(sysSettingCode);
			
			//Assert
			actual.Should().Be("");
		}

		[Test]
		public void GetSysSettingValueByCode_Returns_When_CheckCanManageSolution(){
			//Arrange
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSysSettings")
				.Returns(true);
			CreatioApiGateway sut = new CreatioApiGateway();

			MockSysSettingsEntity(SysSettingCode, "Text");
			
			//Act
			string actual = sut.GetSysSettingValueByCode(SysSettingCode);

			//Assert
			actual.Should().Be(SysSettingValue);
		}

		[Test]
		public void GetSysSettingValueByCode_Trows_When_Not_CheckCanManageSolution(){
			//Arrange
			CreatioApiGateway sut = new CreatioApiGateway();
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSysSettings")
				.Returns(false);
			//Act
			Action act = () => sut.GetSysSettingValueByCode(SysSettingCode);

			//Assert
			act.Should()
				.Throw<Exception>()
				.WithMessage("You don't have permission for operation CanManageSysSettings");
		}

		[TestCaseSource("DateTimeData")]
		public void GetSysSettingValueByCode_Returns_PrettyValue(TestDataItem testItem){
			//Arrange
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSysSettings")
				.Returns(true);
			CreatioApiGateway sut = new CreatioApiGateway();
			const string code = "SysSetting_DateTime";
			MockSysSetting(code, testItem.Value);
			MockSysSettingsEntity(code, testItem.ValueTypeName);

			//Act
			string actual = sut.GetSysSettingValueByCode(code);

			//Assert
			actual.Should().Be(testItem.Value.ToString(testItem.FormatString));
		}
		
		public static IEnumerable<TestDataItem> DateTimeData = new List<TestDataItem> {
			new TestDataItem("DateTime", "dd-MMM-yyyy HH:mm:ss"),
			new TestDataItem("Date", "dd-MMM-yyyy"),
			new TestDataItem("Time", "HH:mm:ss")
		};
	}
	public class TestDataItem
	{
		public TestDataItem(string valueTypeName, string formatString){
			Value = DateTime.Now;
			ValueTypeName = valueTypeName;
			FormatString = formatString;
		}
		public DateTime Value {get;}
		public string ValueTypeName {get;}
		public string FormatString {get;}
		
	}
}
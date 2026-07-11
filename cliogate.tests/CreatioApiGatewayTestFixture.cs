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
		
		// NOTE: UnlockPackages/LockPackages read the HttpContext-backed BaseService.UserConnection property,
		// which is unavailable in this harness, so the full method body cannot be driven here (it later throws
		// NullReferenceException from get_UserConnection). These tests target only the ArgumentNullException that
		// the null-safe log fix removes: without the fix, string.Join throws ArgumentNullException *before* the
		// UserConnection access, so the assertion fails; with the fix it is reached and passes. The null-Description
		// (NRE) logic is covered deterministically by the BuildUnlockDescription / SplitLockDescription tests below.
		[Test]
		[Description("UnlockPackages must not throw ArgumentNullException when the package list is null " +
			"(the normal 'unlock all packages by maintainer code' case).")]
		public void UnlockPackages_ShouldNotThrowArgumentNullException_WhenPackagesNull(){
			//Arrange
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")
				.Returns(true);
			MockSysSetting("Maintainer", "CustomMaintainer");
			CreatioApiGateway sut = new CreatioApiGateway();

			//Act
			Action act = () => sut.UnlockPackages(null);

			//Assert
			act.Should().NotThrow<ArgumentNullException>(
				because: "a null payload is the supported 'unlock all by maintainer' signal, logged before the null guard");
		}

		[Description("LockPackages must not throw ArgumentNullException when the package list is null " +
			"(the 'lock all packages by maintainer code' case).")]
		[Test]
		public void LockPackages_ShouldNotThrowArgumentNullException_WhenPackagesNull(){
			//Arrange
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")
				.Returns(true);
			MockSysSetting("Maintainer", "CustomMaintainer");
			CreatioApiGateway sut = new CreatioApiGateway();

			//Act
			Action act = () => sut.LockPackages(null);

			//Assert
			act.Should().NotThrow<ArgumentNullException>(
				because: "a null payload is the supported 'lock all by maintainer' signal, logged before the null guard");
		}

		[Test]
		[Description("BuildUnlockDescription treats a null Description column as empty and appends the maintainer marker instead of throwing.")]
		public void BuildUnlockDescription_ShouldAppendMarkerWithMaintainer_WhenDescriptionIsNull(){
			//Arrange
			//Act
			string actual = CreatioApiGateway.BuildUnlockDescription(null, "Vendor", "#OriginalMaintainer:");

			//Assert
			actual.Should().Be("#OriginalMaintainer:Vendor",
				because: "a null Description must be treated as empty and receive the maintainer marker, not cause an NRE");
		}

		[Test]
		[Description("BuildUnlockDescription preserves the original Description when it already carries the maintainer marker.")]
		public void BuildUnlockDescription_ShouldReturnOriginal_WhenMarkerAlreadyPresent(){
			//Arrange
			const string original = "Notes#OriginalMaintainer:Vendor";

			//Act
			string actual = CreatioApiGateway.BuildUnlockDescription(original, "Other", "#OriginalMaintainer:");

			//Assert
			actual.Should().Be(original,
				because: "an already-marked Description must be preserved untouched");
		}

		[Test]
		[Description("SplitLockDescription treats a null Description column as empty, returning a single empty segment instead of throwing.")]
		public void SplitLockDescription_ShouldReturnSingleEmptySegment_WhenDescriptionIsNull(){
			//Arrange
			//Act
			string[] actual = CreatioApiGateway.SplitLockDescription(null, "#OriginalMaintainer:");

			//Assert
			actual.Should().ContainSingle(
				because: "splitting a null (treated as empty) Description must yield one segment, not an NRE");
			actual[0].Should().BeEmpty(
				because: "the single segment of an empty Description is an empty string");
		}

		[Test]
		[Description("SplitLockDescription separates the human description from the preserved original maintainer marker.")]
		public void SplitLockDescription_ShouldSeparateDescriptionAndMaintainer_WhenMarkerPresent(){
			//Arrange
			//Act
			string[] actual = CreatioApiGateway.SplitLockDescription("Notes#OriginalMaintainer:Vendor", "#OriginalMaintainer:");

			//Assert
			actual.Should().HaveCount(2,
				because: "the marker splits the stored value into description and original maintainer");
			actual[0].Should().Be("Notes",
				because: "segment 0 is the human-readable description");
			actual[1].Should().Be("Vendor",
				because: "segment 1 is the preserved original maintainer");
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
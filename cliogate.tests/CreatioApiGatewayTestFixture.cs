using System;
using cliogate.Files.cs;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Terrasoft.Configuration.Tests;
using Terrasoft.Core;
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

		#region Methods: Protected

		protected override void SetUp(){
			base.SetUp();
			ClassFactory.RebindWithFactoryMethod(() => (UserConnection)UserConnection);
		}

		protected override void SetupSysSettings(){
			base.SetupSysSettings();
			UserConnection.SettingsValues.Add(SysSettingCode, SysSettingValue);
			GlobalAppSettings.FeatureUseSysSettingsEngine = true;
			FakeSysSettings settings = new FakeSysSettings {
				Code = SysSettingCode
			};
			FakeSysSettings.Setup(new[] {settings});
			FakeSysSettingsEngine engine = Substitute.For<FakeSysSettingsEngine>();
			FakeSysSettingsEngine.Setup(engine);
			engine.TryGetSettingsValue(Arg.Is(SysSettingCode), Arg.Any<Guid>(),
					out object vv)
				.Returns(x => {
					x[2] = SysSettingValue;
					return true;
				});
		}

		#endregion

		[Test]
		public void GetSysSettingValueByCode_Returns_EmptyString_When_CodeDoesNotExist(){
			//Arrange
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSysSettings")
				.Returns(true);

			CreatioApiGateway sut = new CreatioApiGateway();

			//Act
			string actual = sut.GetSysSettingValueByCode("fake_code");

			//Assert
			actual.Should().Be("");
		}

		[Test]
		public void GetSysSettingValueByCode_Returns_When_CheckCanManageSolution(){
			//Arrange
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSysSettings")
				.Returns(true);
			CreatioApiGateway sut = new CreatioApiGateway();

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

	}
}
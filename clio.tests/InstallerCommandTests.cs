using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using Autofac;
using Clio.Command;
using Clio.Tests.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests
{
	internal class InstallerCommandTests :BaseClioModuleTests
	{

		#region Constants: Private

		private const string RemoteArtifactServerPath = @"\\tscrm.com\dfs-ts\builds-7";

		#endregion

		#region Fields: Private

		private InstallerCommand _installerCommand;

        #endregion

        #region Constructors: Public

        public override void Setup() {
            base.Setup();
            _installerCommand = Container.Resolve<InstallerCommand>();
            
        }

        #endregion

        #region Methods: Private

        protected override MockFileSystem CreateFs(){
			return new MockFileSystem(new Dictionary<string, MockFileData> {
				{
					@"\\tscrm.com\dfs-ts\builds-7\8.1.2\8.1.2.3888\BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU\8.1.2.3888_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip",
					new MockFileData("")
				}, {
					@"\\tscrm.com\dfs-ts\builds-7\8.1.2\8.1.2.3888\SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU\8.1.2.3888_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_MSSQL_ENU.zip",
					new MockFileData("")
				}, {
					@"\\tscrm.com\dfs-ts\builds-7\8.1.2\8.1.2.3888\Studio_Softkey_ENU\8.1.2.3888_Studio_Softkey_MSSQL_ENU.zip",
					new MockFileData("")
				}, {
					@"\\tscrm.com\dfs-ts\builds-7\8.0.0\8.0.0.0000\BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU\8.0.0.0000_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip",
					new MockFileData("")
				}, {
					@"\\tscrm.com\dfs-ts\builds-7\8.0.0\8.0.0.0000\SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU\8.0.0.0000_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_MSSQL_ENU.zip",
					new MockFileData("")
				}, {
					@"\\tscrm.com\dfs-ts\builds-7\8.0.0\8.0.0.0000\Studio_Softkey_ENU\8.0.0.0000_Studio_Softkey_MSSQL_ENU.zip",
					new MockFileData("")
				}, {
					@"\\tscrm.com\dfs-ts\builds-7\8.1.3\8.1.3.3992\BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU\8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip",
					new MockFileData("")
				}, {
					@"\\tscrm.com\dfs-ts\builds-7\8.1.3\8.1.3.3992\SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU\8.1.3.3992_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_MSSQL_ENU.zip",
					new MockFileData("")
				}, {
					@"\\tscrm.com\dfs-ts\builds-7\8.1.3\8.1.3.3992\Studio_Softkey_ENU\8.1.3.3992_Studio_Softkey_MSSQL_ENU.zip",
					new MockFileData("")
				}, {
					@"\\tscrm.com\dfs-ts\builds-7\8.1.3\8.1.3.3992\BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU\8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU.zip",
					new MockFileData("")
				}, {
					@"\\tscrm.com\dfs-ts\builds-7\8.1.3\8.1.3.3992\Studio_Softkey_ENU\8.1.3.3992_Studio_Softkey_PostgreSQL_ENU.zip",
					new MockFileData("")
				}, {
					@"D:\Projects\creatio_builds\8.1.1\8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip",
					new MockFileData("")
				}, {
					@"D:\Projects\creatio_builds\8.1.1\8.1.1.1425_SalesEnterpriseNet6_Softkey_PostgreSQL_ENU.zip",
					new MockFileData("")
				}, {
					@"\\tscrm.com\dfs-ts\builds-7\8.1.3\8.1.3.3992\SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU\8.1.3.3992_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_PostgreSQL_ENU.zip",
					new MockFileData("")
				}, {
					@"\\tscrm.com\dfs-ts\builds-7\8.1.3\8.1.3.3923\SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_ENU\8.1.3.3923_SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_PostgreSQL_ENU.zip",
					new MockFileData("")
				},
			});
		}

		#endregion

		[Test]
		[Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer(){
			//Arrange
			const string product = "BankSales_BankCustomerJourney_Lending_Marketing";
			const CreatioDBType creatioDbType = CreatioDBType.MSSQL;
			const CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NETFramework;

			//Act
			string filePath = _installerCommand.GetBuildFilePathFromOptions(RemoteArtifactServerPath, product,
				creatioDbType, creatioRuntimePlatform);

			//Assert
			filePath.Should()
				.Be(
					@$"{RemoteArtifactServerPath}\8.1.3\8.1.3.3992\BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU\8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip");
		}

		[Test]
		[Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer_bcj(){
			//Arrange
			PfInstallerOptions options = new() {
				Product = "bcj"
			};
			string product = options.Product;
			const CreatioDBType creatioDbType = CreatioDBType.MSSQL;
			const CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NETFramework;

			//Act
			string filePath = _installerCommand.GetBuildFilePathFromOptions(RemoteArtifactServerPath, product,
				creatioDbType, creatioRuntimePlatform);

			//Assert
			filePath.Should()
				.Be(
					@$"{RemoteArtifactServerPath}\8.1.3\8.1.3.3992\BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU\8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip");
		}

		[Test]
		[Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer_MSSQL_NF_S(){
			//Arrange
			PfInstallerOptions options = new() {
				Product = "s"
			};
			string product = options.Product;
			CreatioDBType creatioDBType = CreatioDBType.MSSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NETFramework;

			//Act
			string filePath = _installerCommand.GetBuildFilePathFromOptions(RemoteArtifactServerPath, product,
				creatioDBType, creatioRuntimePlatform);

			//Assert
			filePath.Should()
				.Be(
					@$"{RemoteArtifactServerPath}\8.1.3\8.1.3.3992\Studio_Softkey_ENU\8.1.3.3992_Studio_Softkey_MSSQL_ENU.zip");
		}

		[Test]
		[Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer_MSSQL_NF_Studio(){
			//Arrange
			const string product = "studio";
			CreatioDBType creatioDBType = CreatioDBType.MSSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NETFramework;

			//Act
			string filePath = _installerCommand.GetBuildFilePathFromOptions(RemoteArtifactServerPath, product,
				creatioDBType, creatioRuntimePlatform);

			//Assert
			filePath.Should()
				.Be(
					@$"{RemoteArtifactServerPath}\8.1.3\8.1.3.3992\Studio_Softkey_ENU\8.1.3.3992_Studio_Softkey_MSSQL_ENU.zip");
		}

		[Test]
		[Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer_PG_NF_S(){
			//Arrange
			PfInstallerOptions options = new() {
				Product = "s"
			};
			string product = options.Product;
			CreatioDBType creatioDBType = CreatioDBType.PostgreSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NETFramework;

			//Act
			string filePath = _installerCommand.GetBuildFilePathFromOptions(RemoteArtifactServerPath, product,
				creatioDBType, creatioRuntimePlatform);

			//Assert
			filePath.Should()
				.Be(
					@$"{RemoteArtifactServerPath}\8.1.3\8.1.3.3992\Studio_Softkey_ENU\8.1.3.3992_Studio_Softkey_PostgreSQL_ENU.zip");
		}

		[Test]
		[Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer_PG_NF_S_Local(){
			//Arrange
			PfInstallerOptions options = new PfInstallerOptions {
				Product = "s"
			};
			string product = options.Product;
			CreatioDBType creatioDBType = CreatioDBType.PostgreSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NETFramework;

			string remoteArtifactServerPath = "D:\\Projects\\creatio_builds\\";
			//Act
			string filePath = _installerCommand.GetBuildFilePathFromOptions(remoteArtifactServerPath, product,
				creatioDBType, creatioRuntimePlatform);

			//Assert
			filePath.Should().Be(@"D:\Projects\creatio_builds\8.1.1\8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip");
		}

		[Test]
		[Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer_PG_NF_SE_Local(){
			PfInstallerOptions options = new() {
				Product = "SalesEnterprise"
			};
			string product = options.Product;
			CreatioDBType creatioDBType = CreatioDBType.PostgreSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NET6;
			string remoteArtifactServerPath = @"D:\Projects\creatio_builds\";
			string filePath = _installerCommand.GetBuildFilePathFromOptions(remoteArtifactServerPath, product,
				creatioDBType, creatioRuntimePlatform);
			filePath.Should()
				.Be(@"D:\Projects\creatio_builds\8.1.1\8.1.1.1425_SalesEnterpriseNet6_Softkey_PostgreSQL_ENU.zip");
		}

		[Test]
		[Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer_PG_NF_SE_M_SE(){
			//Arrange
			const string product = "SalesEnterprise_Marketing_ServiceEnterprise";
			CreatioDBType creatioDBType = CreatioDBType.PostgreSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NETFramework;

			//Act
			string filePath = _installerCommand.GetBuildFilePathFromOptions(RemoteArtifactServerPath, product,
				creatioDBType, creatioRuntimePlatform);

			//Assert
			filePath.Should()
				.Be(
					@$"{RemoteArtifactServerPath}\8.1.3\8.1.3.3992\SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU\8.1.3.3992_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_PostgreSQL_ENU.zip");
		}

		[Test]
		[Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServerNet6Studio(){
			//Arrange
			const string product = "SalesEnterprise_Marketing_ServiceEnterprise";
			const CreatioDBType creatioDBType = CreatioDBType.PostgreSQL;
			const CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NET6;

			//Act
			string filePath = _installerCommand.GetBuildFilePathFromOptions(RemoteArtifactServerPath, product,
				creatioDBType, creatioRuntimePlatform);

			//Assert
			filePath.Should()
				.Be(
					@$"{RemoteArtifactServerPath}\8.1.3\8.1.3.3923\SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_ENU\8.1.3.3923_SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_PostgreSQL_ENU.zip");
		}

		[Test]
		[Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServerNet6Studio_semse(){
			PfInstallerOptions options = new() {
				Product = "semse"
			};
			string product = options.Product;
			const CreatioDBType creatioDBType = CreatioDBType.PostgreSQL;
			const CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NET6;
			string filePath = _installerCommand.GetBuildFilePathFromOptions(RemoteArtifactServerPath, product,
				creatioDBType, creatioRuntimePlatform);
			filePath.Should()
				.Be(
					@$"{RemoteArtifactServerPath}\8.1.3\8.1.3.3923\SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_ENU\8.1.3.3923_SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_PostgreSQL_ENU.zip");
		}

		[Test]
		public void GetBuildFilePathFromOptions_Returns_Expected(){
			PfInstallerOptions options = new() {
				Product = "semse"
			};
			string product = options.Product;
			const CreatioDBType creatioDBType = CreatioDBType.PostgreSQL;
			const CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NET6;
			string filePath = _installerCommand.GetBuildFilePathFromOptions(RemoteArtifactServerPath, product,
				creatioDBType, creatioRuntimePlatform);
			filePath.Should()
				.Be(
					@$"{RemoteArtifactServerPath}\8.1.3\8.1.3.3923\SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_ENU\8.1.3.3923_SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_PostgreSQL_ENU.zip");
		}

		[Test]
		public void GetLatestVersion_Return_Version(){
			//Act
			Version actual = _installerCommand.GetLatestVersion(RemoteArtifactServerPath);

			//Assert
			actual.Should().Be(Version.Parse("8.1.3"));
		}

	}
}
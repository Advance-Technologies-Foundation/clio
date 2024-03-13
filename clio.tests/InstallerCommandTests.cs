using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ms = System.IO.Abstractions;

namespace Clio.Tests
{
	internal class InstallerCommandTests
	{
		[Test, Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer() {
			//Arrange
			InstallerCommand installerCommand = new InstallerCommand();
			const string product = "BankSales_BankCustomerJourney_Lending_Marketing";
			const CreatioDBType creatioDbType = CreatioDBType.MSSQL;
			const CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NETFramework;
			const string remoteArtifactServerPath = "\\\\tscrm.com\\dfs-ts\\builds-7";

			//Act
			string filePath = installerCommand.GetBuildFilePathFromOptions(remoteArtifactServerPath, product, creatioDbType, creatioRuntimePlatform);

			//Assert
			filePath.Should().Be("\\\\tscrm.com\\dfs-ts\\builds-7\\8.1.3\\8.1.3.3992\\BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU\\8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip");
		}

		[Ignore("Bad tests looks into actual FS")]
		[Test, Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer_bcj() {
			var installerCommand = new InstallerCommand();
			var options = new PfInstallerOptions() {
				Product = "bcj"
			};
			string product = options.Product;
			CreatioDBType creatioDBType = CreatioDBType.MSSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NETFramework;
			string remoteArtifactServerPath = "\\\\tscrm.com\\dfs-ts\\builds-7";
			var filePath = installerCommand.GetBuildFilePathFromOptions(remoteArtifactServerPath, product, creatioDBType, creatioRuntimePlatform);
			filePath.Should().Be("\\\\tscrm.com\\dfs-ts\\builds-7\\8.1.3\\8.1.3.3992\\BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU\\8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip");
		}

		[Ignore("Bad tests looks into actual FS")]
		[Test, Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServerNet6Studio() {
			var installerCommand = new InstallerCommand();
			string product = "SalesEnterprise_Marketing_ServiceEnterprise";
			CreatioDBType creatioDBType = CreatioDBType.PostgreSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NET6;
			string remoteArtifactServerPath = "\\\\tscrm.com\\dfs-ts\\builds-7";
			var filePath = installerCommand.GetBuildFilePathFromOptions(remoteArtifactServerPath, product, creatioDBType, creatioRuntimePlatform);
			filePath.Should().Be("\\\\tscrm.com\\dfs-ts\\builds-7\\8.1.3\\8.1.3.3923\\SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_ENU\\8.1.3.3923_SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_PostgreSQL_ENU.zip");
		}

		[Ignore("Bad tests looks into actual FS")]
		[Test, Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServerNet6Studio_semse() {
			var installerCommand = new InstallerCommand();
			var options = new PfInstallerOptions() {
				Product = "semse"
			};
			string product = options.Product;
			CreatioDBType creatioDBType = CreatioDBType.PostgreSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NET6;
			string remoteArtifactServerPath = "\\\\tscrm.com\\dfs-ts\\builds-7";
			var filePath = installerCommand.GetBuildFilePathFromOptions(remoteArtifactServerPath, product, creatioDBType, creatioRuntimePlatform);
			filePath.Should().Be("\\\\tscrm.com\\dfs-ts\\builds-7\\8.1.3\\8.1.3.3923\\SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_ENU\\8.1.3.3923_SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_PostgreSQL_ENU.zip");
		}

		[Ignore("Bad tests looks into actual FS")]
		[Test, Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer_PG_NF_SE_M_SE() {
			var installerCommand = new InstallerCommand();
			string product = "SalesEnterprise_Marketing_ServiceEnterprise";
			CreatioDBType creatioDBType = CreatioDBType.PostgreSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NETFramework;
			string remoteArtifactServerPath = "\\\\tscrm.com\\dfs-ts\\builds-7";
			var filePath = installerCommand.GetBuildFilePathFromOptions(remoteArtifactServerPath, product, creatioDBType, creatioRuntimePlatform);
			filePath.Should().Be("\\\\tscrm.com\\dfs-ts\\builds-7\\8.1.3\\8.1.3.3988\\SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU\\8.1.3.3988_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_PostgreSQL_ENU.zip");
		}

		[Ignore("Bad tests looks into actual FS")]
		[Test, Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer_MSSQL_NF_Studio() {
			var installerCommand = new InstallerCommand();
			string product = "studio";
			CreatioDBType creatioDBType = CreatioDBType.MSSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NETFramework;
			string remoteArtifactServerPath = "\\\\tscrm.com\\dfs-ts\\builds-7";
			var filePath = installerCommand.GetBuildFilePathFromOptions(remoteArtifactServerPath, product, creatioDBType, creatioRuntimePlatform);
			filePath.Should().Be("\\\\tscrm.com\\dfs-ts\\builds-7\\8.1.3\\8.1.3.3988\\Studio_Softkey_ENU\\8.1.3.3988_Studio_Softkey_MSSQL_ENU.zip");
		}

		[Ignore("Bad tests looks into actual FS")]
		[Test, Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer_MSSQL_NF_S() {
			var installerCommand = new InstallerCommand();
			var options = new PfInstallerOptions() {
				Product = "s"
			};
			string product = options.Product;
			CreatioDBType creatioDBType = CreatioDBType.MSSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NETFramework;
			string remoteArtifactServerPath = "\\\\tscrm.com\\dfs-ts\\builds-7";
			var filePath = installerCommand.GetBuildFilePathFromOptions(remoteArtifactServerPath, product, creatioDBType, creatioRuntimePlatform);
			filePath.Should().Be("\\\\tscrm.com\\dfs-ts\\builds-7\\8.1.3\\8.1.3.3988\\Studio_Softkey_ENU\\8.1.3.3988_Studio_Softkey_MSSQL_ENU.zip");
		}

		[Ignore("Bad tests looks into actual FS")]
		[Test, Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer_PG_NF_S() {
			var installerCommand = new InstallerCommand();
			var options = new PfInstallerOptions() {
				Product = "s"
			};
			string product = options.Product;
			CreatioDBType creatioDBType = CreatioDBType.PostgreSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NETFramework;
			string remoteArtifactServerPath = "\\\\tscrm.com\\dfs-ts\\builds-7";
			var filePath = installerCommand.GetBuildFilePathFromOptions(remoteArtifactServerPath, product, creatioDBType, creatioRuntimePlatform);
			filePath.Should().Be("\\\\tscrm.com\\dfs-ts\\builds-7\\8.1.3\\8.1.3.3881\\Studio_Softkey_ENU\\8.1.3.3881_Studio_Softkey_PostgreSQL_ENU.zip");
		}

		[Ignore("Bad tests looks into actual FS")]
		[Test, Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer_PG_NF_SE_Local() {
			var installerCommand = new InstallerCommand();
			var options = new PfInstallerOptions() {
				Product = "SalesEnterprise"
			};
			string product = options.Product;
			CreatioDBType creatioDBType = CreatioDBType.PostgreSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NET6;
			string remoteArtifactServerPath = "D:\\Projects\\creatio_builds\\";
			var filePath = installerCommand.GetBuildFilePathFromOptions(remoteArtifactServerPath, product, creatioDBType, creatioRuntimePlatform);
			filePath.Should().Be("D:\\Projects\\creatio_builds\\8.1.1\\8.1.1.1425_SalesEnterpriseNet6_Softkey_PostgreSQL_ENU.zip");
		}

		[Ignore("Bad tests looks into actual FS")]
		[Test, Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer_PG_NF_S_Local() {
			var installerCommand = new InstallerCommand();
			var options = new PfInstallerOptions() {
				Product = "s"
			};
			string product = options.Product;
			CreatioDBType creatioDBType = CreatioDBType.PostgreSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NETFramework;
			string remoteArtifactServerPath = "D:\\Projects\\creatio_builds\\";
			var filePath = installerCommand.GetBuildFilePathFromOptions(remoteArtifactServerPath, product, creatioDBType, creatioRuntimePlatform);
			filePath.Should().Be("D:\\Projects\\creatio_builds\\8.1.1\\8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip");
		}

		[Test]
		public async Task GetLatestVersion_Return_Version(){
			
			
			const string remoteArtifactServerPath = "\\\\tscrm.com\\dfs-ts\\builds-7";
			var mfd = new Dictionary<string, MockFileData>();
			Ms.IFileSystem fs = new MockFileSystem(mfd);
			
			
			
			
			FileSystem ffs = new FileSystem(fs);
			
			var installerCommand = new InstallerCommand(null, null, null, null, null, ffs);
			
			fs.Directory.CreateDirectory(remoteArtifactServerPath);
			
			var result = installerCommand.GetLatestVersion(remoteArtifactServerPath);
			
		}
		
	}
}

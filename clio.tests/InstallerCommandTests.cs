using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clio.Tests
{
	internal class InstallerCommandTests
	{
		[Test, Category("Unit")]
		public void FindZipFilePathFromOptionsRemoteServer() {
			var installerCommand = new InstallerCommand();
			string product = "BankSales_BankCustomerJourney_Lending_Marketing";
			CreatioDBType creatioDBType = CreatioDBType.MSSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NETFramework;
			string remoteArtifactServerPath = "\\\\tscrm.com\\dfs-ts\\builds-7";
			var filePath = installerCommand.GetBuildFilePathFromOptions(remoteArtifactServerPath, product, creatioDBType, creatioRuntimePlatform);
			filePath.Should().Be("\\\\tscrm.com\\dfs-ts\\builds-7\\8.1.3\\8.1.3.3992\\BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU\\8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip");
		}

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
	}
}

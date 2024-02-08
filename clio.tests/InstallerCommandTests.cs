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
		public void FindZipFilePathFromOptionsRemoteServerNet6Studio() {
			var installerCommand = new InstallerCommand();
			string product = "Studio";
			CreatioDBType creatioDBType = CreatioDBType.MSSQL;
			CreatioRuntimePlatform creatioRuntimePlatform = CreatioRuntimePlatform.NET6;
			string remoteArtifactServerPath = "\\\\tscrm.com\\dfs-ts\\builds-7";
			var filePath = installerCommand.GetBuildFilePathFromOptions(remoteArtifactServerPath, product, creatioDBType, creatioRuntimePlatform);
			filePath.Should().Be("\\\\tscrm.com\\dfs-ts\\builds-7\\8.1.3\\8.1.3.3992\\BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU\\8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip");
		}
	}
}

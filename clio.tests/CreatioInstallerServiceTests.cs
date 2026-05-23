using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.Common.K8;
using Clio.Tests.Command;
using FluentAssertions;
using k8s;
using NSubstitute;
using NSubstitute.Core;
using NUnit.Framework;

namespace Clio.Tests;

[Property("Module", "Core")]
internal class CreatioInstallerServiceTests : BaseClioModuleTests{
	#region Fields: Private

	private readonly string _localArtifactServerPath = Environment.OSVersion.Platform == PlatformID.Win32NT
		? @"D:\Projects\creatio_builds"
		: "/usr/usrA/creatio_builds";

	private readonly string _remoteArtifactServerPath = Environment.OSVersion.Platform == PlatformID.Win32NT
		? @"\\tscrm.com\dfs-ts\builds-7"
		: "/mnt/tscrm.com/dfs-ts/builds-7";

	private CreatioInstallerService _creatioInstallerService;
	private IProcessExecutor _processExecutor;

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		IKubernetes kuber = Substitute.For<IKubernetes>();
		containerBuilder.AddSingleton(kuber);

		Ik8Commands k8Commands = Substitute.For<Ik8Commands>();
		containerBuilder.AddSingleton(k8Commands);

		_processExecutor = Substitute.For<IProcessExecutor>();
		containerBuilder.AddSingleton(_processExecutor);
	}

	protected override MockFileSystem CreateFs() {
		return new MockFileSystem(new Dictionary<string, MockFileData> {
			{
				Path.Combine(_remoteArtifactServerPath, "8.1.2", "8.1.2.3888",
					"BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU",
					"8.1.2.3888_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip"),
				new MockFileData("")
			}, {
				Path.Combine(_remoteArtifactServerPath, "8.1.2", "8.1.2.3888",
					"SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU",
					"8.1.2.3888_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_MSSQL_ENU.zip"),
				new MockFileData("")
			}, {
				Path.Combine(_remoteArtifactServerPath, "8.1.2", "8.1.2.3888", "Studio_Softkey_ENU",
					"8.1.2.3888_Studio_Softkey_MSSQL_ENU.zip"),
				new MockFileData("")
			}, {
				Path.Combine(_remoteArtifactServerPath, "8.0.0", "8.0.0.0000",
					"BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU",
					"8.0.0.0000_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip"),
				new MockFileData("")
			}, {
				Path.Combine(_remoteArtifactServerPath, "8.0.0", "8.0.0.0000",
					"SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU",
					"8.0.0.0000_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_MSSQL_ENU.zip"),
				new MockFileData("")
			}, {
				Path.Combine(_remoteArtifactServerPath, "8.0.0", "8.0.0.0000", "Studio_Softkey_ENU",
					"8.0.0.0000_Studio_Softkey_MSSQL_ENU.zip"),
				new MockFileData("")
			}, {
				Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992",
					"BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU",
					"8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip"),
				new MockFileData("")
			}, {
				Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992",
					"SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU",
					"8.1.3.3992_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_MSSQL_ENU.zip"),
				new MockFileData("")
			}, {
				Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992", "Studio_Softkey_ENU",
					"8.1.3.3992_Studio_Softkey_MSSQL_ENU.zip"),
				new MockFileData("")
			}, {
				Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992",
					"BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU",
					"8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU.zip"),
				new MockFileData("")
			}, {
				Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992", "Studio_Softkey_ENU",
					"8.1.3.3992_Studio_Softkey_PostgreSQL_ENU.zip"),
				new MockFileData("")
			}, {
				Path.Combine(_localArtifactServerPath, "8.1.1", "8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip"),
				new MockFileData("")
			}, {
				Path.Combine(_localArtifactServerPath, "8.1.1",
					"8.1.1.1425_SalesEnterpriseNet6_Softkey_PostgreSQL_ENU.zip"),
				new MockFileData("")
			}, {
				Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992",
					"SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU",
					"8.1.3.3992_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_PostgreSQL_ENU.zip"),
				new MockFileData("")
			}, {
				Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3923",
					"SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_ENU",
					"8.1.3.3923_SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_PostgreSQL_ENU.zip"),
				new MockFileData("")
			}
		});
	}

	#endregion

	#region Methods: Public

	[Test]
	[Category("Unit")]
	public void FindZipFilePathFromOptionsRemoteServer() {
		//Arrange
		const string product = "BankSales_BankCustomerJourney_Lending_Marketing";

		//Act
		string filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath, product,
			CreatioDBType.MSSQL, CreatioRuntimePlatform.NETFramework);

		string expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992",
			"BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU",
			"8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip");

		//Assert
		filePath.Should().Be(expected,
			"because the method should construct the correct path for the specified product, database type, and runtime platform.");
	}

	[Test]
	[Category("Unit")]
	[Description("Should construct the correct path for the specified product, database type, and runtime platform.")]
	public void FindZipFilePathFromOptionsRemoteServer_bcj() {
		//Arrange
		PfInstallerOptions options = new() {
			Product = "bcj"
		};

		//Act
		string filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath,
			options.Product,
			CreatioDBType.MSSQL, CreatioRuntimePlatform.NETFramework);

		string expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992",
			"BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU",
			"8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip");

		//Assert
		filePath.Should().Be(expected,
			"because the method should construct the correct path for the specified product, database type, and runtime platform.");
	}

	[Test]
	[Category("Unit")]
	[Description("Should construct the correct path for Studio product, MSSQL, .NET Framework.")]
	public void FindZipFilePathFromOptionsRemoteServer_MSSQL_NF_S() {
		//Arrange
		PfInstallerOptions options = new() {
			Product = "s"
		};

		//Act
		string filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath,
			options.Product,
			CreatioDBType.MSSQL, CreatioRuntimePlatform.NETFramework);

		string expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992", "Studio_Softkey_ENU",
			"8.1.3.3992_Studio_Softkey_MSSQL_ENU.zip");

		//Assert
		filePath.Should()
				.Be(expected,
					"because the method should construct the correct path for Studio product, MSSQL, .NET Framework.");
	}

	[Test]
	[Category("Unit")]
	public void FindZipFilePathFromOptionsRemoteServer_MSSQL_NF_Studio() {
		//Arrange
		const string product = "studio";

		//Act
		string filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath, product,
			CreatioDBType.MSSQL, CreatioRuntimePlatform.NETFramework);

		string expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992", "Studio_Softkey_ENU",
			"8.1.3.3992_Studio_Softkey_MSSQL_ENU.zip");

		//Assert
		filePath.Should().Be(expected,
			"because the method should construct the correct path for Studio product, MSSQL, .NET Framework.");
	}

	[Test]
	[Category("Unit")]
	public void FindZipFilePathFromOptionsRemoteServer_PG_NF_S() {
		//Arrange
		PfInstallerOptions options = new() {
			Product = "s"
		};

		//Act
		string filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath,
			options.Product,
			CreatioDBType.PostgreSQL, CreatioRuntimePlatform.NETFramework);

		string expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992", "Studio_Softkey_ENU",
			"8.1.3.3992_Studio_Softkey_PostgreSQL_ENU.zip");

		//Assert
		filePath.Should().Be(expected,
			"because the method should construct the correct path for PostgreSQL, .NET Framework, Studio product from remote artifact server.");
	}

	[Test]
	[Category("Unit")]
	[Description(
		"Should return correct zip file path for PostgreSQL, .NET Framework, Studio product from local artifact server.")]
	public void FindZipFilePathFromOptionsRemoteServer_PG_NF_S_Local() {
		//Arrange
		PfInstallerOptions options = new() {
			Product = "s"
		};

		//Act
		string filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_localArtifactServerPath,
			options.Product,
			CreatioDBType.PostgreSQL, CreatioRuntimePlatform.NETFramework);

		string expected = Path.Combine(_localArtifactServerPath, "8.1.1",
			"8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip");

		//Assert
		filePath.Should().Be(expected,
			"because the method should construct the correct path for PostgreSQL, .NET Framework, Studio product from local artifact server");
	}

	[Test]
	[Category("Unit")]
	public void FindZipFilePathFromOptionsRemoteServer_PG_NF_SE_Local() {
		//Arrange
		PfInstallerOptions options = new() {
			Product = "SalesEnterprise"
		};

		string filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_localArtifactServerPath,
			options.Product,
			CreatioDBType.PostgreSQL, CreatioRuntimePlatform.NET6);

		//Assert
		string expectedPath = Path.Combine(_localArtifactServerPath, "8.1.1",
			"8.1.1.1425_SalesEnterpriseNet6_Softkey_PostgreSQL_ENU.zip");


		filePath.Should().Be(expectedPath,
			"because the method should construct the correct path for SalesEnterprise product, PostgreSQL, .NET 6 from local artifact server.");
	}

	[Test]
	[Category("Unit")]
	public void FindZipFilePathFromOptionsRemoteServer_PG_NF_SE_M_SE() {
		//Arrange
		const string product = "SalesEnterprise_Marketing_ServiceEnterprise";

		//Act
		string filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath, product,
			CreatioDBType.PostgreSQL, CreatioRuntimePlatform.NETFramework);

		string expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992",
			"SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU",
			"8.1.3.3992_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_PostgreSQL_ENU.zip");

		//Assert
		filePath.Should()
				.Be(expected,
					"because the method should construct the correct path for SalesEnterprise_Marketing_ServiceEnterprise product, PostgreSQL, .NET Framework.");
	}

	[Test]
	[Category("Unit")]
	public void FindZipFilePathFromOptionsRemoteServerNet6Studio() {
		//Arrange
		const string product = "SalesEnterprise_Marketing_ServiceEnterprise";

		//Act
		string filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath, product,
			CreatioDBType.PostgreSQL, CreatioRuntimePlatform.NET6);

		string expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3923",
			"SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_ENU",
			"8.1.3.3923_SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_PostgreSQL_ENU.zip");

		//Assert
		filePath.Should().Be(expected,
			"because the method should construct the correct path for SalesEnterprise_Marketing_ServiceEnterpriseNet6 product, PostgreSQL, .NET 6.");
	}

	[Test]
	[Category("Unit")]
	public void FindZipFilePathFromOptionsRemoteServerNet6Studio_semse() {
		//Arrange
		PfInstallerOptions options = new() {
			Product = "semse"
		};

		//Act
		string filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath,
			options.Product,
			CreatioDBType.PostgreSQL, CreatioRuntimePlatform.NET6);

		//Assert
		string expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3923",
			"SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_ENU",
			"8.1.3.3923_SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_PostgreSQL_ENU.zip");
		filePath.Should().Be(expected,
			"because the method should construct the correct path for semse product, PostgreSQL, .NET 6.");
	}

	[Test]
	public void GetBuildFilePathFromOptions_Returns_Expected() {
		//Arrange
		PfInstallerOptions options = new() {
			Product = "semse"
		};

		//Act
		string filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath,
			options.Product,
			CreatioDBType.PostgreSQL, CreatioRuntimePlatform.NET6);

		//Assert
		string expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3923",
			"SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_ENU",
			"8.1.3.3923_SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_PostgreSQL_ENU.zip");
		filePath.Should().Be(expected,
			"because the method should construct the correct path for semse product, PostgreSQL, .NET 6.");
	}

	[Test]
	public void GetLatestVersion_Return_Version() {
		//Act
		Version actual = _creatioInstallerService.GetLatestVersion(_remoteArtifactServerPath);

		//Assert
		actual.Should().Be(Version.Parse("8.1.3"), "because the latest version in the mock file system is 8.1.3.");
	}

	[Test]
	[Category("Unit")]
	[Description("Should launch browser via IProcessExecutor for non-IIS deployment using localhost URL.")]
	public void StartWebBrowser_UsesProcessExecutor_ForNonIisDeployment() {
		// Arrange
		PfInstallerOptions options = new() {
			SitePort = 8091
		};
		string expectedProgram = GetExpectedBrowserProgram();

		// Act
		int result = _creatioInstallerService.StartWebBrowser(options, false);

		// Assert
		result.Should().Be(0, "because browser start should succeed when process execution is delegated");
		int executeCalls = _processExecutor.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IProcessExecutor.Execute));
		executeCalls.Should().Be(1, "because browser launch should invoke the process executor exactly once");
		ICall executeCall = _processExecutor.ReceivedCalls()
			.Single(call => call.GetMethodInfo().Name == nameof(IProcessExecutor.Execute));
		object[] arguments = executeCall.GetArguments();
		arguments[0].Should().Be(expectedProgram, "because OS-specific browser launcher command should be selected");
		arguments[1].Should().BeOfType<string>("because process arguments should contain the URL to open");
		((string)arguments[1]).Should().Contain("localhost:8091", "because non-IIS browser URL should use localhost");
		arguments[2].Should().Be(false, "because browser launch should be fire-and-forget");
	}

	[Test]
	[Category("Unit")]
	[Description("Should launch browser via IProcessExecutor for IIS deployment using site port in URL.")]
	public void StartWebBrowser_UsesProcessExecutor_ForIisDeployment() {
		// Arrange
		PfInstallerOptions options = new() {
			SitePort = 8092
		};
		string expectedProgram = GetExpectedBrowserProgram();

		// Act
		int result = _creatioInstallerService.StartWebBrowser(options, true);

		// Assert
		result.Should().Be(0, "because browser start should succeed when process execution is delegated");
		int executeCalls = _processExecutor.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IProcessExecutor.Execute));
		executeCalls.Should().Be(1, "because browser launch should invoke the process executor exactly once");
		ICall executeCall = _processExecutor.ReceivedCalls()
			.Single(call => call.GetMethodInfo().Name == nameof(IProcessExecutor.Execute));
		object[] arguments = executeCall.GetArguments();
		arguments[0].Should().Be(expectedProgram, "because OS-specific browser launcher command should be selected");
		arguments[1].Should().BeOfType<string>("because process arguments should contain the URL to open");
		((string)arguments[1]).Should().Contain(":8092", "because IIS browser URL should include the configured port");
		arguments[2].Should().Be(false, "because browser launch should be fire-and-forget");
	}

	private static string GetExpectedBrowserProgram() {
		if (OperatingSystem.IsWindows()) {
			return "cmd";
		}

		if (OperatingSystem.IsLinux()) {
			return "xdg-open";
		}

		if (OperatingSystem.IsMacOS()) {
			return "open";
		}

		return string.Empty;
	}

	public override void Setup() {
		base.Setup();
		_creatioInstallerService = Container.GetRequiredService<CreatioInstallerService>();
	}

	#endregion
}

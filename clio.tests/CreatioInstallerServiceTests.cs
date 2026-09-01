using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.Common.IIS;
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
	[Test]
	[Description("Readiness timeout throws so deploy cannot emit a successful terminal outcome.")]
	public void ThrowIfServerNotReady_ShouldThrow_WhenReadinessTimesOut() {
		// Arrange
		const bool isReady = false;

		// Act
		Action act = () => CreatioInstallerService.ThrowIfServerNotReady(isReady);

		// Assert
		act.Should().Throw<TimeoutException>(
			because: "a readiness timeout must fail the wait-ready stage and the deployment run");
	}

	#region Fields: Private

	private readonly string _localArtifactServerPath = Environment.OSVersion.Platform == PlatformID.Win32NT
		? @"D:\Projects\creatio_builds"
		: "/usr/usrA/creatio_builds";

	private readonly string _remoteArtifactServerPath = Environment.OSVersion.Platform == PlatformID.Win32NT
		? @"\\tscrm.com\dfs-ts\builds-7"
		: "/mnt/tscrm.com/dfs-ts/builds-7";

	private CreatioInstallerService _creatioInstallerService;
	private IProcessExecutor _processExecutor;
	private IIisDeploymentPortReservation _iisDeploymentPortReservation;
	private IDeploymentTargetReservation _deploymentTargetReservation;
	private ITcpPortReservationReader _tcpPortReservationReader;

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
		_iisDeploymentPortReservation = Substitute.For<IIisDeploymentPortReservation>();
		containerBuilder.AddSingleton(_iisDeploymentPortReservation);
		_deploymentTargetReservation = Substitute.For<IDeploymentTargetReservation>();
		containerBuilder.AddSingleton(_deploymentTargetReservation);
		_tcpPortReservationReader = Substitute.For<ITcpPortReservationReader>();
		_tcpPortReservationReader.GetReservedPorts(Arg.Any<int>(), Arg.Any<int>()).Returns([]);
		containerBuilder.AddSingleton(_tcpPortReservationReader);
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
	[Description("IIS deployment rejects a reserved port before unzip, file copy, or database restore can mutate the target.")]
	public void Execute_ShouldFailBeforeMutation_WhenIisPortCannotBeReserved() {
		// Arrange
		const int port = 40187;
		string zipPath = Path.Combine(_localArtifactServerPath, "8.1.1",
			"8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip");
		_iisDeploymentPortReservation.Acquire(port).Returns(_ => throw new InvalidOperationException(
			"IIS port 40187 is not available."));
		PfInstallerOptions options = new() {
			SiteName = "collision-probe",
			SitePort = port,
			ZipFile = zipPath,
			DeploymentMethod = "iis",
			AutoRun = false
		};

		// Act
		Action act = () => _creatioInstallerService.Execute(options);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*not available*",
				because: "the machine-scoped port reservation is the first deployment mutation boundary");
		_iisDeploymentPortReservation.Received(1).Acquire(port);
		_iisDeploymentPortReservation.DidNotReceive().AcquireFirstAvailable(Arg.Any<int>(), Arg.Any<int>());
		_deploymentTargetReservation.Received(1).Acquire(Arg.Is<string>(path =>
			Path.IsPathFullyQualified(path) && path.EndsWith("collision-probe", StringComparison.Ordinal)));
	}

	[Test]
	[Category("Unit")]
	[Description("IIS deployment uses the configured range when no explicit or fixed site port is present and fails before target mutation when the range is full.")]
	public void Execute_ShouldUseConfiguredRangeAndFailBeforeMutation_WhenNoPortCanBeReserved() {
		// Arrange
		const int rangeStart = 40100;
		const int rangeEnd = 40199;
		string zipPath = Path.Combine(_localArtifactServerPath, "8.1.1",
			"8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip");
		_iisDeploymentPortReservation.AcquireFirstAvailable(rangeStart, rangeEnd).Returns(_ =>
			throw new InvalidOperationException("No available IIS port in [40100, 40199]."));
		PfInstallerOptions options = new() {
			SiteName = "automatic-port-probe",
			SitePortRange = [rangeStart, rangeEnd],
			ZipFile = zipPath,
			DeploymentMethod = "iis",
			AutoRun = false,
			IsSilent = true
		};

		// Act
		Action act = () => _creatioInstallerService.Execute(options);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*[40100, 40199]*",
			because: "an exhausted configured range must be reported without prompting or falling back");
		_iisDeploymentPortReservation.Received(1).AcquireFirstAvailable(rangeStart, rangeEnd);
		_iisDeploymentPortReservation.DidNotReceive().Acquire(Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("IIS deployment rejects an invalid configured site-port range before acquiring target or port reservations.")]
	public void Execute_ShouldRejectInvalidConfiguredRange_BeforeReservations() {
		// Arrange
		PfInstallerOptions options = new() {
			SiteName = "invalid-range-probe",
			SitePortRange = [40199, 40100],
			ZipFile = Path.Combine(_localArtifactServerPath, "8.1.1",
				"8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip"),
			DeploymentMethod = "iis",
			AutoRun = false,
			IsSilent = true
		};

		// Act
		Action act = () => _creatioInstallerService.Execute(options);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*1 <= start <= end <= 65535*",
			because: "invalid range configuration must fail before deployment can mutate the target");
		_deploymentTargetReservation.DidNotReceive().Acquire(Arg.Any<string>());
		_iisDeploymentPortReservation.DidNotReceive().AcquireFirstAvailable(Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("IIS deployment rejects an explicitly configured empty site-port range instead of treating it as absent.")]
	public void Execute_ShouldRejectEmptyConfiguredRange_BeforeReservations() {
		// Arrange
		PfInstallerOptions options = new() {
			SiteName = "empty-range-probe",
			SitePortRange = [],
			ZipFile = Path.Combine(_localArtifactServerPath, "8.1.1",
				"8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip"),
			DeploymentMethod = "iis",
			AutoRun = false,
			IsSilent = true
		};

		// Act
		Action act = () => _creatioInstallerService.Execute(options);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*exactly two ports*",
			because: "an empty configured range is invalid rather than an instruction to prompt or restore defaults");
		_deploymentTargetReservation.DidNotReceive().Acquire(Arg.Any<string>());
		_iisDeploymentPortReservation.DidNotReceive().AcquireFirstAvailable(Arg.Any<int>(), Arg.Any<int>());
	}

	[TestCase(0)]
	[TestCase(65536)]
	[Category("Unit")]
	[Description("IIS deployment rejects an invalid explicitly supplied site port instead of falling back to automatic selection.")]
	public void Execute_ShouldRejectInvalidExplicitSitePort_BeforeReservations(int sitePort) {
		// Arrange
		PfInstallerOptions options = new() {
			SiteName = "invalid-explicit-port-probe",
			SitePort = sitePort,
			SitePortRange = [40100, 40199],
			ZipFile = Path.Combine(_localArtifactServerPath, "8.1.1",
				"8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip"),
			DeploymentMethod = "iis",
			AutoRun = false,
			IsSilent = true
		};

		// Act
		Action act = () => _creatioInstallerService.Execute(options);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*Invalid explicit site port*",
			because: "an explicit override must be validated exactly and never turn into omission");
		_deploymentTargetReservation.DidNotReceive().Acquire(Arg.Any<string>());
		_iisDeploymentPortReservation.DidNotReceive().AcquireFirstAvailable(Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Silent IIS deployment fails fast when neither a fixed port nor a configured range is available.")]
	public void Execute_ShouldFailFast_WhenSilentIisDeploymentHasNoPortConfiguration() {
		// Arrange
		PfInstallerOptions options = new() {
			SiteName = "missing-port-config-probe",
			ZipFile = Path.Combine(_localArtifactServerPath, "8.1.1",
				"8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip"),
			DeploymentMethod = "iis",
			AutoRun = false,
			IsSilent = true
		};

		// Act
		Action act = () => _creatioInstallerService.Execute(options);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*requires --site-port*site-port-range*",
			because: "silent and MCP invocations cannot answer an interactive port prompt");
		_deploymentTargetReservation.DidNotReceive().Acquire(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Silent DotNet deployment never reads console input when its default port is occupied.")]
	public void Execute_ShouldFailFast_WhenSilentDotNetDefaultPortIsOccupied() {
		// Arrange
		_tcpPortReservationReader.GetReservedPorts(8080, 8080).Returns([8080]);
		PfInstallerOptions options = new() {
			SiteName = "silent-dotnet-port-probe",
			ZipFile = Path.Combine(_localArtifactServerPath, "8.1.1",
				"8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip"),
			DeploymentMethod = "dotnet",
			AutoRun = false,
			IsSilent = true
		};

		// Act
		Action act = () => _creatioInstallerService.Execute(options);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*8080*not available*--site-port*",
			because: "silent and MCP invocations must fail instead of consuming console or JSON-RPC input");
		_deploymentTargetReservation.DidNotReceive().Acquire(Arg.Any<string>());
		_tcpPortReservationReader.Received(1).GetReservedPorts(8080, 8080);
	}

	[Test]
	[Category("Unit")]
	[Description("Deployment target reservation is acquired before the IIS port or any target mutation.")]
	public void Execute_ShouldFailBeforePortReservation_WhenDeploymentTargetIsAlreadyReserved() {
		// Arrange
		const int port = 40188;
		string zipPath = Path.Combine(_localArtifactServerPath, "8.1.1",
			"8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip");
		_deploymentTargetReservation.Acquire(Arg.Any<string>()).Returns(_ =>
			throw new InvalidOperationException("deployment target is already being changed"));
		PfInstallerOptions options = new() {
			SiteName = "target-collision-probe",
			SitePort = port,
			ZipFile = zipPath,
			DeploymentMethod = "iis",
			AutoRun = false
		};

		// Act
		Action act = () => _creatioInstallerService.Execute(options);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*already being changed*",
			because: "same-path deploys and deploy-versus-uninstall must serialize before mutation");
		_iisDeploymentPortReservation.DidNotReceive().Acquire(Arg.Any<int>());
	}

	[TestCase("../escape")]
	[TestCase("bad/name")]
	[TestCase("bad\\name")]
	[TestCase("\"bad\"")]
	[TestCase("work.")]
	[TestCase("CON")]
	[Category("Unit")]
	[Description("IIS deployment rejects site names that can escape or corrupt the configured IIS target.")]
	public void Execute_ShouldRejectUnsafeIisSiteName_BeforeTargetMutation(string siteName) {
		// Arrange
		PfInstallerOptions options = new() {
			SiteName = siteName,
			SitePort = 40189,
			ZipFile = Path.Combine(_localArtifactServerPath, "8.1.1",
				"8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip"),
			DeploymentMethod = "iis",
			AutoRun = false
		};

		// Act
		Action act = () => _creatioInstallerService.Execute(options);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*single safe name*",
			because: "siteName must never become a rooted or parent-relative deployment directory");
		_deploymentTargetReservation.DidNotReceive().Acquire(Arg.Any<string>());
		_iisDeploymentPortReservation.DidNotReceive().Acquire(Arg.Any<int>());
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

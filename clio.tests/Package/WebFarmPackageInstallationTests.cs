namespace Clio.Tests.Package;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Clio.Common;
using Clio.Package;
using Clio.WebApplication;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

/// <summary>
/// Installs a real package archive through a real <see cref="CreatioClientAdapter"/> against
/// <see cref="WebFarmGatewayStub"/>, which routes the way the Control Plane runtime gateway does.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
[Property("Module", "Package")]
internal sealed class WebFarmPackageInstallationTests {

	#region Constants: Private

	private const string PackageFileName = "UsrWebFarmPackage.gz";

	// Larger than one upload chunk (1 MiB), so the archive travels in two requests like cliogate_netcore.gz.
	private const int PackagePayloadBytes = 1_536_000;

	#endregion

	#region Fields: Private

	private readonly List<CreatioClientAdapter> _adapters = [];
	private string _workingDirectory;
	private bool _originalFailOnError;

	#endregion

	#region Methods: Private

	private string CreatePackageArchive() {
		byte[] payload = new byte[PackagePayloadBytes];
		new Random(102757).NextBytes(payload);
		string path = Path.Combine(_workingDirectory, PackageFileName);
		using FileStream file = File.Create(path);
		using GZipStream gzip = new(file, CompressionLevel.NoCompression);
		gzip.Write(payload, 0, payload.Length);
		return path;
	}

	private PackageInstaller CreateInstaller(EnvironmentSettings environmentSettings) {
		IApplicationClientFactory clientFactory = Substitute.For<IApplicationClientFactory>();
		clientFactory.CreateClient(Arg.Any<EnvironmentSettings>()).Returns(_ => {
			CreatioClientAdapter adapter = new(environmentSettings.Uri, environmentSettings.Login,
				environmentSettings.Password, environmentSettings.IsNetCore);
			_adapters.Add(adapter);
			return adapter;
		});
		ServiceUrlBuilder serviceUrlBuilder = new(environmentSettings);
		ILogger logger = Substitute.For<ILogger>();
		Application application = new(environmentSettings, Substitute.For<IApplicationClient>(), serviceUrlBuilder,
			new System.IO.Abstractions.FileSystem(), logger);
		return new PackageInstaller(new ApplicationLogProvider(serviceUrlBuilder), environmentSettings,
			clientFactory, application, Substitute.For<IPackageArchiver>(), Substitute.For<ISqlScriptExecutor>(),
			serviceUrlBuilder, new Clio.Common.FileSystem(new System.IO.Abstractions.FileSystem()), logger,
			Substitute.For<IPackageLockManager>(), new DesignNodeSessionGuard(serviceUrlBuilder, logger));
	}

	private static EnvironmentSettings CreateEnvironment(WebFarmGatewayStub gateway) => new() {
		Uri = gateway.Url,
		Login = "Supervisor",
		Password = "Supervisor",
		IsNetCore = true
	};

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_originalFailOnError = GlobalContext.FailOnError;
		GlobalContext.FailOnError = false;
		_workingDirectory = Path.Combine(Path.GetTempPath(), $"clio-webfarm-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_workingDirectory);
	}

	[TearDown]
	public void TearDown() {
		foreach (CreatioClientAdapter adapter in _adapters) {
			adapter.Dispose();
		}
		_adapters.Clear();
		GlobalContext.FailOnError = _originalFailOnError;
		Directory.Delete(_workingDirectory, recursive: true);
	}

	[Test]
	[Description("ENG-102757: a multi-chunk package installed through the shared URL of a web farm must land on the design pod, chunks and installation alike.")]
	public void Install_ShouldInstallOnDesignPod_WhenEnvironmentIsAWebFarmBehindTheStickySessionGateway() {
		// Arrange
		using WebFarmGatewayStub gateway = new(WebFarmGatewayMode.WebFarm);
		EnvironmentSettings environmentSettings = CreateEnvironment(gateway);
		string packagePath = CreatePackageArchive();
		PackageInstaller installer = CreateInstaller(environmentSettings);

		// Act
		bool result = installer.Install(packagePath, environmentSettings);

		// Assert
		result.Should().BeTrue(
			because: $"the archive must arrive whole on the pod that installs it; gateway saw: {string.Join("; ", gateway.Requests)}");
		gateway.PodsServing("/UploadPackage").Should().Equal([WebFarmGatewayStub.DesignPod],
			because: "every upload chunk must reach the design pod, which owns PackageInstallation");
		gateway.PodsServing("/InstallPackage").Should().Equal([WebFarmGatewayStub.DesignPod],
			because: "the installation must run on the pod that received the archive");
		gateway.PodsServing("/CreateBackup").Should().Equal([WebFarmGatewayStub.DesignPod],
			because: "the backup belongs to the same installation session");
		gateway.PodsServing("/GetLogFile").Should().Equal([WebFarmGatewayStub.DesignPod],
			because: "the installation log is kept per pod, so it must be read from the pod that installs");
		gateway.PodsServing("/RestartApp").Should().Equal([WebFarmGatewayStub.DesignPod],
			because: "the restart must reach the pod that installed the package, not a common pod");
		gateway.LoginCount.Should().Be(1,
			because: "one installation runs in one session instead of logging in again for every step");
	}

	[Test]
	[Description("ENG-102757: when the session holds StickySession but the gateway still does not reach the design pod, the installation stops before anything is uploaded.")]
	public void Install_ShouldStopBeforeUpload_WhenStickySessionDoesNotReachTheDesignPod() {
		// Arrange
		using WebFarmGatewayStub gateway = new(WebFarmGatewayMode.WebFarmWithoutStickyRoute);
		EnvironmentSettings environmentSettings = CreateEnvironment(gateway);
		string packagePath = CreatePackageArchive();
		PackageInstaller installer = CreateInstaller(environmentSettings);

		// Act
		Action act = () => installer.Install(packagePath, environmentSettings);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "only the design pod may change, so an installation that would land on a common pod is refused")
			.WithMessage("*does not reach the design node*HTTP 503*");
		gateway.PodsServing("/UploadPackage").Should().BeEmpty(
			because: "nothing may be uploaded once the session is known not to reach the design pod");
	}

	[Test]
	[Description("ENG-102757: a single node outside a web farm never issues StickySession, so the installation proceeds without the design-node probe.")]
	public void Install_ShouldInstallWithoutDesignProbe_WhenEnvironmentIsASingleNode() {
		// Arrange
		using WebFarmGatewayStub gateway = new(WebFarmGatewayMode.SingleNode);
		EnvironmentSettings environmentSettings = CreateEnvironment(gateway);
		string packagePath = CreatePackageArchive();
		PackageInstaller installer = CreateInstaller(environmentSettings);

		// Act
		bool result = installer.Install(packagePath, environmentSettings);

		// Assert
		result.Should().BeTrue(
			because: $"a single node behaves as before the fix; gateway saw: {string.Join("; ", gateway.Requests)}");
		gateway.PodsServing("/DesignReady").Should().BeEmpty(
			because: "the design-node probe answers 503 outside a web farm and must not be consulted there");
		gateway.LoginCount.Should().Be(1,
			because: "the single session is reused on a single node as well");
	}

	#endregion

}

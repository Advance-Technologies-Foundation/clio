namespace Clio.Tests.Package;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
			Substitute.For<IPackageLockManager>(), CreateGuard(serviceUrlBuilder, logger));
	}

	// The wait for an unavailable design node runs on a fake clock: each pause advances it instead of sleeping.
	private static DesignNodeSessionGuard CreateGuard(IServiceUrlBuilder serviceUrlBuilder, ILogger logger) {
		ManualTimeProvider clock = new();
		return new DesignNodeSessionGuard(serviceUrlBuilder, logger, clock, new ClockAdvancingDelay(clock));
	}

	private static EnvironmentSettings CreateEnvironment(WebFarmGatewayStub gateway, bool isNetCore = true) => new() {
		Uri = gateway.Url,
		Login = "Supervisor",
		Password = "Supervisor",
		IsNetCore = isNetCore
	};

	private static string[] LoginPods(WebFarmGatewayStub gateway) =>
		gateway.Requests
			.Where(entry => entry.Split(" -> ")[0].EndsWith("/AuthService.svc/Login", StringComparison.OrdinalIgnoreCase))
			.Select(entry => entry.Split(" -> ")[1])
			.ToArray();

	private static int ProbesBeforeFirstUpload(WebFarmGatewayStub gateway) {
		IReadOnlyList<string> requests = gateway.Requests;
		int firstUpload = requests.ToList().FindIndex(entry => entry.Contains("/UploadPackage", StringComparison.Ordinal));
		return requests.Take(firstUpload < 0 ? requests.Count : firstUpload)
			.Count(entry => entry.Contains("/api/Probes/DesignReady", StringComparison.Ordinal));
	}

	private static void AssertInstalledOnDesignPod(WebFarmGatewayStub gateway, string because) {
		gateway.PodsServing("/UploadPackage").Should().Equal([WebFarmGatewayStub.DesignPod],
			because: $"every upload chunk must reach the design pod, which owns PackageInstallation; {because}");
		gateway.PodsServing("/CreateBackup").Should().Equal([WebFarmGatewayStub.DesignPod],
			because: $"the backup belongs to the same installation session; {because}");
		gateway.PodsServing("/InstallPackage").Should().Equal([WebFarmGatewayStub.DesignPod],
			because: $"the installation must run on the pod that received the archive; {because}");
		gateway.PodsServing("/GetLogFile").Should().Equal([WebFarmGatewayStub.DesignPod],
			because: $"the installation log is kept per pod, so it must be read from the pod that installs; {because}");
	}

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

	[TestCase(true)]
	[TestCase(false)]
	[Description("ENG-102757: a multi-chunk package installed through the shared URL of a web farm must land on the design pod, chunks and installation alike, on .NET and on .NET Framework, where the post-login 0/ping starts no session.")]
	public void Install_ShouldInstallOnDesignPod_WhenEnvironmentIsAWebFarmBehindTheStickySessionGateway(
		bool isNetCore) {
		// Arrange
		using WebFarmGatewayStub gateway = new(WebFarmGatewayMode.WebFarm);
		EnvironmentSettings environmentSettings = CreateEnvironment(gateway, isNetCore);
		string packagePath = CreatePackageArchive();
		PackageInstaller installer = CreateInstaller(environmentSettings);
		string[] expectedRestartPods = isNetCore ? [WebFarmGatewayStub.DesignPod] : [];
		string expectedProbe = $"GET {(isNetCore ? string.Empty : "/0")}/api/Probes/DesignReady -> {WebFarmGatewayStub.DesignPod}";

		// Act
		bool result = installer.Install(packagePath, environmentSettings);

		// Assert
		result.Should().BeTrue(
			because: $"the archive must arrive whole on the pod that installs it; gateway saw: {string.Join("; ", gateway.Requests)}");
		AssertInstalledOnDesignPod(gateway, "the session holds StickySession from the warm-up on");
		gateway.Requests.Should().Contain(expectedProbe,
			because: "the design node is confirmed through the probe route of the environment's runtime before the upload");
		gateway.PodsServing("/RestartApp").Should().Equal(expectedRestartPods,
			because: ".NET restarts the pod that installed the package, and .NET Framework restarts only in developer mode");
		gateway.LoginCount.Should().Be(1,
			because: "one installation runs in one session instead of logging in again for every step");
	}

	[Test]
	[Description("ENG-102757 AC-8: a session that expires in the middle of an installation is logged in again by the client, and the new login and every later request still carry StickySession, so they stay on the design pod.")]
	public void Install_ShouldStayOnDesignPod_WhenSessionExpiresDuringTheInstallation() {
		// Arrange
		using WebFarmGatewayStub gateway = new(WebFarmGatewayMode.WebFarm, expireSessionOnceAt: "/CreateBackup");
		EnvironmentSettings environmentSettings = CreateEnvironment(gateway);
		string packagePath = CreatePackageArchive();
		PackageInstaller installer = CreateInstaller(environmentSettings);

		// Act
		bool result = installer.Install(packagePath, environmentSettings);

		// Assert
		result.Should().BeTrue(
			because: $"the client recovers the expired session and repeats the request; gateway saw: {string.Join("; ", gateway.Requests)}");
		gateway.LoginCount.Should().Be(2, because: "the expired session is replaced by exactly one new login");
		LoginPods(gateway).Last().Should().Be(WebFarmGatewayStub.DesignPod,
			because: "the re-login drops only the authentication cookies, so it still carries StickySession");
		AssertInstalledOnDesignPod(gateway, "StickySession survives the re-login");
		gateway.PodsServing("/RestartApp").Should().Equal([WebFarmGatewayStub.DesignPod],
			because: "the restart after the re-login still reaches the pod that installed the package");
	}

	[Test]
	[Description("ENG-102757: a design pod that is still restarting after a previous installation (the gateway answers 503) is waited for, and the installation then runs on it instead of failing.")]
	public void Install_ShouldWaitForTheDesignPod_WhenItIsRestarting() {
		// Arrange
		using WebFarmGatewayStub gateway = new(WebFarmGatewayMode.WebFarm, designUnavailableProbes: 3);
		EnvironmentSettings environmentSettings = CreateEnvironment(gateway);
		string packagePath = CreatePackageArchive();
		PackageInstaller installer = CreateInstaller(environmentSettings);

		// Act
		bool result = installer.Install(packagePath, environmentSettings);

		// Assert
		result.Should().BeTrue(
			because: $"the design pod came back within the wait; gateway saw: {string.Join("; ", gateway.Requests)}");
		ProbesBeforeFirstUpload(gateway).Should().Be(4,
			because: "the upload starts only after the probe, repeated three times, confirms the design pod is back");
		AssertInstalledOnDesignPod(gateway, "the upload starts only after the design pod is confirmed");
		gateway.LoginCount.Should().Be(1, because: "waiting for the design pod does not log in again");
	}

	[Test]
	[Description("ENG-102757 EC-4: RestartApp answers before the design pod stops, so an installation must not return until the restarted design pod is back; the next installation of the same pipeline then runs on it instead of uploading into a pod that is about to stop.")]
	public void Install_ShouldLetTheNextInstallationRunOnTheDesignPod_WhenTheRestartTakesItDown() {
		// Arrange
		using WebFarmGatewayStub gateway = new(WebFarmGatewayMode.WebFarm, restartStopsDesign: true);
		EnvironmentSettings environmentSettings = CreateEnvironment(gateway);
		string packagePath = CreatePackageArchive();
		PackageInstaller installer = CreateInstaller(environmentSettings);

		// Act
		bool first = installer.Install(packagePath, environmentSettings);
		int requestsOfFirst = gateway.Requests.Count;
		bool second = installer.Install(packagePath, environmentSettings);

		// Assert
		first.Should().BeTrue(because: "the first installation runs on a design pod that is up");
		gateway.Requests.Skip(requestsOfFirst)
			.TakeWhile(entry => !entry.Contains("/UploadPackage", StringComparison.Ordinal))
			.Count(entry => entry.Contains("/api/Probes/DesignReady", StringComparison.Ordinal))
			.Should().Be(1,
				because: "the first installation already waited out the restart, so the second one is confirmed at once");
		second.Should().BeTrue(
			because: $"the second installation starts only after the restarted design pod is back; gateway saw: {string.Join("; ", gateway.Requests)}");
		AssertInstalledOnDesignPod(gateway, "no installation request may reach the design pod while it is down");
		gateway.Requests.Count(entry => entry.Contains("/RestartApp", StringComparison.Ordinal)).Should().Be(2,
			because: "each successful .NET installation restarts the design pod once");
	}

	[Test]
	[Description("ENG-102757 EC-3: installation-log polls that overlap the in-flight InstallPackage go through the same session, stay on the design pod and do not log in again.")]
	public void Install_ShouldPollTheLogOnTheDesignPod_WhileInstallPackageIsInFlight() {
		// Arrange
		using WebFarmGatewayStub gateway = new(WebFarmGatewayMode.WebFarm, holdInstallUntilLogPoll: true);
		EnvironmentSettings environmentSettings = CreateEnvironment(gateway);
		string packagePath = CreatePackageArchive();
		PackageInstaller installer = CreateInstaller(environmentSettings);

		// Act
		bool result = installer.Install(packagePath, environmentSettings);

		// Assert
		result.Should().BeTrue(
			because: $"a poll on the shared session must not block or break the installation; gateway saw: {string.Join("; ", gateway.Requests)}");
		gateway.LogPolledDuringInstall.Should().BeTrue(
			because: "the log listener polls while the InstallPackage request is still waiting for its answer");
		gateway.PodsServing("/GetLogFile").Should().Equal([WebFarmGatewayStub.DesignPod],
			because: "the overlapping polls carry the session's StickySession too");
		gateway.LoginCount.Should().Be(1, because: "polling on the shared session does not log in again");
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
			.WithMessage("*does not reach the design node*HTTP 503*",
				because: "the refusal names the status the common pod answered");
		gateway.PodsServing("/UploadPackage").Should().BeEmpty(
			because: "nothing may be uploaded once the session is known not to reach the design pod");
		gateway.Requests.Count(entry => entry.Contains("/api/Probes/DesignReady", StringComparison.Ordinal))
			.Should().Be(1, because: "a common pod's own answer is final, so the guard does not wait on it");
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("ENG-102757: a single node outside a web farm never issues StickySession, so the installation proceeds without the design-node probe on .NET and .NET Framework alike.")]
	public void Install_ShouldInstallWithoutDesignProbe_WhenEnvironmentIsASingleNode(bool isNetCore) {
		// Arrange
		using WebFarmGatewayStub gateway = new(WebFarmGatewayMode.SingleNode);
		EnvironmentSettings environmentSettings = CreateEnvironment(gateway, isNetCore);
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

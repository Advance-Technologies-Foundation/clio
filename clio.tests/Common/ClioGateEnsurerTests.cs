using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using Clio.WebApplication;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class ClioGateEnsurerTests {

	// Each test gets a unique URI so the static ConfirmedEnvironments cache cannot leak
	// between tests (the cache is keyed by normalized URI).
	private static (ClioGateEnsurer Ensurer, IClioGateway Gateway, IPackageInstaller Installer,
		IApplication App, EnvironmentSettings Env)
		Create(bool isNetCore = false) {
		EnvironmentSettings env = new() {
			Uri = $"http://test-host/{Guid.NewGuid():N}",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = isNetCore
		};
		IClioGateway gateway = Substitute.For<IClioGateway>();
		IPackageInstaller installer = Substitute.For<IPackageInstaller>();
		IApplication app = Substitute.For<IApplication>();
		IWorkingDirectoriesProvider dirs = Substitute.For<IWorkingDirectoriesProvider>();
		dirs.ExecutingDirectory.Returns(@"C:\fake");
		return (new ClioGateEnsurer(env, gateway, installer, app, dirs), gateway, installer, app, env);
	}

	[Test]
	[Description("After the first confirmed call, subsequent calls must return Present() without re-querying the gateway.")]
	public void EnsureInstalled_Should_Return_Present_On_Cache_Hit_Without_Querying_Gateway() {
		var (ensurer, gateway, _, _, _) = Create();
		gateway.GetInstalledVersion().Returns(new PackageVersion(new Version(1, 0, 0), string.Empty));

		ClioGateEnsureResult first = ensurer.EnsureInstalled();
		ClioGateEnsureResult second = ensurer.EnsureInstalled();

		first.AlreadyInstalled.Should().BeTrue("ClioGate was already present on first call");
		second.AlreadyInstalled.Should().BeTrue("cache hit must report Present");
		second.Warning.Should().BeNull();
		gateway.Received(1).GetInstalledVersion();
	}

	[Test]
	[Description("On .NET Framework, a successful install must call Restart() because PackageInstaller does not restart for framework targets.")]
	public void EnsureInstalled_Should_Install_And_Restart_On_NetFramework_When_Missing() {
		var (ensurer, gateway, installer, app, _) = Create(isNetCore: false);
		gateway.GetInstalledVersion().Returns(default(PackageVersion));
		installer.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), null, null, true).Returns(true);

		ClioGateEnsureResult result = ensurer.EnsureInstalled();

		result.JustInstalled.Should().BeTrue();
		result.Warning.Should().Contain("installed automatically");
		installer.Received(1).Install(
			Arg.Is<string>(p => p.Contains("cliogate.gz")),
			Arg.Any<EnvironmentSettings>(), null, null, true);
		app.Received(1).Restart();
	}

	[Test]
	[Description("On NetCore, a successful install must NOT call Restart() because PackageInstaller already restarts the app during install for NetCore targets.")]
	public void EnsureInstalled_Should_Install_Without_Restart_On_NetCore_When_Missing() {
		var (ensurer, gateway, installer, app, _) = Create(isNetCore: true);
		gateway.GetInstalledVersion().Returns(default(PackageVersion));
		installer.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), null, null, true).Returns(true);

		ClioGateEnsureResult result = ensurer.EnsureInstalled();

		result.JustInstalled.Should().BeTrue();
		installer.Received(1).Install(
			Arg.Is<string>(p => p.Contains("cliogate_netcore.gz")),
			Arg.Any<EnvironmentSettings>(), null, null, true);
		app.DidNotReceive().Restart();
	}

	[Test]
	[Description("When GetInstalledVersion throws (e.g. network error), EnsureInstalled should swallow the exception and proceed to install rather than surfacing it.")]
	public void EnsureInstalled_Should_Attempt_Install_When_GetInstalledVersion_Throws() {
		var (ensurer, gateway, installer, _, _) = Create(isNetCore: true);
		gateway.GetInstalledVersion().Throws(new Exception("Network error"));
		installer.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), null, null, true).Returns(true);

		ClioGateEnsureResult result = ensurer.EnsureInstalled();

		result.JustInstalled.Should().BeTrue();
		installer.Received(1).Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), null, null, true);
	}

	[Test]
	[Description("When the package installer returns false, EnsureInstalled should return a Failed result with a descriptive warning and must not add the env to the cache.")]
	public void EnsureInstalled_Should_Return_Failed_When_Installer_Returns_False() {
		var (ensurer, gateway, installer, _, _) = Create();
		gateway.GetInstalledVersion().Returns(default(PackageVersion));
		installer.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), null, null, true).Returns(false);

		ClioGateEnsureResult result = ensurer.EnsureInstalled();

		result.AlreadyInstalled.Should().BeFalse();
		result.JustInstalled.Should().BeFalse();
		result.Warning.Should().Contain("auto-install failed");
	}

	[Test]
	[Description("When two concurrent calls race for the same uncached environment, Install must be executed exactly once — the double-check inside the lock prevents a duplicate install.")]
	public void EnsureInstalled_Should_Install_Only_Once_Under_Concurrent_Access() {
		// Use a shared EnvironmentSettings so both ensurers map to the same cache key.
		EnvironmentSettings env = new() {
			Uri = $"http://test-host/{Guid.NewGuid():N}",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true
		};
		IClioGateway gateway = Substitute.For<IClioGateway>();
		IPackageInstaller installer = Substitute.For<IPackageInstaller>();
		IApplication app = Substitute.For<IApplication>();
		IWorkingDirectoriesProvider dirs = Substitute.For<IWorkingDirectoriesProvider>();
		dirs.ExecutingDirectory.Returns(@"C:\fake");

		ManualResetEventSlim installStarted = new(false);
		ManualResetEventSlim releaseInstall = new(false);
		ManualResetEventSlim t2Waiting = new(false);

		gateway.GetInstalledVersion().Returns(default(PackageVersion));
		installer.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), null, null, true)
			.Returns(_ => {
				installStarted.Set();
				releaseInstall.Wait();
				return true;
			});

		ClioGateEnsurer ensurer1 = new(env, gateway, installer, app, dirs);
		ClioGateEnsurer ensurer2 = new(env, gateway, installer, app, dirs);

		ClioGateEnsureResult? result1 = null;
		ClioGateEnsureResult? result2 = null;

		Task t1 = Task.Run(() => result1 = ensurer1.EnsureInstalled());
		// Wait until t1 is inside Install (holding the semaphore), then start t2.
		installStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("t1 should reach Install() within 5 s");
		// t2 will immediately block on gate.Wait() because t1 still holds the semaphore.
		Task t2 = Task.Run(() => {
			t2Waiting.Set();
			result2 = ensurer2.EnsureInstalled();
		});
		// Wait for t2 to start (so it is past the fast-path check and heading to gate.Wait()).
		t2Waiting.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("t2 should start within 5 s");
		releaseInstall.Set();
		Task.WaitAll(new[] { t1, t2 }, TimeSpan.FromSeconds(10)).Should().BeTrue("both tasks must finish");

		installer.Received(1).Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(), null, null, true);
		result1!.JustInstalled.Should().BeTrue("t1 performed the install");
		result2!.AlreadyInstalled.Should().BeTrue("t2 must hit the cache after waiting for the lock");
	}
}

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ComponentRegistryRefreshCommandTests {

	private static IMobileComponentRegistryClient MobileAlwaysSucceeds() {
		IMobileComponentRegistryClient m = Substitute.For<IMobileComponentRegistryClient>();
		m.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(true));
		return m;
	}

	private static IRequestRegistryClient RequestsAlwaysSucceeds() {
		IRequestRegistryClient r = Substitute.For<IRequestRegistryClient>();
		r.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(true));
		return r;
	}

	// The default for the pre-existing tests: requests-registry ENABLED, so the requests flavor is
	// refreshed alongside web and mobile (the behavior those tests assert). A bare substitute (feature
	// OFF) is used by the gated-off tests below.
	private static IFeatureToggleService RequestsFeatureEnabled() {
		IFeatureToggleService f = Substitute.For<IFeatureToggleService>();
		f.IsFeatureEnabled("requests-registry").Returns(true);
		return f;
	}

	[Test]
	[Description("With no flags the verb refreshes the latest.json alias for web, mobile, and requests and exits 0 when the CDN responds.")]
	public void Execute_Refreshes_Latest_When_No_Flags() {
		// Arrange
		FakeComponentRegistryClient client = new();
		client.SetRefreshResult("latest", success: true);
		ComponentRegistryRefreshCommand command = new(client, MobileAlwaysSucceeds(), RequestsAlwaysSucceeds(), Substitute.For<IFileSystem>(), RequestsFeatureEnabled(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions());

		// Assert
		exitCode.Should().Be(0, because: "a successful refresh of latest is the default-success path");
		client.RefreshedVersions.Should().ContainSingle().Which.Should().Be("latest",
			because: "no --version means we touch the latest.json alias on CDN");
	}

	[Test]
	[Description("When CDN refuses to serve the web flavor the verb exits 1 so CI/scripts can detect the failure.")]
	public void Execute_Returns_NonZero_When_Cdn_Unavailable() {
		// Arrange
		FakeComponentRegistryClient client = new();
		client.SetRefreshResult("latest", success: false);
		ComponentRegistryRefreshCommand command = new(client, MobileAlwaysSucceeds(), RequestsAlwaysSucceeds(), Substitute.For<IFileSystem>(), RequestsFeatureEnabled(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions());

		// Assert
		exitCode.Should().Be(1, because: "exit code must surface a CDN failure so users notice the cache is unchanged");
	}

	[Test]
	[Description("With --version the verb refreshes exactly that file for both flavors.")]
	public void Execute_Refreshes_Specific_Version_When_Provided() {
		// Arrange
		FakeComponentRegistryClient client = new();
		client.SetRefreshResult("8.2.1", success: true);
		ComponentRegistryRefreshCommand command = new(client, MobileAlwaysSucceeds(), RequestsAlwaysSucceeds(), Substitute.For<IFileSystem>(), RequestsFeatureEnabled(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions { Version = "8.2.1" });

		// Assert
		exitCode.Should().Be(0);
		client.RefreshedVersions.Should().ContainSingle().Which.Should().Be("8.2.1",
			because: "an explicit --version pins the target so users can pull a specific GA on demand");
	}

	[Test]
	[Description("With --all the verb enumerates the web, mobile, and requests cache directories, deduplicates versions, and refreshes every distinct version for every flavor.")]
	public void Execute_Refreshes_All_Cached_Versions_When_All_Flag_Set() {
		// Arrange
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);
		fileSystem.GetFiles(Arg.Any<string>()).Returns(new[] {
			"/cache/8.2.0.json",
			"/cache/8.2.0.meta.json",      // sidecar — must be skipped
			"/cache/8.3.0.json",
			"/cache/latest.json",
			"/cache/8.3.0.json.tmp"         // atomic-write scratch — must be skipped
		});

		FakeComponentRegistryClient client = new();
		client.SetRefreshResult("8.2.0", success: true);
		client.SetRefreshResult("8.3.0", success: true);
		client.SetRefreshResult("latest", success: true);
		ComponentRegistryRefreshCommand command = new(client, MobileAlwaysSucceeds(), RequestsAlwaysSucceeds(), fileSystem, RequestsFeatureEnabled(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions { All = true });

		// Assert
		exitCode.Should().Be(0);
		client.RefreshedVersions.Should().BeEquivalentTo(new[] { "8.2.0", "8.3.0", "latest" },
			because: "every per-version json must be refreshed; sidecars and .tmp scratch files must be ignored; versions from web, mobile, and requests dirs are deduplicated before refresh");
	}

	[Test]
	[Description("Running --all on a fresh machine without a cache directory is a no-op (exit 0).")]
	public void Execute_Returns_Zero_When_All_Flag_Without_Cache_Directory() {
		// Arrange
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(false);
		FakeComponentRegistryClient client = new();
		ComponentRegistryRefreshCommand command = new(client, MobileAlwaysSucceeds(), RequestsAlwaysSucceeds(), fileSystem, RequestsFeatureEnabled(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions { All = true });

		// Assert
		exitCode.Should().Be(0, because: "no cache yet is a benign state, not an error");
		client.RefreshedVersions.Should().BeEmpty();
	}

	[Test]
	[Description("An exception inside the web registry client is contained and reported as a failed version, not a process crash.")]
	public void Execute_Reports_Exception_Per_Version_Without_Crashing() {
		// Arrange
		FakeComponentRegistryClient client = new();
		client.SetRefreshThrows("latest", new InvalidOperationException("boom"));
		ComponentRegistryRefreshCommand command = new(client, MobileAlwaysSucceeds(), RequestsAlwaysSucceeds(), Substitute.For<IFileSystem>(), RequestsFeatureEnabled(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions());

		// Assert
		exitCode.Should().Be(1, because: "a thrown exception must be visible to the caller via a non-zero exit code");
	}

	[Test]
	[Description("The verb refreshes the requests flavor too: with no flags the requests registry client's RefreshAsync is invoked for the latest alias, so a regression that drops the requests refresh is caught.")]
	public async Task Execute_Refreshes_Requests_Flavor_When_No_Flags() {
		// Arrange
		FakeComponentRegistryClient client = new();
		client.SetRefreshResult("latest", success: true);
		IRequestRegistryClient requests = RequestsAlwaysSucceeds();
		ComponentRegistryRefreshCommand command = new(client, MobileAlwaysSucceeds(), requests, Substitute.For<IFileSystem>(), RequestsFeatureEnabled(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions());

		// Assert
		exitCode.Should().Be(0, because: "every flavor refresh succeeds in this arrangement");
		await requests.Received(1).RefreshAsync("latest", Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("When the requests flavor fails to refresh, the verb exits 1 — a requests-flavor CDN failure must surface exactly like a web or mobile failure.")]
	public void Execute_Returns_NonZero_When_Requests_Flavor_Fails() {
		// Arrange
		FakeComponentRegistryClient client = new();
		client.SetRefreshResult("latest", success: true);
		IRequestRegistryClient requests = Substitute.For<IRequestRegistryClient>();
		requests.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));
		ComponentRegistryRefreshCommand command = new(client, MobileAlwaysSucceeds(), requests, Substitute.For<IFileSystem>(), RequestsFeatureEnabled(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions());

		// Assert
		exitCode.Should().Be(1, because: "a requests-flavor refresh failure must bubble to a non-zero exit code so CI/scripts notice the requests cache is unchanged");
	}

	[Test]
	[Description("With --all the verb enumerates the requests cache subdirectory too: a version present ONLY under the requests subdir is discovered and refreshed.")]
	public void Execute_All_Enumerates_Requests_Subdirectory() {
		// Arrange — only the requests subdir holds a cached version; the web and mobile dirs are empty.
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);
		fileSystem.GetFiles(Arg.Any<string>()).Returns(Array.Empty<string>());
		fileSystem.GetFiles(Arg.Is<string>(path =>
				path.EndsWith(RegistryFlavor.Requests.CacheSubdirectoryName, StringComparison.OrdinalIgnoreCase)))
			.Returns(new[] { "/cache/requests/9.9.9.json" });

		FakeComponentRegistryClient client = new();
		client.SetRefreshResult("9.9.9", success: true);
		ComponentRegistryRefreshCommand command = new(client, MobileAlwaysSucceeds(), RequestsAlwaysSucceeds(), fileSystem, RequestsFeatureEnabled(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions { All = true });

		// Assert
		exitCode.Should().Be(0,
			because: "every discovered version refreshes successfully across all flavors in this arrangement");
		client.RefreshedVersions.Should().ContainSingle().Which.Should().Be("9.9.9",
			because: "a version cached only under the requests subdirectory must still be enumerated by --all and refreshed across flavors");
	}

	[Test]
	[Description("With the requests-registry feature disabled (the default), the verb does NOT refresh the requests flavor — a user who never opted in must not depend on the requests CDN feed.")]
	public async Task Execute_Skips_Requests_Flavor_When_RequestsRegistry_Disabled() {
		// Arrange — a bare feature-toggle substitute reports requests-registry as OFF.
		FakeComponentRegistryClient client = new();
		client.SetRefreshResult("latest", success: true);
		IRequestRegistryClient requests = RequestsAlwaysSucceeds();
		ComponentRegistryRefreshCommand command = new(client, MobileAlwaysSucceeds(), requests,
			Substitute.For<IFileSystem>(), Substitute.For<IFeatureToggleService>(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions());

		// Assert
		exitCode.Should().Be(0, because: "web and mobile refreshed successfully and the requests flavor is skipped while the feature is off");
		await requests.DidNotReceive().RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("With requests-registry disabled, --all does NOT enumerate the requests cache subdirectory: a version cached only under requests/ (e.g. from a prior enabled run) is neither turned into a refresh target nor refreshed, so an opted-out user's --all run is a no-op success rather than a web/mobile 404 penalty.")]
	public void Execute_All_Skips_Requests_Subdirectory_When_RequestsRegistry_Disabled() {
		// Arrange — only the requests subdir holds a cached version; web and mobile dirs are empty; feature OFF.
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);
		fileSystem.GetFiles(Arg.Any<string>()).Returns(Array.Empty<string>());
		fileSystem.GetFiles(Arg.Is<string>(path =>
				path.EndsWith(RegistryFlavor.Requests.CacheSubdirectoryName, StringComparison.OrdinalIgnoreCase)))
			.Returns(new[] { "/cache/requests/9.9.9.json" });

		FakeComponentRegistryClient client = new();
		IRequestRegistryClient requests = RequestsAlwaysSucceeds();
		ComponentRegistryRefreshCommand command = new(client, MobileAlwaysSucceeds(), requests,
			fileSystem, Substitute.For<IFeatureToggleService>(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions { All = true });

		// Assert
		exitCode.Should().Be(0,
			because: "the requests-only cached version is not enumerated while the feature is off, so nothing is refreshed and the run is a no-op success");
		client.RefreshedVersions.Should().BeEmpty(
			because: "a version present only under the requests cache subdir must not drive a web/mobile refresh for an opted-out user");
	}

	[Test]
	[Description("A user who never enabled requests-registry is not penalised by an unpublished requests CDN file: even when the requests client would report cdn-unavailable, the flavor is skipped so the command still exits 0 when web and mobile succeed.")]
	public async Task Execute_Returns_Zero_When_Requests_Cdn_Unavailable_But_Feature_Disabled() {
		// Arrange — requests would 404 (RefreshAsync returns false), but the feature is off so it is never called.
		FakeComponentRegistryClient client = new();
		client.SetRefreshResult("latest", success: true);
		IRequestRegistryClient requests = Substitute.For<IRequestRegistryClient>();
		requests.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));
		ComponentRegistryRefreshCommand command = new(client, MobileAlwaysSucceeds(), requests,
			Substitute.For<IFileSystem>(), Substitute.For<IFeatureToggleService>(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions());

		// Assert
		exitCode.Should().Be(0, because: "the requests flavor is not a requested refresh while the feature is off, so its would-be CDN failure must not flip the exit code for an opted-out user");
		await requests.DidNotReceive().RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	private sealed class FakeComponentRegistryClient : IComponentRegistryClient {
		private readonly System.Collections.Generic.Dictionary<string, bool> _refreshResults = new(StringComparer.OrdinalIgnoreCase);
		private readonly System.Collections.Generic.Dictionary<string, Exception> _refreshErrors = new(StringComparer.OrdinalIgnoreCase);
		public System.Collections.Generic.List<string> RefreshedVersions { get; } = new();

		public void SetRefreshResult(string version, bool success) => _refreshResults[version] = success;
		public void SetRefreshThrows(string version, Exception ex) => _refreshErrors[version] = ex;

		public Task<ComponentRegistryFetchResult> GetAsync(string requestedVersion, CancellationToken cancellationToken = default)
			=> throw new NotImplementedException("GetAsync is not exercised by the refresh CLI tests.");

		public Task<bool> RefreshAsync(string version, CancellationToken cancellationToken = default) {
			RefreshedVersions.Add(version);
			if (_refreshErrors.TryGetValue(version, out Exception? ex)) {
				throw ex;
			}
			return Task.FromResult(_refreshResults.TryGetValue(version, out bool success) && success);
		}
	}
}

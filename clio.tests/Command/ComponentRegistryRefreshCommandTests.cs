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

	[Test]
	[Description("With no flags the verb refreshes the latest.json alias and exits 0 when the CDN responds.")]
	public void Execute_Refreshes_Latest_When_No_Flags() {
		// Arrange
		FakeComponentRegistryClient client = new();
		client.SetRefreshResult("latest", success: true);
		ComponentRegistryRefreshCommand command = new(client, Substitute.For<IFileSystem>(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions());

		// Assert
		exitCode.Should().Be(0, because: "a successful refresh of latest is the default-success path");
		client.RefreshedVersions.Should().ContainSingle().Which.Should().Be("latest",
			because: "no --version means we touch the latest.json alias on CDN");
	}

	[Test]
	[Description("When CDN refuses to serve the verb exits 1 so CI/scripts can detect the failure.")]
	public void Execute_Returns_NonZero_When_Cdn_Unavailable() {
		// Arrange
		FakeComponentRegistryClient client = new();
		client.SetRefreshResult("latest", success: false);
		ComponentRegistryRefreshCommand command = new(client, Substitute.For<IFileSystem>(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions());

		// Assert
		exitCode.Should().Be(1, because: "exit code must surface a CDN failure so users notice the cache is unchanged");
	}

	[Test]
	[Description("With --version the verb refreshes exactly that file and ignores the others.")]
	public void Execute_Refreshes_Specific_Version_When_Provided() {
		// Arrange
		FakeComponentRegistryClient client = new();
		client.SetRefreshResult("8.2.1", success: true);
		ComponentRegistryRefreshCommand command = new(client, Substitute.For<IFileSystem>(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions { Version = "8.2.1" });

		// Assert
		exitCode.Should().Be(0);
		client.RefreshedVersions.Should().ContainSingle().Which.Should().Be("8.2.1",
			because: "an explicit --version pins the target so users can pull a specific GA on demand");
	}

	[Test]
	[Description("With --all the verb enumerates the cache directory and refreshes every per-version file it finds.")]
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
		ComponentRegistryRefreshCommand command = new(client, fileSystem, Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions { All = true });

		// Assert
		exitCode.Should().Be(0);
		client.RefreshedVersions.Should().BeEquivalentTo(new[] { "8.2.0", "8.3.0", "latest" },
			because: "every per-version json must be refreshed; sidecars and .tmp scratch files must be ignored");
	}

	[Test]
	[Description("Running --all on a fresh machine without a cache directory is a no-op (exit 0).")]
	public void Execute_Returns_Zero_When_All_Flag_Without_Cache_Directory() {
		// Arrange
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(false);
		FakeComponentRegistryClient client = new();
		ComponentRegistryRefreshCommand command = new(client, fileSystem, Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions { All = true });

		// Assert
		exitCode.Should().Be(0, because: "no cache yet is a benign state, not an error");
		client.RefreshedVersions.Should().BeEmpty();
	}

	[Test]
	[Description("An exception inside the registry client is contained and reported as a failed version, not a process crash.")]
	public void Execute_Reports_Exception_Per_Version_Without_Crashing() {
		// Arrange
		FakeComponentRegistryClient client = new();
		client.SetRefreshThrows("latest", new InvalidOperationException("boom"));
		ComponentRegistryRefreshCommand command = new(client, Substitute.For<IFileSystem>(), Substitute.For<ILogger>());

		// Act
		int exitCode = command.Execute(new ComponentRegistryRefreshOptions());

		// Assert
		exitCode.Should().Be(1, because: "a thrown exception must be visible to the caller via a non-zero exit code");
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

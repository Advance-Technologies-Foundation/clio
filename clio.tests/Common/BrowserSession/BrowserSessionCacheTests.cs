using System;
using System.Text.RegularExpressions;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// Story 3 (browser-session-handoff): the on-disk storageState cache must key on a stable,
/// filesystem-safe identifier (env URI + credential hash, never <c>env.Name</c>), write owner-only,
/// and validate a caller-supplied <c>--output-path</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class BrowserSessionCacheTests {

	private IFileSystem _fileSystem;
	private IFileSecurityHardening _hardening;
	private BrowserSessionCache _sut;

	[SetUp]
	public void SetUp() {
		_fileSystem = Substitute.For<IFileSystem>();
		_hardening = Substitute.For<IFileSecurityHardening>();
		_sut = new BrowserSessionCache(_fileSystem, _hardening);
	}

	[TearDown]
	public void TearDown() {
		_fileSystem.ClearReceivedCalls();
		_hardening.ClearReceivedCalls();
	}

	private static EnvironmentSettings Env(string uri = "https://dev.creatio.com/0",
		string login = "Supervisor", string password = "Supervisor", string clientId = null,
		bool isNetCore = false) =>
		new() { Uri = uri, Login = login, Password = password, ClientId = clientId, IsNetCore = isNetCore };

	[Test]
	[Description("BuildKey produces a filesystem-safe stem with no path separators or colons, regardless of the URI shape.")]
	public void BuildKey_ShouldProduceFilesystemSafeKey_WhenUriContainsSeparators() {
		// Arrange
		EnvironmentSettings env = Env(uri: "https://dev.creatio.com:8080/0");

		// Act
		string key = _sut.BuildKey(env);

		// Assert
		key.Should().MatchRegex("^[a-z0-9._-]+_[0-9a-f]{16}$",
			"the key is used as a file-name stem and must contain no '/', '\\\\' or ':'");
		key.Should().NotContainAny(["/", "\\", ":", ".."],
			"path separators and traversal tokens must never reach the file name");
	}

	[Test]
	[Description("BuildKey is deterministic: the same environment yields the same key.")]
	public void BuildKey_ShouldBeStable_WhenSameEnvironment() {
		// Arrange & Act
		string first = _sut.BuildKey(Env());
		string second = _sut.BuildKey(Env());

		// Assert
		first.Should().Be(second, because: "a stable key lets the cache be reused across calls");
	}

	[Test]
	[Description("BuildKey differentiates credentials on the same URI so two users never collide on one cache file.")]
	public void BuildKey_ShouldDiffer_WhenSameUriDifferentCredentials() {
		// Arrange & Act
		string a = _sut.BuildKey(Env(password: "secret-a"));
		string b = _sut.BuildKey(Env(password: "secret-b"));

		// Assert
		a.Should().NotBe(b, because: "different credentials on the same URI must map to different cache files");
	}

	[Test]
	[Description("BuildKey differentiates OAuth environments (empty Login) by ClientId so they do not collide.")]
	public void BuildKey_ShouldDiffer_WhenOAuthEnvironmentsDifferByClientId() {
		// Arrange & Act
		string a = _sut.BuildKey(Env(login: null, password: null, clientId: "client-a"));
		string b = _sut.BuildKey(Env(login: null, password: null, clientId: "client-b"));

		// Assert
		a.Should().NotBe(b, because: "an empty Login must not collapse distinct OAuth client ids onto one key");
	}

	[Test]
	[Description("GetPath returns the absolute cache path under the sessions directory without creating the file.")]
	public void GetPath_ShouldReturnSessionsPath_WithoutCreatingFile() {
		// Act
		string path = _sut.GetPath("dev-creatio-com-0_0123456789abcdef");

		// Assert
		path.Replace('\\', '/').Should().EndWith("sessions/dev-creatio-com-0_0123456789abcdef.storageState.json",
			because: "the cache lives under {clio-home}/sessions/{key}.storageState.json");
		_fileSystem.DidNotReceiveWithAnyArgs().WriteOwnerOnlyTextToFile(default, default);
		_fileSystem.DidNotReceiveWithAnyArgs().CreateDirectoryIfNotExists(default);
	}

	[Test]
	[Description("Write creates the sessions directory, writes the JSON to the keyed path owner-only, and hardens both.")]
	public void Write_ShouldWriteKeyedPathAndHardenOwnerOnly_WhenNoOverride() {
		// Arrange
		const string key = "dev-creatio-com-0_0123456789abcdef";
		const string json = "{\"cookies\":[],\"origins\":[]}";
		string expectedPath = _sut.GetPath(key);

		// Act
		_sut.Write(key, json);

		// Assert
		_fileSystem.Received(1).CreateDirectoryIfNotExists(
			Arg.Is<string>(d => d.Replace('\\', '/').EndsWith("sessions")));
		_fileSystem.Received(1).WriteOwnerOnlyTextToFile(expectedPath, json);
		_hardening.Received(1).HardenDirectory(Arg.Is<string>(d => d.Replace('\\', '/').EndsWith("sessions")));
		_hardening.Received(1).HardenFile(expectedPath);
	}

	[Test]
	[Description("Write to a valid --output-path writes there (not the cache path) owner-only and hardens it.")]
	public void Write_ShouldWriteToOverridePath_WhenValidOverrideProvided() {
		// Arrange
		string overridePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clio-session-test.json");
		const string json = "{\"cookies\":[]}";

		// Act
		_sut.Write("ignored-key", json, overridePath);

		// Assert
		_fileSystem.Received(1).WriteOwnerOnlyTextToFile(System.IO.Path.GetFullPath(overridePath), json);
		_hardening.Received(1).HardenFile(System.IO.Path.GetFullPath(overridePath));
	}

	[Test]
	[Description("Write rejects an --output-path containing traversal ('..') and writes nothing.")]
	public void Write_ShouldThrowAndWriteNothing_WhenOverridePathHasTraversal() {
		// Arrange
		string overridePath = System.IO.Path.Combine("safe", "..", "..", "etc", "evil.json");

		// Act
		Action act = () => _sut.Write("key", "{}", overridePath);

		// Assert
		act.Should().Throw<ArgumentException>(
			because: "a path-traversal --output-path must be refused before any write");
		_fileSystem.DidNotReceiveWithAnyArgs().WriteOwnerOnlyTextToFile(default, default);
		_hardening.DidNotReceiveWithAnyArgs().HardenFile(default);
	}

	[Test]
	[Category("Integration")]
	[Description("Write rejects an --output-path whose parent directory is a symlink, to prevent a bearer token being written through a planted link.")]
	public void Write_ShouldThrowAndWriteNothing_WhenParentDirectoryIsSymlink() {
		// Symbolic-link creation requires SeCreateSymbolicLinkPrivilege on Windows, which the CI
		// runner does not hold. Skip early rather than letting CreateSymbolicLink throw IOException.
		if (OperatingSystem.IsWindows()) {
			Assert.Ignore("Symlink creation requires elevation on this Windows configuration.");
		}

		// Arrange — create a real symlink in the temp directory.
		string tempDir = System.IO.Path.GetTempPath();
		string realTarget = System.IO.Path.Combine(tempDir, "clio-cache-test-real-" + System.Guid.NewGuid().ToString("N"));
		string symlinkDir = System.IO.Path.Combine(tempDir, "clio-cache-test-link-" + System.Guid.NewGuid().ToString("N"));
		System.IO.Directory.CreateDirectory(realTarget);
		try {
			System.IO.Directory.CreateSymbolicLink(symlinkDir, realTarget);
			string overridePath = System.IO.Path.Combine(symlinkDir, "session.json");

			// Act
			Action act = () => _sut.Write("key", "{}", overridePath);

			// Assert
			act.Should().Throw<ArgumentException>(
				because: "a parent-directory symlink must be rejected before any write to prevent token exfiltration");
			_fileSystem.DidNotReceiveWithAnyArgs().WriteOwnerOnlyTextToFile(default, default);
		} finally {
			try { System.IO.Directory.Delete(symlinkDir); } catch { /* best effort */ }
			try { System.IO.Directory.Delete(realTarget, recursive: true); } catch { /* best effort */ }
		}
	}

	[Test]
	[Description("TryRead returns true and the path when the cache file exists.")]
	public void TryRead_ShouldReturnTrueAndPath_WhenFileExists() {
		// Arrange
		const string key = "k_0123456789abcdef";
		string path = _sut.GetPath(key);
		_fileSystem.ExistsFile(path).Returns(true);

		// Act
		bool found = _sut.TryRead(key, out string filePath);

		// Assert
		found.Should().BeTrue(because: "an existing cache file is a hit");
		filePath.Should().Be(path, because: "the absolute cached path is returned on a hit");
	}

	[Test]
	[Description("TryRead returns false and null when no cache file exists for the key.")]
	public void TryRead_ShouldReturnFalseAndNull_WhenFileMissing() {
		// Arrange
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(false);

		// Act
		bool found = _sut.TryRead("missing_0123456789abcdef", out string filePath);

		// Assert
		found.Should().BeFalse(because: "a missing file is a cache miss");
		filePath.Should().BeNull(because: "no path is produced on a miss");
	}

	[Test]
	[Description("Delete removes the keyed cache file via the file system (idempotent delete-if-exists).")]
	public void Delete_ShouldDeleteKeyedFile_WhenInvoked() {
		// Arrange
		const string key = "k_0123456789abcdef";
		string path = _sut.GetPath(key);

		// Act
		_sut.Delete(key);

		// Assert
		_fileSystem.Received(1).DeleteFileIfExists(path);
	}
}

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Clio.Common;
using Clio.Common.Assertions;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.Assertions;

[TestFixture]
[Category("Unit")]
public class FsPermissionAssertionTests{
	#region Fields: Private

	private ILogger _logger;
	private ISettingsRepository _settingsRepository;
	private FsPermissionAssertion _sut;

	#endregion

	#region Methods: Public

	[Test]
	[Description("Should validate current user has permissions on temp directory")]
	public void Execute_WhenCurrentUserOnTempDir_ShouldReturnSuccess() {
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			Assert.Inconclusive("This test only runs on Windows");
			return;
		}

		// Arrange
		string testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(testDir);

		try {
			// Get current user
			WindowsIdentity currentUser = WindowsIdentity.GetCurrent();
			string user = currentUser.Name;

			// Grant current user full control explicitly
			DirectoryInfo dirInfo = new(testDir);
			DirectorySecurity security = dirInfo.GetAccessControl();
			security.AddAccessRule(new FileSystemAccessRule(
				currentUser.Name,
				FileSystemRights.FullControl,
				InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
				PropagationFlags.None,
				AccessControlType.Allow));
			dirInfo.SetAccessControl(security);

			// Act
			AssertionResult result = _sut.Execute(testDir, user, "full-control");

			// Assert
			result.Status.Should().Be("pass", "current user should have full control on created directory");
			result.Scope.Should().Be(AssertionScope.Fs, "scope should be filesystem");
			result.Resolved["path"].Should().Be(testDir, "resolved path should match test directory");
			result.Resolved["userIdentity"].Should().Be(user, "user identity should be preserved");
			result.Resolved["permission"].Should().Be("full-control", "permission level should be preserved");
		}
		finally {
			// Cleanup
			try {
				Directory.Delete(testDir, true);
			}
			catch {
				// Ignore cleanup errors
			}
		}
	}

	[Test]
	[Description("Should return failure when directory does not exist")]
	public void Execute_WhenDirectoryDoesNotExist_ShouldReturnFailure() {
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			Assert.Inconclusive("This test only runs on Windows");
			return;
		}

		// Arrange
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		string user = "BUILTIN\\IIS_IUSRS";
		string permission = "full-control";

		// Act
		AssertionResult result = _sut.Execute(path, user, permission);

		// Assert
		result.Status.Should().Be("fail", "non-existent path should fail validation");
		result.FailedAt.Should().Be(AssertionPhase.FsPath, "failure occurs at path validation");
		result.Reason.Should().Contain("does not exist", "error should indicate path doesn't exist");
	}

	[Test]
	[Description("Should return failure when not running on Windows")]
	public void Execute_WhenNotOnWindows_ShouldReturnFailure() {
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			Assert.Inconclusive("This test only runs on non-Windows platforms");
			return;
		}

		// Arrange
		const string path = "/tmp/test";
		const string user = "testuser";
		const string permission = "full-control";

		// Act
		AssertionResult result = _sut.Execute(path, user, permission);

		// Assert
		result.Status.Should().Be("fail", "permission checks are Windows-only");
		result.FailedAt.Should().Be(AssertionPhase.FsPerm, "failure is at permission phase");
		result.Reason.Should().Contain("Windows", "error should mention Windows requirement");
	}

	[Test]
	[Description("Should return failure when path parameter is null")]
	public void Execute_WhenPathIsNull_ShouldReturnFailure() {
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			Assert.Inconclusive("This test only runs on Windows");
			return;
		}

		// Arrange
		string path = null;
		string user = "BUILTIN\\IIS_IUSRS";
		string permission = "full-control";

		// Act
		AssertionResult result = _sut.Execute(path, user, permission);

		// Assert
		result.Status.Should().Be("fail", "null path should fail validation");
		result.FailedAt.Should().Be(AssertionPhase.FsPath, "failure occurs at path validation");
	}

	[Test]
	[Description("Should resolve iis-clio-root-path setting key")]
	public void Execute_WhenPathIsSettingKey_ShouldResolveFromSettings() {
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			Assert.Inconclusive("This test only runs on Windows");
			return;
		}

		// Arrange
		string settingKey = "iis-clio-root-path";
		string configuredPath = Path.GetTempPath();
		string user = "BUILTIN\\Users";
		string permission = "read";

		_settingsRepository.GetIISClioRootPath().Returns(configuredPath);

		// Act
		AssertionResult result = _sut.Execute(settingKey, user, permission);

		// Assert
		_settingsRepository.Received(1).GetIISClioRootPath();
		result.Details["requestedPath"].Should().Be(settingKey,
			"details should show the original setting key");
	}

	[Test]
	[Description("Should handle case-insensitive permission levels")]
	[TestCase("READ")]
	[TestCase("Read")]
	[TestCase("read")]
	[TestCase("WRITE")]
	[TestCase("MODIFY")]
	[TestCase("FULL-CONTROL")]
	[TestCase("Full-Control")]
	public void Execute_WhenPermissionHasDifferentCase_ShouldStillParse(string permission) {
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			Assert.Inconclusive("This test only runs on Windows");
			return;
		}

		// Arrange
		string testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(testDir);

		try {
			WindowsIdentity currentUser = WindowsIdentity.GetCurrent();
			string user = currentUser.Name;

			// Act
			AssertionResult result = _sut.Execute(testDir, user, permission);

			// Assert
			result.Reason.Should().NotContain("Invalid permission",
				$"permission '{permission}' should be recognized regardless of case");
		}
		finally {
			// Cleanup
			try {
				Directory.Delete(testDir, true);
			}
			catch {
				// Ignore cleanup errors
			}
		}
	}

	[Test]
	[Description("Should accept 'full' as alias for 'full-control'")]
	public void Execute_WhenPermissionIsFull_ShouldAcceptAsFullControl() {
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			Assert.Inconclusive("This test only runs on Windows");
			return;
		}

		// Arrange
		string testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(testDir);

		try {
			// Get current user
			WindowsIdentity currentUser = WindowsIdentity.GetCurrent();
			string user = currentUser.Name;

			// Act
			AssertionResult result = _sut.Execute(testDir, user, "full");

			// Assert
			// We can't guarantee the current user has full control, but we should not get
			// an "invalid permission" error - we should get either success or permission denied
			result.Reason.Should().NotContain("Invalid permission",
				"'full' should be recognized as a valid permission level");
		}
		finally {
			// Cleanup
			try {
				Directory.Delete(testDir, true);
			}
			catch {
				// Ignore cleanup errors
			}
		}
	}

	[Test]
	[Description("Should return failure for invalid permission level")]
	public void Execute_WhenPermissionIsInvalid_ShouldReturnFailure() {
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			Assert.Inconclusive("This test only runs on Windows");
			return;
		}

		// Arrange
		string path = Path.GetTempPath();
		string user = "BUILTIN\\IIS_IUSRS";
		string permission = "invalid-permission";

		// Act
		AssertionResult result = _sut.Execute(path, user, permission);

		// Assert
		result.Status.Should().Be("fail", "invalid permission level should fail validation");
		result.FailedAt.Should().Be(AssertionPhase.FsPerm, "failure occurs at permission validation");
		result.Reason.Should().Contain("Invalid permission", "error should mention invalid permission");
	}

	[Test]
	[Description("Should return failure when permission parameter is null")]
	public void Execute_WhenPermissionIsNull_ShouldReturnFailure() {
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			Assert.Inconclusive("This test only runs on Windows");
			return;
		}

		// Arrange
		string path = Path.GetTempPath();
		string user = "BUILTIN\\IIS_IUSRS";
		string permission = null;

		// Act
		AssertionResult result = _sut.Execute(path, user, permission);

		// Assert
		result.Status.Should().Be("fail", "null permission should fail validation");
		result.FailedAt.Should().Be(AssertionPhase.FsPerm, "failure occurs at permission validation");
	}

	[Test]
	[Description("Should return failure when user parameter is null")]
	public void Execute_WhenUserIsNull_ShouldReturnFailure() {
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			Assert.Inconclusive("This test only runs on Windows");
			return;
		}

		// Arrange
		string path = Path.GetTempPath();
		string user = null;
		string permission = "full-control";

		// Act
		AssertionResult result = _sut.Execute(path, user, permission);

		// Assert
		result.Status.Should().Be("fail", "null user should fail validation");
		result.FailedAt.Should().Be(AssertionPhase.FsUser, "failure occurs at user validation");
	}

	[Test]
	[Description("Should log validation message when checking permissions")]
	public void Execute_WhenValidatingPermissions_ShouldLogMessage() {
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			Assert.Inconclusive("This test only runs on Windows");
			return;
		}

		// Arrange
		string path = Path.GetTempPath();
		string user = "BUILTIN\\Users";
		string permission = "read";

		// Act
		AssertionResult result = _sut.Execute(path, user, permission);

		// Assert
		_logger.Received(1).WriteInfo(Arg.Is<string>(msg =>
			msg.Contains("Validating permissions") && msg.Contains(user)));
	}

	[SetUp]
	public void SetUp() {
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_logger = Substitute.For<ILogger>();
		_sut = new FsPermissionAssertion(_settingsRepository, _logger);
	}

	#endregion
}

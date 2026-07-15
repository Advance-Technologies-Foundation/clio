using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using Clio.Common.DbHub;
using Clio.Tests.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.DbHub;

[TestFixture]
[Platform("Win")]
[Property("Module", "Common")]
public sealed class DbHubAtomicFileWriterPermissionTests : BaseClioModuleTests {
	private string _directory;
	private IDbHubAtomicFileWriter _sut;

	public override void Setup() {
		base.Setup();
		_directory = Path.Combine(Path.GetTempPath(), $"clio-dbhub-permissions-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_directory);
		_sut = Container.GetRequiredService<IDbHubAtomicFileWriter>();
	}

	public override void TearDown() {
		if (Directory.Exists(_directory)) {
			Directory.Delete(_directory, recursive: true);
		}
		base.TearDown();
	}

	[Test]
	[Description("Atomic TOML replacement preserves the destination Windows access-control descriptor.")]
	public void Commit_ShouldPreserveWindowsAccessControl() {
		// Arrange
		string path = Path.Combine(_directory, "dbhub.toml");
		File.WriteAllText(path, "# original");
		byte[] before = new FileInfo(path).GetAccessControl(AccessControlSections.Access)
			.GetSecurityDescriptorBinaryForm();

		// Act
		_sut.Commit(path, "# replacement");

		// Assert
		byte[] after = new FileInfo(path).GetAccessControl(AccessControlSections.Access)
			.GetSecurityDescriptorBinaryForm();
		after.Should().Equal(before,
			because: "atomic source synchronization must not broaden or discard an existing TOML ACL");
	}

	[Test]
	[Description("New TOML files protect database credentials from inherited broad read permissions.")]
	public void Commit_ShouldCreateProtectedAcl_ForNewFile() {
		// Arrange
		string path = Path.Combine(_directory, "new-dbhub.toml");

		// Act
		_sut.Commit(path, "password = \"secret\"");

		// Assert
		FileSecurity security = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
		security.AreAccessRulesProtected.Should().BeTrue(
			because: "a newly created credential-bearing TOML file must not inherit directory readers");
		SecurityIdentifier[] broadReaders = [
			new(WellKnownSidType.WorldSid, null),
			new(WellKnownSidType.AuthenticatedUserSid, null),
			new(WellKnownSidType.BuiltinUsersSid, null)
		];
		security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
			.Cast<FileSystemAccessRule>()
			.Where(rule => rule.AccessControlType == AccessControlType.Allow)
			.Select(rule => (SecurityIdentifier)rule.IdentityReference)
			.Should().NotIntersectWith(broadReaders,
				because: "unauthenticated dbHub database credentials must remain private to privileged principals");
	}

	[Test]
	[Description("Existing TOML files retain their user-managed ACL even when it includes additional readers.")]
	public void Commit_ShouldPreserveBroadExistingAcl() {
		// Arrange
		string path = Path.Combine(_directory, "broad-dbhub.toml");
		File.WriteAllText(path, "# original");
		FileInfo info = new(path);
		FileSecurity security = info.GetAccessControl();
		security.AddAccessRule(new FileSystemAccessRule(
			new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read,
			AccessControlType.Allow));
		info.SetAccessControl(security);
		byte[] before = info.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();

		// Act
		Action action = () => _sut.Commit(path, "password = \"secret\"");

		// Assert
		action.Should().NotThrow(
			because: "clio must preserve user-managed permissions on an adopted TOML rather than enforce a partial ACL policy");
		info.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm().Should().Equal(before,
			because: "atomic replacement must preserve the complete existing access-control descriptor");
		File.ReadAllText(path).Should().Be("password = \"secret\"",
			because: "the validated update should still be committed");
	}
}

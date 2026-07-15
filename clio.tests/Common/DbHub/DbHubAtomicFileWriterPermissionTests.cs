using System;
using System.IO;
using System.Security.AccessControl;
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
}

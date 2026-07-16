using System;
using System.IO;
using Clio.Common.DbHub;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.DbHub;

[TestFixture]
[Property("Module", "Common")]
public sealed class DbHubTomlAtomicRollbackTests : BaseClioModuleTests {
	private IDbHubAtomicFileWriter _atomicFileWriter;
	private IDbHubTomlStore _sut;
	private string _directory;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_atomicFileWriter = Substitute.For<IDbHubAtomicFileWriter>();
		containerBuilder.AddSingleton(_atomicFileWriter);
	}

	public override void Setup() {
		base.Setup();
		_directory = Path.Combine(Path.GetTempPath(), $"clio-dbhub-rollback-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_directory);
		_sut = Container.GetRequiredService<IDbHubTomlStore>();
	}

	public override void TearDown() {
		if (Directory.Exists(_directory)) {
			Directory.Delete(_directory, recursive: true);
		}
		_atomicFileWriter.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("A failure during atomic replacement leaves the original valid user TOML unchanged.")]
	public void Upsert_ShouldPreserveOriginal_WhenAtomicCommitFails() {
		// Arrange
		string path = Path.Combine(_directory, "dbhub.toml");
		const string original = "# user-owned\n[[sources]]\nid = \"manual\"\ntype = \"postgres\"\ndsn = \"postgres://manual\"\n";
		File.WriteAllText(path, original);
		_atomicFileWriter.When(writer => writer.Commit(path, Arg.Any<string>()))
			.Do(_ => throw new IOException("simulated atomic replace failure"));
		DbHubSourceDefinition source = new("dev", "dev", "postgres", "localhost", 5432, "creatio", "app", "secret");

		// Act
		DbHubSyncResult result = _sut.Upsert(path, source);

		// Assert
		result.Skipped.Should().BeTrue(because: "the atomic writer did not commit the candidate");
		File.ReadAllText(path).Should().Be(original,
			because: "a commit failure must leave every user-authored byte intact");
		$"{result.Warning.Message} {result.Warning.Detail}".Should().NotContain("secret",
			because: "rollback diagnostics must not expose the candidate source password");
	}
}

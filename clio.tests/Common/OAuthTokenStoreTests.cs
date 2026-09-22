using System;
using System.Text.Json;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Unit tests for <see cref="OAuthTokenStore"/> over a substituted <see cref="IFileSystem"/> and
/// <see cref="IFileSecurityHardening"/>, following <c>BrowserSessionCacheTests</c>.
/// </summary>
/// <remarks>
/// The store holds a live refresh token. Two properties matter and both fail silently when regressed:
/// a token written for one client id must never be handed to another (the key discriminates, and the
/// read re-checks), and a file the umask left group- or world-readable must be refused rather than
/// used.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class OAuthTokenStoreTests {

	private IFileSystem _fileSystem;
	private IFileSecurityHardening _hardening;
	private OAuthTokenStore _sut;

	[SetUp]
	public void SetUp() {
		_fileSystem = Substitute.For<IFileSystem>();
		_hardening = Substitute.For<IFileSecurityHardening>();
		_hardening.IsOwnerOnly(Arg.Any<string>()).Returns(true);
		_sut = new OAuthTokenStore(_fileSystem, _hardening);
	}

	private static EnvironmentSettings Env(string uri = "https://work.creatio.com",
		string clientId = "clio-client") =>
		new() { Uri = uri, ClientId = clientId, AuthFlow = OAuthFlow.AuthorizationCode };

	private static OAuthTokenSet Token(string clientId = "clio-client") =>
		new("access", "refresh", DateTimeOffset.UtcNow.AddHours(1), "https://id.example/connect/token",
			clientId, DateTimeOffset.UtcNow);

	[Test]
	[Description("The key is used as a file-name stem, so it must carry no path separators or traversal tokens whatever the environment url looks like.")]
	public void BuildKey_ShouldProduceFilesystemSafeKey_WhenUriContainsSeparators() {
		// Arrange & Act
		string key = _sut.BuildKey(Env(uri: "https://work.creatio.com:8080/0"));

		// Assert
		key.Should().MatchRegex("^[a-z0-9._-]+_[0-9a-f]{16}$");
		key.Should().NotContainAny(["/", "\\", ":", ".."],
			because: "path separators and traversal tokens must never reach the file name");
	}

	[Test]
	[Description("Two OAuth clients on the same environment are different sessions, so they must not share one token file.")]
	public void BuildKey_ShouldDiffer_WhenClientIdDiffers() {
		// Arrange & Act
		string first = _sut.BuildKey(Env(clientId: "client-a"));
		string second = _sut.BuildKey(Env(clientId: "client-b"));

		// Assert
		first.Should().NotBe(second, because: "a token issued to one client must never be reused for another");
	}

	[Test]
	[Description("A round-trip returns what was written, so the write format and the read parser stay in step.")]
	public void TryRead_ShouldReturnTheWrittenToken_WhenTheFileIsIntact() {
		// Arrange
		OAuthTokenSet written = Token();
		string persisted = null;
		_fileSystem.When(fs => fs.WriteOwnerOnlyTextToFileAtomic(Arg.Any<string>(), Arg.Any<string>()))
			.Do(call => persisted = call.ArgAt<string>(1));
		_sut.Write(Env(), written);
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns(_ => persisted);

		// Act
		bool read = _sut.TryRead(Env(), out OAuthTokenSet token);

		// Assert
		read.Should().BeTrue();
		token.AccessToken.Should().Be(written.AccessToken);
		token.RefreshToken.Should().Be(written.RefreshToken);
		token.TokenEndpoint.Should().Be(written.TokenEndpoint);
		token.ClientId.Should().Be(written.ClientId);
	}

	[Test]
	[Description("A file whose recorded client id is not the one being asked for is not this environment's session; handing it over would cross a credential boundary.")]
	public void TryRead_ShouldRefuse_WhenTheStoredClientIdDoesNotMatch() {
		// Arrange
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns(Persist(Token(clientId: "another-client")));

		// Act
		bool read = _sut.TryRead(Env(clientId: "clio-client"), out OAuthTokenSet token);

		// Assert
		read.Should().BeFalse(because: "a token recorded for another client id is not this environment's session");
		token.Should().BeNull();
	}

	[Test]
	[Description("A token file the umask left group- or world-readable is refused loudly rather than used, because a live refresh token must not be read from a file other users can read too.")]
	public void TryRead_ShouldThrow_WhenThePermissionsAreWiderThanOwnerOnly() {
		// Arrange
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_hardening.IsOwnerOnly(Arg.Any<string>()).Returns(false);

		// Act
		Action act = () => _sut.TryRead(Env(), out _);

		// Assert
		act.Should().Throw<UnauthorizedAccessException>().WithMessage("*owner-only*");
	}

	[Test]
	[Description("A truncated or hand-edited file reads as 'no session' instead of crashing the command.")]
	public void TryRead_ShouldReturnFalse_WhenTheFileIsMalformed() {
		// Arrange
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns("{not json");

		// Act
		bool read = _sut.TryRead(Env(), out OAuthTokenSet token);

		// Assert
		read.Should().BeFalse(because: "an unreadable cache is a missing session, not a fatal error");
		token.Should().BeNull();
	}

	[Test]
	[Description("No file means no session; nothing is read and nothing throws.")]
	public void TryRead_ShouldReturnFalse_WhenTheFileIsAbsent() {
		// Arrange
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(false);

		// Act
		bool read = _sut.TryRead(Env(), out OAuthTokenSet token);

		// Assert
		read.Should().BeFalse();
		token.Should().BeNull();
		_fileSystem.DidNotReceive().ReadAllText(Arg.Any<string>());
	}

	[Test]
	[Description("The write is owner-only and atomic, and the directory and file are hardened - a reader must never observe a truncated token, and neither artifact may be left world-readable.")]
	public void Write_ShouldWriteOwnerOnlyAtomically_AndHardenBothArtifacts() {
		// Arrange & Act
		_sut.Write(Env(), Token());

		// Assert
		_fileSystem.Received(1).WriteOwnerOnlyTextToFileAtomic(
			Arg.Is<string>(path => path.EndsWith(".json", StringComparison.Ordinal)), Arg.Any<string>());
		_hardening.Received(1).HardenDirectory(Arg.Any<string>());
		_hardening.Received(1).HardenFile(Arg.Any<string>());
	}

	[Test]
	[Description("Signing out twice must not fail on the second attempt, so Delete tolerates a missing file.")]
	public void Delete_ShouldBeIdempotent_WhenTheFileIsAbsent() {
		// Arrange
		_fileSystem.DeleteFileIfExists(Arg.Any<string>()).Returns(false);

		// Act
		Action act = () => _sut.Delete(Env());

		// Assert
		act.Should().NotThrow();
		_fileSystem.Received(1).DeleteFileIfExists(Arg.Any<string>());
	}

	private static string Persist(OAuthTokenSet token) => JsonSerializer.Serialize(new {
		access_token = token.AccessToken,
		refresh_token = token.RefreshToken,
		expires_at = token.ExpiresAt,
		token_endpoint = token.TokenEndpoint,
		client_id = token.ClientId,
		obtained_at = token.ObtainedAt,
		identity = token.Identity
	});
}

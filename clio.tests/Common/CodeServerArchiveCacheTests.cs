using System;
using System.IO.Abstractions;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
public class CodeServerArchiveCacheTests {
	[Test]
	[Description("EnsureArchiveAvailable should close the temporary download stream before moving the archive into the cache location.")]
	public void EnsureArchiveAvailable_ShouldDownloadArchiveAndMoveItIntoCache() {
		// Arrange
		System.IO.Abstractions.IFileSystem msFileSystem = new System.IO.Abstractions.FileSystem();
		Clio.Common.IFileSystem fileSystem = new Clio.Common.FileSystem(msFileSystem);
		ILogger logger = Substitute.For<ILogger>();
		string tempRoot = msFileSystem.Path.Combine(msFileSystem.Path.GetTempPath(), "code-server-cache-tests", Guid.NewGuid().ToString("N"));
		msFileSystem.Directory.CreateDirectory(tempRoot);
		try {
			using HttpClient httpClient = new(new StubHttpMessageHandler("cached-code-server"));
			CodeServerArchiveCache cache = new(httpClient, fileSystem, msFileSystem, logger, tempRoot);

			// Act
			string archivePath = cache.EnsureArchiveAvailable("4.112.0");

			// Assert
			archivePath.Should().Be(
				msFileSystem.Path.Combine(tempRoot, "docker-assets", "code-server", "4.112.0", "code-server-4.112.0-linux-amd64.tar.gz"),
				"because the cache should return the final archive path after moving the temporary download file into place");
			msFileSystem.File.Exists(archivePath).Should().BeTrue(
				"because the downloaded code-server archive should be written to the cache directory");
			msFileSystem.File.ReadAllText(archivePath).Should().Be("cached-code-server",
				"because the cached archive contents should survive the temporary file move on Windows");
			msFileSystem.File.Exists($"{archivePath}.tmp").Should().BeFalse(
				"because the temporary download file should no longer remain after the archive is moved into place");
		}
		finally {
			if (msFileSystem.Directory.Exists(tempRoot)) {
				msFileSystem.Directory.Delete(tempRoot, true);
			}
		}
	}

	[Test]
	[Description("EnsureArchiveAvailable should reject non-semantic code-server versions before they are used in cache paths or download URLs.")]
	public void EnsureArchiveAvailable_ShouldRejectInvalidVersion() {
		// Arrange
		System.IO.Abstractions.IFileSystem msFileSystem = new System.IO.Abstractions.FileSystem();
		Clio.Common.IFileSystem fileSystem = new Clio.Common.FileSystem(msFileSystem);
		ILogger logger = Substitute.For<ILogger>();
		using HttpClient httpClient = new(new StubHttpMessageHandler());
		CodeServerArchiveCache cache = new(httpClient, fileSystem, msFileSystem, logger, msFileSystem.Path.GetTempPath());

		// Act
		Action act = () => cache.EnsureArchiveAvailable("../../evil");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Unsupported code-server version*",
				"because only semantic versions should be accepted for code-server cache directories and download URLs");
	}

	private sealed class StubHttpMessageHandler(string responseContent = "") : HttpMessageHandler {
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
				Content = new StringContent(responseContent)
			});
		}
	}
}

using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Clio.Common.ScenarioHandlers;
using Clio.Tests.Command;
using FluentAssertions;
using FluentValidation;
using NUnit.Framework;

namespace Clio.Tests.Requests;

[TestFixture]
[Property("Module", "Requests")]
internal class UnzipHandlerTests : BaseClioModuleTests {
	[Test]
	[Description("Validates the unzip request explicitly and rejects invalid scenario handler input before extraction runs.")]
	public async Task Handle_ShouldThrowValidationException_WhenUnzipRequestIsInvalid() {
		// Arrange
		IUnzipHandler handler = Container.GetRequiredService<IUnzipHandler>();
		UnzipRequest request = new() {
			Arguments = []
		};

		// Act
		Func<Task> act = async () => await handler.Handle(request);

		// Assert
		await act.Should().ThrowAsync<ValidationException>(
			because: "the handler should run the registered FluentValidation validator before extracting files");
	}

	[Test]
	[Description("Extracts a valid archive to the target directory when the unzip request passes validation.")]
	public async Task Handle_ShouldExtractArchive_WhenUnzipRequestIsValid() {
		// Arrange
		IUnzipHandler handler = Container.GetRequiredService<IUnzipHandler>();
		string workingDirectory = Path.Combine(Path.GetTempPath(), $"clio-unzip-{Guid.NewGuid():N}");
		string archivePath = Path.Combine(workingDirectory, "archive.zip");
		string destinationDirectory = Path.Combine(workingDirectory, "out");
		string extractedDirectory = Path.Combine(destinationDirectory, "nested");
		Directory.CreateDirectory(workingDirectory);
		Directory.CreateDirectory(destinationDirectory);
		await using (FileStream archiveStream = File.Create(archivePath))
		using (ZipArchive archive = new(archiveStream, ZipArchiveMode.Create)) {
			archive.CreateEntry("nested/");
		}
		UnzipRequest request = new() {
			Arguments = new() {
				["from"] = archivePath,
				["to"] = destinationDirectory
			}
		};

		try {
			// Act
			var result = await handler.Handle(request);

			// Assert
			Directory.Exists(extractedDirectory).Should().BeTrue(
				because: "a valid request should pass validation and execute the unzip handler");
			result.Value.Should().NotBeNull(because: "the handler should return a success response");
		}
		finally {
			if (Directory.Exists(workingDirectory)) {
				Directory.Delete(workingDirectory, true);
			}
		}
	}
}

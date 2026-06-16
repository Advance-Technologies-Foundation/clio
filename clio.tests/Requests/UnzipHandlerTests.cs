using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
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

	// Replace the production ConsoleLogger with a NullLogger so the handler's spinner is a
	// deterministic no-op. Otherwise BeginSpinner would animate via Console.SetCursorPosition on
	// any runner where stdout is not redirected, making the tests OS/terminal-dependent.
	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton<Clio.Common.ILogger>(new Clio.Common.NullLogger());
	}

	#region Methods: Private

	private static byte[] CreateZipBytes(Action<ZipArchive> build) {
		using MemoryStream memoryStream = new();
		using (ZipArchive archive = new(memoryStream, ZipArchiveMode.Create, leaveOpen: true)) {
			build(archive);
		}
		return memoryStream.ToArray();
	}

	private static byte[] CreateEmptyZipBytes() {
		return CreateZipBytes(_ => { });
	}

	// The validator (out of scope for this pilot) still calls the concrete System.IO.File.Exists,
	// so the source archive must exist on the real disk as well as in the injected MockFileSystem.
	private static string CreateRealSourceTouch(string workingDirectory, string fileName) {
		Directory.CreateDirectory(workingDirectory);
		string realPath = Path.Combine(workingDirectory, fileName);
		File.WriteAllBytes(realPath, Array.Empty<byte>());
		return realPath;
	}

	#endregion

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
	[Description("Creates the destination directory when it does not exist before extracting, proving the corrected directory-creation logic.")]
	public async Task Handle_ShouldCreateDestinationDirectory_WhenDestinationDoesNotExist() {
		// Arrange
		IUnzipHandler handler = Container.GetRequiredService<IUnzipHandler>();
		string workingDirectory = Path.Combine(Path.GetTempPath(), $"clio-unzip-{Guid.NewGuid():N}");
		// Empty zip: the extraction loop body never runs, so ONLY the corrected line-88 logic can
		// create the destination directory. The previous inverted logic created it only when it
		// already existed, so this test fails against the bug and passes against the fix.
		byte[] zipBytes = CreateEmptyZipBytes();
		string archivePath = CreateRealSourceTouch(workingDirectory, "archive.zip");
		string destinationDirectory = Path.Combine(workingDirectory, "out");
		FileSystem.AddFile(archivePath, new MockFileData(zipBytes));

		try {
			// Act
			var result = await handler.Handle(new UnzipRequest {
				Arguments = new() {
					["from"] = archivePath,
					["to"] = destinationDirectory
				}
			});

			// Assert
			FileSystem.Directory.Exists(destinationDirectory).Should().BeTrue(
				because: "the handler must create the destination directory when it is missing");
			result.IsT0.Should().BeTrue(
				because: "a readable archive yields an UnzipResponse, not a HandlerError");
			result.AsT0.Status.Should().Be(BaseHandlerResponse.CompletionStatus.Success,
				because: "a valid request with a readable archive should complete successfully");
		}
		finally {
			if (Directory.Exists(workingDirectory)) {
				Directory.Delete(workingDirectory, true);
			}
		}
	}

	[Test]
	[Description("Extracts a valid archive to the target directory when the unzip request passes validation.")]
	public async Task Handle_ShouldExtractArchive_WhenUnzipRequestIsValid() {
		// Arrange
		IUnzipHandler handler = Container.GetRequiredService<IUnzipHandler>();
		string workingDirectory = Path.Combine(Path.GetTempPath(), $"clio-unzip-{Guid.NewGuid():N}");
		string destinationDirectory = Path.Combine(workingDirectory, "out");
		string extractedDirectory = Path.Combine(destinationDirectory, "nested");
		// The handler now reads the archive through the injected MockFileSystem, so the zip is seeded
		// in-memory; a real touch satisfies the (out-of-scope) validator's concrete File.Exists check.
		byte[] zipBytes = CreateZipBytes(archive => archive.CreateEntry("nested/"));
		string archivePath = CreateRealSourceTouch(workingDirectory, "archive.zip");
		FileSystem.AddFile(archivePath, new MockFileData(zipBytes));
		FileSystem.Directory.CreateDirectory(destinationDirectory);

		try {
			// Act
			var result = await handler.Handle(new UnzipRequest {
				Arguments = new() {
					["from"] = archivePath,
					["to"] = destinationDirectory
				}
			});

			// Assert
			FileSystem.Directory.Exists(extractedDirectory).Should().BeTrue(
				because: "a valid request should pass validation and execute the unzip handler against the mocked file system");
			result.IsT0.Should().BeTrue(
				because: "a readable archive yields an UnzipResponse, not a HandlerError");
			result.AsT0.Status.Should().Be(BaseHandlerResponse.CompletionStatus.Success,
				because: "the handler should complete extraction successfully");
		}
		finally {
			if (Directory.Exists(workingDirectory)) {
				Directory.Delete(workingDirectory, true);
			}
		}
	}

	[Test]
	[Category("Integration")]
	[Description("Extracts a real file entry from a zip archive to disk and verifies its contents, exercising the per-entry ExtractToFile byte-write path that MockFileSystem cannot cover.")]
	public async Task Handle_ShouldExtractFileEntry_WhenArchiveContainsFile() {
		// Arrange
		// This test uses the REAL filesystem: entry.ExtractToFile writes bytes via concrete
		// System.IO, so MockFileSystem (used by the Unit tests) cannot exercise this path.
		string workingDirectory = Path.Combine(Path.GetTempPath(), $"clio-unzip-int-{Guid.NewGuid():N}");
		Directory.CreateDirectory(workingDirectory);
		try {
			const string entryName = "hello.txt";
			byte[] expectedBytes = System.Text.Encoding.UTF8.GetBytes("Hello, clio!");
			string archivePath = Path.Combine(workingDirectory, "archive.zip");
			using (FileStream zipStream = File.Create(archivePath))
			using (ZipArchive archive = new(zipStream, ZipArchiveMode.Create)) {
				ZipArchiveEntry entry = archive.CreateEntry(entryName);
				using Stream entryStream = entry.Open();
				entryStream.Write(expectedBytes, 0, expectedBytes.Length);
			}
			string destinationDirectory = Path.Combine(workingDirectory, "out");

			IUnzipHandler handler = new UnzipRequestHandler(
				new UnzipRequestValidator(),
				new System.IO.Abstractions.FileSystem(),
				new Clio.Common.NullLogger());

			// Act
			var result = await handler.Handle(new UnzipRequest {
				Arguments = new() {
					["from"] = archivePath,
					["to"] = destinationDirectory
				}
			});

			// Assert
			result.IsT0.Should().BeTrue(
				because: "a readable archive yields an UnzipResponse, not a HandlerError");
			string extractedFile = Path.Combine(destinationDirectory, entryName);
			File.Exists(extractedFile).Should().BeTrue(
				because: "the file entry must be written to disk via the ExtractToFile byte-write path");
			File.ReadAllBytes(extractedFile).Should().Equal(expectedBytes,
				because: "the extracted file must contain exactly the bytes stored in the archive entry");
		}
		finally {
			if (Directory.Exists(workingDirectory)) {
				Directory.Delete(workingDirectory, true);
			}
		}
	}
}

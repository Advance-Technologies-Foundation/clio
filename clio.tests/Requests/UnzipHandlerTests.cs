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
			result.Value.Should().NotBeNull(
				because: "a valid request with a readable archive should return a success response");
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
			result.Value.Should().NotBeNull(because: "the handler should return a success response");
		}
		finally {
			if (Directory.Exists(workingDirectory)) {
				Directory.Delete(workingDirectory, true);
			}
		}
	}
}

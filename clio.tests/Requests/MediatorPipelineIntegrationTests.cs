using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Clio.Common.ScenarioHandlers;
using Clio.Tests.Command;
using FluentAssertions;
using FluentValidation;
using MediatR;
using NUnit.Framework;

namespace Clio.Tests.Requests;

[TestFixture]
internal class MediatorPipelineIntegrationTests : BaseClioModuleTests {
	[Test]
	[Description("Applies ValidationBehaviour to mediator requests and rejects invalid scenario handler input before the handler runs.")]
	public async Task Send_Should_Throw_ValidationException_For_Invalid_Unzip_Request() {
		// Arrange
		IMediator mediator = Container.GetRequiredService<IMediator>();
		UnzipRequest request = new() {
			Arguments = []
		};

		// Act
		Func<Task> act = async () => await mediator.Send(request);

		// Assert
		await act.Should().ThrowAsync<ValidationException>(
			because: "the open MediatR validation behavior should execute registered FluentValidation validators before handler execution");
	}

	[Test]
	[Description("Dispatches valid scenario handler requests through MediatR and reaches the registered handler implementation.")]
	public async Task Send_Should_Run_Unzip_Handler_For_Valid_Request() {
		// Arrange
		IMediator mediator = Container.GetRequiredService<IMediator>();
		string workingDirectory = Path.Combine(Path.GetTempPath(), $"clio-mediatr-{Guid.NewGuid():N}");
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
			var result = await mediator.Send(request);

			// Assert
			Directory.Exists(extractedDirectory).Should().BeTrue(
				because: "a valid request should pass through the MediatR pipeline and execute the unzip handler");
			result.Value.Should().NotBeNull(because: "the handler should return a success response through MediatR");
		}
		finally {
			if (Directory.Exists(workingDirectory)) {
				Directory.Delete(workingDirectory, true);
			}
		}
	}
}

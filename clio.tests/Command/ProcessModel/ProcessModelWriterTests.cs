using System;
using System.IO;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

[TestFixture]
[Property("Module", "ProcessModel")]
[Category("Unit")]
public sealed class ProcessModelWriterTests {

	[Test]
	[Description("Treats destination paths without a file name as folders and writes the generated process model into <Code>.cs inside that folder.")]
	public void WriteFileFromModel_Should_Write_Process_File_Into_Destination_Folder() {
		// Arrange
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		ProcessModelWriter writer = new(fileSystem);
		Clio.Command.ProcessModel.ProcessModel processModel = CreateProcessModel("UsrFolderProcess");
		string destinationFolder = "generated";
		string expectedFilePath = Path.Combine(destinationFolder, "UsrFolderProcess.cs");
		fileSystem.Combine(destinationFolder, "UsrFolderProcess.cs").Returns(expectedFilePath);
		fileSystem.ExistsFile(expectedFilePath).Returns(false);

		// Act
		writer.WriteFileFromModel(processModel, "Contoso.ProcessModels", destinationFolder, "en-US");

		// Assert
		fileSystem.Received(1).CreateDirectoryIfNotExists(destinationFolder);
		fileSystem.Received(1).WriteAllTextToFile(
			expectedFilePath,
			Arg.Is<string>(content => content.Contains("namespace Contoso.ProcessModels")));
		fileSystem.DidNotReceive().DeleteFile(Arg.Any<string>());
	}

	[Test]
	[Description("Treats destination paths with a file name as explicit file targets and preserves that file name instead of appending <Code>.cs.")]
	public void WriteFileFromModel_Should_Respect_Explicit_File_Path() {
		// Arrange
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		ProcessModelWriter writer = new(fileSystem);
		Clio.Command.ProcessModel.ProcessModel processModel = CreateProcessModel("UsrExplicitFileProcess");
		string explicitFilePath = Path.Combine("generated", "custom-process-model.cs");
		fileSystem.ExistsFile(explicitFilePath).Returns(true);

		// Act
		writer.WriteFileFromModel(processModel, "Contoso.ProcessModels", explicitFilePath, "en-US");

		// Assert
		fileSystem.Received(1).CreateDirectoryIfNotExists("generated");
		fileSystem.DidNotReceive().Combine(explicitFilePath, Arg.Any<string>());
		fileSystem.Received(1).DeleteFile(explicitFilePath);
		fileSystem.Received(1).WriteAllTextToFile(
			explicitFilePath,
			Arg.Is<string>(content => content.Contains("public class UsrExplicitFileProcess")));
	}

	[Test]
	[Description("Keeps paths that already exist as directories in folder mode even when the directory name contains a dot.")]
	public void WriteFileFromModel_Should_Treat_Existing_Dotted_Directory_As_Folder() {
		// Arrange
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		ProcessModelWriter writer = new(fileSystem);
		Clio.Command.ProcessModel.ProcessModel processModel = CreateProcessModel("UsrDottedFolderProcess");
		string destinationFolder = "generated.v1";
		string expectedFilePath = Path.Combine(destinationFolder, "UsrDottedFolderProcess.cs");
		fileSystem.ExistsDirectory(destinationFolder).Returns(true);
		fileSystem.Combine(destinationFolder, "UsrDottedFolderProcess.cs").Returns(expectedFilePath);
		fileSystem.ExistsFile(expectedFilePath).Returns(false);

		// Act
		writer.WriteFileFromModel(processModel, "Contoso.ProcessModels", destinationFolder, "en-US");

		// Assert
		fileSystem.Received(1).CreateDirectoryIfNotExists(destinationFolder);
		fileSystem.Received(1).WriteAllTextToFile(
			expectedFilePath,
			Arg.Is<string>(content => content.Contains("UsrDottedFolderProcess")));
	}

	[Test]
	[Description("Emits nested collection item types fully qualified so a Color property inside a composite-object-list parameter compiles without a System.Drawing import.")]
	public void WriteFileFromModel_Should_Emit_Nested_Color_Item_Fully_Qualified() {
		// Arrange
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		ProcessModelWriter writer = new(fileSystem);
		Clio.Command.ProcessModel.ProcessModel processModel = CreateProcessModel("UsrNestedColorProcess");
		processModel.Parameters = [
			new ProcessParameter {
				Name = "Palette",
				Direction = ProcessParameterDirection.Input,
				DataValueType = CompositeObjectListDataValueTypeUId,
				ItemProperties = [
					new ProcessParameter {
						Name = "Shade",
						DataValueType = ColorDataValueTypeUId
					}
				]
			}
		];
		string explicitFilePath = Path.Combine("generated", "nested-color-model.cs");
		fileSystem.ExistsFile(explicitFilePath).Returns(true);

		// Act
		writer.WriteFileFromModel(processModel, "Contoso.ProcessModels", explicitFilePath, "en-US");

		// Assert
		fileSystem.Received(1).WriteAllTextToFile(
			explicitFilePath,
			Arg.Is<string>(content =>
				content.Contains("public System.Drawing.Color Shade {get; set;}") &&
				!content.Contains("public Color Shade")));
	}

	private static readonly Guid CompositeObjectListDataValueTypeUId =
		DataValueTypeMap.CompositeObjectListDataValueTypeUId;

	//ColorDataValueTypeUId is private on DataValueTypeMap, so the fixture repeats the literal.
	private static readonly Guid ColorDataValueTypeUId =
		new("DAFB71F9-EE9F-4e0b-A4D7-37AA15987155");

	private static Clio.Command.ProcessModel.ProcessModel CreateProcessModel(string code) =>
		new(Guid.NewGuid(), code) {
			Name = code,
			Description = $"{code} description",
			Parameters = []
		};
}

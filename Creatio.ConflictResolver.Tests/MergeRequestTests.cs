namespace Creatio.ConflictResolver.Tests;

[TestFixture, Category("Unit")]
public class MergeRequestTests
{
	[Test]
	public void Constructor_DetectsDescriptor_FromFilePath()
	{
		const string filePath =
			@"C:\Windows\Temp\3\IIS APPPOOL_newtide\Default\TideWorkingCopy\Repositories\ParallelDev\packages\ParallelDev\Schemas\ParallelDev_FormPage\descriptor.json";

		var request = new global::Creatio.ConflictResolver.MergeRequest("base", "local", "remote", filePath);

		Assert.That(request.FileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.DescriptorJson));
		Assert.That(request.FilePath, Is.EqualTo(filePath));
	}

	[Test]
	public void Constructor_DetectsMetadata_FromFilePath()
	{
		const string filePath =
			@"C:\Windows\Temp\3\IIS APPPOOL_newtide\Default\TideWorkingCopy\Repositories\ParallelDev\packages\ParallelDev\Schemas\ParallelDev_FormPage\metadata.json";

		var request = new global::Creatio.ConflictResolver.MergeRequest("base", "local", "remote", filePath);

		Assert.That(request.FileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.MetadataJson));
	}

	[Test]
	public void Constructor_DetectsClientUnitJs_FromFilePath()
	{
		const string filePath =
			@"C:\Windows\Temp\3\IIS APPPOOL_newtide\Default\TideWorkingCopy\Repositories\ParallelDev\packages\ParallelDev\Schemas\ParallelDev_FormPage\ParallelDev_FormPage.js";

		var request = new global::Creatio.ConflictResolver.MergeRequest("base", "local", "remote", filePath);

		Assert.That(request.FileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.ClientUnitJs));
	}

	[Test]
	public void Constructor_DetectsResourceXml_FromFilePath()
	{
		const string filePath =
			@"C:\Windows\Temp\3\IIS APPPOOL_newtide\Default\TideWorkingCopy\Repositories\ParallelDev\packages\ParallelDev\Resources\ParallelDev.Entity\resource.en-US.xml";

		var request = new global::Creatio.ConflictResolver.MergeRequest("base", "local", "remote", filePath);

		Assert.That(request.FileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.ResourceXml));
	}

	[Test]
	public void Constructor_DetectsPropertiesJson_FromFilePath()
	{
		const string filePath =
			@"C:\Windows\Temp\3\IIS APPPOOL_newtide\Default\TideWorkingCopy\Repositories\ParallelDev\packages\ParallelDev\Schemas\RestService_1_Remote\properties.json";

		var request = new global::Creatio.ConflictResolver.MergeRequest("base", "local", "remote", filePath);

		Assert.That(request.FileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.PropertiesJson));
	}

	[Test]
	public void Constructor_DetectsDataJson_FromFilePath()
	{
		const string filePath =
			@"C:\Windows\Temp\3\IIS APPPOOL_newtide\Default\TideWorkingCopy\Repositories\ParallelDev\packages\ParallelDev\Data\data.json";

		var request = new global::Creatio.ConflictResolver.MergeRequest("base", "local", "remote", filePath);

		Assert.That(request.FileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.DataBinding));
	}

	[Test]
	public void Constructor_DetectsLocalizedDataJson_FromFilePath()
	{
		const string filePath =
			@"C:\Windows\Temp\3\IIS APPPOOL_newtide\Default\TideWorkingCopy\Repositories\ParallelDev\packages\ParallelDev\Data\data.en-US.json";

		var request = new global::Creatio.ConflictResolver.MergeRequest("base", "local", "remote", filePath);

		Assert.That(request.FileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.DataBinding));
	}

	[Test]
	public void Constructor_DetectsSqlScript_FromFilePath()
	{
		const string filePath =
			@"C:\Windows\Temp\3\IIS APPPOOL_newtide\Default\TideWorkingCopy\Repositories\ParallelDev\packages\ParallelDev\SqlScripts\update_structure.sql";

		var request = new global::Creatio.ConflictResolver.MergeRequest("base", "local", "remote", filePath);

		Assert.That(request.FileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.SqlScript));
	}

	[Test]
	public void Constructor_DetectsSourceCode_FromFilePath()
	{
		const string filePath =
			@"C:\Windows\Temp\3\IIS APPPOOL_newtide\Default\TideWorkingCopy\Repositories\ParallelDev\packages\ParallelDev\Src\Services\ConflictResolverService.cs";

		var request = new global::Creatio.ConflictResolver.MergeRequest("base", "local", "remote", filePath);

		Assert.That(request.FileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.SourceCode));
	}

	// A process-schema descriptor.json declares ManagerName == "ProcessSchemaManager".
	private const string ProcessDescriptorContent =
		"{ \"Descriptor\": { \"Name\": \"UsrProcess_a1b2c3\", \"ManagerName\": \"ProcessSchemaManager\" } }";

	// A regular (non-process) schema descriptor.json — different ManagerName.
	private const string SourceCodeDescriptorContent =
		"{ \"Descriptor\": { \"Name\": \"SettingsManager\", \"ManagerName\": \"SourceCodeSchemaManager\" } }";

	[Test]
	public void TryDetectFileTypeFromPath_DetectsProcessMetadata_WhenSiblingDescriptorIsProcessSchema()
	{
		const string filePath =
			@"C:\Windows\Temp\3\Repositories\ParallelDev\packages\ParallelDev\Schemas\UsrProcess_a1b2c3\metadata.json";

		var detected = global::Creatio.ConflictResolver.MergeRequest.TryDetectFileTypeFromPath(
			filePath,
			descriptorPath => descriptorPath.EndsWith("descriptor.json", StringComparison.OrdinalIgnoreCase)
				? ProcessDescriptorContent
				: null,
			out var fileType);

		Assert.That(detected, Is.True);
		Assert.That(fileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.ProcessMetadataJson));
	}

	[Test]
	public void TryDetectFileTypeFromPath_ReadsDescriptorFromSameFolderAsMetadata()
	{
		const string filePath =
			@"C:\Packages\ParallelDev\Schemas\UsrProcess_a1b2c3\metadata.json";

		string? requestedPath = null;
		global::Creatio.ConflictResolver.MergeRequest.TryDetectFileTypeFromPath(
			filePath,
			descriptorPath =>
			{
				requestedPath = descriptorPath;
				return ProcessDescriptorContent;
			},
			out _);

		Assert.That(requestedPath, Is.EqualTo("C:/Packages/ParallelDev/Schemas/UsrProcess_a1b2c3/descriptor.json"));
	}

	[Test]
	public void TryDetectFileTypeFromPath_DetectsProcessMetadata_WhenSiblingDescriptorIsConflicted()
	{
		const string filePath =
			@"C:\Packages\ParallelDev\Schemas\UsrProcess_a1b2c3\metadata.json";

		// The sibling descriptor.json may itself be mid-merge: detection must still work through
		// conflict markers and arbitrary whitespace around the ManagerName property.
		const string conflictedDescriptor =
			"{\n" +
			"  \"Descriptor\": {\n" +
			"<<<<<<< Local\n" +
			"    \"ManagerName\"   :    \"ProcessSchemaManager\",\n" +
			"=======\n" +
			"    \"ManagerName\": \"ProcessSchemaManager\",\n" +
			">>>>>>> Remote\n" +
			"    \"Name\": \"UsrProcess_a1b2c3\"\n" +
			"  }\n" +
			"}";

		var detected = global::Creatio.ConflictResolver.MergeRequest.TryDetectFileTypeFromPath(
			filePath,
			_ => conflictedDescriptor,
			out var fileType);

		Assert.That(detected, Is.True);
		Assert.That(fileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.ProcessMetadataJson));
	}

	[Test]
	public void TryDetectFileTypeFromPath_DetectsPlainMetadata_WhenSiblingDescriptorIsNotProcessSchema()
	{
		const string filePath =
			@"C:\Packages\ParallelDev\Schemas\SettingsManager\metadata.json";

		var detected = global::Creatio.ConflictResolver.MergeRequest.TryDetectFileTypeFromPath(
			filePath,
			_ => SourceCodeDescriptorContent,
			out var fileType);

		Assert.That(detected, Is.True);
		Assert.That(fileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.MetadataJson));
	}

	[Test]
	public void TryDetectFileTypeFromPath_DetectsPlainMetadata_WhenSiblingDescriptorMissing()
	{
		const string filePath =
			@"C:\Packages\ParallelDev\Schemas\SettingsManager\metadata.json";

		var detected = global::Creatio.ConflictResolver.MergeRequest.TryDetectFileTypeFromPath(
			filePath,
			_ => null,
			out var fileType);

		Assert.That(detected, Is.True);
		Assert.That(fileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.MetadataJson));
	}

	[Test]
	public void Constructor_DetectsProcessMetadata_FromRealSiblingDescriptorOnDisk()
	{
		var schemaDirectory = Path.Combine(
			Path.GetTempPath(),
			"CreatioConflictResolverTests",
			Guid.NewGuid().ToString("N"),
			"UsrProcess_a1b2c3");
		Directory.CreateDirectory(schemaDirectory);
		try
		{
			File.WriteAllText(Path.Combine(schemaDirectory, "descriptor.json"), ProcessDescriptorContent);
			var metadataPath = Path.Combine(schemaDirectory, "metadata.json");

			var request = new global::Creatio.ConflictResolver.MergeRequest("base", "local", "remote", metadataPath);

			Assert.That(request.FileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.ProcessMetadataJson));
		}
		finally
		{
			Directory.Delete(schemaDirectory, recursive: true);
		}
	}

	[TestCase(@"C:\Packages\ParallelDev\Resources\UsrProcess_a1b2c3.Process\resource.en-US.xml")]
	[TestCase(@"C:/Packages/ParallelDev/Resources/UsrProcess_b06932c.Process/resource.ru-RU.xml")]
	[TestCase(@"C:\Packages\ParallelDev\Resources\Process_SapAccountSync.Process\resource.en-US.xml")]
	[TestCase(@"C:\Packages\ParallelDev\Resources\MrktZRASyncConversationsProcess.Process\resource.en-US.xml")]
	[TestCase(@"C:\Packages\ParallelDev\Resources\labProcess_3085fd9.Process\resource.en-US.xml")]
	[Description("Classifies every Creatio process resource folder by its .Process suffix, independent of naming prefix.")]
	public void TryDetectFileTypeFromPath_DetectsProcessResourceXml_ForProcessResourceFolder(string filePath)
	{
		var detected = global::Creatio.ConflictResolver.MergeRequest.TryDetectFileTypeFromPath(
			filePath,
			out var fileType);

		Assert.That(detected, Is.True);
		Assert.That(fileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.ProcessResourceXml));
	}

	[TestCase(@"C:\Packages\ParallelDev\Resources\GitManager.SourceCode\resource.en-US.xml")]
	[TestCase(@"C:\Packages\ParallelDev\Resources\GitRepository.Entity\resource.en-US.xml")]
	public void TryDetectFileTypeFromPath_DetectsPlainResourceXml_ForNonProcessResourceFolder(string filePath)
	{
		var detected = global::Creatio.ConflictResolver.MergeRequest.TryDetectFileTypeFromPath(
			filePath,
			out var fileType);

		Assert.That(detected, Is.True);
		Assert.That(fileType, Is.EqualTo(global::Creatio.ConflictResolver.ConflictFileType.ResourceXml));
	}

	[Test]
	public void TryDetectFileTypeFromPath_ReturnsFalse_ForMalformedLocalizedDataFileName()
	{
		var detected = global::Creatio.ConflictResolver.MergeRequest.TryDetectFileTypeFromPath(
			@"C:\Packages\ParallelDev\Data\data..json",
			out var fileType);

		Assert.That(detected, Is.False);
		Assert.That(fileType, Is.EqualTo(default(global::Creatio.ConflictResolver.ConflictFileType)));
	}

	[Test]
	public void TryDetectFileTypeFromPath_ReturnsFalse_ForUnknownPath()
	{
		var detected = global::Creatio.ConflictResolver.MergeRequest.TryDetectFileTypeFromPath(
			@"C:\Packages\ParallelDev\Schemas\ParallelDev_FormPage\something.txt",
			out var fileType);

		Assert.That(detected, Is.False);
		Assert.That(fileType, Is.EqualTo(default(global::Creatio.ConflictResolver.ConflictFileType)));
	}

	[Test]
	public void Constructor_Throws_ForUnknownPath()
	{
		var exception = Assert.Throws<ArgumentException>(() =>
			new global::Creatio.ConflictResolver.MergeRequest(
				"base",
				"local",
				"remote",
				@"C:\Packages\ParallelDev\Schemas\ParallelDev_FormPage\something.txt"));

		Assert.That(exception!.ParamName, Is.EqualTo("filePath"));
	}
}

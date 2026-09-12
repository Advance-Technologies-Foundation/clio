using System;
using System.IO;
using System.Linq;
using System.Text;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Property("Module", "Command")]
public sealed class DownloadSysSettingFileTests : BaseCommandTests<DownloadSysSettingFileOptions> {
	private ISysSettingsManager _settings;
	private IDataProvider _provider;
	private DownloadSysSettingFileCommand _command;
	private string _destination;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_settings = Substitute.For<ISysSettingsManager>();
		_provider = Substitute.For<IDataProvider>();
		services.AddSingleton(_settings);
		services.AddSingleton(_provider);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<DownloadSysSettingFileCommand>();
		_destination = Path.Combine(Path.GetTempPath(), "download873", "chosen.xml");
		FileSystem.Directory.CreateDirectory(Path.GetDirectoryName(_destination)!);
		_settings.GetSysSettingTypeByCode("Blob").Returns(("Binary", (string)null));
		_provider.GetSysSettingValue<string>("Blob").Returns(Convert.ToBase64String([0, 255, 17, 13, 10]));
	}

	[TearDown]
	public override void TearDown() {
		_settings.ClearReceivedCalls();
		_provider.ClearReceivedCalls();
		base.TearDown();
	}

	[TestCase("binary")]
	[TestCase("json")]
	[TestCase("xml")]
	[Description("Download preserves all bytes regardless of the chosen filename and content encoding.")]
	public void Download_ShouldPreserveBytes_WhenDestinationExtensionDiffers(string kind) {
		// Arrange
		byte[] bytes = kind switch {
			"json" => Encoding.UTF8.GetBytes("{\"name\":\"日本語\"}\r\n"),
			"xml" => Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("<name>Ω</name>\n")).ToArray(),
			_ => Enumerable.Range(0, 256).Select(i => (byte)i).ToArray()
		};
		_provider.GetSysSettingValue<string>("Blob").Returns(Convert.ToBase64String(bytes));
		// Act
		DownloadSysSettingFileReceipt result = _command.Download(new() { Code = "Blob", FileName = _destination });
		// Assert
		FileSystem.File.ReadAllBytes(_destination).Should().Equal(bytes, because: "download is a byte copy without text conversion");
		result.FileName.Should().Be(_destination, because: "the caller chooses the entire filename");
		result.ByteCount.Should().Be(bytes.Length, because: "metadata counts decoded bytes");
		FileSystem.Directory.GetFiles(Path.GetDirectoryName(_destination)!).Should().Equal([_destination],
			because: "successful publication must not leave temporary files");
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase(" ")]
	[Description("A filename is mandatory before a download can start.")]
	public void Download_ShouldRejectMissingFilename_WhenNullOrBlank(string fileName) {
		// Arrange
		Action act = () => _command.Download(new() { Code = "Blob", FileName = fileName });
		// Act / Assert
		act.Should().Throw<ArgumentException>(because: "download must not guess a filename");
		_provider.ReceivedCalls().Should().BeEmpty(because: "invalid paths must be refused before remote reads");
	}

	[TestCase("Text")]
	[TestCase("SecureText")]
	[TestCase(null)]
	[Description("Missing and non-Binary settings cannot be exported through the Binary read path.")]
	public void Download_ShouldRejectWrongType_WhenSettingIsNotBinary(string type) {
		// Arrange
		_settings.GetSysSettingTypeByCode("Blob").Returns(type is null ? null : (type, (string)null));
		Action act = () => _command.Download(new() { Code = "Blob", FileName = _destination });
		// Act / Assert
		act.Should().Throw<ArgumentException>(because: "only a known Binary definition authorizes blob decoding");
		_provider.ReceivedCalls().Should().BeEmpty(because: "non-Binary values must not be read or leaked");
		FileSystem.File.Exists(_destination).Should().BeFalse(because: "a refused read must not create a file");
	}

	[Test]
	[Description("Existing output bytes are preserved when a caller attempts to download onto them.")]
	public void Download_ShouldPreserveDestination_WhenFileExists() {
		// Arrange
		FileSystem.File.WriteAllBytes(_destination, [42]);
		Action act = () => _command.Download(new() { Code = "Blob", FileName = _destination });
		// Act / Assert
		act.Should().Throw<IOException>(because: "overwriting is not supported");
		FileSystem.File.ReadAllBytes(_destination).Should().Equal([42], because: "the existing file belongs to the caller");
		_provider.ReceivedCalls().Should().BeEmpty(because: "existing output is detected before reading remotely");
	}

	[TestCase("")]
	[TestCase(null)]
	[Description("No value is an explicit failure rather than a successful empty download.")]
	public void Download_ShouldLeaveNoFile_WhenValueIsEmpty(string value) {
		// Arrange
		_provider.GetSysSettingValue<string>("Blob").Returns(value);
		Action act = () => _command.Download(new() { Code = "Blob", FileName = _destination });
		// Act / Assert
		act.Should().Throw<InvalidOperationException>(because: "an empty server value is not a downloadable blob");
		FileSystem.File.Exists(_destination).Should().BeFalse(because: "the absence of data must not publish output");
	}

	[Test]
	[Description("An invalid or oversized payload is refused before local publication.")]
	public void Download_ShouldLeaveNoFile_WhenBinaryValidationFails() {
		// Arrange
		_provider.GetSysSettingValue<string>("Blob").Returns("not base64!");
		Action act = () => _command.Download(new() { Code = "Blob", FileName = _destination });
		// Act / Assert
		act.Should().Throw<FormatException>(because: "invalid Base64 is not file content");
		FileSystem.File.Exists(_destination).Should().BeFalse(because: "invalid content must never be published");
	}

	[TestCase(0)]
	[TestCase(1)]
	[TestCase(3)]
	[Description("The exact byte limit is accepted while either Base64 padding case above the limit is rejected.")]
	public void Download_ShouldEnforceByteLimit_WhenValueIsAtBoundary(int extraBytes) {
		// Arrange
		byte[] bytes = new byte[SysSettingsManager.MaxBinaryValueBytes + extraBytes];
		_provider.GetSysSettingValue<string>("Blob").Returns(Convert.ToBase64String(bytes));
		Action act = () => _command.Download(new() { Code = "Blob", FileName = _destination });
		// Act / Assert
		if (extraBytes == 0) {
			act.Should().NotThrow(because: "padding must not cause a value exactly at the limit to be refused");
			FileSystem.File.ReadAllBytes(_destination).LongLength.Should().Be(bytes.LongLength, because: "all allowed bytes must be saved");
		} else {
			act.Should().Throw<InvalidOperationException>(because: "the decoded byte count must not exceed the limit");
			FileSystem.File.Exists(_destination).Should().BeFalse(because: "oversized values are refused before publication");
		}
	}

	[Test]
	[Description("A destination created during the remote read wins and the download's temporary file is removed.")]
	public void Download_ShouldPreserveRacingFile_WhenDestinationAppearsDuringRead() {
		// Arrange
		_provider.GetSysSettingValue<string>("Blob").Returns(_ => {
			FileSystem.File.WriteAllBytes(_destination, [42]);
			return Convert.ToBase64String([1, 2, 3]);
		});
		Action act = () => _command.Download(new() { Code = "Blob", FileName = _destination });
		// Act / Assert
		act.Should().Throw<IOException>(because: "publication must atomically refuse an existing destination");
		FileSystem.File.ReadAllBytes(_destination).Should().Equal([42], because: "the concurrently created file must remain intact");
		FileSystem.Directory.GetFiles(Path.GetDirectoryName(_destination)!).Should().Equal([_destination],
			because: "failed publication must remove the temporary download");
	}
}


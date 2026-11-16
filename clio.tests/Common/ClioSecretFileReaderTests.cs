using System;
using System.IO;
using System.Text;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Pins how clio reads a secret a host placed in its secrets folder by name: what it accepts, every refusal, and that
/// no refusal ever repeats the content.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class ClioSecretFileReaderTests {
	private const string Secret = "S3cr3t-Value-For-Tests";
	private string _root;
	private string _folder;
	private ClioSecretFileReader _reader;

	[SetUp]
	public void CreateFolder() {
		_root = Path.Combine(Path.GetTempPath(), "clio-secret-tests-" + Guid.NewGuid().ToString("N"));
		_folder = Path.Combine(_root, ClioSecretFileReader.FolderName);
		Directory.CreateDirectory(_folder);
		_reader = new ClioSecretFileReader(_folder);
	}

	[TearDown]
	public void DeleteFolder() {
		if (Directory.Exists(_root)) {
			Directory.Delete(_root, recursive: true);
		}
	}

	private string WriteSecret(string name, string content, bool ownerOnly = true) {
		string path = Path.Combine(_folder, name);
		File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
		if (!OperatingSystem.IsWindows()) {
			File.SetUnixFileMode(path, ownerOnly
				? UnixFileMode.UserRead | UnixFileMode.UserWrite
				: UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
		}
		return path;
	}

	private void AssertRefused(string name, string expectedFragment) {
		// Act
		Action read = () => _reader.Read(name, "password-file");

		// Assert
		ArgumentException refusal = read.Should().Throw<ArgumentException>(
			because: "a secret clio cannot vouch for must be refused, never used").Which;
		refusal.Message.Should().Contain(expectedFragment, because: "the refusal must say what is wrong");
		refusal.Message.Should().NotContain(Secret, because: "a refusal must never repeat the secret");
	}

	[Test]
	[Description("Reads an owner-only file by name and drops only the trailing line break; leading and inner spaces stay.")]
	public void Read_ShouldReturnTheSecretWithoutTheTrailingLineBreak() {
		// Arrange
		WriteSecret("demo-users", " " + Secret + " \r\n");

		// Act
		string secret = _reader.Read("demo-users", "password-file");

		// Assert
		secret.Should().Be(" " + Secret + " ", because: "a trailing line break is not part of the password, spaces are");
	}

	[Test]
	[Description("A UTF-8 byte order mark is not part of the secret.")]
	public void Read_ShouldDropAByteOrderMark() {
		// Arrange
		WriteSecret("bom", "﻿" + Secret + "\n");

		// Act
		string secret = _reader.Read("bom", "password-file");

		// Assert
		secret.Should().Be(Secret, because: "an editor's byte order mark is not part of the password");
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("../demo-users")]
	[TestCase("sub/demo-users")]
	[TestCase("sub\\demo-users")]
	[TestCase(".hidden")]
	[TestCase("..")]
	[TestCase("demo users")]
	[TestCase("/etc/passwd")]
	[Description("A value that is not a plain file name is refused without being repeated, so a path cannot leave the folder and a secret pasted by mistake is not echoed.")]
	public void Read_ShouldRefuseAValueThatIsNotAPlainName(string name) {
		// Act
		Action read = () => _reader.Read(name, "password-file");

		// Assert
		ArgumentException refusal = read.Should().Throw<ArgumentException>(
			because: "only a plain file name inside the secrets folder is accepted").Which;
		refusal.Message.Should().StartWith("password-file must be the name of a file",
			because: "the refusal must say what a valid reference is");
		if (!string.IsNullOrEmpty(name)) {
			refusal.Message.Should().NotContain(name, because: "an invalid value may be a secret, so it is never repeated");
		}
	}

	[Test]
	[Description("A name longer than the limit is refused.")]
	public void Read_ShouldRefuseATooLongName() {
		// Arrange
		string name = new('a', ClioSecretFileReader.MaxNameLength + 1);

		// Act
		Action read = () => _reader.Read(name, "password-file");

		// Assert
		read.Should().Throw<ArgumentException>(because: "a name over the limit is not a plain reference")
			.WithMessage("password-file must be the name of a file*");
	}

	[Test]
	[Description("A missing file is refused with its name and the folder, so a host can tell where to put it.")]
	public void Read_ShouldRefuseAMissingFile() {
		AssertRefused("missing", "'missing' was not found in clio's secrets folder");
	}

	[Test]
	[Description("A file readable by group or others is refused on macOS and Linux.")]
	public void Read_ShouldRefuseAFileOthersCanRead() {
		// Arrange
		Assume.That(OperatingSystem.IsWindows(), Is.False, "Unix file modes only");
		WriteSecret("shared", Secret, ownerOnly: false);

		// Act & Assert
		AssertRefused("shared", "can be read by other users");
	}

	[Test]
	[Description("A file that is a link is refused, so the read cannot be redirected elsewhere.")]
	public void Read_ShouldRefuseASymbolicLink() {
		// Arrange
		Assume.That(OperatingSystem.IsWindows(), Is.False, "creating a link on Windows needs a privilege");
		string target = Path.Combine(_root, "outside");
		File.WriteAllText(target, Secret);
		File.CreateSymbolicLink(Path.Combine(_folder, "linked"), target);

		// Act & Assert
		AssertRefused("linked", "is a link");
	}

	[Test]
	[Description("A secrets folder that is a link is refused, so the whole folder cannot be redirected.")]
	public void Read_ShouldRefuseALinkedSecretsFolder() {
		// Arrange
		Assume.That(OperatingSystem.IsWindows(), Is.False, "creating a link on Windows needs a privilege");
		string real = Path.Combine(_root, "real");
		Directory.CreateDirectory(real);
		string linkedFolder = Path.Combine(_root, "linked-secrets");
		Directory.CreateSymbolicLink(linkedFolder, real);
		ClioSecretFileReader reader = new(linkedFolder);

		// Act
		Action read = () => reader.Read("demo-users", "password-file");

		// Assert
		read.Should().Throw<ArgumentException>(because: "a linked folder could point anywhere")
			.WithMessage("*secrets folder*is a link*");
	}

	[TestCase("")]
	[TestCase("\n")]
	[TestCase("\r\n\r\n")]
	[Description("A file that is empty once the trailing line break is dropped is refused.")]
	public void Read_ShouldRefuseAnEmptyFile(string content) {
		// Arrange
		WriteSecret("empty", content);

		// Act & Assert
		AssertRefused("empty", "'empty' is empty");
	}

	[Test]
	[Description("A file larger than a secret is refused before it is read.")]
	public void Read_ShouldRefuseATooLargeFile() {
		// Arrange
		WriteSecret("large", Secret + new string('x', ClioSecretFileReader.MaxSecretBytes));

		// Act & Assert
		AssertRefused("large", "larger than");
	}

	[Test]
	[Description("A file of more than one line is refused, so a document is never sent as a password.")]
	public void Read_ShouldRefuseMoreThanOneLine() {
		// Arrange
		WriteSecret("lines", Secret + "\nsecond line\n");

		// Act & Assert
		AssertRefused("lines", "more than one line");
	}

	[Test]
	[Description("A file that is not UTF-8 text is refused.")]
	public void Read_ShouldRefuseInvalidUtf8() {
		// Arrange
		string path = Path.Combine(_folder, "binary");
		File.WriteAllBytes(path, [0xC3, 0x28, 0xA0, 0xA1]);
		if (!OperatingSystem.IsWindows()) {
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		}

		// Act & Assert
		AssertRefused("binary", "is not UTF-8 text");
	}

	[Test]
	[Description("The production reader looks in <clio home>/secrets.")]
	public void SecretsFolder_ShouldBeUnderClioHome() {
		// Act
		string folder = new ClioSecretFileReader().SecretsFolder;

		// Assert
		folder.Should().Be(Path.Combine(ClioRuntimePaths.Home, "secrets"),
			because: "the host writes the file there, next to clio's own settings");
	}
}

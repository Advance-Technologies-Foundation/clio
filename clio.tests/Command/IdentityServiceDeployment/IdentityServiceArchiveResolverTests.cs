using System;
using System.IO;
using System.IO.Compression;
using Clio.Command.IdentityServiceDeployment;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.IdentityServiceDeployment;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class IdentityServiceArchiveResolverTests
{
	[Test]
	[Description("Standalone IdentityService archives are returned as-is when they contain IdentityService runtime files.")]
	public void Resolve_Should_Return_Input_Path_When_Archive_Is_Standalone_IdentityService()
	{
		// Arrange
		string archivePath = CreateZip(("IdentityService.dll", "payload"));
		IdentityServiceArchiveResolver resolver = new();

		// Act
		string result = resolver.Resolve(archivePath, "IdentityService.zip");

		// Assert
		result.Should().Be(archivePath,
			because: "standalone IdentityService.zip inputs should not be extracted to a temporary nested archive");
	}

	[Test]
	[Description("Creatio distribution bundles are resolved by extracting the nested IdentityService.zip archive.")]
	public void Resolve_Should_Extract_Nested_IdentityService_Archive_When_Input_Is_Creatio_Bundle()
	{
		// Arrange
		string nestedArchive = CreateZip(("IdentityService.dll", "payload"));
		string bundlePath = CreateZipFromFiles(("IdentityService.zip", nestedArchive));
		IdentityServiceArchiveResolver resolver = new();

		// Act
		string result = resolver.Resolve(bundlePath, "IdentityService.zip");

		// Assert
		File.Exists(result).Should().BeTrue(
			because: "the resolver should materialize the nested IdentityService.zip for the deploy service");
		result.Should().NotBe(bundlePath,
			because: "a Creatio bundle is not itself the standalone IdentityService archive");
		using ZipArchive archive = ZipFile.OpenRead(result);
		archive.GetEntry("IdentityService.dll").Should().NotBeNull(
			because: "the extracted nested archive should be the original IdentityService archive");
	}

	[Test]
	[Description("Archives that are neither standalone IdentityService nor bundled with IdentityService.zip are rejected clearly.")]
	public void Resolve_Should_Throw_When_Archive_Does_Not_Contain_IdentityService()
	{
		// Arrange
		string archivePath = CreateZip(("readme.txt", "not identity"));
		IdentityServiceArchiveResolver resolver = new();

		// Act
		Action act = () => resolver.Resolve(archivePath, "IdentityService.zip");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*neither a standalone IdentityService zip nor a bundle*",
				because: "deploy-identity should fail before mutating IIS when the archive is the wrong artifact");
	}

	private static string CreateZip(params (string Path, string Content)[] entries)
	{
		string archivePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
		using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
		foreach ((string path, string content) in entries) {
			ZipArchiveEntry entry = archive.CreateEntry(path);
			using StreamWriter writer = new(entry.Open());
			writer.Write(content);
		}
		return archivePath;
	}

	private static string CreateZipFromFiles(params (string EntryPath, string SourceFile)[] entries)
	{
		string archivePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
		using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
		foreach ((string entryPath, string sourceFile) in entries) {
			archive.CreateEntryFromFile(sourceFile, entryPath);
		}
		return archivePath;
	}
}

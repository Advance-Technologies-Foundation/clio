using System;
using System.IO;
using System.IO.Abstractions;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Integration")]
[Property("Module", "Command")]
public class RfsEnvironmentTests {
	private const string PackageName = "UsrBuilderDesignArithmetic";
	private readonly Guid _packageUId = Guid.Parse("712c618f-e140-4b48-a8db-2ee4bf2f721f");
	private string _environmentPackagesPath = null!;
	private RfsEnvironment _rfsEnvironment = null!;
	private string _rootPath = null!;
	private string _workspacePackagesPath = null!;

	[SetUp]
	public void Setup() {
		_rootPath = Path.Combine(Path.GetTempPath(), $"clio-rfs-{Guid.NewGuid():N}");
		_environmentPackagesPath = Path.Combine(_rootPath, "environment", "Pkg");
		_workspacePackagesPath = Path.Combine(_rootPath, "workspace", "packages");
		Directory.CreateDirectory(_environmentPackagesPath);
		Directory.CreateDirectory(_workspacePackagesPath);
		Clio.Common.IFileSystem fileSystem = new Clio.Common.FileSystem(new System.IO.Abstractions.FileSystem());
		_rfsEnvironment = new RfsEnvironment(
			fileSystem,
			new PackageUtilities(fileSystem),
			Substitute.For<ILogger>());
	}

	[TearDown]
	public void TearDown() {
		if (Directory.Exists(_rootPath)) {
			Directory.Delete(_rootPath, recursive: true);
		}
	}

	[Test]
	public void Link4Repo_ShouldPreserveRuntimeBinAndLeaveRepositorySourceUnchanged_WhenTargetBinIsAbsent() {
		string environmentPackage = CreatePackage(_environmentPackagesPath, _packageUId);
		string repositoryPackage = CreatePackage(_workspacePackagesPath, _packageUId);
		byte[] runtimeAssembly = [0, 1, 2, 3, 254, 255];
		WriteFile(
			environmentPackage,
			Path.Combine("Files", "Bin", "netstandard", $"{PackageName}.dll"),
			runtimeAssembly);
		string repositorySource = WriteFile(
			repositoryPackage,
			Path.Combine("Files", "src", "Service.cs"),
			"repository source");
		string repositoryDescriptor = Path.Combine(repositoryPackage, "descriptor.json");
		byte[] descriptorBytes = File.ReadAllBytes(repositoryDescriptor);

		_rfsEnvironment.Link4Repo(_environmentPackagesPath, Path.Combine(_rootPath, "workspace"), PackageName);
		_rfsEnvironment.Link4Repo(_environmentPackagesPath, Path.Combine(_rootPath, "workspace"), PackageName);

		File.ReadAllBytes(Path.Combine(repositoryPackage, "Files", "Bin", "netstandard", $"{PackageName}.dll"))
			.Should().Equal(runtimeAssembly, "because runtime output must survive the physical-package replacement byte-for-byte");
		File.ReadAllText(repositorySource).Should().Be("repository source",
			"because linking must not replace repository source files");
		File.ReadAllBytes(repositoryDescriptor).Should().Equal(descriptorBytes,
			"because linking must not rewrite repository package identity");
		AssertEnvironmentPackageLinksTo(repositoryPackage);
	}

	[Test]
	public void Link4Repo_ShouldLeaveExistingRepositoryBinUntouched_WhenRuntimeOutputAlreadyExists() {
		string environmentPackage = CreatePackage(_environmentPackagesPath, _packageUId);
		string repositoryPackage = CreatePackage(_workspacePackagesPath, _packageUId);
		WriteFile(
			environmentPackage,
			Path.Combine("Files", "Bin", "netstandard", $"{PackageName}.dll"),
			[1, 2, 3]);
		string repositoryAssembly = WriteFile(
			repositoryPackage,
			Path.Combine("Files", "Bin", "netstandard", $"{PackageName}.dll"),
			[9, 8, 7]);

		_rfsEnvironment.Link4Repo(_environmentPackagesPath, Path.Combine(_rootPath, "workspace"), PackageName);

		File.ReadAllBytes(repositoryAssembly).Should().Equal([9, 8, 7],
			"because existing repository build output belongs to that worktree and must never be overwritten");
		AssertEnvironmentPackageLinksTo(repositoryPackage);
	}

	[Test]
	public void Link4Repo_ShouldRejectPackageWithoutDeletingSource_WhenDescriptorUIdDiffers() {
		string environmentPackage = CreatePackage(_environmentPackagesPath, _packageUId);
		string repositoryPackage = CreatePackage(_workspacePackagesPath, Guid.NewGuid());
		WriteFile(
			environmentPackage,
			Path.Combine("Files", "Bin", "netstandard", $"{PackageName}.dll"),
			[1, 2, 3]);

		Action act = () => _rfsEnvironment.Link4Repo(
			_environmentPackagesPath,
			Path.Combine(_rootPath, "workspace"),
			PackageName);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*descriptor identity does not match*");
		Directory.Exists(environmentPackage).Should().BeTrue(
			"because identity validation must finish before the physical package can be deleted");
		Directory.Exists(Path.Combine(repositoryPackage, "Files", "Bin")).Should().BeFalse();
	}

	[Test]
	public void Link4Repo_ShouldRejectPackageWithoutDeletingSource_WhenRuntimeBinContainsLink() {
		string environmentPackage = CreatePackage(_environmentPackagesPath, _packageUId);
		CreatePackage(_workspacePackagesPath, _packageUId);
		string binPath = Path.Combine(environmentPackage, "Files", "Bin");
		Directory.CreateDirectory(binPath);
		string outsideFile = WriteFile(_rootPath, "outside.dll", [4, 5, 6]);
		File.CreateSymbolicLink(Path.Combine(binPath, "linked.dll"), outsideFile);

		Action act = () => _rfsEnvironment.Link4Repo(
			_environmentPackagesPath,
			Path.Combine(_rootPath, "workspace"),
			PackageName);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*must not contain symbolic links or reparse points*");
		Directory.Exists(environmentPackage).Should().BeTrue(
			"because an unsafe output tree must be rejected before source deletion");
	}

	[Test]
	public void Link4Repo_ShouldRejectExistingLink_WhenItTargetsDifferentRepositoryPackage() {
		string repositoryPackage = CreatePackage(_workspacePackagesPath, _packageUId);
		string differentPackage = CreatePackage(Path.Combine(_rootPath, "other"), _packageUId);
		string environmentPackage = Path.Combine(_environmentPackagesPath, PackageName);
		Directory.CreateSymbolicLink(environmentPackage, differentPackage);

		Action act = () => _rfsEnvironment.Link4Repo(
			_environmentPackagesPath,
			Path.Combine(_rootPath, "workspace"),
			PackageName);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*linked to a different repository location*");
		new DirectoryInfo(environmentPackage).ResolveLinkTarget(returnFinalTarget: true)!.FullName
			.Should().Be(differentPackage, "because a mismatched existing link must not be replaced");
		Directory.Exists(repositoryPackage).Should().BeTrue();
	}

	[Test]
	public void Link4Repo_ShouldRejectPackageWithoutDeletingSource_WhenDestinationBinIsLink() {
		string environmentPackage = CreatePackage(_environmentPackagesPath, _packageUId);
		string repositoryPackage = CreatePackage(_workspacePackagesPath, _packageUId);
		WriteFile(
			environmentPackage,
			Path.Combine("Files", "Bin", "netstandard", $"{PackageName}.dll"),
			[1, 2, 3]);
		string externalBin = Path.Combine(_rootPath, "external-bin");
		Directory.CreateDirectory(externalBin);
		string repositoryFiles = Path.Combine(repositoryPackage, "Files");
		Directory.CreateDirectory(repositoryFiles);
		Directory.CreateSymbolicLink(Path.Combine(repositoryFiles, "Bin"), externalBin);

		Action act = () => _rfsEnvironment.Link4Repo(
			_environmentPackagesPath,
			Path.Combine(_rootPath, "workspace"),
			PackageName);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*must not contain symbolic links or reparse points*");
		Directory.Exists(environmentPackage).Should().BeTrue(
			"because an unsafe destination must be rejected before source deletion");
	}

	[Test]
	public void Link4Repo_ShouldRejectPackageWithoutDeletingSource_WhenRepositoryPackageIsLink() {
		string environmentPackage = CreatePackage(_environmentPackagesPath, _packageUId);
		string physicalRepositoryPackage = CreatePackage(Path.Combine(_rootPath, "physical-repository"), _packageUId);
		string repositoryPackage = Path.Combine(_workspacePackagesPath, PackageName);
		Directory.CreateSymbolicLink(repositoryPackage, physicalRepositoryPackage);

		Action act = () => _rfsEnvironment.Link4Repo(
			_environmentPackagesPath,
			Path.Combine(_rootPath, "workspace"),
			PackageName);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*must not contain symbolic links or reparse points*");
		Directory.Exists(environmentPackage).Should().BeTrue(
			"because an unsafe repository root must be rejected before source deletion");
	}

	[Test]
	public void Link4Repo_ShouldRejectPackageWithoutDeletingSource_WhenVersionBranchesPathIsLink() {
		string environmentPackage = CreatePackage(_environmentPackagesPath, _packageUId);
		string repositoryPackage = Path.Combine(_workspacePackagesPath, PackageName);
		Directory.CreateDirectory(repositoryPackage);
		string externalBranches = Path.Combine(_rootPath, "external-branches");
		string externalVersion = Path.Combine(externalBranches, "1.0.0");
		Directory.CreateDirectory(externalVersion);
		WriteDescriptor(externalVersion, _packageUId);
		Directory.CreateSymbolicLink(Path.Combine(repositoryPackage, "branches"), externalBranches);

		Action act = () => _rfsEnvironment.Link4Repo(
			_environmentPackagesPath,
			Path.Combine(_rootPath, "workspace"),
			PackageName);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*content must stay inside its physical package root*");
		Directory.Exists(environmentPackage).Should().BeTrue(
			"because an intermediate repository link must be rejected before source deletion");
	}

	private void AssertEnvironmentPackageLinksTo(string repositoryPackage) {
		string environmentPackage = Path.Combine(_environmentPackagesPath, PackageName);
		DirectoryInfo environmentInfo = new(environmentPackage);
		(environmentInfo.Attributes & FileAttributes.ReparsePoint).Should().NotBe(0);
		environmentInfo.ResolveLinkTarget(returnFinalTarget: true)!.FullName.Should().Be(repositoryPackage);
	}

	private string CreatePackage(string packagesPath, Guid uid) {
		string packagePath = Path.Combine(packagesPath, PackageName);
		Directory.CreateDirectory(packagePath);
		WriteDescriptor(packagePath, uid);
		return packagePath;
	}

	private static void WriteDescriptor(string packagePath, Guid uid) {
		File.WriteAllText(
			Path.Combine(packagePath, "descriptor.json"),
			$$"""
			{
			  "Descriptor": {
			    "Name": "{{PackageName}}",
			    "UId": "{{uid:D}}"
			  }
			}
			""");
	}

	private static string WriteFile(string rootPath, string relativePath, byte[] content) {
		string filePath = Path.Combine(rootPath, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
		File.WriteAllBytes(filePath, content);
		return filePath;
	}

	private static string WriteFile(string rootPath, string relativePath, string content) {
		string filePath = Path.Combine(rootPath, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
		File.WriteAllText(filePath, content);
		return filePath;
	}
}

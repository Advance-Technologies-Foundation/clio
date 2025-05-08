using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Clio.Common.CsProjManager;
using Clio.Tests.Command;
using Clio.Workspaces;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.CsProjManager;

[TestFixture(Category = "Unit")]
public class CsprojFileTests : BaseClioModuleTests
{
    private readonly Func<string, Task<byte[]>> _getFileContentAsync = async path =>
        await File.ReadAllBytesAsync(path);

    private ICsprojFile _csprojFile;
    private IWorkspacePathBuilder _workspacePathBuilder;

    public override void Setup()
    {
        base.Setup();
        _csprojFile = container.Resolve<ICsprojFile>();
        _workspacePathBuilder = container.Resolve<IWorkspacePathBuilder>();
    }

    [Test]
    public async Task Initialize_WithFileInfo_ShouldReturnInitializedCsprojFile()
    {
        // Arrange
        const string packageName = "MrktHootsuiteApp";
        string csProjPath = _workspacePathBuilder.BuildPackageProjectPath(packageName);

        byte[] content = await _getFileContentAsync($"Examples/CsProjFiles/{packageName}.csproj");
        fileSystem.AddFile(csProjPath, new MockFileData(content));
        IFileInfo fileInfo = new MockFileInfo(fileSystem, csProjPath);

        // Act
        IInitializedCsprojFile result = _csprojFile.Initialize(fileInfo);

        // Assert
        result.Should().NotBeNull();
        result.CsProjFileContent.Should().Be(Encoding.UTF8.GetString(content));
    }

    [Test]
    public async Task Initialize_WithPackageName_ShouldReturnInitializedCsprojFile()
    {
        // Arrange
        const string packageName = "MrktHootsuiteApp";
        string csProjPath = _workspacePathBuilder.BuildPackageProjectPath(packageName);

        byte[] content = await _getFileContentAsync($"Examples/CsProjFiles/{packageName}.csproj");
        fileSystem.AddFile(csProjPath, new MockFileData(content));

        // Act
        IInitializedCsprojFile result = _csprojFile.Initialize(packageName);

        // Assert
        result.Should().NotBeNull();
        result.CsProjFileContent.Should().Be(Encoding.UTF8.GetString(content));
    }

    [Test]
    public void Initialize_WithPackageName_ShouldReturnEmpty_WhenProjFileDoesNotExist()
    {
        // Arrange
        const string packageName = "MrktHootsuiteApp";

        // Act
        IInitializedCsprojFile actual = _csprojFile.Initialize(packageName);

        // Assert
        actual.Should().NotBeNull();
    }

    [TestCase("MrktHootsuiteApp")]
    [TestCase("CbyWhatsappApp")]
    public async Task GetPackageReferences_ShouldReturnPackageReferences(string packageName)
    {
        // Arrange
        string csProjPath = _workspacePathBuilder.BuildPackageProjectPath(packageName);
        byte[] content = await _getFileContentAsync($"Examples/CsProjFiles/{packageName}.csproj");
        fileSystem.AddFile(csProjPath, new MockFileData(content));
        IInitializedCsprojFile initializedCsProj = _csprojFile.Initialize(packageName);

        // Act
        IEnumerable<Reference> refs = initializedCsProj.GetPackageReferences();

        // Assert
        _ = packageName switch
        {
            "MrktHootsuiteApp" => refs.Should().HaveCount(0),
            "CbyWhatsappApp" => refs.Should().HaveCount(50),
            _ => throw new ArgumentOutOfRangeException(nameof(packageName), packageName, null)
        };
    }

    [TestCase("MrktHootsuiteApp")]
    [TestCase("CbyWhatsappApp")]
    public async Task GetPackageReferences_ShouldNotInclude_TerrasoftConfiguration(string packageName)
    {
        // Arrange
        string csProjPath = _workspacePathBuilder.BuildPackageProjectPath(packageName);
        byte[] content = await _getFileContentAsync($"Examples/CsProjFiles/{packageName}.csproj");
        fileSystem.AddFile(csProjPath, new MockFileData(content));
        IInitializedCsprojFile initializedCsProj = _csprojFile.Initialize(packageName);

        // Act
        IEnumerable<Reference> refs = initializedCsProj.GetPackageReferences();

        // Assert
        refs.Any(r => r.PackageName == "Terrasoft.Configuration").Should().BeFalse();
    }
}

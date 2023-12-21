using System;
using System.IO;
using Clio.Common;
using Clio.Workspace;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
public class PropsBuilder_Tests
{

	private const string RootPath = "rootPath";
	private const string NugetFolderPath = ".nuget";
	private const string PackageFolderPath = "packages";
	private const string PackageName = "testPackage";
	private static readonly Func<string> MockCsProjWithNugetContent = () => @"
		<Project Sdk=""Microsoft.NET.Sdk"">
			<PropertyGroup>
				<TargetFramework>netstandard2.0</TargetFramework>
			</PropertyGroup>
			<ItemGroup Label=""Core References"">
				<Reference Include=""Terrasoft.Common"">
					<HintPath>$(CoreLibPath)/Terrasoft.Common.dll</HintPath>
					<SpecificVersion>False</SpecificVersion>
					<Private>False</Private>
				</Reference>
			</ItemGroup>
			<ItemGroup Label=""3rd Party References"">
				<PackageReference Include=""ATF.Repository"" Version=""2.0.1.5"" />
			</ItemGroup>
		</Project>";
	#region Setup/Teardown

	[SetUp]
	public void SetUp(){
		_fileSystem = Substitute.For<IFileSystem>();
		_logger = Substitute.For<ILogger>();
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		
		_workspacePathBuilder.RootPath.Returns(RootPath);
		_workspacePathBuilder.NugetFolderPath.Returns(Path.Combine(RootPath, NugetFolderPath));
		_workspacePathBuilder.PackagesFolderPath.Returns(Path.Combine(RootPath, PackageFolderPath));
		_workspacePathBuilder.BuildPackageProjectPath(Arg.Is(PackageName))
			.Returns(Path.Combine(RootPath, PackageFolderPath, PackageName, PackageName + ".csproj"));
		_sut = new PropsBuilder(_fileSystem, _logger, _workspacePathBuilder);
	}

	#endregion

	#region Fields: Private

	private PropsBuilder _sut;
	private IFileSystem _fileSystem;
	private ILogger _logger;
	private IWorkspacePathBuilder _workspacePathBuilder;

	#endregion

	[Test]
	public void Test1(){
		//Arrange
		string[] files = new []{"ATF.Repository.dll", "Castle.Core.dll", $"{PackageName}.dll", "Terrasoft.Common.dll"};
		_fileSystem.GetFiles(
			Arg.Is(ExpectedPath("net472")),
			Arg.Is("*.dll"), 
			Arg.Is(SearchOption.TopDirectoryOnly)
		).Returns(files);
		_fileSystem.GetFiles(
			Arg.Is(ExpectedPath("netstandard")),
			Arg.Is("*.dll"), 
			Arg.Is(SearchOption.TopDirectoryOnly)
		).Returns(files);
		
		_fileSystem
			.ReadAllText(Arg.Is<string>(s=>!string.IsNullOrEmpty(s)))
			.Returns(MockCsProjWithNugetContent());
		
		//Act
		_sut.Build(PackageName);

		//Assert
		_fileSystem.Received(1).GetFiles(
			Arg.Is(ExpectedPath("net472")),
			Arg.Is("*.dll"), 
			Arg.Is(SearchOption.TopDirectoryOnly)
			);
		
		_fileSystem.Received(1).GetFiles(
			Arg.Is(ExpectedPath("netstandard")),
			Arg.Is("*.dll"), 
			Arg.Is(SearchOption.TopDirectoryOnly)
			);
		
		return;

		
		//rootPath\.nuget\testPackage\bin\net472
		string ExpectedPath(string moniker) => Path.Combine(RootPath, NugetFolderPath, PackageName, "bin", moniker);
	}

}
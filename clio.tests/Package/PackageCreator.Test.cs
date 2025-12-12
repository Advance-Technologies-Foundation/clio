using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using Autofac;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using Clio.Tests.Extensions;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Package;

[TestFixture]
internal class PackageCreatorTest : BaseClioModuleTests
{

	#region Fields: Private

	private const string PackagesPath = @"T:\\";
	private const string PackageNameOne = "TestPackageOne";
	private const string PackageNameTwo = "TestPackageTwo";
	private const string PackageNameThree = "TestPackageThree";

	#endregion

	#region Methods: Private

	private IWorkspaceSolutionCreator _solutionCreatorMock = Substitute.For<IWorkspaceSolutionCreator>();

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder) {
		_solutionCreatorMock.ClearReceivedCalls();
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.RegisterInstance(_solutionCreatorMock);
	}
	
	private PackageCreator InitCreator(){
		return new PackageCreator(Container.Resolve<EnvironmentSettings>(), Container.Resolve<IWorkspace>(),
			Container.Resolve<IWorkspaceSolutionCreator>(),
			Container.Resolve<ITemplateProvider>(), Container.Resolve<IWorkspacePathBuilder>(),
			Container.Resolve<IStandalonePackageFileManager>(), Container.Resolve<IJsonConverter>(),
			Container.Resolve<IWorkingDirectoriesProvider>(), Container.Resolve<Clio.Common.IFileSystem>());
	}

	#endregion

	#region Methods: Protected

	protected override MockFileSystem CreateFs(){
		MockFileSystem x = (MockFileSystem)base.CreateFs();
		ILogger logger = Substitute.For<ILogger>();
		WorkingDirectoriesProvider wdp = new (logger, x);
		x.MockFolderWithDir(wdp.TemplateDirectory);
		return x;
	}

	#endregion

	[Test]
	public void Create_AddPackageToWorkspaceWithTwoApplication(){
		//Arrange
		PackageCreator creator = InitCreator();

		//Act
		creator.Create(PackagesPath, PackageNameOne, true);
		creator.Create(PackagesPath, PackageNameTwo, true);
		creator.Create(PackagesPath, PackageNameThree);

		//Assert
		string appDescriptorPathOne = Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json");
		FileSystem.File.Exists(appDescriptorPathOne).Should().BeTrue();
		
		string appDescriptorPathTwo = Path.Combine(PackagesPath, PackageNameTwo, "Files", "app-descriptor.json");
		FileSystem.File.Exists(appDescriptorPathTwo).Should().BeTrue();
		
		string appDescriptorPathThree = Path.Combine(PackagesPath, PackageNameThree, "Files", "app-descriptor.json");
		FileSystem.File.Exists(appDescriptorPathThree).Should().BeFalse();
		
		_solutionCreatorMock.Received(3).Create();
		_solutionCreatorMock.ClearReceivedCalls();
		
	}

	[Test]
	public void Create_AddTwoApplicationsToWorkplace(){
		//Arrange
		PackageCreator creator = InitCreator();

		//Act
		creator.Create(PackagesPath, PackageNameOne, true);
		creator.Create(PackagesPath, PackageNameTwo, true);

		//Assert
		string appDescriptorPathOne = Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json");
		string appDescriptorPathTwo = Path.Combine(PackagesPath, PackageNameTwo, "Files", "app-descriptor.json");

		FileSystem.File.Exists(appDescriptorPathOne).Should().BeTrue();
		FileSystem.File.Exists(appDescriptorPathTwo).Should().BeTrue();
		
		_solutionCreatorMock.Received(2).Create();
		_solutionCreatorMock.ClearReceivedCalls();
	}

	[Test]
	public void Create_AddTwoPackagesInEmptyWorkspaceByDefault(){
		//Arrange
		PackageCreator creator = InitCreator();

		//Act
		creator.Create(PackagesPath, PackageNameOne);
		creator.Create(PackagesPath, PackageNameTwo);

		//Assert
		string appDescriptorPathOne = Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json");
		string appDescriptorPathTwo = Path.Combine(PackagesPath, PackageNameTwo, "Files", "app-descriptor.json");

		FileSystem.File.Exists(appDescriptorPathOne).Should().BeFalse();
		FileSystem.File.Exists(appDescriptorPathTwo).Should().BeFalse();
		
		_solutionCreatorMock.Received(2).Create();
		_solutionCreatorMock.ClearReceivedCalls();
	}

	[Test]
	public void Create_AddTwoPackagesWithoutApplication(){
		//Arrange
		PackageCreator creator = InitCreator();

		//Act
		creator.Create(PackagesPath, PackageNameOne, false);
		creator.Create(PackagesPath, PackageNameTwo, false);

		//Assert
		string appDescriptorPathOne = Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json");
		string appDescriptorPathTwo = Path.Combine(PackagesPath, PackageNameTwo, "Files", "app-descriptor.json");

		FileSystem.File.Exists(appDescriptorPathOne).Should().BeFalse();
		FileSystem.File.Exists(appDescriptorPathTwo).Should().BeFalse();
		
		_solutionCreatorMock.Received(2).Create();
		_solutionCreatorMock.ClearReceivedCalls();
	}

	[Test]
	public void Create_RewritePackageIfPackagesWithSameNamesExistsOnDescriptor(){
		//Arrange
		PackageCreator creator = InitCreator();

		//Act
		creator.Create(PackagesPath, PackageNameOne, true);
		creator.Create(PackagesPath, PackageNameTwo);
		FileSystem.Directory.Delete(Path.Combine(PackagesPath, PackageNameTwo), true);
		string appDescriptorPathOne = Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json");
		string appDescriptorContent = FileSystem.File.ReadAllText(appDescriptorPathOne);
		AppDescriptorJson appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);
		appDescriptor.Packages.Add(new Clio.Package.Package {Name = PackageNameTwo, UId = Guid.NewGuid().ToString()});
		creator.SaveAppDescriptorToFile(appDescriptor, appDescriptorPathOne);
		creator.Create(PackagesPath, PackageNameTwo);
		appDescriptorContent = FileSystem.File.ReadAllText(appDescriptorPathOne);
		appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);
		appDescriptor.Packages.Count().Should().Be(2);
		
		_solutionCreatorMock.Received(3).Create();
		_solutionCreatorMock.ClearReceivedCalls();
	}

	[Test]
	public void Create_RewritePackageIfPackageWithSameNameExistsOnDescriptor(){
		//Arrange
		PackageCreator creator = InitCreator();

		//Act
		creator.Create(PackagesPath, PackageNameOne, true);
		creator.Create(PackagesPath, PackageNameTwo);
		FileSystem.Directory.Delete(Path.Combine(PackagesPath, PackageNameTwo), true);
		creator.Create(PackagesPath, PackageNameTwo);
		string appDescriptorPathOne = Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json");
		string appDescriptorContent = FileSystem.File.ReadAllText(appDescriptorPathOne);
		AppDescriptorJson appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);
		appDescriptor.Packages.Count().Should().Be(2);
		
		_solutionCreatorMock.Received(3).Create();
		_solutionCreatorMock.ClearReceivedCalls();
	}

	[Test]
	public void Create_ThrowExceptionIfPackageExists(){
		//Arrange
		PackageCreator creator = InitCreator();

		//Act
		creator.Create(PackagesPath, PackageNameOne, false);
		Action act = () => creator.Create(PackagesPath, PackageNameOne, false);
		
		//Assert
		act.Should().Throw<InvalidOperationException>("because creating a package with the same name should throw an exception");
		_solutionCreatorMock.Received(1).Create();
		_solutionCreatorMock.ClearReceivedCalls();
	}

	[Test]
	public void Create_TwoPackages(){
		//Arrange
		PackageCreator creator = InitCreator();

		//Act
		creator.Create(PackagesPath, PackageNameOne, true);
		creator.Create(PackagesPath, PackageNameTwo);

		//Assert
		string appDescriptorPathOne = Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json");
		string appDescriptorPathTwo = Path.Combine(PackagesPath, PackageNameTwo, "Files", "app-descriptor.json");

		FileSystem.File.Exists(appDescriptorPathOne).Should().BeTrue();
		FileSystem.File.Exists(appDescriptorPathTwo).Should().BeFalse();

		string appDescriptorContent = FileSystem.File.ReadAllText(appDescriptorPathOne);
		AppDescriptorJson appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);
		appDescriptor.Packages.Should().HaveCount(2);
		_solutionCreatorMock.Received(2).Create();
		_solutionCreatorMock.ClearReceivedCalls();
	}

	[Test]
	public void Create_With(){
		//Arrange

		PackageCreator creator = InitCreator();

		//Act
		creator.Create(PackagesPath, PackageNameOne, true);

		//Assert
		string appDescriptorContent
			= FileSystem.File.ReadAllText(Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json"));
		AppDescriptorJson appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);

		appDescriptor.Name.Should().Be(PackageNameOne);
		appDescriptor.Code.Should().Be(PackageNameOne);
		appDescriptor.Color.Should().Be("#FFAC07");
		appDescriptor.Maintainer.Should().Be("Customer");
		appDescriptor.Version.Should().Be("0.1.0");
		appDescriptor.Packages.Should().HaveCount(1);
		appDescriptor.Packages.First().Name.Should().Be(PackageNameOne);
		
		_solutionCreatorMock.Received(1).Create();
		_solutionCreatorMock.ClearReceivedCalls();
	}

	[Test]
	[Description("Ensures that ApplyMacrosToCsProjFile replaces #PackageName# and #RootNameSpace# macros in the .csproj file")]
	public void Create_Should_Replace_Macros_In_CsProj_File() {
		// Arrange
		PackageCreator creator = InitCreator();
		string packageFilesPath = Path.Combine(PackagesPath, PackageNameOne, "Files");
		string csprojPath = Path.Combine(packageFilesPath, $"{PackageNameOne}.csproj");
		
		
		//Act
		creator.Create(PackagesPath, PackageNameOne, true);

		// Assert
		string resultContent = FileSystem.File.ReadAllText(csprojPath);
		resultContent.Should().NotContain("#PackageName#", "because the macro should be replaced with the actual package name");
		resultContent.Should().NotContain("#RootNameSpace#", "because the macro should be replaced with the actual root namespace");
		resultContent.Should().Contain($"<RootNamespace>{PackageNameOne}App</RootNamespace>", "because the root namespace should be present in the csproj file");
		resultContent.Should().Contain(PackageNameOne, "because the package name should be present in the csproj file");
		resultContent.Should().Contain($"{PackageNameOne}App", "because the root namespace should be present in the csproj file");
	}

	
}

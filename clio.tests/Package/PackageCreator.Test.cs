using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using Clio.Tests.Extensions;
using Clio.Workspace;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Package;

[TestFixture]
[Property("Module", "Package")]
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
	private readonly ISchemaBuilder _schemaBuilderMock = Substitute.For<ISchemaBuilder>();

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		_solutionCreatorMock.ClearReceivedCalls();
		_schemaBuilderMock.ClearReceivedCalls();
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_solutionCreatorMock);
		containerBuilder.AddSingleton(_schemaBuilderMock);
	}
	
	private PackageCreator InitCreator(){
		PackageCreator creator = new(Container.GetRequiredService<EnvironmentSettings>(),
			Container.GetRequiredService<IWorkspace>(), Container.GetRequiredService<IWorkspaceSolutionCreator>(),
			Container.GetRequiredService<ITemplateProvider>(), Container.GetRequiredService<IWorkspacePathBuilder>(),
			Container.GetRequiredService<IStandalonePackageFileManager>(), Container.GetRequiredService<IJsonConverter>(),
			Container.GetRequiredService<IWorkingDirectoriesProvider>(),
			Container.GetRequiredService<Clio.Common.IFileSystem>(), Container.GetRequiredService<ISchemaBuilder>());
		return creator;
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
	[Description("Creates a narrowly owned package-level localization schema for application packages.")]
	public void Create_ShouldAddLocalizationSchema_WhenAsAppIsTrue() {
		// Arrange
		PackageCreator creator = InitCreator();

		// Act
		creator.Create(PackagesPath, PackageNameOne, true);

		// Assert
		object[] arguments = _schemaBuilderMock.ReceivedCalls().Should().ContainSingle(
			because: "an application package needs exactly one generated localization owner")
			.Which.GetArguments();
		arguments[0].Should().Be("source-code",
			because: "localizable values need a Creatio schema resource owner");
		arguments[1].Should().Be($"{PackageNameOne}LocalizableStrings",
			because: "the generated owner must be discoverable from the package name");
		arguments[2].Should().Be(Path.Combine(PackagesPath, PackageNameOne),
			because: "the schema must be created inside the new application package");
		SourceCodeSchemaOptions options = arguments[3].Should().BeOfType<SourceCodeSchemaOptions>(
			because: "the shared schema builder should receive data-only localization customization").Subject;
		options.Namespace.Should().Be($"{PackageNameOne}App",
			because: "the generated schema must use the standalone application's namespace");
		options.ClassDocumentation.Should().Contain("no more natural schema owner",
			because: "the generated class must explain its narrow package-level scope");
		options.ClassDocumentation.Should().Contain("Page and other schema resources stay",
			because: "the generated class must reject central-registry ownership");
		options.LocalizableStrings["LocalizableStrings.PackageLevelExample.Value"].Should()
			.Be("Package-level localizable value",
				because: "the new application package needs one working localization primitive");
	}

	[Test]
	[Description("Creates an injectable adapter over Creatio LocalizableString for application packages.")]
	public void Create_ShouldAddInjectableLocalizationAbstraction_WhenAsAppIsTrue() {
		// Arrange
		PackageCreator creator = InitCreator();
		string sourceRoot = Path.Combine(PackagesPath, PackageNameOne, "Files", "src", "cs");

		// Act
		creator.Create(PackagesPath, PackageNameOne, true);

		// Assert
		string resolverPath = Path.Combine(sourceRoot, "LocalizableStrings", "LocalizableStringResolver.cs");
		FileSystem.File.Exists(resolverPath).Should().BeTrue(
			because: "application code needs an injectable boundary over Creatio's concrete type");
		string resolver = FileSystem.File.ReadAllText(resolverPath);
		resolver.Should().Contain("interface ILocalizableStringResolver",
			because: "the generated primitive must expose the resolver abstraction");
		resolver.Should().Contain("class LocalizableStringResolver : ILocalizableStringResolver",
			because: "the conventional implementation should be colocated with its small interface");
		resolver.Should().Contain("new LocalizableString(",
			because: "the concrete adapter must own construction of the Creatio Core type");
		string[] localizableStringConstructors = FileSystem.Directory
			.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
			.Where(path => FileSystem.File.ReadAllText(path).Contains("new LocalizableString("))
			.ToArray();
		string constructorPath = localizableStringConstructors.Should().ContainSingle().Which;
		Path.GetFullPath(constructorPath).Should().Be(Path.GetFullPath(resolverPath),
			because: "only the injectable adapter may construct Creatio's concrete localization primitive");
		resolver.Should().Contain("LocalizableString localizableString = Create(",
			because: "generated code must expose platform values to debugger breakpoints before returning");
		resolver.Should().Contain("throwIfNoManager: false",
			because: "the generated adapter must make the platform boolean's meaning explicit");
		string app = FileSystem.File.ReadAllText(Path.Combine(sourceRoot, $"{PackageNameOne}App.cs"));
		app.Should().Contain("AddTransient<LocalizableStrings.ILocalizableStringResolver",
			because: "the generated abstraction must be resolvable from the application composition root");
		app.Should().NotContain("#LocalizationServices#",
			because: "template macros must not leak into generated source");
	}

	[TestCase("")]
	[TestCase("../Escape")]
	[TestCase(@"..\Escape")]
	[TestCase("Package-Name")]
	[TestCase("123Package")]
	[TestCase("_")]
	[TestCase("PackageNameThatIsLongerThanTheCreatioPackageNameLimitOfSeventyCharacters123456")]
	[Description("Rejects unsafe package names before package or localization files are written.")]
	public void Create_ShouldRejectWithoutWriting_WhenPackageNameIsInvalid(string packageName) {
		// Arrange
		PackageCreator creator = InitCreator();
		string[] filesBefore = FileSystem.AllFiles.OrderBy(path => path).ToArray();

		// Act
		Action act = () => creator.Create(PackagesPath, packageName, true);

		// Assert
		act.Should().Throw<ArgumentException>(
			because: "a package name becomes folder, project, namespace, and schema names")
			.WithParameterName("packageName")
			.WithMessage("Package name must start with a letter or underscore and contain only letters, digits, and underscores.*");
		FileSystem.AllFiles.OrderBy(path => path).Should().Equal(filesBefore,
			because: "validation must run before any path derived from an unsafe name is written");
		_schemaBuilderMock.ReceivedCalls().Should().BeEmpty(
			because: "an invalid package name must not reach schema generation");
	}

	[Test]
	[Description("Accepts a valid package name at Creatio's seventy-character boundary.")]
	public void Create_ShouldCreatePackage_WhenPackageNameHasSeventyCharacters() {
		// Arrange
		PackageCreator creator = InitCreator();
		string packageName = new('A', 70);

		// Act
		creator.Create(PackagesPath, packageName, false);

		// Assert
		FileSystem.Directory.Exists(Path.Combine(PackagesPath, packageName)).Should().BeTrue(
			because: "Creatio accepts package names up to and including seventy characters");
	}

	[TestCase(false)]
	[TestCase(null)]
	[Description("Does not add application localization primitives to ordinary packages.")]
	public void Create_ShouldNotAddLocalizationPrimitives_WhenAsAppIsNotTrue(bool? asApp) {
		// Arrange
		PackageCreator creator = InitCreator();

		// Act
		creator.Create(PackagesPath, PackageNameOne, asApp);

		// Assert
		_schemaBuilderMock.ReceivedCalls().Should().BeEmpty(
			because: "ordinary packages must retain their existing generated structure");
		string sourceRoot = Path.Combine(PackagesPath, PackageNameOne, "Files", "src", "cs");
		FileSystem.File.Exists(Path.Combine(sourceRoot, "LocalizableStrings", "LocalizableStringResolver.cs"))
			.Should().BeFalse(
				because: "the injectable localization primitive is part of the application-package shape only");
		FileSystem.File.ReadAllText(Path.Combine(sourceRoot, $"{PackageNameOne}App.cs"))
			.Should().NotContain("#LocalizationServices#",
				because: "ordinary package source must not retain conditional template macros");
	}

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

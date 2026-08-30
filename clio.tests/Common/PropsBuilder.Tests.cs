using System;
using System.Collections.Generic;
using System.IO;
using Clio.Common;
using FluentAssertions;
using Clio.Workspaces;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class PropsBuilder_Tests
{

	private const string RootPath = "rootPath";
	private const string NugetFolderPath = ".nuget";
	private const string PackageFolderPath = "packages";
	private const string PackageName = "testPackage";
	private const string MockPropItemTemplate = @"<Reference Include=""#dll-name-here#"">
	<HintPath>Libs/#dll-name-here#.dll</HintPath>
</Reference>";

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
	private static readonly Func<string> MockCsProjWithConditionalReferences = () => @"
		<Project Sdk=""Microsoft.NET.Sdk"">
			<PropertyGroup>
				<TargetFrameworks>net472;netstandard2.0</TargetFrameworks>
			</PropertyGroup>
			<ItemGroup>
				<Reference Condition=""'$(TargetFramework)' == 'net472'"" Include=""System.Text.Json"">
					<HintPath>$(CoreLibPath)/System.Text.Json.dll</HintPath>
				</Reference>
				<PackageReference Condition=""'$(TargetFramework)' == 'netstandard2.0'"" Include=""System.Text.Json"" Version=""8.0.0"" />
				<Reference Include=""Terrasoft.Common"">
					<HintPath>$(CoreLibPath)/Terrasoft.Common.dll</HintPath>
				</Reference>
			</ItemGroup>
			<Choose>
				<When Condition=""'$(TargetFramework)' == 'net472'"">
					<ItemGroup>
						<Reference Include=""Castle.Core"">
							<HintPath>$(CoreLibPath)/Castle.Core.dll</HintPath>
						</Reference>
					</ItemGroup>
				</When>
			</Choose>
		</Project>";

	#region Setup/Teardown

	[SetUp]
	public void SetUp(){
		_writtenPropsFiles.Clear();
		_fileSystem = Substitute.For<IFileSystem>();
		_fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);
		_logger = Substitute.For<ILogger>();
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		
		_workspacePathBuilder.RootPath.Returns(RootPath);
		_workspacePathBuilder.NugetFolderPath.Returns(Path.Combine(RootPath, NugetFolderPath));
		_workspacePathBuilder.PackagesFolderPath.Returns(Path.Combine(RootPath, PackageFolderPath));
		_workspacePathBuilder.BuildPackagePropsPath(Arg.Is(PackageName), Arg.Any<string>())
			.Returns(ci => ExpectedPropsPath(ci.ArgAt<string>(1)));
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
	private readonly Dictionary<string, string> _writtenPropsFiles = new();

	#endregion

	[Test]
	[Description("Reads the dlls of both monikers from the nuget bin folders")]
	public void Build_ReadsDllsOfBothMonikers(){
		//Arrange
		string[] files = ["ATF.Repository.dll", "Castle.Core.dll", $"{PackageName}.dll", "Terrasoft.Common.dll"];
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
		
		MockCsProjAndTemplateReads();
		
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

	[Test]
	[Description("Reports no props and does not throw when the nuget bin folder does not exist (issue 263)")]
	public void Build_ReturnsNoProps_When_BinFolderMissing(){
		//Arrange
		_fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(false);

		//Act
		PropsBuildResult actual = _sut.Build(PackageName);

		//Assert
		actual.HasAnyProps.Should().BeFalse(
			because: "a nuget project that failed to build produces no bin folder to read");
		_fileSystem.Received(0).GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>());
	}

	[Test]
	[Description("Does not write a props file when the moniker has no dependency dll (issue 263)")]
	public void Build_DoesNotWritePropsFile_When_NoDllsFound(){
		//Arrange
		_fileSystem.GetFiles(Arg.Any<string>(), Arg.Is("*.dll"), Arg.Is(SearchOption.TopDirectoryOnly))
			.Returns(Array.Empty<string>());

		//Act
		PropsBuildResult actual = _sut.Build(PackageName);

		//Assert
		actual.Net472PropsCreated.Should().BeFalse(
			because: "there is nothing to reference for net472");
		actual.NetStandardPropsCreated.Should().BeFalse(
			because: "there is nothing to reference for netstandard");
		actual.HasAnyProps.Should().BeFalse(because: "no props file was written at all");
		_fileSystem.Received(0).WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
		_fileSystem.Received(1).DeleteFileIfExists(Arg.Is(ExpectedPropsPath("net472")));
		_fileSystem.Received(1).DeleteFileIfExists(Arg.Is(ExpectedPropsPath("netstandard")));
	}

	[Test]
	[Description("Reports which props files were written when only one moniker has dlls (issue 263)")]
	public void Build_ReportsPerMonikerResult_When_OnlyOneMonikerHasDlls(){
		//Arrange
		_fileSystem.GetFiles(Arg.Is(ExpectedBinPath("net472")), Arg.Is("*.dll"),
				Arg.Is(SearchOption.TopDirectoryOnly))
			.Returns(new[] {Path.Combine(ExpectedBinPath("net472"), "ATF.Repository.dll")});
		_fileSystem.GetFiles(Arg.Is(ExpectedBinPath("netstandard")), Arg.Is("*.dll"),
				Arg.Is(SearchOption.TopDirectoryOnly))
			.Returns(Array.Empty<string>());
		MockCsProjAndTemplateReads();

		//Act
		PropsBuildResult actual = _sut.Build(PackageName);

		//Assert
		actual.Net472PropsCreated.Should().BeTrue(because: "net472 has a dependency dll");
		actual.NetStandardPropsCreated.Should().BeFalse(because: "netstandard has none");
		actual.HasAnyProps.Should().BeTrue(because: "one props file was written");
		actual.MaterializedAssemblies.Should().Contain("ATF.Repository",
			because: "the copied assembly must be reported so its package reference can be commented out");
		_fileSystem.Received(1).WriteAllTextToFile(
			Arg.Is(ExpectedPropsPath("net472")),
			Arg.Is<string>(c => c.Contains("<Project>") && c.Contains("ATF.Repository")));
		_fileSystem.Received(0).WriteAllTextToFile(Arg.Is(ExpectedPropsPath("netstandard")), Arg.Any<string>());
	}

	[Test]
	[Description("Keeps a dependency whose file name merely ends with the package name (issue 263)")]
	public void Build_KeepsDependency_WhoseNameEndsWithPackageName(){
		//Arrange
		_fileSystem.GetFiles(Arg.Any<string>(), Arg.Is("*.dll"), Arg.Is(SearchOption.TopDirectoryOnly))
			.Returns(new[] {
				Path.Combine(ExpectedBinPath("net472"), $"Contoso.{PackageName}.dll"),
				Path.Combine(ExpectedBinPath("net472"), $"{PackageName}.dll")
			});
		MockCsProjAndTemplateReads();

		//Act
		_sut.Build(PackageName);

		//Assert
		_fileSystem.Received(1).WriteAllTextToFile(
			Arg.Is(ExpectedPropsPath("net472")),
			Arg.Is<string>(c => c.Contains($"Contoso.{PackageName}")));
		_fileSystem.Received(0).WriteAllTextToFile(
			Arg.Any<string>(),
			Arg.Is<string>(c => c.Contains($"Include=\"{PackageName}\"")));
	}

	private void MockCsProjAndTemplateReads(){
		_fileSystem.ReadAllText(Arg.Is<string>(s => s.EndsWith(".tpl")))
			.Returns(MockPropItemTemplate);
		_fileSystem.ReadAllText(Arg.Is<string>(s => s.EndsWith(".csproj")))
			.Returns(MockCsProjWithNugetContent());
	}

	//rootPath\.nuget\testPackage\bin\<moniker>
	private static string ExpectedBinPath(string moniker) =>
		Path.Combine(RootPath, NugetFolderPath, PackageName, "bin", moniker);

	//rootPath\packages\testPackage\Files\testPackage-<moniker>.nuget.props
	private static string ExpectedPropsPath(string moniker) =>
		Path.Combine(RootPath, PackageFolderPath, PackageName, "Files",
			$"{PackageName}-{moniker}.nuget.props");

	[Test]
	[Description("References a dll that is only declared for another target framework (issue 1283)")]
	public void Build_ReferencesDll_When_ExistingReferenceIsScopedToAnotherTargetFramework(){
		//Arrange
		MockConditionalCsProjAndTemplateReads();
		_fileSystem.GetFiles(Arg.Any<string>(), Arg.Is("*.dll"), Arg.Is(SearchOption.TopDirectoryOnly))
			.Returns(ci => [
				Path.Combine(ci.ArgAt<string>(0), "System.Text.Json.dll"),
				Path.Combine(ci.ArgAt<string>(0), "Castle.Core.dll"),
				Path.Combine(ci.ArgAt<string>(0), "Terrasoft.Common.dll")
			]);

		//Act
		_sut.Build(PackageName);

		//Assert
		string netStandardProps = CapturedPropsContent("netstandard");
		netStandardProps.Should().Contain("System.Text.Json",
			because: "the existing reference to it applies to net472 only");
		netStandardProps.Should().Contain("Castle.Core",
			because: "its reference lives in a Choose/When scoped to net472");
	}

	[Test]
	[Description("Skips a dll that is already referenced for the target framework being built (issue 1283)")]
	public void Build_SkipsDll_When_ExistingReferenceAppliesToTargetFramework(){
		//Arrange
		MockConditionalCsProjAndTemplateReads();
		_fileSystem.GetFiles(Arg.Any<string>(), Arg.Is("*.dll"), Arg.Is(SearchOption.TopDirectoryOnly))
			.Returns(ci => [
				Path.Combine(ci.ArgAt<string>(0), "System.Text.Json.dll"),
				Path.Combine(ci.ArgAt<string>(0), "Terrasoft.Common.dll"),
				//An unreferenced dll, so the props file is written and the assertions below
				//are made against real content instead of a file that never existed
				Path.Combine(ci.ArgAt<string>(0), "ATF.Repository.dll")
			]);

		//Act
		_sut.Build(PackageName);

		//Assert
		string net472Props = CapturedPropsContent("net472");
		net472Props.Should().Contain("ATF.Repository",
			because: "an unreferenced dll must be written into the props file");
		net472Props.Should().NotContain("System.Text.Json",
			because: "it is already referenced under a matching net472 condition");
		net472Props.Should().NotContain("Terrasoft.Common",
			because: "it is referenced unconditionally, so it applies to net472 too");
	}

	private string CapturedPropsContent(string moniker){
		//Returning string.Empty for a file that was never written would turn "nothing was
		//written" into a passing NotContain assertion
		_writtenPropsFiles.TryGetValue(ExpectedPropsPath(moniker), out string content).Should().BeTrue(
			because: $"the {moniker} props file had to be written for this assertion to mean anything");
		return content;
	}

	private void MockConditionalCsProjAndTemplateReads(){
		_fileSystem.When(fs => fs.WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>()))
			.Do(ci => _writtenPropsFiles[ci.ArgAt<string>(0)] = ci.ArgAt<string>(1));
		_fileSystem.ReadAllText(Arg.Is<string>(s => s.EndsWith(".tpl")))
			.Returns(MockPropItemTemplate);
		_fileSystem.ReadAllText(Arg.Is<string>(s => s.EndsWith(".csproj")))
			.Returns(MockCsProjWithConditionalReferences());
	}

	[TestCase("'$(TargetFramework)' == 'net472' Or '$(TargetFramework)' == 'netstandard2.0'")]
	[TestCase("!('$(TargetFramework)' == 'net472')")]
	[TestCase("'$(TargetFramework)' == 'net472' And '$(Configuration)' == 'Debug'")]
	[TestCase("'$(TargetFrameworkVersion)' == 'v4.7.2'")]
	[Description("Writes the dll into the props file when the existing reference carries a condition clio cannot evaluate (issue 1283)")]
	public void Build_ReferencesDll_When_ConditionCannotBeEvaluated(string condition){
		//Arrange
		_fileSystem.ReadAllText(Arg.Is<string>(s => s.EndsWith(".tpl"))).Returns(MockPropItemTemplate);
		_fileSystem.ReadAllText(Arg.Is<string>(s => s.EndsWith(".csproj"))).Returns($@"
			<Project Sdk=""Microsoft.NET.Sdk"">
				<ItemGroup>
					<Reference Condition=""{condition}"" Include=""ATF.Repository"">
						<HintPath>$(CoreLibPath)/ATF.Repository.dll</HintPath>
					</Reference>
				</ItemGroup>
			</Project>");
		_fileSystem.When(fs => fs.WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>()))
			.Do(ci => _writtenPropsFiles[ci.ArgAt<string>(0)] = ci.ArgAt<string>(1));
		_fileSystem.GetFiles(Arg.Any<string>(), Arg.Is("*.dll"), Arg.Is(SearchOption.TopDirectoryOnly))
			.Returns(ci => [Path.Combine(ci.ArgAt<string>(0), "ATF.Repository.dll")]);

		//Act
		_sut.Build(PackageName);

		//Assert
		CapturedPropsContent("net472").Should().Contain("ATF.Repository",
			because: "a duplicate reference is an MSBuild warning, while a missing one is a "
				+ "compile error, so an unreadable condition must not drop the dependency");
	}

	[TestCase("'net472' == '$(TargetFramework)'")]
	[TestCase("  '$(TargetFramework)'=='net472'  ")]
	[TestCase("'$(TargetFramework)' != 'netstandard2.0'")]
	[Description("Understands the operand orders, spacing and negation of a simple target-framework condition (issue 1283)")]
	public void Build_SkipsDll_When_SimpleConditionMatchesTargetFramework(string condition){
		//Arrange
		_fileSystem.ReadAllText(Arg.Is<string>(s => s.EndsWith(".tpl"))).Returns(MockPropItemTemplate);
		_fileSystem.ReadAllText(Arg.Is<string>(s => s.EndsWith(".csproj"))).Returns($@"
			<Project Sdk=""Microsoft.NET.Sdk"">
				<ItemGroup>
					<Reference Condition=""{condition}"" Include=""ATF.Repository"">
						<HintPath>$(CoreLibPath)/ATF.Repository.dll</HintPath>
					</Reference>
					<Reference Include=""Castle.Core"">
						<HintPath>$(CoreLibPath)/Castle.Core.dll</HintPath>
					</Reference>
				</ItemGroup>
			</Project>");
		_fileSystem.When(fs => fs.WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>()))
			.Do(ci => _writtenPropsFiles[ci.ArgAt<string>(0)] = ci.ArgAt<string>(1));
		_fileSystem.GetFiles(Arg.Any<string>(), Arg.Is("*.dll"), Arg.Is(SearchOption.TopDirectoryOnly))
			.Returns(ci => [
				Path.Combine(ci.ArgAt<string>(0), "ATF.Repository.dll"),
				Path.Combine(ci.ArgAt<string>(0), "Newtonsoft.Json.dll")
			]);

		//Act
		_sut.Build(PackageName);

		//Assert
		string net472Props = CapturedPropsContent("net472");
		net472Props.Should().NotContain("ATF.Repository",
			because: "the condition resolves to net472, so the reference already applies");
		net472Props.Should().Contain("Newtonsoft.Json",
			because: "an unreferenced dll must still be written");
	}

}

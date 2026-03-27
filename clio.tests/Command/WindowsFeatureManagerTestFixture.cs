using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using System.IO;
using System.Linq;

namespace Clio.Tests.Command;

public class WindowsFeatureManagerTestFixture : BaseClioModuleTests
{

	IWindowsFeatureManager _sut;
	IWorkingDirectoriesProvider _workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
	
	public override void Setup(){
		base.Setup();
		_sut = Container.GetRequiredService<IWindowsFeatureManager>();
	}
	protected override void AdditionalRegistrations(IServiceCollection containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton<IWorkingDirectoriesProvider>(_workingDirectoriesProvider);
		//containerBuilder.RegisterInstance<ConsoleProgressbar>();
	}

	[Test]
	[Description("Should not require .NET Framework 3.5 feature-on-demand components in the shipped Windows features manifest")]
	public void RequirmentNETFrameworkFeatures_ExcludesRemovedNetFramework35Features() {
		// Arrange
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		string repositoryTemplateDirectory = Path.GetFullPath(Path.Combine(
			TestContext.CurrentContext.TestDirectory,
			"..",
			"..",
			"..",
			"..",
			"clio",
			"tpl"));
		workingDirectoriesProvider.TemplateDirectory.Returns(repositoryTemplateDirectory);
		IWindowsFeatureProvider windowsFeatureProvider = Substitute.For<IWindowsFeatureProvider>();
		ILogger logger = Substitute.For<ILogger>();
		INetFrameworkVersionChecker netFrameworkVersionChecker = Substitute.For<INetFrameworkVersionChecker>();
		var windowsFeatureManager = new WindowsFeatureManager(
			workingDirectoriesProvider,
			new ConsoleProgressbar(),
			windowsFeatureProvider,
			logger,
			netFrameworkVersionChecker);

		// Act
		string[] requiredFeatures = windowsFeatureManager.RequirmentNETFrameworkFeatures.ToArray();

		// Assert
		File.Exists(Path.Combine(repositoryTemplateDirectory, "windows_features", "RequirmentNetFramework.txt"))
			.Should().BeTrue("because the regression test must validate the source manifest rather than stale copied build artifacts");
		requiredFeatures.Should().NotContain("WCF-HTTP-Activation",
			"because Windows 11 26H1 and later no longer provide the .NET Framework 3.5 WCF HTTP activation feature on demand");
		requiredFeatures.Should().NotContain("WCF-NonHTTP-Activation",
			"because Windows 11 26H1 and later no longer provide the .NET Framework 3.5 WCF non-HTTP activation feature on demand");
	}

	[Test]
	[Description("Should return the maximum item length when calculating aligned progress output")]
	public void BuildPrintString_AllignsItems(){
		// Arrange

		// Act
		var actual = _sut.GetActionMaxLength(["x","xx","xxx"]);

		// Assert
		actual.Should().Be(3, "because the longest item contains three characters");
	}
	

}

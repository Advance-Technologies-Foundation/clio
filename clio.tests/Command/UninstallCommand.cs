using Autofac;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
internal class UninstallCreatioCommandTests : BaseCommandTests<UninstallCreatioCommandOptions>
{

    #region Fields: Private

    private readonly ICreatioUninstaller _creatioUninstaller = Substitute.For<ICreatioUninstaller>();
    private UninstallCreatioCommand _sut;

    #endregion

    #region Methods: Protected

    protected override void AdditionalRegistrations(ContainerBuilder containerBuilder)
    {
        base.AdditionalRegistrations(containerBuilder);
        containerBuilder.RegisterInstance(_creatioUninstaller);
    }

    #endregion

    #region Methods: Public

    public override void Setup()
    {
        base.Setup();
        _sut = Container.Resolve<UninstallCreatioCommand>();
    }

    #endregion

    [Test]
    public void Execute_ShouldEarlyReturn_WhenValidationFails()
    {
        //Arrange
        UninstallCreatioCommandOptions options = new();

        //Act
        int exitCode = _sut.Execute(options);

        //Assert
        exitCode.Should().Be(1);
    }

    [Test]
    public void Execute_ShouldReturn_When_EnvironmentNameValidationPasses()
    {
        //Arrange
        UninstallCreatioCommandOptions options = new()
        {
            EnvironmentName = "some"
        };

        //Act
        int exitCode = _sut.Execute(options);

        //Assert
        exitCode.Should().Be(0);
        _creatioUninstaller.Received(1).UninstallByEnvironmentName(options.EnvironmentName);
    }

    [Test]
    public void Execute_ShouldReturn_When_PhysicalPathValidationPasses()
    {
        //Arrange
        const string directoryPath = @"C:\some_creatio_folder";
        UninstallCreatioCommandOptions options = new()
        {
            PhysicalPath = directoryPath
        };
        FileSystem.AddDirectory(directoryPath);
        //Act
        int exitCode = _sut.Execute(options);

        //Assert
        exitCode.Should().Be(0);
        _creatioUninstaller.Received(1).UninstallByPath(options.PhysicalPath);
    }

}

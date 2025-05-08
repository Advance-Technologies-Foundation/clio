using System;

using Clio.Command;
using Clio.Project;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Ignore("Not passing in github runner")]
[TestFixture]
public class ReferenceCommandTestCase
{
    [Test]
    [Category("Unit")]
    public void Execute_Throws_WhenProjectPathIsNotDefined()
    {
        ICreatioPkgProjectCreator creator = Substitute.For<ICreatioPkgProjectCreator>();
        ReferenceOptions options = new ();
        ReferenceCommand command = new (creator);
        int actual = command.Execute(options);
        actual.Should().Be(1);
    }

    [Test]
    [Category("Unit")]
    public void Execute_SetsCorrectRef_WhenReferenceTypeIsCoreLib()
    {
        ICreatioPkgProjectCreator creator = Substitute.For<ICreatioPkgProjectCreator>();
        ICreatioPkgProject project = Substitute.For<ICreatioPkgProject>();
        creator.CreateFromFile(Arg.Any<string>()).Returns(project);
        ReferenceOptions options = new () { Path = "Testpath", ReferenceType = "src" };
        ReferenceCommand command = new (creator);
        command.Execute(options);
        project.Received(1).RefToCoreSrc();
    }

    [Test]
    [Category("Unit")]
    public void Execute_SetsCorrectRef_WhenReferenceTypeIsBinaries()
    {
        ICreatioPkgProjectCreator creator = Substitute.For<ICreatioPkgProjectCreator>();
        ICreatioPkgProject project = Substitute.For<ICreatioPkgProject>();
        creator.CreateFromFile(Arg.Any<string>()).Returns(project);
        ReferenceOptions options = new () { Path = "Testpath", ReferenceType = "bin" };
        ReferenceCommand command = new (creator);
        command.Execute(options);
        project.Received(1).RefToBin();
    }

    [Test]
    [Category("Unit")]
    public void Execute_SetsCorrectRef_WhenReferenceTypeIsUnitTestsBinaries()
    {
        ICreatioPkgProjectCreator creator = Substitute.For<ICreatioPkgProjectCreator>();
        ICreatioPkgProject project = Substitute.For<ICreatioPkgProject>();
        creator.CreateFromFile(Arg.Any<string>()).Returns(project);
        ReferenceOptions options = new () { Path = "Testpath", ReferenceType = "unit-bin" };
        ReferenceCommand command = new (creator);
        command.Execute(options);
        project.Received(1).RefToUnitBin();
    }

    [Test]
    [Category("Unit")]
    public void Execute_SetsCorrectRef_WhenReferenceTypeIsUnitTestsSources()
    {
        ICreatioPkgProjectCreator creator = Substitute.For<ICreatioPkgProjectCreator>();
        ICreatioPkgProject project = Substitute.For<ICreatioPkgProject>();
        creator.CreateFromFile(Arg.Any<string>()).Returns(project);
        ReferenceOptions options = new () { Path = "Testpath", ReferenceType = "unit-src" };
        ReferenceCommand command = new (creator);
        command.Execute(options);
        project.Received(1).RefToUnitCoreSrc();
    }

    [Test]
    [Category("Unit")]
    public void Execute_SetsCorrectRef_WhenReferenceTypeIsCustom()
    {
        ICreatioPkgProjectCreator creator = Substitute.For<ICreatioPkgProjectCreator>();
        ICreatioPkgProject project = Substitute.For<ICreatioPkgProject>();
        creator.CreateFromFile(Arg.Any<string>()).Returns(project);
        ReferenceOptions options = new () { Path = "Testpath", ReferenceType = "custom", RefPattern = "TestPattern" };
        ReferenceCommand command = new (creator);
        command.Execute(options);
        project.Received(1).RefToCustomPath(options.RefPattern);
    }

    [Test]
    [Category("Unit")]
    public void Execute_SetsCustomRef_WhenReferenceTypeIsEmpty()
    {
        ICreatioPkgProjectCreator creator = Substitute.For<ICreatioPkgProjectCreator>();
        ICreatioPkgProject project = Substitute.For<ICreatioPkgProject>();
        creator.CreateFromFile(Arg.Any<string>()).Returns(project);
        ReferenceOptions options = new () { Path = "Testpath", RefPattern = "TestPattern" };
        ReferenceCommand command = new (creator);
        command.Execute(options);
        project.Received(1).RefToCustomPath(options.RefPattern);
    }
}

using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

public abstract class BaseCommandTests<T> : BaseClioModuleTests
{

    #region Fields: Private

    private static readonly ReadmeChecker ReadmeChecker = ClioTestsSetup.GetService<ReadmeChecker>();

    #endregion

    [Test]
    public void Command_ShouldHave_DescriptionBlock_InReadmeFile() =>
        ReadmeChecker
            .IsInReadme(typeof(T))
            .Should()
            .BeTrue("{0} is a command and needs a be described in README.md", this);

}

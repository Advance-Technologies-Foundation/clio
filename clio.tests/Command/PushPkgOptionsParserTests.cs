using CommandLine;
using FluentAssertions;
using NUnit.Framework;
using Clio.Command;

namespace Clio.Tests.Command;

[TestFixture]
public class PushPkgOptionsParserTests {
    [Test]
    public void ForceCompilation_Defaults_ToTrue() {
        var args = new[] { "push-pkg", "MyPackage" };
        var result = Parser.Default.ParseArguments<PushPkgOptions>(args);
        result.Tag.Should().Be(ParserResultType.Parsed);
        var options = ((Parsed<PushPkgOptions>)result).Value;
        options.ForceCompilation.Should().BeTrue();
    }

    [Test]
    public void ForceCompilation_CanBeDisabled() {
        var args = new[] { "push-pkg", "MyPackage", "--force-compilation", "false" };
        var result = Parser.Default.ParseArguments<PushPkgOptions>(args);
        result.Tag.Should().Be(ParserResultType.Parsed);
        var options = ((Parsed<PushPkgOptions>)result).Value;
        options.ForceCompilation.Should().BeFalse();
    }
}

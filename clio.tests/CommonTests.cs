using System.Collections.Generic;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

internal class CommonTests
{
    [Test]
    [Category("Unit")]
    public void BpmPkg_Create_CheckCorrectTplFilePathGettingFromPath()
    {
        string input = "Test1,Test2, Test3, Test4 , Test5 ";
        string[] expected = { "Test1", "Test2", "Test3", "Test4", "Test5" };
        IEnumerable<string> actual = StringParser.ParseArray(input);
        actual.Should().BeEquivalentTo(expected);
    }
}

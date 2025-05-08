using System.Collections.Concurrent;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using Castle.Core.Logging;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;
using ConsoleLogger = Clio.Common.ConsoleLogger;
using ILogger = Clio.Common.ILogger;

namespace Clio.Tests;

[TestFixture]
internal class WorkingDirectoryProviderTest
{
    [Test]
    public void GetTemplatePath_TemplateName_ReturnsTemplatePath()
    {
        // Arrange
        ILogger logger = ConsoleLogger.Instance;
        WorkingDirectoriesProvider provider = new(logger, new MockFileSystem());
        string templateName = "TestTemplate";
        string expectedPath = Path.Combine(provider.TemplateDirectory, $"{templateName}.tpl");

        // Act
        string actualPath = provider.GetTemplatePath(templateName);

        // Assert
        expectedPath.Should().Be(actualPath);
    }

    [Test]
    public void GetTemplatePath_Multiple_Create_Temp_Directory()
    {
        // Arrange
        ILogger logger = ConsoleLogger.Instance;
        WorkingDirectoriesProvider provider = new(logger, new MockFileSystem());
        ConcurrentBag<string> paths = new();

        // Act
        int repeatCount = 100;
        Parallel.ForEach(Enumerable.Range(0, repeatCount), i => { paths.Add(provider.GenerateTempDirectoryPath()); });

        int uniqCount = paths.Distinct().Count();
        int dashContainsCount = paths.Count(p => p.Contains("-"));

        // Assert
        paths.Distinct().Should().HaveCount(repeatCount);
        dashContainsCount.Should().Be(0);
    }
}

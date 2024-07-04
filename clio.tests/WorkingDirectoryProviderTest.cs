using Castle.Core.Logging;
using Clio.Common;
using NUnit.Framework;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using ConsoleLogger = Clio.Common.ConsoleLogger;

namespace Clio.Tests
{
	[TestFixture]
	internal class WorkingDirectoryProviderTest
	{
		[Test]
		public void GetTemplatePath_TemplateName_ReturnsTemplatePath() {
			// Arrange
			var logger = ConsoleLogger.Instance; 
			var provider = new WorkingDirectoriesProvider(logger);
			var templateName = "TestTemplate";
			var expectedPath = Path.Combine(provider.TemplateDirectory, $"{templateName}.tpl");

			// Act
			var actualPath = provider.GetTemplatePath(templateName);

			// Assert
			expectedPath.Should().Be(actualPath);
		}

		[Test]
		public void GetTemplatePath_Multiple_Create_Temp_Directory() {
			// Arrange
			var logger = ConsoleLogger.Instance;
			var provider = new WorkingDirectoriesProvider(logger);
			ConcurrentBag<string> paths = new ConcurrentBag<string>();
			
			// Act
			var repeatCount = 100;
			Parallel.ForEach(Enumerable.Range(0, repeatCount), i => {
				paths.Add(provider.GenerateTempDirectoryPath());
			});

			var uniqCount = paths.Distinct().Count();
			var dashContainsCount = paths.Count(p => p.Contains("-"));
			
			// Assert
			paths.Distinct().Should().HaveCount(repeatCount);
			dashContainsCount.Should().Be(0);
		}
	}
}

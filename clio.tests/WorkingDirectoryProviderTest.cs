using Castle.Core.Logging;
using Clio.Common;
using NUnit.Framework;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConsoleLogger = Clio.Common.ConsoleLogger;

namespace Clio.Tests
{
	[TestFixture]
	internal class WorkingDirectoryProviderTest
	{
		[Test]
		public void GetTemplatePath_TemplateName_ReturnsTemplatePath() {
			// Arrange
			var logger = new ConsoleLogger(); 
			var provider = new WorkingDirectoriesProvider(logger);
			var templateName = "TestTemplate";
			var expectedPath = Path.Combine(provider.TemplateDirectory, $"{templateName}.tpl");

			// Act
			var actualPath = provider.GetTemplatePath(templateName);

			// Assert
			Assert.AreEqual(expectedPath, actualPath);
		}

		[Test]
		public void GetTemplatePath_Multiple_Create_Temp_Directory() {
			// Arrange
			var logger = new ConsoleLogger();
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
			Assert.AreEqual(repeatCount, uniqCount);
			Assert.AreEqual(0, dashContainsCount);
		}
	}
}

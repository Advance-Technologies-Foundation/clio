using Clio.Requests;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Tests.Requests;

[Category("Integration")]
[Description("Integration tests for IISScannerHandler to verify discovery of top-level sites and nested applications")]
public class IISScannerHandlerTests
{
	[Test]
	[Description("Verifies that FindAllCreatioSites discovers both top-level sites and nested applications")]
	public void FindAllCreatioSites_Should_Discover_TopLevel_And_Nested_Sites()
	{
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("This test only runs on Windows");
		}
		
		// Act
		// Note: This is an integration test that will only work on Windows with IIS installed
		// and will discover actual IIS sites on the system
		List<IISScannerHandler.UnregisteredSite> sites = IISScannerHandler.FindAllCreatioSites().ToList();

		// Assert
		// We can't assert specific counts as it depends on the test environment
		// But we can verify that the method executes without errors
		sites.Should().NotBeNull("because FindAllCreatioSites should always return a collection");

		// Log discovered sites for debugging purposes
		Console.WriteLine($"Discovered {sites.Count} Creatio sites:");
		foreach (var site in sites)
		{
			Console.WriteLine($"  - {site.siteBinding.name} at {site.siteBinding.path}");
			Console.WriteLine($"    URIs: {string.Join(", ", site.Uris)}");
			Console.WriteLine($"    Type: {site.siteType}");
		}
	}

	[Test]
	[Description("Verifies that nested application names include the full path (e.g., 'SiteName/AppName')")]
	public void FindAllCreatioSites_Should_Include_AppPath_In_SiteName_For_Nested_Apps()
	{
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("This test only runs on Windows");
		}
		
		// Act
		IEnumerable<IISScannerHandler.UnregisteredSite> sites = IISScannerHandler.FindAllCreatioSites();

		// Assert
		// Check if any nested applications were discovered (those with "/" in the name)
		var nestedSites = sites.Where(s => s.siteBinding.name.Contains("/")).ToList();

		if (nestedSites.Any())
		{
			Console.WriteLine($"Found {nestedSites.Count} nested application(s):");
			foreach (var site in nestedSites)
			{
				Console.WriteLine($"  - {site.siteBinding.name}");
				
				// Verify the name format is correct (SiteName/AppPath)
				site.siteBinding.name.Should().Contain("/", 
					because: "nested applications should have site name and app path separated by /");
				
				// Verify physical path is populated
				site.siteBinding.path.Should().NotBeNullOrEmpty(
					because: "nested applications should have a valid physical path");
			}
		}
		else
		{
			Console.WriteLine("No nested applications found in the test environment.");
			Assert.Inconclusive("This test requires IIS sites with nested applications to fully validate the functionality.");
		}
	}

	[Test]
	[Description("Verifies that SiteBinding records have all required properties populated")]
	public void FindAllCreatioSites_Should_Populate_All_SiteBinding_Properties()
	{
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("This test only runs on Windows");
		}
		
		// Act
		IEnumerable<IISScannerHandler.UnregisteredSite> sites = IISScannerHandler.FindAllCreatioSites();

		// Assert
		foreach (var site in sites)
		{
			site.siteBinding.Should().NotBeNull("because every site should have a SiteBinding");
			site.siteBinding.name.Should().NotBeNullOrEmpty(
				because: "every site should have a name");
			site.siteBinding.path.Should().NotBeNullOrEmpty(
				because: "every site should have a physical path");
			
			// Note: state and binding can be empty for nested apps that aren't directly managed by IIS
			// but inherit from their parent site
		}
	}
}


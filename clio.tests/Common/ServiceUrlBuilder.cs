using System;
using System.Collections.Generic;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("UnitTests")]
[TestFixture]
internal class ServiceUrlBuilderCommandTests
{

	#region Properties: Public

	public static IEnumerable<TestCaseData> TestCases {
		get {
			yield return new TestCaseData(false, "http://localhost", "somepath1", "http://localhost/0/somepath1");
			yield return new TestCaseData(true, "http://localhost", "somepath1", "http://localhost/somepath1");

			yield return new TestCaseData(false, "http://localhost/", "somepath1", "http://localhost/0/somepath1");
			yield return new TestCaseData(true, "http://localhost/", "somepath1", "http://localhost/somepath1");

			yield return new TestCaseData(false, "http://localhost:81", "somepath1", "http://localhost:81/0/somepath1");
			yield return new TestCaseData(true, "http://localhost:81", "somepath1", "http://localhost:81/somepath1");

			yield return new TestCaseData(false, "http://localhost:81/sub", "somepath1",
				"http://localhost:81/sub/0/somepath1");
			yield return new TestCaseData(true, "http://localhost:81/sub", "somepath1",
				"http://localhost:81/sub/somepath1");

			yield return new TestCaseData(false, "https://localhost", "somepath1", "https://localhost/0/somepath1");
			yield return new TestCaseData(true, "https://localhost", "somepath1", "https://localhost/somepath1");

			yield return new TestCaseData(false, "https://localhost/", "somepath1", "https://localhost/0/somepath1");
			yield return new TestCaseData(true, "https://localhost/", "somepath1", "https://localhost/somepath1");

			yield return new TestCaseData(false, "https://localhost:81", "somepath1",
				"https://localhost:81/0/somepath1");
			yield return new TestCaseData(true, "https://localhost:81", "somepath1", "https://localhost:81/somepath1");

			yield return new TestCaseData(false, "https://localhost:81/sub", "somepath1",
				"https://localhost:81/sub/0/somepath1");
			yield return new TestCaseData(true, "https://localhost:81/sub", "somepath1",
				"https://localhost:81/sub/somepath1");
		}
	}

	public static IEnumerable<TestCaseDataWithEnvSetting> TestCasesWithEnvSettings {
		get {
			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = false, Uri = "http://localhost"}, "http://localhost/0/somepath1");
			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = true, Uri = "http://localhost"}, "http://localhost/somepath1");

			
			yield return new TestCaseDataWithEnvSetting("/somepath1",
				new EnvironmentSettings {IsNetCore = false, Uri = "http://localhost"}, "http://localhost/0/somepath1");
			yield return new TestCaseDataWithEnvSetting("/somepath1",
				new EnvironmentSettings {IsNetCore = true, Uri = "http://localhost"}, "http://localhost/somepath1");

			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = false, Uri = "http://localhost/"}, "http://localhost/0/somepath1");
			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = true, Uri = "http://localhost/"}, "http://localhost/somepath1");
			
			yield return new TestCaseDataWithEnvSetting("/somepath1",
				new EnvironmentSettings {IsNetCore = false, Uri = "http://localhost/"}, "http://localhost/0/somepath1");
			yield return new TestCaseDataWithEnvSetting("/somepath1",
				new EnvironmentSettings {IsNetCore = true, Uri = "http://localhost/"}, "http://localhost/somepath1");
			
			
			

			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = false, Uri = "http://localhost:81/"},
				"http://localhost:81/0/somepath1");
			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = true, Uri = "http://localhost:81/"},
				"http://localhost:81/somepath1");
			
			
			
			yield return new TestCaseDataWithEnvSetting("/somepath1",
				new EnvironmentSettings {IsNetCore = false, Uri = "http://localhost:81/"},
				"http://localhost:81/0/somepath1");
			yield return new TestCaseDataWithEnvSetting("/somepath1",
				new EnvironmentSettings {IsNetCore = true, Uri = "http://localhost:81/"},
				"http://localhost:81/somepath1");
			
			

			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = false, Uri = "http://localhost:81/sub"},
				"http://localhost:81/sub/0/somepath1");
			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = true, Uri = "http://localhost:81/sub"},
				"http://localhost:81/sub/somepath1");
			
			yield return new TestCaseDataWithEnvSetting("/somepath1",
				new EnvironmentSettings {IsNetCore = false, Uri = "http://localhost:81/sub"},
				"http://localhost:81/sub/0/somepath1");
			yield return new TestCaseDataWithEnvSetting("/somepath1",
				new EnvironmentSettings {IsNetCore = true, Uri = "http://localhost:81/sub"},
				"http://localhost:81/sub/somepath1");
			
			
			yield return new TestCaseDataWithEnvSetting("/somepath1",
				new EnvironmentSettings {IsNetCore = false, Uri = "http://localhost:81/sub/"},
				"http://localhost:81/sub/0/somepath1");
			yield return new TestCaseDataWithEnvSetting("/somepath1",
				new EnvironmentSettings {IsNetCore = true, Uri = "http://localhost:81/sub/"},
				"http://localhost:81/sub/somepath1");
			
			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = false, Uri = "https://localhost"},
				"https://localhost/0/somepath1");
			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = true, Uri = "https://localhost"}, "https://localhost/somepath1");

			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = false, Uri = "https://localhost/"},
				"https://localhost/0/somepath1");
			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = true, Uri = "https://localhost/"}, "https://localhost/somepath1");

			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = false, Uri = "https://localhost:81/"},
				"https://localhost:81/0/somepath1");
			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = true, Uri = "https://localhost:81/"},
				"https://localhost:81/somepath1");

			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = false, Uri = "http://localhost:81/sub"},
				"http://localhost:81/sub/0/somepath1");
			yield return new TestCaseDataWithEnvSetting("somepath1",
				new EnvironmentSettings {IsNetCore = true, Uri = "http://localhost:81/sub"},
				"http://localhost:81/sub/somepath1");
		}
	}

	public static IEnumerable<TestCaseDataWithEnvSettingAndKnownRoutes> TestCasesWithEnvSettingsAndKnownRoutes {
		get {
			yield return new TestCaseDataWithEnvSettingAndKnownRoutes(
				ServiceUrlBuilder.KnownRoute.RestoreFromPackageBackup,
				new EnvironmentSettings {IsNetCore = false, Uri = "http://localhost"},
				"http://localhost/0/ServiceModel/PackageInstallerService.svc/RestoreFromPackageBackup");
		}
	}

	public static IEnumerable<TestCaseDataWithKnownRoutes> TestCasesWithKnownRoute {
		get {
			yield return new TestCaseDataWithKnownRoutes(false, "http://localhost",
				ServiceUrlBuilder.KnownRoute.RestoreFromPackageBackup,
				"http://localhost/0/ServiceModel/PackageInstallerService.svc/RestoreFromPackageBackup");
			yield return new TestCaseDataWithKnownRoutes(true, "http://localhost",
				ServiceUrlBuilder.KnownRoute.RestoreFromPackageBackup,
				"http://localhost/ServiceModel/PackageInstallerService.svc/RestoreFromPackageBackup");

			yield return new TestCaseDataWithKnownRoutes(false, "http://localhost/",
				ServiceUrlBuilder.KnownRoute.RestoreFromPackageBackup,
				"http://localhost/0/ServiceModel/PackageInstallerService.svc/RestoreFromPackageBackup");
			yield return new TestCaseDataWithKnownRoutes(true, "http://localhost/",
				ServiceUrlBuilder.KnownRoute.RestoreFromPackageBackup,
				"http://localhost/ServiceModel/PackageInstallerService.svc/RestoreFromPackageBackup");
		}
	}

	#endregion

	#region Methods: Public

	[TestCaseSource(nameof(TestCases))]
	public void Build_Returns_CorrectUrl(TestCaseData testCaseData){
		//Arrange
		EnvironmentSettings environmentSettingsMock = new() {
			Uri = testCaseData.Uri,
			IsNetCore = testCaseData.IsNetCore
		};
		ServiceUrlBuilder sut = new(environmentSettingsMock);

		//Act
		string actual = sut.Build(testCaseData.Route);

		//Assert
		actual.Should().Be(testCaseData.ExpectedUrl);
	}

	[TestCase("")]
	[TestCase("Fdgbzdf")]
	public void Build_Throws_When_Invalid_Url(string url){
		EnvironmentSettings environmentSettingsMock = new() {
			Uri = url
		};
		ServiceUrlBuilder sut = new(environmentSettingsMock);

		//Act
		Action act = () => sut.Build("");

		//Assert

		act.Should().Throw<ArgumentException>().WithMessage("Misconfigured Url, check settings and try again *");
	}

	[TestCaseSource(nameof(TestCasesWithEnvSettings))]
	public void BuildWithEnvs_Returns_CorrectUrl(TestCaseDataWithEnvSetting testCaseData){
		//Arrange
		EnvironmentSettings environmentSettingsMock = new() {
			Uri = testCaseData.EnvironmentSettings.Uri,
			IsNetCore = testCaseData.EnvironmentSettings.IsNetCore
		};
		ServiceUrlBuilder sut = new(new EnvironmentSettings());

		//Act

		string actual = sut.Build(testCaseData.Route, environmentSettingsMock);

		//Assert
		actual.Should().Be(testCaseData.ExpectedUrl);
	}

	[TestCaseSource(nameof(TestCasesWithEnvSettingsAndKnownRoutes))]
	public void BuildWithEnvsAndKnownRoute_Returns_CorrectUrl(TestCaseDataWithEnvSettingAndKnownRoutes testCaseData){
		//Arrange
		EnvironmentSettings environmentSettingsMock = new() {
			Uri = testCaseData.EnvironmentSettings.Uri,
			IsNetCore = testCaseData.EnvironmentSettings.IsNetCore
		};
		ServiceUrlBuilder sut = new(new EnvironmentSettings());

		//Act

		string actual = sut.Build(testCaseData.KnownRoute, environmentSettingsMock);

		//Assert
		actual.Should().Be(testCaseData.ExpectedUrl);
	}

	[TestCaseSource(nameof(TestCasesWithKnownRoute))]
	public void BuildWithKnownRoute_Returns_CorrectUrl(TestCaseDataWithKnownRoutes testCaseData){
		//Arrange
		EnvironmentSettings environmentSettingsMock = new() {
			Uri = testCaseData.Uri,
			IsNetCore = testCaseData.IsNetCore
		};
		ServiceUrlBuilder sut = new(environmentSettingsMock);

		//Act
		string actual = sut.Build(testCaseData.KnownRoute);

		//Assert
		actual.Should().Be(testCaseData.ExpectedUrl);
	}

	
	[Test]
	public void AllEnumsHaveRoutes(){
		ServiceUrlBuilder sut = new (new EnvironmentSettings());
		foreach(ServiceUrlBuilder.KnownRoute route in Enum.GetValues<ServiceUrlBuilder.KnownRoute>()) {
			sut.KnownRoutes[route].Should().NotBeNullOrWhiteSpace();
		}
	}
	
	#endregion

	public record TestCaseData(bool IsNetCore, string Uri, string Route, string ExpectedUrl);

	public record TestCaseDataWithKnownRoutes(bool IsNetCore, string Uri, ServiceUrlBuilder.KnownRoute KnownRoute,
		string ExpectedUrl);

	public record TestCaseDataWithEnvSetting(string Route, EnvironmentSettings EnvironmentSettings, string ExpectedUrl);

	public record TestCaseDataWithEnvSettingAndKnownRoutes(ServiceUrlBuilder.KnownRoute KnownRoute,
		EnvironmentSettings EnvironmentSettings, string ExpectedUrl);

}
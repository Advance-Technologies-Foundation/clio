using System;
using System.Linq;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Requests;
using Clio.Tests.Command;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Requests;

[TestFixture]
[Property("Module", "Requests")]
internal class IisScannerHandlerValidationTests : BaseClioModuleTests {
	private IProcessExecutor _processExecutor;
	private IIisScanner _scanner;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_processExecutor = Substitute.For<IProcessExecutor>();
		services.AddSingleton(_processExecutor);
	}

	public override void Setup() {
		base.Setup();
		_scanner = Container.GetRequiredService<IIisScanner>();
	}

	[TestCase("work", false, true)]
	[TestCase("work/child", false, false)]
	[TestCase("work/child", true, true)]
	[Description("Only root sites or explicitly pool-managed targets are stopped because AppCmd cannot stop nested applications.")]
	public void ShouldStopSite_ShouldSkipUnsupportedNestedAppStop(string siteName, bool manageAppPool,
		bool expected) {
		// Arrange

		// Act
		bool result = IisScannerHandler.ShouldStopSite(siteName, manageAppPool);

		// Assert
		result.Should().Be(expected,
			because: "a nested application has no supported app-scoped AppCmd stop operation");
	}

	[TestCase("<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"pool\" SITE.NAME=\"work\" /></appcmd>", "work", true)]
	[TestCase("<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"pool\" SITE.NAME=\"work\" /><APP APP.NAME=\"work/other\" APPPOOL.NAME=\"other\" SITE.NAME=\"work\" /></appcmd>", "work", false)]
	[TestCase("<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"pool\" SITE.NAME=\"work\" /><APP APP.NAME=\"work/other\" APPPOOL.NAME=\"other\" SITE.NAME=\"work\" /></appcmd>", "work/other", true)]
	[TestCase("<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"pool\" SITE.NAME=\"work\" /><APP APP.NAME=\"work/other\" APPPOOL.NAME=\"other\" SITE.NAME=\"work\" /><APP APP.NAME=\"work/other/child\" APPPOOL.NAME=\"child\" SITE.NAME=\"work\" /></appcmd>", "work/other", false)]
	[TestCase("<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"pool\" SITE.NAME=\"work\" /><APP APPPOOL.NAME=\"pool\" SITE.NAME=\"other\" /></appcmd>", "work", false)]
	[TestCase("<html />", "work", false)]
	[TestCase("<appcmd><ERROR /></appcmd>", "work", false)]
	[TestCase("<appcmd>ERROR</appcmd>", "work", false)]
	[TestCase("not XML", "work", false)]
	[Description("An IIS target is removable only when complete app metadata proves no sibling would be deleted.")]
	public void IsIisTargetExclusive_ShouldMatchSafeTargetTopology_WhenAppXmlIsProvided(string appsXml,
		string siteName,
		bool expected) {
		// Arrange

		// Act
		bool result = IisScannerHandler.IsIisTargetExclusive(appsXml, siteName);

		// Assert
		result.Should().Be(expected,
			because: "shared pools and their Windows profiles must survive uninstall of one application");
	}

	[TestCase(@"C:\sites\work", "pool", @"C:\sites\work", "pool", true)]
	[TestCase(@"C:\sites\replacement", "pool", @"C:\sites\work", "pool", false)]
	[TestCase(@"C:\sites\work", "replacement-pool", @"C:\sites\work", "pool", false)]
	[Description("A fresh IIS target must retain the originally resolved physical path and application pool.")]
	public void IsExpectedIisTargetIdentity_ShouldRejectSameNameReplacement(string actualPath,
		string actualAppPoolName,
		string expectedPath,
		string expectedAppPoolName,
		bool expected) {
		// Arrange

		// Act
		bool result = IisScannerHandler.IsExpectedIisTargetIdentity(actualPath, actualAppPoolName,
			expectedPath, expectedAppPoolName);

		// Assert
		result.Should().Be(expected,
			because: "a same-name replacement must not inherit authority from the originally selected target");
	}

	[TestCase("<appcmd />", "work", true)]
	[TestCase("<appcmd><APP APP.NAME=\"other/\" APPPOOL.NAME=\"other\" SITE.NAME=\"other\" /></appcmd>", "work", true)]
	[TestCase("<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"pool\" SITE.NAME=\"work\" /></appcmd>", "work", false)]
	[TestCase("<html />", "work", false)]
	[TestCase("<appcmd><ERROR /></appcmd>", "work", false)]
	[Description("An IIS target is absent only when complete AppCmd output proves it is no longer present.")]
	public void IsIisTargetAbsent_ShouldFailClosed_WhenAppXmlIsInvalidOrTargetRemains(string appsXml,
		string siteName,
		bool expected) {
		// Arrange

		// Act
		bool result = IisScannerHandler.IsIisTargetAbsent(appsXml, siteName);

		// Assert
		result.Should().Be(expected,
			because: "database and file deletion must not continue after an unverified IIS deletion");
	}

	[TestCase("<appcmd><APP APP.NAME=\"other/\" APPPOOL.NAME=\"other\" SITE.NAME=\"other\" /></appcmd>", "pool", true)]
	[TestCase("<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"pool\" SITE.NAME=\"work\" /></appcmd>", "pool", false)]
	[TestCase("<appcmd><APP APPPOOL.NAME=\"other\" SITE.NAME=\"other\" /></appcmd>", "pool", false)]
	[TestCase("<html />", "pool", false)]
	[TestCase("<appcmd><ERROR /></appcmd>", "pool", false)]
	[TestCase("<appcmd>ERROR</appcmd>", "pool", false)]
	[TestCase("not XML", "pool", false)]
	[Description("An application pool is deletable only after complete IIS metadata proves it has no assignments.")]
	public void CanDeleteAppPool_ShouldFailClosed_WhenAppXmlIsIncompleteOrPoolIsAssigned(string appsXml,
		string appPoolName,
		bool expected) {
		// Arrange

		// Act
		bool result = IisScannerHandler.CanDeleteAppPool(appsXml, appPoolName);

		// Assert
		result.Should().Be(expected,
			because: "application-pool ownership must be revalidated immediately before destructive removal");
	}

	[TestCase("<appcmd><APPPOOL APPPOOL.NAME=\"other\" /></appcmd>", "pool", true)]
	[TestCase("<appcmd><APPPOOL APPPOOL.NAME=\"pool\" /></appcmd>", "pool", false)]
	[TestCase("<appcmd><APPPOOL /></appcmd>", "pool", false)]
	[TestCase("<html />", "pool", false)]
	[TestCase("<appcmd><ERROR /></appcmd>", "pool", false)]
	[TestCase("<appcmd>ERROR</appcmd>", "pool", false)]
	[TestCase("not XML", "pool", false)]
	[Description("Application-pool deletion is considered successful only when complete IIS output proves absence.")]
	public void IsAppPoolAbsent_ShouldFailClosed_WhenPoolXmlIsIncompleteOrPoolRemains(string poolsXml,
		string appPoolName,
		bool expected) {
		// Arrange

		// Act
		bool result = IisScannerHandler.IsAppPoolAbsent(poolsXml, appPoolName);

		// Assert
		result.Should().Be(expected,
			because: "profile cleanup is safe only after application-pool removal is verified");
	}

	[TestCase("work", @"C:\sites\work", "pool", "delete site \"/site.name:work\"")]
	[TestCase("work/child", @"C:\sites\child", "pool", "delete app \"/app.name:work/child\"")]
	[Description("Target deletion uses the correct AppCmd object and requires identity, topology, and absence checks in order.")]
	public void TryDeleteIisTarget_ShouldVerifyIdentityAndAbsence_WhenTargetIsExclusive(string siteName,
		string physicalPath,
		string appPoolName,
		string expectedDeleteCommand) {
		// Arrange
		string appName = siteName.Contains('/') ? siteName : $"{siteName}/";
		string siteNameOnly = siteName.Split('/')[0];
		string targetXml = $"<appcmd><APP APP.NAME=\"{appName}\" APPPOOL.NAME=\"{appPoolName}\" SITE.NAME=\"{siteNameOnly}\" /></appcmd>";
		_processExecutor.Execute(Arg.Any<string>(), Arg.Is<string>(args => args.Contains("/text:physicalPath")), true)
			.Returns(physicalPath);
		_processExecutor.Execute(Arg.Any<string>(), Arg.Is<string>(args => args.Contains("/text:applicationPool")), true)
			.Returns(appPoolName);
		_processExecutor.Execute(Arg.Any<string>(), "list app /xml", true).Returns(targetXml, "<appcmd />");

		// Act
		bool result = _scanner.TryDeleteIisTarget(siteName, physicalPath, appPoolName);

		// Assert
		result.Should().BeTrue(
			because: "the target identity is unchanged and verified absent after the supported delete command");
		Received.InOrder(() => {
			_processExecutor.Execute(Arg.Any<string>(), "list app /xml", true);
			_processExecutor.Execute(Arg.Any<string>(), Arg.Is<string>(args => args.Contains("/text:physicalPath")), true);
			_processExecutor.Execute(Arg.Any<string>(), Arg.Is<string>(args => args.Contains("/text:applicationPool")), true);
			_processExecutor.Execute(Arg.Any<string>(), expectedDeleteCommand, true);
			_processExecutor.Execute(Arg.Any<string>(), "list app /xml", true);
		});
	}

	[Test]
	[Description("A same-name replacement is not stopped when its physical path no longer matches the resolved target.")]
	public void TryStopIisTarget_ShouldNotStopSite_WhenIdentityWasReplaced() {
		// Arrange
		_processExecutor.Execute(Arg.Any<string>(), "list app /xml", true).Returns(
			"<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"pool\" SITE.NAME=\"work\" /></appcmd>");
		_processExecutor.Execute(Arg.Any<string>(), Arg.Is<string>(args => args.Contains("/text:physicalPath")), true)
			.Returns(@"C:\sites\replacement");
		_processExecutor.Execute(Arg.Any<string>(), Arg.Is<string>(args => args.Contains("/text:applicationPool")), true)
			.Returns("pool");

		// Act
		bool result = _scanner.TryStopIisTarget("work", @"C:\sites\work", "pool");

		// Assert
		result.Should().BeFalse(
			because: "authority for the original site must not transfer to a same-name replacement");
		_processExecutor.DidNotReceive().Execute(Arg.Any<string>(), "stop site \"/site.name:work\"", true);
		_processExecutor.DidNotReceive().Execute(Arg.Any<string>(), "delete site \"/site.name:work\"", true);
	}

	[Test]
	[Description("A shared pool is not stopped or deleted when a fresh application inventory still references it.")]
	public void TryDeleteAppPoolIfUnused_ShouldPreservePool_WhenAssignmentRemains() {
		// Arrange
		_processExecutor.Execute(Arg.Any<string>(), "list app /xml", true).Returns(
			"<appcmd><APP APP.NAME=\"other/\" APPPOOL.NAME=\"pool\" SITE.NAME=\"other\" /></appcmd>");

		// Act
		bool result = _scanner.TryDeleteAppPoolIfUnused("pool");

		// Assert
		result.Should().BeFalse(
			because: "an application assignment retains ownership of the shared pool");
		_processExecutor.DidNotReceive().Execute(Arg.Any<string>(), Arg.Is<string>(args =>
			args.StartsWith("stop apppool") || args.StartsWith("delete apppool")), true);
	}

	[Test]
	[Description("An unused pool is deleted only between fresh assignment and verified-absence snapshots.")]
	public void TryDeleteAppPoolIfUnused_ShouldVerifyRemoval_WhenNoAssignmentRemains() {
		// Arrange
		_processExecutor.Execute(Arg.Any<string>(), "list app /xml", true).Returns("<appcmd />");
		_processExecutor.Execute(Arg.Any<string>(), "list apppool /xml", true).Returns("<appcmd />");

		// Act
		bool result = _scanner.TryDeleteAppPoolIfUnused("pool");

		// Assert
		result.Should().BeTrue(
			because: "complete AppCmd snapshots prove the pool unused before deletion and absent afterward");
		Received.InOrder(() => {
			_processExecutor.Execute(Arg.Any<string>(), "list app /xml", true);
			_processExecutor.Execute(Arg.Any<string>(), "delete apppool \"/apppool.name:pool\"", true);
			_processExecutor.Execute(Arg.Any<string>(), "list app /xml", true);
			_processExecutor.Execute(Arg.Any<string>(), "list apppool /xml", true);
		});
	}

	[Test]
	[Description("Validates the IIS scanner request explicitly and rejects invalid external link input before the scan runs.")]
	public async Task Handle_ShouldThrowValidationException_WhenIISScannerRequestIsInvalid() {
		// Arrange
		// A well-formed absolute URI with no "return" query parameter is invalid on every platform:
		// on Windows the OS rule passes and the ARG001 ("Return type cannot be empty") rule fails;
		// on non-Windows the OS001 rule fails first (CascadeMode.Stop short-circuits before the
		// null-Uri dereference). Both paths raise a FluentValidation.ValidationException.
		IExternalLinkHandler handler = Container.GetServices<IExternalLinkHandler>()
			.First(h => h.RequestType == typeof(IISScannerRequest));
		IISScannerRequest request = new() {
			Content = "clio://IISScannerRequest/"
		};

		// Act
		Func<Task> act = async () => await handler.Handle(request);

		// Assert
		await act.Should().ThrowAsync<ValidationException>(
			because: "the handler should run the registered FluentValidation validator before scanning IIS");
	}
}

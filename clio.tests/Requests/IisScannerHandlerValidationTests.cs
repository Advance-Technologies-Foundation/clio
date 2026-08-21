using System;
using System.Collections.Generic;
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
		_processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>()).Returns(
			Task.FromResult(new ProcessExecutionResult { Started = true, ExitCode = 0 }));
	}

	private void MockAppCmd(string arguments, params string[] outputs) {
		Queue<string> pending = new(outputs);
		_processExecutor.ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments == arguments)).Returns(_ => Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0,
				StandardOutput = pending.Count > 1 ? pending.Dequeue() : pending.Peek()
			}));
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
	// Creatio registers two applications per site - the root loader and the nested "/0". Requiring a
	// single application rejected every Creatio environment, so uninstall-creatio could never remove a
	// Creatio IIS site (#1093).
	[TestCase("<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"work\" SITE.NAME=\"work\" /><APP APP.NAME=\"work/0\" APPPOOL.NAME=\"work\" SITE.NAME=\"work\" /></appcmd>", "work", true)]
	// A sibling in the site's OWN pool must still block removal: whole-site deletion would destroy it,
	// and pool membership is not evidence of ownership.
	[TestCase("<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"work\" SITE.NAME=\"work\" /><APP APP.NAME=\"work/custom\" APPPOOL.NAME=\"work\" SITE.NAME=\"work\" /></appcmd>", "work", false)]
	// A sibling in a different pool must block removal too.
	[TestCase("<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"work\" SITE.NAME=\"work\" /><APP APP.NAME=\"work/0\" APPPOOL.NAME=\"work\" SITE.NAME=\"work\" /><APP APP.NAME=\"work/tenant/0\" APPPOOL.NAME=\"DefaultAppPool\" SITE.NAME=\"work\" /></appcmd>", "work", false)]
	// A pool shared with ANOTHER SITE does not block site removal: uninstall-creatio.md and
	// help/en/uninstall-creatio.txt promise that only the target is removed while the shared pool and its
	// Windows profile survive, which CanDeleteAppPool / TryDeleteAppPoolIfUnused enforce downstream.
	[TestCase("<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"shared\" SITE.NAME=\"work\" /><APP APP.NAME=\"other/\" APPPOOL.NAME=\"shared\" SITE.NAME=\"other\" /></appcmd>", "work", true)]
	// The site's root application must exist; a site whose only application is nested is unresolved.
	[TestCase("<appcmd><APP APP.NAME=\"work/0\" APPPOOL.NAME=\"work\" SITE.NAME=\"work\" /></appcmd>", "work", false)]
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
			because: "deleting a non-nested target removes the whole site, so it is removable only when the "
			+ "site holds no sibling beyond Creatio's own root and \"/0\" applications - regardless of which "
			+ "application pool they use, and independently of pools shared with other sites, which survive "
			+ "downstream");
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
		MockAppCmd($"list VDIR \"{siteName.TrimEnd('/')}/\" /text:physicalPath", physicalPath);
		MockAppCmd($"list APP \"{appName}\" /text:applicationPool", appPoolName);
		MockAppCmd("list app /xml", targetXml, "<appcmd />");

		// Act
		bool result = _scanner.TryDeleteIisTarget(siteName, physicalPath, appPoolName);

		// Assert
		result.Should().BeTrue(
			because: "the target identity is unchanged and verified absent after the supported delete command");
		_processExecutor.Received(1).ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments == expectedDeleteCommand));
	}

	[Test]
	[Description("A same-name replacement is not stopped when its physical path no longer matches the resolved target.")]
	public void TryStopIisTarget_ShouldNotStopSite_WhenIdentityWasReplaced() {
		// Arrange
		MockAppCmd("list app /xml",
			"<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"pool\" SITE.NAME=\"work\" /></appcmd>");
		MockAppCmd("list VDIR \"work/\" /text:physicalPath", @"C:\sites\replacement");
		MockAppCmd("list APP \"work/\" /text:applicationPool", "pool");

		// Act
		bool result = _scanner.TryStopIisTarget("work", @"C:\sites\work", "pool");

		// Assert
		result.Should().BeFalse(
			because: "authority for the original site must not transfer to a same-name replacement");
		_processExecutor.DidNotReceive().ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments == "stop site \"/site.name:work\""));
		_processExecutor.DidNotReceive().ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments == "delete site \"/site.name:work\""));
	}

	[Test]
	[Description("A nonzero AppCmd site-stop exit fails the safe target mutation.")]
	public void TryStopIisTarget_ShouldFail_WhenAppCmdSiteStopFails() {
		// Arrange
		MockAppCmd("list app /xml",
			"<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"pool\" SITE.NAME=\"work\" /></appcmd>");
		MockAppCmd("list VDIR \"work/\" /text:physicalPath", @"C:\sites\work");
		MockAppCmd("list APP \"work/\" /text:applicationPool", "pool");
		_processExecutor.ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments == "stop site \"/site.name:work\"")).Returns(Task.FromResult(
				new ProcessExecutionResult { Started = true, ExitCode = 1 }));

		// Act
		bool result = _scanner.TryStopIisTarget("work", @"C:\sites\work", "pool");

		// Assert
		result.Should().BeFalse(because: "database and file deletion must not follow a failed IIS stop");
	}

	[Test]
	[Description("An application pool is stopped when every assignment belongs to the IIS targets selected for removal.")]
	public void TryStopAppPoolIfOwnedByTargets_ShouldStopPool_WhenAllAssignmentsAreTargets() {
		// Arrange
		MockAppCmd("list app /xml",
			"<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"shared\" SITE.NAME=\"work\" />"
			+ "<APP APP.NAME=\"alias/\" APPPOOL.NAME=\"shared\" SITE.NAME=\"alias\" /></appcmd>");

		// Act
		IisAppPoolMutationResult result = _scanner.StopAppPoolIfOwnedByTargets("shared", ["work", "alias"]);

		// Assert
		result.Should().Be(IisAppPoolMutationResult.Completed,
			because: "stopping a pool is safe when every application assigned to it is being removed");
		_processExecutor.Received(1).ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments == "stop apppool \"/apppool.name:shared\""));
	}

	[Test]
	[Description("An application pool shared with an unrelated IIS application is left running.")]
	public void TryStopAppPoolIfOwnedByTargets_ShouldPreservePool_WhenUnrelatedAssignmentRemains() {
		// Arrange
		MockAppCmd("list app /xml",
			"<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"shared\" SITE.NAME=\"work\" />"
			+ "<APP APP.NAME=\"other/\" APPPOOL.NAME=\"shared\" SITE.NAME=\"other\" /></appcmd>");

		// Act
		IisAppPoolMutationResult result = _scanner.StopAppPoolIfOwnedByTargets("shared", ["work"]);

		// Assert
		result.Should().Be(IisAppPoolMutationResult.PreservedShared,
			because: "stopping the shared pool would interrupt an unrelated application");
		_processExecutor.DidNotReceive().ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments == "stop apppool \"/apppool.name:shared\""));
	}

	[Test]
	[Description("A shared pool is not stopped or deleted when a fresh application inventory still references it.")]
	public void TryDeleteAppPoolIfUnused_ShouldPreservePool_WhenAssignmentRemains() {
		// Arrange
		MockAppCmd("list app /xml",
			"<appcmd><APP APP.NAME=\"other/\" APPPOOL.NAME=\"pool\" SITE.NAME=\"other\" /></appcmd>");

		// Act
		IisAppPoolMutationResult result = _scanner.DeleteAppPoolIfUnused("pool");

		// Assert
		result.Should().Be(IisAppPoolMutationResult.PreservedShared,
			because: "an application assignment retains ownership of the shared pool");
		_processExecutor.DidNotReceive().ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments.StartsWith("stop apppool") || options.Arguments.StartsWith("delete apppool")));
	}

	[Test]
	[Description("An unused pool is deleted only between fresh assignment and verified-absence snapshots.")]
	public void TryDeleteAppPoolIfUnused_ShouldVerifyRemoval_WhenNoAssignmentRemains() {
		// Arrange
		MockAppCmd("list app /xml", "<appcmd />");
		MockAppCmd("list apppool /xml", "<appcmd><APPPOOL APPPOOL.NAME=\"pool\" /></appcmd>", "<appcmd />");

		// Act
		IisAppPoolMutationResult result = _scanner.DeleteAppPoolIfUnused("pool");

		// Assert
		result.Should().Be(IisAppPoolMutationResult.Completed,
			because: "complete AppCmd snapshots prove the pool unused before deletion and absent afterward");
		_processExecutor.Received(1).ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments == "delete apppool \"/apppool.name:pool\""));
	}

	[Test]
	[Description("Complete IIS discovery returns damaged or non-Creatio sites instead of filtering them from destructive path matching.")]
	public void TryFindAllIisTargets_ShouldReturnUnfilteredSitesAndNestedApplications() {
		// Arrange
		MockAppCmd("list sites /xml",
			"<appcmd><SITE SITE.NAME=\"work\" state=\"Started\" bindings=\"http/*:40100:\" /></appcmd>");
		MockAppCmd("list app /xml",
			"<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"root-pool\" SITE.NAME=\"work\" />"
			+ "<APP APP.NAME=\"work/0\" APPPOOL.NAME=\"webapp-pool\" SITE.NAME=\"work\" /></appcmd>");
		MockAppCmd("list VDIR \"work/\" /text:physicalPath", @"C:\broken-creatio");
		MockAppCmd("list APP \"work/\" /text:applicationPool", "root-pool");
		MockAppCmd("list VDIR \"work/0/\" /text:physicalPath", @"C:\broken-creatio\Terrasoft.WebApp");
		MockAppCmd("list APP \"work/0\" /text:applicationPool", "webapp-pool");

		// Act
		bool success = _scanner.TryFindAllIisTargets(out IReadOnlyList<UnregisteredSite> targets);

		// Assert
		success.Should().BeTrue(because: "valid AppCmd metadata is complete even when files are damaged");
		targets.Should().HaveCount(2, because: "both the root site and nested application must be visible");
		targets[0].siteType.Should().Be(SiteType.NotCreatioSite,
			because: "destructive discovery must not filter a damaged target by current file contents");
		targets.Select(target => target.siteBinding.appPoolName).Should().BeEquivalentTo(
			["root-pool", "webapp-pool"], because: "every assigned pool must remain available for cleanup");
	}

	[Test]
	[Description("Pool discovery includes every pool assigned to the root and slash-zero applications of a selected site.")]
	public void TryFindAppPoolsForTargets_ShouldReturnAllPoolsOwnedBySelectedRootSite() {
		// Arrange
		MockAppCmd("list app /xml",
			"<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"root-pool\" SITE.NAME=\"work\" />"
			+ "<APP APP.NAME=\"work/0\" APPPOOL.NAME=\"webapp-pool\" SITE.NAME=\"work\" />"
			+ "<APP APP.NAME=\"other/\" APPPOOL.NAME=\"foreign\" SITE.NAME=\"other\" /></appcmd>");

		// Act
		bool success = _scanner.TryFindAppPoolsForTargets(["work"], out IReadOnlyCollection<string> pools);

		// Assert
		success.Should().BeTrue(because: "the application inventory is complete");
		pools.Should().BeEquivalentTo(["root-pool", "webapp-pool"],
			because: "the root and slash-zero applications can legitimately use different pools");
	}

	[Test]
	[Description("A nonzero AppCmd exit makes complete IIS discovery fail closed.")]
	public void TryFindAllIisTargets_ShouldFail_WhenAppCmdInventoryCommandFails() {
		// Arrange
		_processExecutor.ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments == "list sites /xml")).Returns(Task.FromResult(new ProcessExecutionResult {
			Started = true,
			ExitCode = 1,
			StandardError = "fixture failure"
		}));

		// Act
		bool success = _scanner.TryFindAllIisTargets(out IReadOnlyList<UnregisteredSite> targets);

		// Assert
		success.Should().BeFalse(because: "an incomplete inventory cannot authorize filesystem deletion");
		targets.Should().BeEmpty(because: "partial discovery must never escape as authoritative metadata");
	}

	[Test]
	[Description("A failed AppCmd pool stop is distinguished from preserving a pool shared with unrelated applications.")]
	public void StopAppPoolIfOwnedByTargets_ShouldFail_WhenAppCmdStopFails() {
		// Arrange
		MockAppCmd("list app /xml",
			"<appcmd><APP APP.NAME=\"work/\" APPPOOL.NAME=\"pool\" SITE.NAME=\"work\" /></appcmd>");
		_processExecutor.ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments.StartsWith("stop apppool"))).Returns(Task.FromResult(new ProcessExecutionResult {
			Started = true,
			ExitCode = 1
		}));

		// Act
		IisAppPoolMutationResult result = _scanner.StopAppPoolIfOwnedByTargets("pool", ["work"]);

		// Assert
		result.Should().Be(IisAppPoolMutationResult.Failed,
			because: "the uninstaller must abort on mutation failure rather than treating it as shared preservation");
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

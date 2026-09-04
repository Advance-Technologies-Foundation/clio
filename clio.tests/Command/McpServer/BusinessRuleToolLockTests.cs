using System.Collections.Generic;
using Clio.Command.BusinessRules;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 19 (ENG-95262) regression tests for the execution-lock key taken by the business-rule tool family.
/// <para>
/// The eight business-rule tools all funnel their Creatio round-trip through
/// <c>BusinessRuleToolExecutor.Execute</c>, which resolves a per-tenant service. Before the fix they ran it
/// inside the ENVIRONMENT-LESS <c>ExecuteWithCleanLog</c> overload, and — a second, quieter defect — none of
/// them passed <c>commandResolver</c> to their <c>BaseTool</c> base constructor, so even the two create tools
/// that already used the options-aware overload still resolved to
/// <see cref="McpToolExecutionLock.SharedFallbackKey"/>. Either way every unrelated tenant serialized behind
/// one business-rule call.
/// </para>
/// <para>
/// One read tool and one create tool are covered here rather than all eight: they are the two distinct
/// pre-fix shapes (environment-less overload; options-aware overload with no resolver threaded), and the
/// remaining six are the same two shapes repeated per entity/page and per verb.
/// </para>
/// </summary>
// NonParallelizable: these tests swap substitutes into McpToolExecutionLock's PROCESS-GLOBAL static facade
// via Configure() and restore the real ones in TearDown. Under the assembly-level
// [Parallelizable(ParallelScope.Fixtures)] that window is visible to every other MCP tool test.
[NonParallelizable]
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class BusinessRuleToolLockTests {

	private const string TenantKey = "https://tenant-under-test.creatio.com|rule-author";

	private IToolCommandResolver _commandResolver = null!;
	private List<string> _lockKeys = null!;
	private List<string> _markInUseKeys = null!;
	private List<string> _markAvailableKeys = null!;

	[SetUp]
	public void SetUp() {
		ConsoleLogger.Instance.ClearMessages();
		_lockKeys = [];
		_markInUseKeys = [];
		_markAvailableKeys = [];

		_commandResolver = Substitute.For<IToolCommandResolver>();
		// MUST be stubbed: an unstubbed NSubstitute string returns "", which McpToolExecutionLock.Normalize
		// turns into SharedFallbackKey — so the fixed and the broken tool would look identical.
		_commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns(TenantKey);
		_commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<IEntityBusinessRuleService>());

		object sharedLock = new();
		ITenantExecutionLockProvider lockProvider = Substitute.For<ITenantExecutionLockProvider>();
		// GetLock is object-typed: an unstubbed substitute returns null and production `lock (...)` would die
		// at Monitor.ReliableEnter, so hand back one stable object for every key.
		lockProvider.GetLock(Arg.Do<string>(_lockKeys.Add)).Returns(sharedLock);
		lockProvider.When(provider => provider.MarkAvailable(Arg.Any<string>()))
			.Do(call => _markAvailableKeys.Add(call.Arg<string>()));
		ISessionContainerCache sessionCache = Substitute.For<ISessionContainerCache>();
		sessionCache.When(cache => cache.MarkInUse(Arg.Any<string>()))
			.Do(call => _markInUseKeys.Add(call.Arg<string>()));
		McpToolExecutionLock.Configure(lockProvider, sessionCache);
	}

	[TearDown]
	public void TearDown() {
		ConsoleLogger.Instance.ClearMessages();
		// Restore the production facade wiring, so a swapped-in double cannot leak into the next fixture.
		McpToolExecutionLock.Configure(
			TenantExecutionLockProvider.Shared,
			new SessionContainerCache(SessionContainerCacheDefaults.IdleTtl, SessionContainerCacheDefaults.MaxSessions));
		_commandResolver.ClearReceivedCalls();
	}

	[Test]
	[Description("Story 19 (ENG-95262) AC-04: read-entity-business-rules takes the RESOLVED tenant's execution lock, never McpToolExecutionLock.SharedFallbackKey — it used the environment-less overload while resolving a real per-tenant service, which serialized every other environment behind one read.")]
	public void ReadEntityBusinessRules_ShouldLockOnResolvedTenantKey_WhenEnvironmentResolves() {
		// Arrange
		ReadEntityBusinessRuleTool sut = new(_commandResolver, ConsoleLogger.Instance);
		ReadEntityBusinessRulesArgs args = new() {
			EnvironmentName = "dev",
			PackageName = "Custom",
			EntitySchemaName = "UsrDeliveryItem"
		};

		// Act
		sut.BusinessRulesRead(args);

		// Assert
		_lockKeys.Should().Equal([TenantKey],
			because: "the tool resolves a per-tenant business-rule service, so its execution lock must key on that tenant");
		_lockKeys.Should().NotContain(McpToolExecutionLock.SharedFallbackKey,
			because: "the shared fallback key is reserved for calls with no per-tenant identity; taking it blocks every other environment");
		_markInUseKeys.Should().Equal([TenantKey],
			because: "MarkInUse is skipped entirely for a fallback key, so seeing the tenant key proves a real tenant session was pinned");
		_markAvailableKeys.Should().Equal(_lockKeys,
			because: "GetLock pins the lock-provider mapping in-use at hand-out and only MarkAvailable releases it");
		// The lock key is only correct if it matches the key Resolve caches the container under. Both are
		// derived by ToolCommandResolver.ResolveSettingsAndKey from the SAME environment name, so proving the
		// resolve ran with that name inside the locked region is what makes the key equality real rather than
		// coincidental — and it proves the round-trip happened under the lock at all.
		_commandResolver.Received(1).Resolve<IEntityBusinessRuleService>(
			Arg.Is<EnvironmentOptions>(options => options.Environment == "dev"));
		_commandResolver.Received(1).GetTenantKey(
			Arg.Is<EnvironmentOptions>(options => options.Environment == "dev"));
	}

	[Test]
	[Description("Story 19 (ENG-95262) AC-04: create-entity-business-rules resolves a per-tenant lock key rather than the shared fallback. Scope: this covers the BASE-CONSTRUCTOR threading only — an empty rules batch is rejected before the Creatio round-trip, which is deliberate, because the key is computed from the options before the body runs and that is precisely the step this tool used to get wrong while looking correct.")]
	public void CreateEntityBusinessRules_ShouldLockOnResolvedTenantKey_WhenResolverIsThreadedToBase() {
		// Arrange
		CreateEntityBusinessRuleTool sut = new(_commandResolver, ConsoleLogger.Instance);
		CreateEntityBusinessRulesArgs args = new() {
			EnvironmentName = "dev",
			PackageName = "Custom",
			EntitySchemaName = "UsrDeliveryItem",
			Rules = []
		};

		// Act
		sut.BusinessRuleCreate(args);

		// Assert
		_lockKeys.Should().Equal([TenantKey],
			because: "threading commandResolver to the BaseTool base constructor is what lets ResolveTenantLockKey reach a real tenant key at all");
		_lockKeys.Should().NotContain(McpToolExecutionLock.SharedFallbackKey,
			because: "an options-aware overload on a tool whose base has no resolver silently degrades to the shared key");
		_markInUseKeys.Should().Equal([TenantKey],
			because: "MarkInUse is skipped entirely for a fallback key, so seeing the tenant key proves a real tenant session was pinned");
		_markAvailableKeys.Should().Equal(_lockKeys,
			because: "GetLock pins the lock-provider mapping in-use at hand-out and only MarkAvailable releases it");
	}
}

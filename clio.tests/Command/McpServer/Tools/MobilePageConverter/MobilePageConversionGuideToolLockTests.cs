using System;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Story 12 (ENG-95262) regression tests for the per-tenant execution lock taken by
/// <see cref="MobilePageConversionGuideTool"/> around every page read.
/// <para>
/// Before the fix the tool took <c>McpToolExecutionLock.GetLock(SharedFallbackKey)</c> at three sites and
/// never called the balancing <c>MarkAvailable</c>. Two defects followed: unrelated tenants serialized
/// behind one mobile conversion, and — because <c>GetLock</c> pins the lock-provider mapping in-use at
/// hand-out — that mapping was pinned permanently, so it could never be evicted again.
/// </para>
/// <para>
/// All three sites (the source-page read, the web-template baseline, the mobile-template probe) now route
/// through the single <see cref="MobilePageConversionGuideTool.ReadPageUnderTenantLock"/> seam, which is
/// what these tests drive directly: exercising the seam covers all three call sites, and it needs no live
/// environment.
/// </para>
/// </summary>
// NonParallelizable: these tests swap substitutes into McpToolExecutionLock's PROCESS-GLOBAL static facade
// via Configure() and restore the real ones in TearDown. Under the assembly-level
// [Parallelizable(ParallelScope.Fixtures)] that window is visible to every other MCP tool test — see the
// RestartToolTests header for the two directions in which such a swap leaks.
[NonParallelizable]
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class MobilePageConversionGuideToolLockTests {

	private const string TenantKey = "https://tenant-under-test.creatio.com|converter-user";

	private IToolCommandResolver _commandResolver;
	private ILogger _logger;
	private MobilePageConversionGuideTool _sut;

	[SetUp]
	public void SetUp() {
		_commandResolver = Substitute.For<IToolCommandResolver>();
		_logger = Substitute.For<ILogger>();
		_commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns(TenantKey);
		_commandResolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>()).Returns(CreateInertPageGetCommand());
		_sut = new MobilePageConversionGuideTool(
			_commandResolver,
			_logger,
			Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(),
			Substitute.For<IWebToMobilePageConversionRulesCatalog>(),
			Substitute.For<IPlatformVersionResolverFactory>(),
			Substitute.For<ISettingsRepository>());
	}

	[TearDown]
	public void TearDown() {
		// Restore the production facade wiring, so a swapped-in double cannot leak into the next fixture.
		McpToolExecutionLock.Configure(
			TenantExecutionLockProvider.Shared,
			new SessionContainerCache(SessionContainerCacheDefaults.IdleTtl, SessionContainerCacheDefaults.MaxSessions));
		_commandResolver.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("TC-U-F04: the page read locks on the RESOLVED tenant key, never on the shared fallback key that would serialize unrelated tenants.")]
	public void ReadPageUnderTenantLock_Should_Lock_On_Resolved_Tenant_Key_Not_SharedFallbackKey() {
		// Arrange
		(ITenantExecutionLockProvider lockProvider, ISessionContainerCache sessionCache) = ConfigureLockDoubles();

		// Act
		_sut.ReadPageUnderTenantLock(NewOptions("UsrContactFormPage"));

		// Assert
		lockProvider.Received(1).GetLock(TenantKey);
		lockProvider.DidNotReceive().GetLock(McpToolExecutionLock.SharedFallbackKey);
		sessionCache.Received(1).MarkInUse(TenantKey);
		sessionCache.DidNotReceive().MarkInUse(McpToolExecutionLock.SharedFallbackKey);
	}

	[Test]
	[Description("TC-U-F03: on the normal path every GetLock is balanced by exactly one MarkAvailable for the same key, so the lock-provider mapping is left unpinned.")]
	public void ReadPageUnderTenantLock_Should_Balance_GetLock_With_MarkAvailable_On_Success_Path() {
		// Arrange
		(ITenantExecutionLockProvider lockProvider, ISessionContainerCache sessionCache) = ConfigureLockDoubles();

		// Act
		_sut.ReadPageUnderTenantLock(NewOptions("UsrContactFormPage"));

		// Assert
		lockProvider.Received(1).GetLock(TenantKey);
		lockProvider.Received(1).MarkAvailable(TenantKey);
		sessionCache.Received(1).MarkAvailable(TenantKey);
	}

	[Test]
	[Description("TC-U-F03: a failure while resolving the command inside the lock still releases the in-use pin, and the exception reaches the caller unchanged.")]
	public void ReadPageUnderTenantLock_Should_Balance_GetLock_With_MarkAvailable_On_Exception_Path() {
		// Arrange
		(ITenantExecutionLockProvider lockProvider, ISessionContainerCache sessionCache) = ConfigureLockDoubles();
		_commandResolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>())
			.Throws(new EnvironmentResolutionException("environment 'ghost' is not registered"));

		// Act
		Action act = () => _sut.ReadPageUnderTenantLock(NewOptions("UsrContactFormPage"));

		// Assert
		act.Should().Throw<EnvironmentResolutionException>(
			because: "the caller decides how a resolution failure degrades; the lock helper must not swallow it");
		lockProvider.Received(1).GetLock(TenantKey);
		lockProvider.Received(1).MarkAvailable(TenantKey);
		sessionCache.Received(1).MarkAvailable(TenantKey);
	}

	[Test]
	[Description("TC-U-F03: against the real lock provider the tenant mapping is evictable once the call completes - an unbalanced pin would keep it alive forever.")]
	public void ReadPageUnderTenantLock_Should_Leave_Mapping_Evictable_After_A_Completed_Call() {
		// Arrange — a real provider on a controllable clock so idle eviction can be forced rather than waited out.
		DateTime now = new(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc);
		TimeSpan idleTtl = TimeSpan.FromMinutes(5);
		ITenantExecutionLockProvider lockProvider =
			new TenantExecutionLockProvider(idleTtl, maxEntries: 16, utcNow: () => now);
		McpToolExecutionLock.Configure(lockProvider, Substitute.For<ISessionContainerCache>());

		// A deliberately UNBALANCED hand-out on a second key, so this test proves its own mechanism: a leaked
		// pin is exactly what the pre-fix tool left behind, and the assertions below show the difference is
		// observable rather than assumed.
		const string leakedKey = "https://other-tenant.creatio.com|leaked";
		object leakedLockBefore = lockProvider.GetLock(leakedKey);

		// Act — one completed read, then a balanced probe pair that captures the mapping's current lock object.
		_sut.ReadPageUnderTenantLock(NewOptions("UsrContactFormPage"));
		object lockBeforeEviction = lockProvider.GetLock(TenantKey);
		lockProvider.MarkAvailable(TenantKey);

		// The provider sweeps idle mappings on every hand-out, so advancing past the TTL and asking again is
		// what makes eviction observable.
		now = now.Add(idleTtl).AddMinutes(1);
		object lockAfterEviction = lockProvider.GetLock(TenantKey);
		lockProvider.MarkAvailable(TenantKey);
		object leakedLockAfter = lockProvider.GetLock(leakedKey);
		lockProvider.MarkAvailable(leakedKey);

		// Assert
		lockAfterEviction.Should().NotBeSameAs(lockBeforeEviction,
			because: "a completed read leaves no in-use pin, so the idle mapping is evicted and the next hand-out mints a fresh lock object");
		leakedLockAfter.Should().BeSameAs(leakedLockBefore,
			because: "an unbalanced GetLock keeps its mapping pinned past the idle TTL - which is what the assertion above would have seen had the tool still leaked its pin");
	}

	// Swaps test doubles into the process-global facade. GetLock is object-typed, so an unstubbed substitute
	// hands back null and the production `lock (...)` would die at Monitor.ReliableEnter; return one stable
	// object per key instead, mirroring the real provider's same-key-same-lock contract.
	private static (ITenantExecutionLockProvider LockProvider, ISessionContainerCache SessionCache) ConfigureLockDoubles() {
		ITenantExecutionLockProvider lockProvider = Substitute.For<ITenantExecutionLockProvider>();
		object sharedLock = new();
		lockProvider.GetLock(Arg.Any<string>()).Returns(sharedLock);
		ISessionContainerCache sessionCache = Substitute.For<ISessionContainerCache>();
		McpToolExecutionLock.Configure(lockProvider, sessionCache);
		return (lockProvider, sessionCache);
	}

	private static PageGetOptions NewOptions(string schemaName) => new() {
		SchemaName = schemaName,
		Uri = "https://tenant-under-test.creatio.com",
		Login = "converter-user",
		Password = "not-a-real-password"
	};

	// A PageGetCommand whose collaborators are all substitutes: TryGetPage wraps its whole body in a
	// catch-all, so it degrades to Success=false without any network access and without throwing. These
	// tests are about the lock markers, not about what the read returns.
	private static PageGetCommand CreateInertPageGetCommand() => new(
		Substitute.For<IApplicationClient>(),
		Substitute.For<IServiceUrlBuilder>(),
		Substitute.For<ILogger>(),
		Substitute.For<IPageDesignerHierarchyClient>(),
		Substitute.For<IPageSchemaBodyParser>(),
		Substitute.For<IPageBundleBuilder>(),
		Substitute.For<IPageFileWriter>());
}

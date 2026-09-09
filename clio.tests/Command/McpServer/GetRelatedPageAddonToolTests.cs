using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Command.RelatedPages;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

// NonParallelizable: the lock-key test swaps substitutes into McpToolExecutionLock's PROCESS-GLOBAL static
// facade via Configure() and TearDown restores the production wiring. Under the assembly-level
// [Parallelizable(ParallelScope.Fixtures)] that window would otherwise be visible to every other MCP tool
// test — see the MobilePageConversionGuideToolLockTests header for the same reasoning.
[TestFixture]
[NonParallelizable]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class GetRelatedPageAddonToolTests {
	private const string TenantKey = "https://tenant-under-test.creatio.com|addon-reader";

	private IRelatedPageAddonService _service = null!;
	private GetRelatedPageAddonOptions _resolverOptions;
	private RelatedPageAddonReadRequest _capturedRead;
	private IToolCommandResolver _commandResolver = null!;
	private GetRelatedPageAddonTool _tool = null!;

	[SetUp]
	public void SetUp() {
		ConsoleLogger.Instance.ClearMessages();
		_resolverOptions = null;
		_capturedRead = null;

		// The environment-resolved command runs for real over a mocked service (no command subclass — sealed).
		_service = Substitute.For<IRelatedPageAddonService>();
		_service.Get(Arg.Any<RelatedPageAddonReadRequest>()).Returns(call => {
			_capturedRead = call.Arg<RelatedPageAddonReadRequest>();
			return new RelatedPageAddonReadResult(
				"UsrDeliveryItem", "bb000000-0000-0000-0000-000000000002", "Custom",
				"aa000000-0000-0000-0000-000000000001", "RelatedPage", null, 1,
				new[] {
					new RelatedPageEntry("cc000000-0000-0000-0000-00000000000a", "UsrDeliveryItemFormPage",
						true, false, false, null, null, null)
				});
		});
		GetRelatedPageAddonCommand resolvedCommand = new(_service, ConsoleLogger.Instance);

		_commandResolver = Substitute.For<IToolCommandResolver>();
		// MUST be stubbed: an unstubbed NSubstitute string returns "", which McpToolExecutionLock.Normalize
		// turns into SharedFallbackKey — so the fixed and the broken tool would look identical.
		_commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns(TenantKey);
		_commandResolver.Resolve<GetRelatedPageAddonCommand>(Arg.Any<GetRelatedPageAddonOptions>())
			.Returns(call => {
				_resolverOptions = call.Arg<GetRelatedPageAddonOptions>();
				return resolvedCommand;
			});

		IRelatedPageAddonService defaultService = Substitute.For<IRelatedPageAddonService>();
		defaultService.Get(Arg.Any<RelatedPageAddonReadRequest>())
			.Returns(_ => throw new InvalidOperationException("the startup default command must not be invoked"));
		GetRelatedPageAddonCommand defaultCommand = new(defaultService, ConsoleLogger.Instance);

		_tool = new GetRelatedPageAddonTool(defaultCommand, ConsoleLogger.Instance, _commandResolver);
	}

	[TearDown]
	public void TearDown() {
		ConsoleLogger.Instance.ClearMessages();
		// Unconditional: a substitute left behind in the process-global facade would leak into every
		// following fixture, so the production wiring is restored even for tests that never swapped it.
		McpToolExecutionLock.Configure(
			TenantExecutionLockProvider.Shared,
			new SessionContainerCache(SessionContainerCacheDefaults.IdleTtl, SessionContainerCacheDefaults.MaxSessions));
		_commandResolver.ClearReceivedCalls();
	}

	private static GetRelatedPageAddonArgs Args(
		string entitySchemaName = "UsrDeliveryItem",
		string packageName = "Custom",
		string environmentName = "dev",
		string schemaType = null) =>
		new(entitySchemaName, packageName, environmentName, null, null, null, schemaType);

	[Test]
	[Description("Resolves the command for the requested environment, maps the args onto the options, and returns the resolved command's read response.")]
	public void GetRelatedPageAddon_ShouldResolveCommandAndReturnResponse_WhenEnvironmentProvided() {
		// Act
		GetRelatedPageAddonResponse response = _tool.GetRelatedPageAddon(Args());

		// Assert
		response.Success.Should().BeTrue(
			because: "the resolved command reports success");
		response.EntitySchemaName.Should().Be("UsrDeliveryItem",
			because: "the tool returns the resolved command's response built from the service read result");
		response.PageCount.Should().Be(1,
			because: "the decoded page count flows through the resolved command's response");

		_resolverOptions.Should().NotBeNull(
			because: "the environment-resolved command is invoked, not the startup default");
		_resolverOptions.EntitySchemaName.Should().Be("UsrDeliveryItem",
			because: "entity-schema-name maps onto the command options");
		_resolverOptions.PackageName.Should().Be("Custom",
			because: "package-name maps onto the command options");
		_resolverOptions.Environment.Should().Be("dev",
			because: "the requested environment-name is threaded onto the command options");
	}

	[Test]
	[Description("Maps schema-type=mobile through the options and into the RelatedPageAddonReadRequest as SchemaType.Mobile, so the read targets the MobileRelatedPage add-on; an omitted schema-type defaults to the web RelatedPage add-on.")]
	public void GetRelatedPageAddon_ShouldMapSchemaType_OntoReadRequest() {
		// Act — mobile read.
		_tool.GetRelatedPageAddon(Args(schemaType: "mobile"));

		// Assert
		_resolverOptions.Should().NotBeNull(because: "the tool must resolve an environment-bound command");
		_resolverOptions.SchemaType.Should().Be("mobile",
			because: "the schema-type arg maps onto the command options");
		_capturedRead.Should().NotBeNull(because: "the resolved command forwards a read request to the service");
		_capturedRead.SchemaType.Should().Be(RelatedPageSchemaType.Mobile,
			because: "the command parses schema-type=mobile into SchemaType.Mobile so the MobileRelatedPage add-on is read");

		// Act — omitted schema-type defaults to web.
		_tool.GetRelatedPageAddon(Args());

		// Assert
		_capturedRead.SchemaType.Should().Be(RelatedPageSchemaType.Web,
			because: "an omitted schema-type defaults to the web RelatedPage add-on");
	}

	[Test]
	[Description("Rejects a blank entity-schema-name in the structured response without resolving a command.")]
	public void GetRelatedPageAddon_ShouldRejectMissingEntitySchemaName_WhenBlank() {
		// Act
		GetRelatedPageAddonResponse response = _tool.GetRelatedPageAddon(Args(entitySchemaName: " "));

		// Assert
		response.Success.Should().BeFalse(
			because: "a blank entity-schema-name is invalid input");
		response.Error.Should().Contain("entity-schema-name",
			because: "the error should name the missing field");
		_commandResolver.DidNotReceiveWithAnyArgs().Resolve<GetRelatedPageAddonCommand>(default!);
	}

	[Test]
	[Description("Rejects a blank package-name in the structured response without resolving a command.")]
	public void GetRelatedPageAddon_ShouldRejectMissingPackageName_WhenBlank() {
		// Act
		GetRelatedPageAddonResponse response = _tool.GetRelatedPageAddon(Args(packageName: " "));

		// Assert
		response.Success.Should().BeFalse(
			because: "a blank package-name is invalid input");
		response.Error.Should().Contain("package-name",
			because: "the error should name the missing field");
		_commandResolver.DidNotReceiveWithAnyArgs().Resolve<GetRelatedPageAddonCommand>(default!);
	}

	[Test]
	[Description("Story 19 (ENG-95262) AC-02: the read takes the RESOLVED tenant's execution lock, never McpToolExecutionLock.SharedFallbackKey — that shared key is what every environment-less tool falls back to, so holding it across this Creatio round-trip would serialize every other environment behind one tenant's read.")]
	public void GetRelatedPageAddon_ShouldLockOnResolvedTenantKey_WhenEnvironmentResolves() {
		// Arrange
		LockKeyRecorder recorder = ConfigureLockDoubles();

		// Act
		_tool.GetRelatedPageAddon(Args());

		// Assert
		recorder.LockKeys.Should().Equal([TenantKey],
			because: "the tool must key its execution lock on the tenant the command resolved for, so tenants do not serialize against each other");
		recorder.LockKeys.Should().NotContain(McpToolExecutionLock.SharedFallbackKey,
			because: "the shared fallback key is reserved for calls that carry no per-tenant identity; taking it here blocks every other environment");
		recorder.MarkInUseKeys.Should().Equal([TenantKey],
			because: "MarkInUse is skipped entirely for a fallback key, so seeing the tenant key proves a real tenant session was pinned rather than the fallback");
	}

	[Test]
	[Description("Story 19 (ENG-95262) AC-02: every GetLock the read takes is balanced by exactly one MarkAvailable for the same key, so the lock-provider mapping is not left pinned in-use after the call.")]
	public void GetRelatedPageAddon_ShouldBalanceGetLockWithMarkAvailable_WhenCallCompletes() {
		// Arrange
		LockKeyRecorder recorder = ConfigureLockDoubles();

		// Act
		_tool.GetRelatedPageAddon(Args());

		// Assert
		recorder.MarkAvailableKeys.Should().Equal(recorder.LockKeys,
			because: "GetLock pins the lock-provider mapping in-use at hand-out and only MarkAvailable releases it");
		recorder.MarkAvailableKeys.Should().NotContain(McpToolExecutionLock.SharedFallbackKey,
			because: "releasing the shared fallback key would mean the call had taken it in the first place");
	}

	// Swaps recording doubles into the PROCESS-GLOBAL McpToolExecutionLock facade; TearDown restores the
	// production wiring. GetLock is object-typed, so an unstubbed substitute hands back null and the
	// production `lock (...)` would die at Monitor.ReliableEnter — return one stable object for every key,
	// mirroring the real provider's same-key-same-lock contract.
	private static LockKeyRecorder ConfigureLockDoubles() {
		LockKeyRecorder recorder = new();
		object sharedLock = new();
		ITenantExecutionLockProvider lockProvider = Substitute.For<ITenantExecutionLockProvider>();
		lockProvider.GetLock(Arg.Do<string>(recorder.LockKeys.Add)).Returns(sharedLock);
		lockProvider.When(provider => provider.MarkAvailable(Arg.Any<string>()))
			.Do(call => recorder.MarkAvailableKeys.Add(call.Arg<string>()));
		ISessionContainerCache sessionCache = Substitute.For<ISessionContainerCache>();
		sessionCache.When(cache => cache.MarkInUse(Arg.Any<string>()))
			.Do(call => recorder.MarkInUseKeys.Add(call.Arg<string>()));
		McpToolExecutionLock.Configure(lockProvider, sessionCache);
		return recorder;
	}

	// Data-only carrier for the keys the facade was driven with, so assertions can name the key rather than
	// merely counting that some lock was taken.
	private sealed record LockKeyRecorder {
		public List<string> LockKeys { get; } = [];
		public List<string> MarkInUseKeys { get; } = [];
		public List<string> MarkAvailableKeys { get; } = [];
	}
}

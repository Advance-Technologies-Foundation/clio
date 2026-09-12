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
// [Parallelizable(ParallelScope.Fixtures)] that window would otherwise be visible to every other MCP tool test.
[TestFixture]
[NonParallelizable]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class CreateRelatedPageAddonToolTests {
	private const string TenantKey = "https://tenant-under-test.creatio.com|addon-writer";

	private IRelatedPageAddonService _service = null!;
	private IRelatedPageAddonService _defaultService = null!;
	private RelatedPageAddonRequest _captured;
	private CreateRelatedPageAddonOptions _resolverOptions;
	private IToolCommandResolver _commandResolver = null!;
	private CreateRelatedPageAddonTool _tool = null!;

	[SetUp]
	public void SetUp() {
		ConsoleLogger.Instance.ClearMessages();
		_captured = null;
		_resolverOptions = null;

		// The environment-resolved command runs for real over a mocked service, so the test exercises the
		// command's actual mapping instead of a hand-written stand-in (no command subclass — the command is sealed).
		_service = Substitute.For<IRelatedPageAddonService>();
		_service.Create(Arg.Any<RelatedPageAddonRequest>()).Returns(call => {
			_captured = call.Arg<RelatedPageAddonRequest>();
			return new RelatedPageAddonResult("entity-uid", "package-uid", _captured.Pages.Count, "RelatedPage");
		});
		CreateRelatedPageAddonCommand resolvedCommand = new(_service, ConsoleLogger.Instance);

		_commandResolver = Substitute.For<IToolCommandResolver>();
		// MUST be stubbed: an unstubbed NSubstitute string returns "", which McpToolExecutionLock.Normalize
		// turns into SharedFallbackKey — so the fixed and the broken tool would look identical.
		_commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns(TenantKey);
		_commandResolver.Resolve<CreateRelatedPageAddonCommand>(Arg.Any<CreateRelatedPageAddonOptions>())
			.Returns(call => {
				_resolverOptions = call.Arg<CreateRelatedPageAddonOptions>();
				return resolvedCommand;
			});

		// The startup-injected default command must never run when an environment is requested; back it with a
		// service that fails the test if its round-trip is ever invoked.
		_defaultService = Substitute.For<IRelatedPageAddonService>();
		_defaultService.Create(Arg.Any<RelatedPageAddonRequest>())
			.Returns(_ => throw new InvalidOperationException("the startup default command must not be invoked"));
		CreateRelatedPageAddonCommand defaultCommand = new(_defaultService, ConsoleLogger.Instance);

		_tool = new CreateRelatedPageAddonTool(defaultCommand, ConsoleLogger.Instance, _commandResolver);
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

	private static CreateRelatedPageAddonArgs Args(
		string entitySchemaName = "UsrDeliveryItem",
		string packageName = "UsrDeliveryTracking",
		System.Collections.Generic.IReadOnlyList<RelatedPageArg> pages = null,
		string environmentName = "dev",
		string schemaType = null) =>
		new(
			entitySchemaName,
			packageName,
			pages ?? new[] { new RelatedPageArg("UsrDeliveryItemFormPage", true, null, null, null, null) },
			null,
			environmentName,
			null,
			null,
			null,
			schemaType);

	[Test]
	[Description("Resolves the command for the requested environment, maps the args onto the command options, and returns the resolved command's response.")]
	public void CreateRelatedPageAddon_ShouldMapArgsAndReturnResolvedResponse_WhenEnvironmentProvided() {
		// Act
		CreateRelatedPageAddonResponse response = _tool.CreateRelatedPageAddon(Args(pages: new[] {
			new RelatedPageArg("UsrDeliveryItemFormPage", true, null, null, null, null),
			new RelatedPageArg("UsrDeliveryItemAddPage", null, true, null, null, null)
		}));

		// Assert — the tool surfaces the RESOLVED command's response, proving it ran and its response flowed back.
		response.Success.Should().BeTrue(
			because: "the resolved command reports success for a valid pages payload");
		response.EntitySchemaName.Should().Be("UsrDeliveryItem",
			because: "the tool returns the resolved command's response built from the mapped options");
		response.PageCount.Should().Be(2,
			because: "both mapped page entries reached the resolved command's service request");

		// The options handed to the resolver carry the full arg→option mapping the tool is responsible for.
		_resolverOptions.Should().NotBeNull(
			because: "the tool must resolve an environment-bound command, not the startup default");
		_resolverOptions.EntitySchemaName.Should().Be("UsrDeliveryItem",
			because: "entity-schema-name maps onto the command options");
		_resolverOptions.PackageName.Should().Be("UsrDeliveryTracking",
			because: "package-name maps onto the command options");
		_resolverOptions.Environment.Should().Be("dev",
			because: "the requested environment-name is threaded onto the command options");
		_resolverOptions.Pages.Should().HaveCount(2,
			because: "both pages-array entries are mapped to RelatedPageSpec");
		_resolverOptions.Pages[0].IsDefault.Should().BeTrue(
			because: "the first entry's is-default flag maps through");
		_resolverOptions.Pages[1].IsAdd.Should().BeTrue(
			because: "the second entry's is-add flag maps through");

		_captured.Pages.Should().HaveCount(2,
			because: "the resolved command forwards the mapped pages to the related-page service");
		_defaultService.DidNotReceiveWithAnyArgs().Create(default!);
	}

	[Test]
	[Description("Maps every RelatedPageArg field onto the correct RelatedPageSpec property (and the top-level type-column-uid), so a positional-record field swap (e.g. role <-> role-name) is caught.")]
	public void CreateRelatedPageAddon_ShouldMapEveryFieldOntoTheCorrectProperty_WhenAllFieldsSet() {
		// Arrange — one entry with every field set to a distinct, identifiable value.
		RelatedPageArg arg = new(
			"UsrMappingPage",                        // page-schema-name
			true,                                     // is-default
			false,                                    // is-add (distinct from is-default to catch a swap)
			true,                                     // is-ssp-default
			"11111111-1111-1111-1111-111111111111",  // role
			"TYPE-VALUE-RECORD-ID",                   // type-column-value
			"All external users");                    // role-name
		CreateRelatedPageAddonArgs args = new(
			"UsrDeliveryItem", "UsrDeliveryTracking", new[] { arg },
			"TYPE-COLUMN-UID", "dev", null, null, null);

		// Act — the resolver captures the mapped options before the command runs, so an invalid role+role-name
		// combo on the entry does not matter here; this test asserts the field-by-field mapping only.
		_tool.CreateRelatedPageAddon(args);

		// Assert
		_resolverOptions.Should().NotBeNull(
			because: "the tool must forward mapped options to the command resolver");
		_resolverOptions.TypeColumnUId.Should().Be("TYPE-COLUMN-UID",
			because: "the top-level type-column-uid maps onto the command options");
		RelatedPageSpec spec = _resolverOptions.Pages.Should().ContainSingle().Which;
		spec.PageSchemaName.Should().Be("UsrMappingPage", because: "page-schema-name maps by position");
		spec.IsDefault.Should().BeTrue(because: "is-default maps by position");
		spec.IsAdd.Should().BeFalse(because: "is-add maps by position, not swapped with is-default");
		spec.IsSspDefault.Should().BeTrue(because: "is-ssp-default maps by position");
		spec.Role.Should().Be("11111111-1111-1111-1111-111111111111",
			because: "role maps by position, not swapped with role-name");
		spec.RoleName.Should().Be("All external users",
			because: "role-name maps by position, not swapped with role");
		spec.TypeColumnValue.Should().Be("TYPE-VALUE-RECORD-ID",
			because: "type-column-value maps by position");
	}

	[Test]
	[Description("Maps schema-type=mobile through the options and into the RelatedPageAddonRequest as SchemaType.Mobile, so the command writes the MobileRelatedPage add-on rather than the web RelatedPage add-on.")]
	public void CreateRelatedPageAddon_ShouldMapMobileSchemaType_OntoMobileAddonRequest() {
		// Act
		_tool.CreateRelatedPageAddon(Args(schemaType: "mobile"));

		// Assert
		_resolverOptions.Should().NotBeNull(because: "the tool must resolve an environment-bound command");
		_resolverOptions.SchemaType.Should().Be("mobile",
			because: "the schema-type arg maps onto the command options");
		_captured.Should().NotBeNull(because: "the resolved command forwards a request to the service");
		_captured.SchemaType.Should().Be(RelatedPageSchemaType.Mobile,
			because: "the command parses schema-type=mobile into SchemaType.Mobile so the MobileRelatedPage add-on is targeted");
	}

	[Test]
	[Description("Defaults to the web RelatedPage add-on (SchemaType.Web) when schema-type is omitted, preserving the tool's original web-only behavior for existing callers.")]
	public void CreateRelatedPageAddon_ShouldDefaultToWebSchemaType_WhenOmitted() {
		// Act — no schema-type supplied.
		_tool.CreateRelatedPageAddon(Args());

		// Assert
		_captured.Should().NotBeNull(because: "the resolved command forwards a request to the service");
		_captured.SchemaType.Should().Be(RelatedPageSchemaType.Web,
			because: "an omitted schema-type defaults to the web RelatedPage add-on");
	}

	[Test]
	[Description("Rejects a blank entity-schema-name in the structured response without resolving a command.")]
	public void CreateRelatedPageAddon_ShouldRejectMissingEntitySchemaName_WhenBlank() {
		// Act
		CreateRelatedPageAddonResponse response = _tool.CreateRelatedPageAddon(Args(entitySchemaName: " "));

		// Assert
		response.Success.Should().BeFalse(
			because: "a blank entity-schema-name is invalid input");
		response.Error.Should().Contain("entity-schema-name",
			because: "the error should name the missing field");
		_commandResolver.DidNotReceiveWithAnyArgs().Resolve<CreateRelatedPageAddonCommand>(default!);
	}

	[Test]
	[Description("Rejects a blank package-name in the structured response without resolving a command.")]
	public void CreateRelatedPageAddon_ShouldRejectMissingPackageName_WhenBlank() {
		// Act
		CreateRelatedPageAddonResponse response = _tool.CreateRelatedPageAddon(Args(packageName: " "));

		// Assert
		response.Success.Should().BeFalse(
			because: "a blank package-name is invalid input");
		response.Error.Should().Contain("package-name",
			because: "the error should name the missing field");
		_commandResolver.DidNotReceiveWithAnyArgs().Resolve<CreateRelatedPageAddonCommand>(default!);
	}

	[Test]
	[Description("Accepts an empty pages array as a reset-to-inline: forwards the empty set through the resolved command to the service (clear all bindings) rather than rejecting at the tool layer.")]
	public void CreateRelatedPageAddon_ShouldForwardEmptyPages_AsResetToInline() {
		// Act
		CreateRelatedPageAddonResponse response = _tool.CreateRelatedPageAddon(Args(pages: Array.Empty<RelatedPageArg>()));

		// Assert
		response.Success.Should().BeTrue(
			because: "an empty pages list is a valid reset-to-inline, not a rejected request");
		_commandResolver.Received(1).Resolve<CreateRelatedPageAddonCommand>(Arg.Any<CreateRelatedPageAddonOptions>());
		_resolverOptions.Pages.Should().BeEmpty(
			because: "the empty set is forwarded to the command to clear all bindings");
		_captured.Pages.Should().BeEmpty(
			because: "the command forwards the empty set to the service (the effective delete)");
	}

	[Test]
	[Description("Rejects a null entry inside the pages array in the structured response without resolving a command.")]
	public void CreateRelatedPageAddon_ShouldRejectNullPagesEntry_WhenPresent() {
		// Act
		CreateRelatedPageAddonResponse response = _tool.CreateRelatedPageAddon(Args(pages: new RelatedPageArg[] { null }));

		// Assert
		response.Success.Should().BeFalse(
			because: "a null pages entry is invalid and must not reach mapping (NRE)");
		response.Error.Should().Contain("pages",
			because: "the error should explain that a null pages entry was provided");
		_commandResolver.DidNotReceiveWithAnyArgs().Resolve<CreateRelatedPageAddonCommand>(default!);
	}

	[Test]
	[Description("Returns the resolver's failure inside the structured response when command resolution throws.")]
	public void CreateRelatedPageAddon_ShouldReturnError_WhenCommandResolutionFails() {
		// Arrange — the resolver throws for this call instead of returning a command.
		_commandResolver.Resolve<CreateRelatedPageAddonCommand>(Arg.Any<CreateRelatedPageAddonOptions>())
			.Returns(_ => throw new InvalidOperationException("boom"));

		// Act
		CreateRelatedPageAddonResponse response = _tool.CreateRelatedPageAddon(Args());

		// Assert
		response.Success.Should().BeFalse(
			because: "a failed command resolution is reported as a failure, not surfaced as an exception");
		response.Error.Should().Contain("boom",
			because: "the resolver's exception message is carried into the structured response");
	}

	[Test]
	[Description("Story 19 (ENG-95262) AC-04: the write takes the RESOLVED tenant's execution lock, never McpToolExecutionLock.SharedFallbackKey — this sibling of get-related-page-addon carried the identical defect, and its Creatio round-trip is a write, so serializing every other environment behind it lasts longer still.")]
	public void CreateRelatedPageAddon_ShouldLockOnResolvedTenantKey_WhenEnvironmentResolves() {
		// Arrange
		List<string> lockKeys = [];
		List<string> markInUseKeys = [];
		List<string> markAvailableKeys = [];
		object sharedLock = new();
		ITenantExecutionLockProvider lockProvider = Substitute.For<ITenantExecutionLockProvider>();
		// GetLock is object-typed: an unstubbed substitute returns null and production `lock (...)` would die
		// at Monitor.ReliableEnter, so hand back one stable object for every key.
		lockProvider.GetLock(Arg.Do<string>(lockKeys.Add)).Returns(sharedLock);
		lockProvider.When(provider => provider.MarkAvailable(Arg.Any<string>()))
			.Do(call => markAvailableKeys.Add(call.Arg<string>()));
		ISessionContainerCache sessionCache = Substitute.For<ISessionContainerCache>();
		sessionCache.When(cache => cache.MarkInUse(Arg.Any<string>()))
			.Do(call => markInUseKeys.Add(call.Arg<string>()));
		McpToolExecutionLock.Configure(lockProvider, sessionCache);

		// Act
		_tool.CreateRelatedPageAddon(Args());

		// Assert
		lockKeys.Should().Equal([TenantKey],
			because: "the tool must key its execution lock on the tenant the command resolved for, so tenants do not serialize against each other");
		lockKeys.Should().NotContain(McpToolExecutionLock.SharedFallbackKey,
			because: "the shared fallback key is reserved for calls that carry no per-tenant identity; taking it here blocks every other environment");
		markInUseKeys.Should().Equal([TenantKey],
			because: "MarkInUse is skipped entirely for a fallback key, so seeing the tenant key proves a real tenant session was pinned");
		markAvailableKeys.Should().Equal(lockKeys,
			because: "GetLock pins the lock-provider mapping in-use at hand-out and only MarkAvailable releases it");
	}
}

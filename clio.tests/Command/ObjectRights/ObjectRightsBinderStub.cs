using System;
using Clio.Command.ObjectRights;
using NSubstitute;

namespace Clio.Tests.Command.ObjectRights;

/// <summary>
/// A package binder for the set-object-rights tests that are not about the binding: every object resolves to an
/// editable package that also holds its schema, and every binding write succeeds.
/// </summary>
internal static class ObjectRightsBinderStub {

	internal const string PackageName = "UsrApp";

	internal static ObjectRightsBindingTarget Target(string schemaName = "UsrFoo") =>
		new(PackageName, Guid.Parse("5a1c4f3e-0000-4000-8000-000000000001"),
			ObjectRightsPackageBinder.BindingNameOf(schemaName), [PackageName]);

	/// <summary>A write lease that is always granted, for the tests that are not about it.</summary>
	internal static Clio.Common.ObjectRights.IAdministratedObjectWriteLease Lease() {
		Clio.Common.ObjectRights.IAdministratedObjectWriteLease lease =
			Substitute.For<Clio.Common.ObjectRights.IAdministratedObjectWriteLease>();
		lease.Acquire(default).ReturnsForAnyArgs(_ => Substitute.For<IDisposable>());
		return lease;
	}

	internal static IObjectRightsPackageBinder Binding() {
		IObjectRightsPackageBinder binder = Substitute.For<IObjectRightsPackageBinder>();
		binder.ResolveTarget(default, default, default, default).ReturnsForAnyArgs(call => Target((string)call[0]));
		binder.Bind(default, default, default).ReturnsForAnyArgs(new ObjectRightsBindingResult(
			ObjectRightsBindingStatus.Written, 1));
		return binder;
	}
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ATF.Repository.Providers;
using Clio;
using Clio.Common;
using Clio.UserEnvironment;
using Creatio.Client;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Guards the credential-passthrough cross-identity contract (ENG-93208 B1): a child container
/// built from an ephemeral bearer <see cref="EnvironmentSettings"/> — exactly as
/// <c>ToolCommandResolver.ResolvePassthrough</c> does — must produce Creatio clients that carry the
/// caller's bearer token and NEVER silently fall back to the Login/Password "Supervisor" default.
/// A regression that drops the token (authenticating every tenant as Supervisor) must fail here.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Unit")]
[Property("Module", "McpServer")]
public class CredentialPassthroughClientIdentityTests {

	private const string BearerToken = "tenant-A-bearer-token-abc123";
	private const string EnvironmentUri = "https://tenant-a.creatio.example.com";
	private const string SupervisorLogin = "Supervisor";

	private static IServiceProvider BuildBearerPassthroughContainer() {
		// Mirrors ToolCommandResolver.ResolvePassthrough / BuildEphemeralSettings exactly: an
		// in-memory EnvironmentSettings carrying ONLY the url + access token, with Login/Password/
		// ClientId left null, fed to the same public child-container build.
		EnvironmentSettings settings = new() {
			Uri = EnvironmentUri,
			AccessToken = BearerToken
		};
		return new BindingsModule().Register(settings);
	}

	private static T GetPrivateField<T>(object instance, string fieldName) {
		FieldInfo field = instance.GetType().GetField(fieldName,
			BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		field.Should().NotBeNull(
			because: $"the reflection target field '{fieldName}' must exist on {instance.GetType().FullName}; " +
				"if the NuGet field name changed, adapt the test rather than skip the identity assertion");
		return (T)field.GetValue(instance);
	}

	// Bounded, visited-guarded reflective walk that returns every Creatio.Client.CreatioClient
	// instance reachable from the root object graph. Used for IDataProvider, whose bearer-vs-login
	// difference lives inside the ATF RemoteDataProvider's private client field; navigating by type
	// (not hardcoded ATF field names) keeps the assertion resilient to ATF internals.
	private static IReadOnlyList<CreatioClient> FindCreatioClients(object root, int maxDepth = 8) {
		List<CreatioClient> found = new();
		HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
		Queue<(object Node, int Depth)> queue = new();
		queue.Enqueue((root, 0));
		while (queue.Count > 0) {
			(object node, int depth) = queue.Dequeue();
			if (node is null || depth > maxDepth || !visited.Add(node)) {
				continue;
			}
			if (node is CreatioClient client) {
				found.Add(client);
				continue;
			}
			Type type = node.GetType();
			if (type.IsPrimitive || node is string || type.IsEnum) {
				continue;
			}
			if (node is IEnumerable enumerable and not string) {
				foreach (object item in enumerable) {
					if (item is not null) {
						queue.Enqueue((item, depth + 1));
					}
				}
			}
			for (Type t = type; t is not null && t != typeof(object); t = t.BaseType) {
				foreach (FieldInfo field in t.GetFields(
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)) {
					if (field.FieldType.IsPrimitive || field.FieldType.IsEnum) {
						continue;
					}
					object value = field.GetValue(node);
					if (value is not null) {
						queue.Enqueue((value, depth + 1));
					}
				}
			}
		}
		return found;
	}

	[Test]
	[Description("The CreatioClient built in the bearer child container must carry the caller's token (via the bearer ctor) and must NOT authenticate as Supervisor.")]
	public void ChildContainer_ShouldBuildTokenBoundCreatioClient_WhenSettingsCarryAccessToken() {
		// Arrange
		IServiceProvider container = BuildBearerPassthroughContainer();

		// Act
		CreatioClient client = container.GetRequiredService<CreatioClient>();
		string oauthToken = GetPrivateField<string>(client, "_oauthToken");
		string userName = GetPrivateField<string>(client, "_userName");

		// Assert
		oauthToken.Should().Be(BearerToken,
			because: "an ephemeral bearer EnvironmentSettings must build CreatioClient via the bearer ctor so the caller's token is attached (ENG-93208 B1)");
		userName.Should().NotBe(SupervisorLogin,
			because: "a bearer passthrough request must never silently authenticate as Supervisor — that is the cross-identity/privilege-escalation bug this feature exists to prevent");
	}

	[Test]
	[Description("The IApplicationClient resolved from the bearer child container must wrap NoReauthExecutor so the bearer client never attempts a login/password re-authentication.")]
	public void ChildContainer_ShouldWireNoReauthExecutor_WhenSettingsCarryAccessToken() {
		// Arrange
		IServiceProvider container = BuildBearerPassthroughContainer();

		// Act
		IApplicationClient applicationClient = container.GetRequiredService<IApplicationClient>();
		object reauthExecutor = GetPrivateField<object>(applicationClient, "_reauthExecutor");

		// Assert
		reauthExecutor.Should().BeOfType<NoReauthExecutor>(
			because: "bearer material cannot be re-logged-in, so the passthrough adapter must use NoReauthExecutor rather than the default closure-based ReauthExecutor (ENG-93208 B1)");
	}

	[Test]
	[Description("The IDataProvider built in the bearer child container must carry the caller's token (RemoteDataProvider bearer ctor), not drop it and connect anonymously/as Supervisor.")]
	public void ChildContainer_ShouldBuildTokenBoundDataProvider_WhenSettingsCarryAccessToken() {
		// Arrange
		IServiceProvider container = BuildBearerPassthroughContainer();

		// Act — resolve the IDataProvider and force the LazyDataProvider to build the real
		// RemoteDataProvider, then locate the underlying CreatioClient by type.
		IDataProvider dataProvider = container.GetRequiredService<IDataProvider>();
		object lazy = GetPrivateField<object>(dataProvider, "_lazy");
		object realProvider = lazy.GetType()
			.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)!
			.GetValue(lazy);
		IReadOnlyList<CreatioClient> clients = FindCreatioClients(realProvider);

		// Assert
		clients.Should().NotBeEmpty(
			because: "the bearer RemoteDataProvider must construct an underlying CreatioClient reachable for the identity assertion");
		clients.Should().Contain(
			c => GetPrivateField<string>(c, "_oauthToken") == BearerToken,
			because: "the data provider must be built via the RemoteDataProvider bearer ctor so the caller's token flows to the DB/ESQ path; dropping it authenticates the tenant anonymously/as Supervisor (ENG-93208 B1)");
	}
}

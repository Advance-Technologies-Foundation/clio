using System;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Live round-trip against a real Creatio environment exercising the actual create/update/delete
/// tools over the shared <see cref="IApplicationClient"/> transport. Explicit: run on demand only.
/// </summary>
[TestFixture]
[Explicit("Hits a live Creatio environment; run manually.")]
[Category("Integration")]
public sealed class ODataWriteToolsLiveIntegrationTests {
	private static IToolCommandResolver BuildResolver() {
		string? uri = Environment.GetEnvironmentVariable("CLIO_ODATA_IT_URL");
		if (string.IsNullOrWhiteSpace(uri)) {
			Assert.Ignore("Set CLIO_ODATA_IT_URL (and optionally CLIO_ODATA_IT_LOGIN/PASSWORD) to run the live OData round-trip.");
		}
		EnvironmentSettings settings = new() {
			Uri = uri,
			Login = Environment.GetEnvironmentVariable("CLIO_ODATA_IT_LOGIN") ?? "Supervisor",
			Password = Environment.GetEnvironmentVariable("CLIO_ODATA_IT_PASSWORD") ?? "Supervisor",
			IsNetCore = bool.TryParse(Environment.GetEnvironmentVariable("CLIO_ODATA_IT_NETCORE"), out bool netCore) && netCore
		};
		IServiceProvider container = new BindingsModule().Register(settings);
		return new ContainerResolver(container);
	}

	private static JsonElement Obj(string json) => JsonDocument.Parse(json).RootElement.Clone();

	[Test]
	public void Create_Read_Update_Delete_RoundTrip() {
		IToolCommandResolver resolver = BuildResolver();
		ODataCreateTool create = new(resolver);
		ODataReadTool read = new(resolver);
		ODataUpdateTool update = new(resolver);
		ODataDeleteTool delete = new(resolver);
		string name = $"clio-crud-it-{Guid.NewGuid():N}";
		string? id = null;

		try {
			// CREATE
			ODataCreateBatchResponse created = create.Create(new ODataCreateArgs {
				EnvironmentName = "live", Entity = "Contact", Rows = Obj($"[{{\"Name\":\"{name}\"}}]")
			});
			created.Error.Should().BeNull();
			ODataRowResult createdRow = created.Results[0];
			createdRow.Success.Should().BeTrue(because: createdRow.Error);
			createdRow.Id.Should().NotBeNullOrEmpty();
			id = createdRow.Id;

			// READ created
			ODataReadResponse afterCreate = read.Read(ReadById(id!));
			afterCreate.Success.Should().BeTrue(because: afterCreate.Error);
			afterCreate.Count.Should().Be(1);

			// UPDATE (PATCH via IApplicationClient.ExecutePatchRequest)
			string newName = name + "-upd";
			ODataWriteResponse updated = update.Update(new ODataUpdateArgs {
				EnvironmentName = "live", Entity = "Contact", Id = id!, Data = Obj($"{{\"Name\":\"{newName}\"}}"), Confirm = true
			});
			updated.Success.Should().BeTrue(because: updated.Error);

			// READ updated value
			ODataReadResponse afterUpdate = read.Read(ReadById(id!));
			afterUpdate.Count.Should().Be(1);
			afterUpdate.Value!.Value[0].GetProperty("Name").GetString().Should().Be(newName);

			// DELETE
			ODataWriteResponse deleted = delete.Delete(new ODataDeleteArgs {
				EnvironmentName = "live", Entity = "Contact", Id = id!, Confirm = true
			});
			deleted.Success.Should().BeTrue(because: deleted.Error);
			id = null;

			// READ confirms gone
			ODataReadResponse afterDelete = read.Read(ReadById(createdRow.Id!));
			afterDelete.Success.Should().BeTrue(because: afterDelete.Error);
			afterDelete.Count.Should().Be(0);
		} finally {
			if (id is not null) {
				delete.Delete(new ODataDeleteArgs { EnvironmentName = "live", Entity = "Contact", Id = id, Confirm = true });
			}
		}
	}

	private static ODataReadArgs ReadById(string id) => new() {
		EnvironmentName = "live",
		Entity = "Contact",
		Select = ["Id", "Name"],
		Filters = new ODataFilters {
			All = [new ODataFilterCondition { Field = "Id", Op = "eq", Value = Obj($"\"{id}\"") }]
		},
		Top = 1
	};

	private sealed class ContainerResolver(IServiceProvider serviceProvider) : IToolCommandResolver {
		public string LastResolvedTenantKey { get; private set; }
		public TCommand Resolve<TCommand>(EnvironmentOptions options) {
			LastResolvedTenantKey = GetTenantKey(options);
			return serviceProvider.GetRequiredService<TCommand>();
		}
		public TCommand ResolveWithoutEnvironment<TCommand>(EnvironmentOptions options) =>
			serviceProvider.GetRequiredService<TCommand>();
		public string GetTenantKey(EnvironmentOptions options) =>
			$"test:{options?.Environment ?? options?.Uri ?? "default"}";
	}
}

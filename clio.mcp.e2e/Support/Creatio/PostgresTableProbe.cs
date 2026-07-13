using Npgsql;

namespace Clio.Mcp.E2E.Support.Creatio;

internal static class PostgresTableProbe {
	internal static bool Exists(string connectionString, string tableName) {
		using NpgsqlConnection connection = new(connectionString);
		connection.Open();
		using NpgsqlCommand command = connection.CreateCommand();
		command.CommandText = """
			select exists (
				select 1
				from pg_catalog.pg_class relation
				join pg_catalog.pg_namespace namespace on namespace.oid = relation.relnamespace
				where relation.relname = @tableName
					and relation.relkind in ('r', 'p')
					and namespace.nspname not in ('pg_catalog', 'information_schema')
			)
			""";
		command.Parameters.AddWithValue("tableName", tableName);
		return (bool)command.ExecuteScalar()!;
	}
}

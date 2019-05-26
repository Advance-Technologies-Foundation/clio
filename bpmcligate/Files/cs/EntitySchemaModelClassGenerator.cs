using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrasoft.Core.Entities;

namespace bpmcligate.Files.cs
{
	public class EntitySchemaModelClassGenerator
	{

		public EntitySchemaManager entitySchemaManager;

		private List<string> relatedSchemas = new List<string>();

		string rootSchemaName;

		public EntitySchemaModelClassGenerator(EntitySchemaManager entitySchemaManager) {
			this.entitySchemaManager = entitySchemaManager;
		}

		private void FindAllRelatedSchemas(string schemaName) {
			if (!relatedSchemas.Contains(schemaName)) {
				relatedSchemas.Add(schemaName);
				var schema = entitySchemaManager.GetInstanceByName(schemaName);
				foreach (var column in schema.Columns) {
					if (column.CreatedInSchemaUId != schema.UId && column != schema.PrimaryDisplayColumn) {
						continue;
					}
					if (column.IsLookupType) {
						FindAllRelatedSchemas(column.ReferenceSchema.Name);
					}
				}
			}
		}

		public Dictionary<string, string> Generate(string entitySchemaName) {
			FindAllRelatedSchemas(entitySchemaName);
			var result = new Dictionary<string, string>();
			foreach (var item in relatedSchemas) {
				result.Add(item, GetSchemaClass(item));
			}
			return result;
		}

		private string GetSchemaClass(string entitySchemaName) {
			var schema = entitySchemaManager.GetInstanceByName(entitySchemaName);
			string classTemplate = File.ReadAllText(@"C:\Temp\class-template.cs");
			string columnTemplate = File.ReadAllText(@"C:\Temp\column-template.cs");
			var columnsBuilder = new StringBuilder();
			foreach (var column in schema.Columns) {
				if (column.CreatedInSchemaUId != schema.UId && column != schema.PrimaryDisplayColumn) {
					continue;
				}
				columnsBuilder.AppendFormat(columnTemplate, column.Name, column.DataValueType.IsLookup ? column.ReferenceSchema.Name : column.DataValueType.ValueType.Name);
				columnsBuilder.AppendLine();
			}
			return string.Format(classTemplate, entitySchemaName, columnsBuilder.ToString());
		}
	}


}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrasoft.Core.Entities;

namespace bpmcligate.Files.cs
{
	public  class EntitySchemaModelClassGenerator
	{

		public EntitySchemaManager entitySchemaManager;

		private List<string> relatedSchemas;

		string rootSchemaName;

		public EntitySchemaModelClassGenerator(EntitySchemaManager entitySchemaManager) {
			this.entitySchemaManager = entitySchemaManager;
		}

		private void FindAllRelatedSchemas(string schemaName) {
			if (!relatedSchemas.Contains(schemaName)) {
				var schema = entitySchemaManager.GetInstanceByName(schemaName);
				foreach (var item in schema.Columns) {
					if (item.IsLookupType) {
						FindAllRelatedSchemas(item.ReferenceSchema.Name);
					}
				}
			}
		}

		public Dictionary<string, string> Generate(string entitySchemaName) {
			var result = new Dictionary<string, string>();
			foreach (var item in relatedSchemas) {
				result.Add(item, GetSchemaClass(item));
			}
			return result;
		}

		private string GetSchemaClass(string entitySchemaName) {
			var schema = entitySchemaManager.GetInstanceByName(entitySchemaName);
			string classTemplate = "";
			string columnTemplate = "";
			var columnsBuilder = new StringBuilder();
			foreach (var column in schema.Columns) {
				columnsBuilder.AppendFormat(columnTemplate, column.Name);
			}
			return string.Format(classTemplate, entitySchemaName, columnsBuilder.ToString()); 
		}
	}


}

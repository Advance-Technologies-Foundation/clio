using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Terrasoft.Core.Entities;

namespace cliogate.Files.cs
{
	public class EntitySchemaModelClassGenerator
	{

		public EntitySchemaManager entitySchemaManager;

		private List<string> relatedSchemas = new List<string>();

		private string tplFolder { get => AppDomain.CurrentDomain.BaseDirectory + @"\Terrasoft.Configuration\Pkg\cliogate\Files\tpl"; }

		private string _lookupColumnTemplate;
		private string lookupColumnTemplate { get => _lookupColumnTemplate ?? (_lookupColumnTemplate = File.ReadAllText($"{tplFolder}\\lookup-template.tpl")); }

		private string _valueColumnTemplate;
		private string valueColumnTemplate { get => _valueColumnTemplate ?? (_valueColumnTemplate = File.ReadAllText($"{tplFolder}\\column-template.tpl")); }


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
			string classTemplate = File.ReadAllText($"{tplFolder}\\class-template.tpl");
			
			var columnsBuilder = new StringBuilder();
			foreach (var column in schema.Columns) {
				if (column.CreatedInSchemaUId != schema.UId && column != schema.PrimaryDisplayColumn) {
					continue;
				}
				columnsBuilder.Append(GetColumnPart(column));
				columnsBuilder.AppendLine();
			}
			return string.Format(classTemplate, entitySchemaName, columnsBuilder.ToString());
		}

		private string GetColumnPart(EntitySchemaColumn column) {
			return column.IsLookupType
				? GetLookupColumnPart(column)
				: GetValueColumnPart(column);
		}

		private string GetLookupColumnPart(EntitySchemaColumn column) {
			return string.Format(lookupColumnTemplate, column.Name, column.DataValueType.ValueType.Name, column.ReferenceSchema.Name);
		}

		private string GetValueColumnPart(EntitySchemaColumn column) {
			return string.Format(valueColumnTemplate, column.Name, column.DataValueType.ValueType.Name);
		}
	}

}

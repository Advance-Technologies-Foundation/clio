using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Terrasoft.Core.Entities;

namespace cliogate.Files.cs;

public class EntitySchemaModelClassGenerator(EntitySchemaManager entitySchemaManager)
{
    public EntitySchemaManager entitySchemaManager = entitySchemaManager;

    private readonly List<string> relatedSchemas = [];

    private readonly string tplFolder = Path.Combine(CreatioPathBuilder.GetPackageFilePath("cliogate"), "tpl");

    private string _lookupColumnTemplate;

    private string lookupColumnTemplate => _lookupColumnTemplate ??=
                                               File.ReadAllText($"{tplFolder}\\lookup-template.tpl");

    private string _valueColumnTemplate;

    private string valueColumnTemplate => _valueColumnTemplate ??=
                                              File.ReadAllText($"{tplFolder}\\column-template.tpl");

    private void FindAllRelatedSchemas(string schemaName, List<string> columns = null)
    {
        if (!relatedSchemas.Contains(schemaName))
        {
            relatedSchemas.Add(schemaName);
            EntitySchema? schema = entitySchemaManager.GetInstanceByName(schemaName);
            foreach (EntitySchemaColumn? column in schema.Columns)
            {
                if (column.CreatedInSchemaUId != schema.UId && column != schema.PrimaryDisplayColumn)
                {
                    continue;
                }

                if (columns.Count > 0 && !columns.Contains(column.Name))
                {
                    continue;
                }

                if (column.IsLookupType)
                {
                    FindAllRelatedSchemas(column.ReferenceSchema.Name);
                }
            }
        }
    }

    public Dictionary<string, string> Generate(string entitySchemaName, List<string> columns)
    {
        FindAllRelatedSchemas(entitySchemaName, columns);
        Dictionary<string, string> result = [];
        foreach (string? schemaName in relatedSchemas)
        {
            List<string> schemaColumns = [];
            if (schemaName == entitySchemaName)
            {
                schemaColumns = columns;
            }

            result.Add(schemaName, GetSchemaClass(schemaName, schemaColumns));
        }

        return result;
    }

    private string GetSchemaClass(string entitySchemaName, List<string> columns)
    {
        EntitySchema? schema = entitySchemaManager.GetInstanceByName(entitySchemaName);
        string classTemplate = File.ReadAllText($"{tplFolder}\\class-template.tpl");

        StringBuilder columnsBuilder = new();
        foreach (EntitySchemaColumn? column in schema.Columns)
        {
            if (column.CreatedInSchemaUId != schema.UId && column != schema.PrimaryDisplayColumn)
            {
                continue;
            }

            if (columns.Count > 0 && !columns.Contains(column.Name))
            {
                continue;
            }

            columnsBuilder.Append(GetColumnPart(column));
            columnsBuilder.AppendLine();
        }

        return string.Format(classTemplate, entitySchemaName, columnsBuilder.ToString());
    }

    private string GetColumnPart(EntitySchemaColumn column) =>
        column.IsLookupType
            ? GetLookupColumnPart(column)
            : GetValueColumnPart(column);

    private string GetLookupColumnPart(EntitySchemaColumn column) => string.Format(lookupColumnTemplate, column.Name,
        column.DataValueType.ValueType.Name, column.ReferenceSchema.Name);

    private string GetValueColumnPart(EntitySchemaColumn column) =>
        string.Format(valueColumnTemplate, column.Name, column.DataValueType.ValueType.Name);
}

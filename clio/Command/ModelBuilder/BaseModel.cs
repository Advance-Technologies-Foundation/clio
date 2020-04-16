using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Clio.Command.ModelBuilder
{
    public class BaseModel
    {
        public BaseModel()
        {
            Class = new ModelBuilderClass();
            ModelBuilderUsings = new List<string>
            {
                "System",
                "Creatio.DataService.Attributes"
            };
        }

        public List<string> ModelBuilderUsings { get; private set; }
        public string ModelBuilderNamespace { get; set; }
        public ModelBuilderClass Class { get; set; }


        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Clear();

            foreach (string classUsing in ModelBuilderUsings)
            {
                sb.AppendLine($"using {classUsing};");
            }

            sb.AppendLine($"namespace { ModelBuilderNamespace}.Models");
            sb.AppendLine(@"{");
            sb.AppendLine($"\t{Class.ClassAttribute}");
            sb.AppendLine($"\tpublic class {Class.Name} : BaseEntity");
            sb.AppendLine("\t{");

            //Props Go Here
            var valueList = from v in Class.ModelBuilderClassProperties
                            where v.PropAttribute.StartsWith("[CProperty(ColumnPath", System.StringComparison.InvariantCulture)
                            orderby v.Name ascending
                            select v;

            if (valueList.Any())
            {
                sb.AppendLine("\t\t#region Values");
                foreach (ModelBuilderClassProperty prop in valueList)
                {
                    sb.AppendLine($"\t\t{prop.PropAttribute}");
                    sb.AppendLine($"\t\tpublic {prop.Type} {prop.Name} {{ get; set; }}");
                }
                sb.AppendLine("\t\t#endregion");
                sb.AppendLine();
            }


            var navigationList = from n in Class.ModelBuilderClassProperties
                                 where n.PropAttribute.StartsWith("[CProperty(Navigation", System.StringComparison.InvariantCulture)
                                 orderby n.Type ascending
                                 select n;

            sb.AppendLine("\t\t#region Navigation");
            foreach (ModelBuilderClassProperty prop in navigationList)
            {
                sb.AppendLine($"\t\t{prop.PropAttribute}");
                sb.AppendLine($"\t\tpublic {prop.Type} {prop.Name} {{ get; set; }}");
            }
            sb.AppendLine("\t\t#endregion");
            sb.AppendLine();

            var associationList = from a in Class.ModelBuilderClassProperties
                                  where a.PropAttribute.StartsWith("[CProperty(Association", System.StringComparison.InvariantCulture)
                                  orderby a.Type ascending
                                  select a;

            sb.AppendLine("\t\t#region Associations");
            foreach (ModelBuilderClassProperty prop in associationList)
            {
                sb.AppendLine($"\t\t{prop.PropAttribute}");

                if (prop.Type.StartsWith("ICollection", System.StringComparison.InvariantCulture)
                    || prop.Type.StartsWith("List", System.StringComparison.InvariantCulture)
                    )
                {
                    sb.AppendLine($"\t\tpublic virtual {prop.Type} {prop.Name} {{ get; set; }}");
                }
            }
            sb.AppendLine("\t\t#endregion");


            //Close Class
            sb.AppendLine("\t}");

            //Close NameSpace
            sb.AppendLine("}");

            string result = sb.ToString();
            return result;
        }
    }

    public class ModelBuilderClass
    {
        public ModelBuilderClass()
        {
            ModelBuilderClassProperties = new List<ModelBuilderClassProperty>();
        }
        public string Name { get; set; }
        public string ClassAttribute { get; set; }
        public List<ModelBuilderClassProperty> ModelBuilderClassProperties { get; private set; }
    }

    public class ModelBuilderClassProperty
    {
        public string PropAttribute { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }

    }
}

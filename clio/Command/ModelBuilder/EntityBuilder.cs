using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using static Clio.Command.ModelBuilder.ConsoleWriter;

namespace Clio.Command.ModelBuilder
{
    public class EntityBuilder
    {
        public BaseModel Build(string nameSpace, XElement entity,
            IEnumerable<XElement> keys, List<XElement> associations)
        {
            if (entity == null)
                throw new Exception("Empty entity XElement");

            string className = entity.Attribute("Name").Value;
            BaseModel bm = Factory.Create<BaseModel>();

            bm.ModelBuilderUsings.Add("System.Collections.Generic");
            bm.ModelBuilderNamespace = "Creatio.DataService";

            bm.Class.ClassAttribute = $"[CObject(RootSchemaName = \"{className}\")]";
            bm.Class.Name = className;

            var properties = from prop in entity.Descendants()
                             where prop.Name.LocalName == "Property"
                             select prop;

            foreach (XElement member in properties)
            {
                string name = "";
                string type = "";
                bool nullable = true;

                var attrs = member.Attributes();
                foreach (XAttribute attr in member.Attributes())
                {
                    switch (attr.Name.ToString())
                    {
                        case "Name":
                            name = attr.Value;
                            break;
                        case "Type":
                            type = attr.Value.Remove(0, 4);
                            break;
                        case "Nullable":
                            nullable = (attr.Value == "false") ? false : true;
                            break;
                    }
                }

                if (type == "String") type = "string";
                if (type == "Int32") type = "int";
                if (type == "Boolean") type = "bool";
                if (type == "Decimal") type = "decimal";
                if (type == "Stream") type = "byte[]";

                ModelBuilderClassProperty property = Factory.Create<ModelBuilderClassProperty>();
                if (keys.FirstOrDefault().Attribute("Name").Value == name)
                {

                    property.PropAttribute = $"[CProperty(ColumnPath=\"{name}\", IsKey=true)]";
                }
                else
                {
                    property.PropAttribute = $"[CProperty(ColumnPath =\"{name}\")]";
                }
                property.Type = type;
                property.Name = name;
                bm.Class.ModelBuilderClassProperties.Add(property);
            }

            var navigationProperties = from navProp in entity.Descendants()
                                       where navProp.Name.LocalName == "NavigationProperty"
                                       && navProp.Attribute("FromRole").Value.StartsWith(className, StringComparison.InvariantCulture)
                                       select navProp;

            foreach (var item in navigationProperties)
            {
                ModelBuilderClassProperty property = Factory.Create<ModelBuilderClassProperty>();

                //relationship = Terrasoft.Configuration.AcademyURL_CreatedBy
                string relationship = item.Attribute("Relationship").Value;
                string name = item.Attribute("Name").Value;
                string toRole = item.Attribute("ToRole").Value;
                string fromRole = item.Attribute("FromRole").Value;

                property.PropAttribute = "";
                //relationship = AcademyURL_CreatedBy
                if (relationship.StartsWith($"{nameSpace}.", StringComparison.InvariantCulture))
                {
                    relationship = relationship.Remove(0, nameSpace.Count() + 1);
                }

                //Look for association by relationship
                var associationProperties = from i in associations
                                            where i.Attribute("Name").Value == relationship
                                            select i.Descendants();


                //<End Type="Terrasoft.Configuration.Contact" Role="CreatedBy" Multiplicity="0..1" />
                //<End Type = "Terrasoft.Configuration.AcademyURL" Role = "AcademyURL" Multiplicity = "*" />
                foreach (var iitem in associationProperties)
                {
                    var ii = from iii in iitem
                             where iii.Attribute("Role").Value == toRole
                             select iii;

                    //Type = "Terrasoft.Configuration.Contact"
                    string parentType = ii.FirstOrDefault().Attribute("Type").Value;
                    if (parentType.StartsWith($"{nameSpace}.", StringComparison.InvariantCulture))
                    {
                        parentType = parentType.Remove(0, nameSpace.Count() + 1);
                    }

                    if (ii.FirstOrDefault().Attribute("Multiplicity").Value == "*")
                    {
                        property.Type = $"ICollection<{parentType}>";
                        string roleName = ii.FirstOrDefault().Attribute("Role").Value;

                        string[] parts = roleName.Split("_");
                        if (parts.Length == 2)
                        {
                            string attr = $"[CProperty(Association =\"{parentType}:{parts[1]}Id\")]";
                            property.PropAttribute = attr;
                        }
                        else
                        {
                            string attr = $"[CProperty(Association =\"ERROR\")]";
                            property.PropAttribute = attr;
                            ConsoleWriter.WriteMessage(MessageType.Error, $"Class: {className} Property:{property.Name} Attibute:{attr}");
                        }

                        roleName = roleName.Replace("_", "By", StringComparison.InvariantCulture);
                        property.Name = roleName;
                    }
                    else
                    {
                        //Multiplicity = 0..1
                        string roleName = ii.FirstOrDefault().Attribute("Role").Value;

                        if (roleName == "CreatedBy" || roleName == "ModifiedBy")
                        {
                            string attr = $"[CProperty(Navigation=\"{parentType}:{roleName}Id\")]";
                            property.PropAttribute = attr;
                        }
                        else
                        {
                            //<End Type="Terrasoft.Configuration.Contact" Role="Contact_ContactCollectionByOwner" Multiplicity="0..1" />
                            string[] parts = roleName.Split("_");
                            if (parts.Length == 2)
                            {
                                string toRemove = $"{className}CollectionBy";
                                string connectionColumn = parts[1].Remove(0, toRemove.Length);

                                //string attr = $"[CProperty(Navigation =\"{parentType}:{parts[1]}Id\")]";
                                string attr = $"[CProperty(Navigation =\"{parentType}:{connectionColumn}Id\")]";
                                property.PropAttribute = attr;
                            }
                            else
                            {
                                string attr = $"[CProperty(Navigation =\"ERROR\")]";
                                property.PropAttribute = attr;
                            }
                        }

                        property.Type = parentType;
                        property.Name = name;
                    }
                }
                bm.Class.ModelBuilderClassProperties.Add(property);
            }
            return bm;
        }
    }
}

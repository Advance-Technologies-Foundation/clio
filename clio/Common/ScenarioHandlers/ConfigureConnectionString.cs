using MediatR;
using OneOf;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Clio.Common.ScenarioHandlers {


    public class ConfigureConnectionStringRequest : BaseHandlerRequest {
    }

    public class ConfigureConnectionStringResponse : BaseHandlerResponse {
    }
    internal class ConfigureConnectionStringRequestHandler : IRequestHandler<ConfigureConnectionStringRequest, OneOf<BaseHandlerResponse, HandlerError>> {

        
        public async Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(ConfigureConnectionStringRequest request, CancellationToken cancellationToken) {

            string folder = request.Arguments["folderPath"];
            string dbString = request.Arguments["dbString"]; 
            string redisString = request.Arguments["redis"];
            
            string cnPath = Path.Join(folder, "ConnectionStrings.config");
            string result = ConfigureConnectionStrings(cnPath, dbString, redisString);
            
            bool isNetFrameWork = bool.Parse(request.Arguments["isNetFramework"]);
            if(!isNetFrameWork) {
                string webConfigPath = Path.Join(folder, "Terrasoft.WebHost.dll.config");
                result = result+"\n"+UpdateWebConfig(webConfigPath);
            }
            
            return new ConfigureConnectionStringResponse {
                Status = BaseHandlerResponse.CompletionStatus.Success,
                Description = result
            };
        }

        
        private static string UpdateWebConfig(string webConfigPath) {
            string configContent = File.ReadAllText(webConfigPath);
            XmlDocument doc = new();
            doc.LoadXml(configContent);
            XmlNode root = doc.DocumentElement;
            
            XmlNode cookiesSameSiteModeNode = root?.SelectSingleNode("descendant::add[@key='CookiesSameSiteMode']");
            if(cookiesSameSiteModeNode != null) {
                cookiesSameSiteModeNode.Attributes["value"].Value = "Lax";
            }
            doc.Save(webConfigPath);
            return "Set CookiesSameSiteMode to Lax";
        }
        
        private static string ConfigureConnectionStrings(string cnFilePath, string db, string redis) {
            string cnFileContent = File.ReadAllText(cnFilePath);
            XmlDocument doc = new();
            doc.LoadXml(cnFileContent);
            XmlNode root = doc.DocumentElement;

            XmlNode dbPostgreSqlNode = root?.SelectSingleNode("descendant::add[@name='dbPostgreSql']");
            if(dbPostgreSqlNode != null) {
                dbPostgreSqlNode.Attributes["connectionString"].Value = db;
            }

            XmlNode dbNode = root?.SelectSingleNode("descendant::add[@name='db']");
            if(dbNode != null ) {
                dbNode.Attributes["connectionString"].Value = db;
            }
            
            
            XmlNode redisNode = root?.SelectSingleNode("descendant::add[@name='redis']");
            if(redisNode != null) {
                redisNode.Attributes["connectionString"].Value = redis;
            }

            doc.Save(cnFilePath);

            StringBuilder sb = new();
            sb.AppendLine("Set db to:")
                .Append("\t").AppendLine(db)
                .AppendLine("Set redis to:")
                .Append("\t").Append(redis)
                .AppendLine();

            return sb.ToString();
        }
    }
}

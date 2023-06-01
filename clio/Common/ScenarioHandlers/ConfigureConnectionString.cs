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

            string cnPath = @"C:\inetpub\wwwroot\demosite\ConnectionStrings.config";
            var dbString = BuildConnectionStringFromTemplate(request, "dbCnTemplate");
            var redisString = BuildConnectionStringFromTemplate(request, "redisCnTemplate");
            
            string result = ConfigureConnectionStrings(cnPath, dbString, redisString);
            return new ConfigureConnectionStringResponse() {
                Status = BaseHandlerResponse.CompletionStatus.Success,
                Description = result
            };
        }

        private static string ConfigureConnectionStrings(string cnFilePath, string db, string redis) {
            string cnFileContent = File.ReadAllText(cnFilePath);
            XmlDocument doc = new();
            doc.LoadXml(cnFileContent);
            XmlNode root = doc.DocumentElement;

            XmlNode dbNode = root.SelectSingleNode("descendant::add[@name='db']");
            dbNode.Attributes["connectionString"].Value = db;

            XmlNode redisNode = root.SelectSingleNode("descendant::add[@name='redis']");
            redisNode.Attributes["connectionString"].Value = redis;

            doc.Save(cnFilePath);

            StringBuilder sb = new();
            sb.AppendLine("Set db to:")
                .Append("\t").AppendLine(db)
                .AppendLine("Set redis to:")
                .Append("\t").Append(redis)
                .AppendLine();

            return sb.ToString();
        }

        private static string BuildConnectionStringFromTemplate(ConfigureConnectionStringRequest request, string templateName) {

            string template = request.Arguments[templateName];
            string pattern = @"\{\{.*?\}\}";
            return Regex.Replace(template, pattern, (Match m) => {
                string key = m.Value.Trim('{', '}');
                if (request.Arguments.ContainsKey(key)) {
                    return request.Arguments[key];
                }
                return m.Value;
            });
        }

    }
}

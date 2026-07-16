using OneOf;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace Clio.Common.ScenarioHandlers {


    public class ConfigureConnectionStringRequest : BaseHandlerRequest {
    }

    public class ConfigureConnectionStringResponse : BaseHandlerResponse {
    }

    /// <summary>
    /// Handles <see cref="ConfigureConnectionStringRequest"/> scenario steps by writing the
    /// database and Redis connection strings into the deployed Creatio configuration files.
    /// </summary>
    public interface IConfigureConnectionStringHandler {

        /// <summary>
        /// Writes the database and Redis connection strings described by the request
        /// <c>Arguments</c> (<c>folderPath</c>, <c>dbString</c>, <c>redis</c>, <c>isNetFramework</c>)
        /// into <c>ConnectionStrings.config</c> and, for .NET environments, updates
        /// <c>Terrasoft.WebHost.dll.config</c>.
        /// </summary>
        /// <param name="request">The request carrying the connection string configuration arguments.</param>
        /// <returns>
        /// A <see cref="OneOf{T0, T1}"/> containing a <see cref="BaseHandlerResponse"/>
        /// (a <see cref="ConfigureConnectionStringResponse"/>) on success or a <see cref="HandlerError"/> on failure.
        /// </returns>
        Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(ConfigureConnectionStringRequest request);
    }

    internal class ConfigureConnectionStringRequestHandler : IConfigureConnectionStringHandler {

        /// <inheritdoc />
        public async Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(ConfigureConnectionStringRequest request) {

            string folder = request.GetRequired("folderPath");
            string dbString = request.GetRequired("dbString");
            string redisString = request.GetRequired("redis");
            
            string cnPath = Path.Combine(folder, "ConnectionStrings.config");
            
            // Check if ConnectionStrings.config exists
            if (!File.Exists(cnPath)) {
                return new HandlerError {
                    ErrorDescription = $"ConnectionStrings.config not found at {cnPath}. Archive may not have been fully extracted."
                };
            }
            
            string result = ConfigureConnectionStrings(cnPath, dbString, redisString);
            
            bool isNetFrameWork = request.GetRequired<bool>("isNetFramework");
            if(!isNetFrameWork) {
                string webConfigPath = Path.Combine(folder, "Terrasoft.WebHost.dll.config");
                if (File.Exists(webConfigPath)) {
                    result = result+"\n"+UpdateWebConfig(webConfigPath);
                } else {
                    result = result+"\n"+$"Warning: Terrasoft.WebHost.dll.config not found at {webConfigPath}";
                }
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
            try {
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
                sb.AppendLine("Successfully configured connection strings")
                    .AppendLine("Database and Redis connection values were written without logging credentials.");

                return sb.ToString();
            } catch (Exception ex) {
                return $"Error configuring connection strings: {ex.Message}";
            }
        }
    }
}

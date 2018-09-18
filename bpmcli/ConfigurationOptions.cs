using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace bpmcli
{
    public class EnvironmentSettings
    {
        public string Uri { get; set; }
        public string Login { get; set; }
        public string Password { get; set; }
    }

    public class Settings
    {
        public Dictionary<string, EnvironmentSettings> Environments { get; set; }
    }

    public class SettingsRepository
    {
        public EnvironmentSettings GetEnvironment(string name = null)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables();
            IConfigurationRoot configuration = builder.Build();
            var settings = new Settings();
            configuration.Bind(settings);
            var environment = !String.IsNullOrEmpty(name) ? settings.Environments[name] : settings.Environments.First().Value; 
            if (settings.Environments.Count == 0) {
                throw new Exception("Could not find enviroment settings in file ");
            }
            return environment;
        }
    }
}

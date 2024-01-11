{
	"$schema": "http://json-schema.org/schema",
	"properties": {
		"iis-clio-root-path":{
			"type":"string",
			"description": "Default IIS folder where Creatio instances will run from"
		},
		"ActiveEnvironmentKey":{
			"type":"string",
			"description": "Default environment key"
		},
		"Autoupdate":{
			"type" : "boolean",
			"description": "Auto update clio",
			"default": false
		},
		"dbConnectionStringKeys":{
			"type":"object",
			"patternProperties": {
				".*" : {"$ref": "#/definitions/dbconnectionstring"}
			},
			"description": "List of configured environments that clio can interact with"
		},
		"Environments":{
			"type":"object",
			"patternProperties": {
				".*" : {"$ref": "#/definitions/environment"}
			},
			"description": "List of configured environments that clio can interact with"
		}
		

	},
	"description": "Clio environment description file",
	"required": ["ActiveEnvironmentKey", "Autoupdate", "Environments"],
	
	"definitions": {
		"environment":{
			"type":"object",
			"properties": {
				"Uri":{
					"type": "string",
					"default": "http://localhost:8080",
					"description": "Creatio url, this url will be used to call all REST services"
				},
				"Login":{
					"type": "string",
					"default": "Supervisor",
					"description": "Username to use for the environment"
				},
				"Password":{
					"type": "string",
					"default": "Supervisor",
					"description": "Password to use for the environment"
				},
				"Maintainer":{
					"type": "string",
					"default": "Customer",
					"description": "Sets maintainer (SysSetting:Publisher) for the environment"
				},
				"IsNetCore":{
					"type": "boolean",
					"default": false,
					"description": "True when Creatio is a NetCore version otherwise false, affects baseUrl for all clio calls"
				},
				"Safe":{
					"type": "boolean",
					"default": false,
					"description": "When true, an additional prompt is displayed before invoking a command"
				},
				"DeveloperModeEnabled":{
					"type": "boolean",
					"default": false,
					"description": "When enabled, unlocks package upon installation"
				},
				"ClientId":{
					"type": "string",
					"description": "Client id when used with IdentityService"
				},
				"ClientSecret":{
					"type": "string",
					"description": "Client secret when used with IdentityService"
				},
				"AuthAppUri":{
					"type": "string",
					"description": "IdentityService Url"
				}
			},
			"description": "Clio environment",
			"required": ["Uri", "Login","Password"],
			"default":{
				"Uri": "https://localhost:8080",
				"Login": "Supervisor",
				"Password": "Supervisor",
				"Maintainer": "Customer",
				"IsNetCore": false,
				"Safe": false,
				"DeveloperModeEnabled": true
			}
		},
		"dbconnectionstring":{
			"type":"object",
			"properties": {
				"uri":{
					"type": "string",
					"default": "",
					"description": "connection string mssql://login:password@hostname:port"
				},
				"workingFolder":{
					"type": "string",
					"default": "C:\\",
					"description": "Folder path visible to host"
				}
			},
			"description": "Db server properties",
			"required": ["uri", "workingFolder"],
			"default":{
				"uri": "mssql://SA:SAPA55word@localhost:1433",
				"workingFolder": "C:\\"
			}
		}
	}
}
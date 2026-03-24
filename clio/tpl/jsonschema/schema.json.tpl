{
	"$schema": "http://json-schema.org/schema",
	"properties": {
		"iis-clio-root-path": {
			"type": "string",
			"description": "Default IIS folder where Creatio instances will run from"
		},
		"workspaces-root": {
			"type": "string",
			"description": "Default absolute base directory for create-workspace --empty when --directory is omitted"
		},
		"container-image-cli": {
			"type": "string",
			"description": "Default container image CLI used by build-docker-image",
			"enum": ["docker", "nerdctl"],
			"default": "docker"
		},
		"RemoteArtefactServerPath": {
			"type": "string",
			"description": "Path to remote artefact server"
		},
		"ActiveEnvironmentKey": {
			"type": "string",
			"description": "Default environment key"
		},
		"Autoupdate": {
			"type": "boolean",
			"description": "Auto update clio",
			"default": false
		},
		"dbConnectionStringKeys": {
			"type": "object",
			"patternProperties": {
				".*": {
					"$ref": "#/definitions/dbconnectionstring"
				}
			},
			"description": "List of configured environments that clio can interact with"
		},
		"db": {
			"type": "object",
			"patternProperties": {
				".*": {
					"$ref": "#/definitions/localdbserverconfiguration"
				}
			},
			"description": "List of configured local database servers used by local restore/deploy/assert commands"
		},
		"redis": {
			"type": "object",
			"patternProperties": {
				".*": {
					"$ref": "#/definitions/localredisserverconfiguration"
				}
			},
			"description": "List of configured local Redis servers used by local deploy/assert commands"
		},
		"defaultRedis": {
			"type": "string",
			"description": "Default local Redis server key from redis section when multiple servers are enabled"
		},
		"Environments": {
			"type": "object",
			"patternProperties": {
				".*": {
					"$ref": "#/definitions/environment"
				}
			},
			"description": "List of configured environments that clio can interact with"
		}
	},
	"description": "Clio environment description file",
	"required": [
		"ActiveEnvironmentKey",
		"Autoupdate",
		"Environments"
	],
	"definitions": {
		"environment": {
			"type": "object",
			"properties": {
				"Uri": {
					"type": "string",
					"default": "http://localhost:8080",
					"description": "Creatio url, this url will be used to call all REST services"
				},
				"Login": {
					"type": "string",
					"default": "Supervisor",
					"description": "Username to use for the environment"
				},
				"Password": {
					"type": "string",
					"default": "Supervisor",
					"description": "Password to use for the environment"
				},
				"Maintainer": {
					"type": "string",
					"default": "Customer",
					"description": "Sets maintainer (SysSetting:Publisher) for the environment"
				},
				"IsNetCore": {
					"type": "boolean",
					"default": false,
					"description": "True when Creatio is a NetCore version otherwise false, affects baseUrl for all clio calls"
				},
				"Safe": {
					"type": "boolean",
					"default": false,
					"description": "When true, an additional prompt is displayed before invoking a command"
				},
				"DeveloperModeEnabled": {
					"type": "boolean",
					"default": false,
					"description": "When enabled, unlocks package upon installation"
				},
				"ClientId": {
					"type": "string",
					"description": "Client id when used with IdentityService"
				},
				"ClientSecret": {
					"type": "string",
					"description": "Client secret when used with IdentityService"
				},
				"AuthAppUri": {
					"type": "string",
					"description": "IdentityService Url"
				},
				"EnvironmentPath": {
					"type": "string",
					"description": "Path to the environment on disk"
				}
			},
			"description": "Clio environment",
			"required": [
				"Uri",
				"Login",
				"Password"
			],
			"default": {
				"Uri": "https://localhost:8080",
				"Login": "Supervisor",
				"Password": "Supervisor",
				"Maintainer": "Customer",
				"IsNetCore": false,
				"Safe": false,
				"DeveloperModeEnabled": true,
				"EnvironmentPath": "C:\\inetpub\\wwwroot\\clio\\<envkey>"
			}
		},
		"dbconnectionstring": {
			"type": "object",
			"properties": {
				"uri": {
					"type": "string",
					"default": "",
					"description": "connection string mssql://login:password@hostname:port"
				},
				"workingFolder": {
					"type": "string",
					"default": "C:\\",
					"description": "Folder path visible to host"
				}
			},
			"description": "Db server properties",
			"required": [
				"uri",
				"workingFolder"
			],
			"default": {
				"uri": "mssql://SA:SAPA55word@localhost:1433",
				"workingFolder": "C:\\"
			}
		},
		"localdbserverconfiguration": {
			"type": "object",
			"properties": {
				"DbType": {
					"type": "string",
					"description": "Database type. Supported values: mssql, postgres, postgresql"
				},
				"Hostname": {
					"type": "string",
					"description": "Database server host name or IP. For PostgreSQL running in Docker, use the host-reachable name such as localhost or host.docker.internal with the published port. For MSSQL named instances use host\\\\instance"
				},
				"Port": {
					"type": "integer",
					"description": "Database server port. For PostgreSQL running in Docker, use the published host port (for example 5433). For MSSQL named instances use 0"
				},
				"Username": {
					"type": "string",
					"description": "Database user name"
				},
				"Password": {
					"type": "string",
					"description": "Database password"
				},
				"UseWindowsAuth": {
					"type": "boolean",
					"default": false,
					"description": "Use integrated Windows authentication for MSSQL"
				},
				"Description": {
					"type": "string",
					"description": "Optional configuration description"
				},
				"PgToolsPath": {
					"type": "string",
					"description": "Optional path to PostgreSQL tools directory containing pg_restore. pg_restore must be installed on the machine running clio, even when PostgreSQL runs in Docker"
				},
				"Enabled": {
					"type": "boolean",
					"default": true,
					"description": "When false, this local DB server configuration is ignored by clio commands"
				}
			},
			"description": "Local database server settings used by local scope operations",
			"required": [
				"DbType",
				"Hostname",
				"Port",
				"Enabled"
			]
		},
		"localredisserverconfiguration": {
			"type": "object",
			"properties": {
				"Hostname": {
					"type": "string",
					"description": "Redis server host name or IP"
				},
				"Port": {
					"type": "integer",
					"default": 6379,
					"description": "Redis server port"
				},
				"Username": {
					"type": "string",
					"description": "Redis ACL username"
				},
				"Password": {
					"type": "string",
					"description": "Redis password"
				},
				"Description": {
					"type": "string",
					"description": "Optional configuration description"
				},
				"Enabled": {
					"type": "boolean",
					"default": true,
					"description": "When false, this local Redis server configuration is ignored by clio commands"
				}
			},
			"description": "Local Redis server settings used by local scope operations",
			"required": [
				"Hostname",
				"Port",
				"Enabled"
			],
			"default": {
				"Hostname": "localhost",
				"Port": 6379,
				"Username": "default",
				"Password": "password",
				"Enabled": true,
				"Description": "Local Redis server"
			]
		}
	}
}


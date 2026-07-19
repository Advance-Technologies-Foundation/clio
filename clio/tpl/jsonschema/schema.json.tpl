{
	"$schema": "http://json-schema.org/schema",
	"properties": {
		"iis-clio-root-path": {
			"type": "string",
			"description": "Default IIS folder where Creatio instances will run from"
		},
		"iis-certificate-thumbprint": {
			"type": "string",
			"pattern": "^[A-F0-9]{40}$",
			"description": "Optional normalized LocalMachine/My certificate thumbprint preferred when multiple usable IIS HTTPS certificates match the machine FQDN",
			"examples": ["DFC3141FAA198BA485538E2406CF52D90E812709"]
		},
		"workspaces-root": {
			"type": "string",
			"description": "Default absolute base directory for create-workspace --empty when --directory is omitted"
		},
		"knowledge": {
			"$ref": "#/definitions/knowledgeconfiguration",
			"description": "Trusted knowledge sources, local cache, and deterministic topic-resolution settings"
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
		"telemetry": {
			"$ref": "#/definitions/telemetrysettings",
			"description": "Product telemetry upload configuration for the MCP server"
		},
		"dbhub": {
			"$ref": "#/definitions/dbhubsettings",
			"description": "Local dbHub HTTP MCP server integration"
		},
		"Environments": {
			"type": "object",
			"patternProperties": {
				".*": {
					"$ref": "#/definitions/environment"
				}
			},
			"description": "List of configured environments that clio can interact with"
		},
		"features": {
			"type": "object",
			"description": "Experimental feature toggles, keyed by feature name (matched case-insensitively). Set a key to true to enable the gated command(s) and MCP tool/prompt/resource(s) for that feature; an absent or false key keeps them hidden and unreachable on every surface (CLI parsing, help, generated docs, and MCP registration). All flags are off by default. This 'features' object is a sibling of 'Environments' at the document root, NOT nested inside 'Environments'. Prefer managing flags with 'clio experimental --name <key> --enable' / '--disable' over editing this file by hand.",
			"patternProperties": {
				".*": {
					"type": "boolean",
					"description": "true enables the feature; false or an absent key disables it"
				}
			},
			"examples": [
				{
					"process-designer": true
				}
			]
		}
	},
	"description": "Clio environment description file",
	"required": [
		"ActiveEnvironmentKey",
		"Autoupdate",
		"Environments"
	],
	"definitions": {
		"dbhubsettings": {
			"type": "object",
			"additionalProperties": false,
			"properties": {
				"enabled": { "type": "boolean", "default": false },
				"config-path": { "type": "string", "minLength": 1 },
				"host": { "type": "string", "enum": ["127.0.0.1"], "default": "127.0.0.1" },
				"port": { "type": "integer", "minimum": 1, "maximum": 65535, "default": 7999 },
				"sync-local-environments": { "type": "boolean", "default": true }
			},
			"required": ["enabled", "config-path", "host", "port", "sync-local-environments"]
		},
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
		"telemetrysettings": {
			"type": "object",
			"properties": {
				"enabled": {
					"type": "boolean",
					"default": true,
					"description": "Master switch for telemetry uploads; false disables uploading entirely even with the default endpoint and granted consent (overridden by CLIO_TELEMETRY_ENABLED)"
				},
				"endpoint": {
					"type": "string",
					"description": "Full OTLP/HTTP logs endpoint URL (https, or http only for a loopback host); overrides the shipped production default, which is used when this is empty"
				},
				"ingest-key": {
					"type": "string",
					"description": "Optional public ingest key sent as the X-Ingest-Key request header"
				}
			},
			"description": "Product telemetry upload configuration"
		},
		"knowledgeconfiguration": {
			"type": "object",
			"properties": {
				"root-path": {
					"type": "string",
					"description": "Absolute directory where Clio persists installed knowledge bundles and readable extracted content"
				},
				"sources": {
					"type": "object",
					"additionalProperties": { "$ref": "#/definitions/knowledgesource" },
					"description": "Trusted knowledge sources keyed by operator-friendly alias"
				},
				"topic-pins": {
					"type": "object",
					"additionalProperties": { "type": "string" },
					"description": "Logical topic IDs pinned to stable library IDs"
				}
			}
		},
		"knowledgesource": {
			"type": "object",
			"properties": {
				"library-id": { "type": "string" },
				"type": { "type": "string", "enum": ["nuget", "git"] },
				"location": { "type": "string" },
				"trusted-key-id": {
					"type": "string",
					"description": "Signature key ID authorized for bundles from this source"
				},
				"trusted-public-key-path": {
					"type": "string",
					"description": "Absolute local path to public verification-key material; never a private key"
				},
				"package-id": { "type": "string" },
				"branch": { "type": "string" },
				"tag": { "type": "string" },
				"commit": { "type": "string" },
				"artifact-path": { "type": "string", "default": "knowledge-bundle.zip" },
				"enabled": { "type": "boolean", "default": true },
				"priority": { "type": "integer", "default": 0 },
				"participation": {
					"type": "string",
					"enum": ["isolated", "supplement", "authoritative"],
					"default": "supplement"
				}
			},
			"required": ["library-id", "type", "location", "trusted-key-id", "trusted-public-key-path", "enabled", "priority", "participation"]
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
			}
		}
	}
}


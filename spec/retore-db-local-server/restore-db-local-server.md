I want to update restore-db command to be able to restore to a db server that is not in kubernetes cluster. I should be able to specify all necessary info in appsetting.json file . For instance I will define a json object like. This is not set in stone, fee free to suggest improvements.

```json

"db" : {
    "my-local-db-server-mssql": {
        "dbtype":"mssql",
        "hostname":"mydbserver.example.com",
        "port":1433,
        "username":"mydbuser",
        "password":"mydbpassword",
    },
    "my-local-db-server-postgres": {
        "dbtype":"postgres",
        "hostname":"mydbserver.example.com",
        "port":5432,
        "username":"mydbuser",
        "password":"mydbpassword"
    }
}

```


Restore command should take backup file from zip or a folder and restore to the specified db server based on the dbtype. It would be great if command could automatically determine the dbtype based on the backup file extension.

The command should be considered when db restore and we can make a test connection to it.


Command should fail fast: I want to make sure that before we do any restore we try to establish a test connection. Error messages should be sufficient for an AI agent to provide troubleshooting as well as for a human to understand the issue.


This command should preserve current behavior when if nothing is specified we first restore into kubernetes cluster db server


Also you should update schema.json file describing appsettings.json file to include the new db object and its properties.
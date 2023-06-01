using MediatR;
using OneOf;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.ScenarioHandlers {


    public class RestoreBdRequest : BaseHandlerRequest {
    }



    public class RestoreBdResponse : BaseHandlerResponse {
    }

    internal class RestoreBdRequestHandler : IRequestHandler<RestoreBdRequest, OneOf<BaseHandlerResponse, HandlerError>> {

        public Dictionary<string, string > Arguments{ get; set; }

        public async Task<OneOf<BaseHandlerResponse, HandlerError>> Handle(RestoreBdRequest request, CancellationToken cancellationToken) {

            Arguments = request.Arguments;             
            var adminConectionString = request.Arguments["adminConectionString"];
            CopyBackUpFile();
            ConnectToSQl(adminConectionString);

            return new RestoreBdResponse() {
                Status = BaseHandlerResponse.CompletionStatus.Success
            };
        }

        private void CopyBackUpFile() {

            var backupFolderPath = Arguments["backupFolderPath"];
            var backUpSrc = Arguments["backUpSrc"];

            var dbBackUpFileFolderPath = Path.Join(backUpSrc, "db");
            var files = Directory.GetFiles(dbBackUpFileFolderPath);
            FileInfo src = new(files.FirstOrDefault());
            var backupFileName = Path.Join(backupFolderPath, src.Name);
            src.CopyTo(backupFileName, true);
        }

        private void ConnectToSQl(string connectionString) {
                        
            using var connection = new System.Data.SqlClient.SqlConnection(connectionString);
            connection.Open();
            RestoreDbFromFile(connection);
            CreateDbUser(connection);
            GrantPermissionToUser(connection);
        }

        private void RestoreDbFromFile(System.Data.SqlClient.SqlConnection connection) {

            var backupFolderPath = Arguments["backupFolderPath"];
            var dataFolderPath = Arguments["dataFolderPath"];
            var backUpSrc = Arguments["backUpSrc"];
            var dbName = Arguments["dbName"];

            var dbBackUpFileFolderPath = Path.Join(backUpSrc, "db");
            var files = Directory.GetFiles(dbBackUpFileFolderPath);
            FileInfo ffi = new(files.FirstOrDefault());
            var backupFileName = Path.Join(backupFolderPath, ffi.Name);


            string mdf = Path.Join(dataFolderPath, dbName + ".mdf");
            string ldf = Path.Join(dataFolderPath, dbName + ".ldf");


            string query = @$"
                USE [master] RESTORE DATABASE [{dbName}] 
                FROM  DISK = N'{backupFileName}' WITH  FILE = 1, 
                MOVE N'TSOnline_Data' TO N'{mdf}', 
                MOVE N'TSOnline_Log' TO N'{ldf}', 
                NOUNLOAD,  STATS = 5
            ";

            System.Data.SqlClient.SqlCommand sqlCommand = new(query, connection);
            sqlCommand.ExecuteNonQuery();
        }

        private void CreateDbUser(System.Data.SqlClient.SqlConnection connection) {
            var dbUserName = Arguments["dbUserName"];
            var dbPassword = Arguments["dbPassword"];
            string query = @$"
                if not Exists (select loginname from master.dbo.syslogins where name = '{dbUserName}')
                Begin
	                declare @SqlStatement as nvarchar(max) = 'USE [master]'
	                EXEC sp_executesql @SqlStatement
	                select @SqlStatement = 'CREATE LOGIN [{dbUserName}] WITH PASSWORD=N'+'''{dbPassword}'''+', DEFAULT_DATABASE=[master], CHECK_EXPIRATION=OFF, CHECK_POLICY=OFF'
	                EXEC sp_executesql @SqlStatement
                End
            ";

            System.Data.SqlClient.SqlCommand sqlCommand = new(query, connection);
            sqlCommand.ExecuteNonQuery();

        }

        private void GrantPermissionToUser(System.Data.SqlClient.SqlConnection connection) {

            var dbName = Arguments["dbName"];
            var dbUserName= Arguments["dbUserName"];
            string query = @$"USE [{dbName}]
                CREATE USER [{dbUserName}] FOR LOGIN [{dbUserName}];
                ALTER ROLE [db_owner] ADD MEMBER [{dbUserName}];
                ";
            System.Data.SqlClient.SqlCommand sqlCommand = new(query, connection);
            sqlCommand.ExecuteNonQuery();
        }
    }
}

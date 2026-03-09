Remove-Item -Path .\allure-report\* -Recurse -Force;
Remove-Item -Path .\bin\Debug\net10.0\* -Recurse -Force;
dotnet test .\clio.mcp.e2e.csproj; 
allure generate; 
allure serve

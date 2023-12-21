dotnet test .\clio.tests\clio.tests.csproj -nowarn:none `
/p:CollectCoverage=true /p:CoverletOutputFormat=lcov `
/p:CoverletOutput=".\..\.qodana\code-coverage";
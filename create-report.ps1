$env:URL = "http://kkrylovn.tscrm.com:40015";
#$env:URL = "http://ts1-mrkt-web01:88/clio-f";
$env:LOGIN = "Supervisor";
$env:PASSWORD = "Supervisor";
$env:IS_NETCORE = "false";

dotnet build clio\clio.csproj;
#dotnet test clio.TestsAPI\clio.TestsAPI.csproj;
dotnet test --filter Category=SetWebServiceUrlCommand  clio.TestsAPI\clio.TestsAPI.csproj;

livingdoc test-assembly .\clio.TestsAPI\bin\Debug\net8.0\clio.TestsAPI.dll `
-t .\clio.TestsAPI\bin\Debug\net8.0\TestExecution.json `
-o .\TestResults\Api-Test-Results;

$currentDir = Get-Location
Start-Process -FilePath "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" -ArgumentList "$currentDir\TestResults\Api-Test-Results\LivingDoc.html" 
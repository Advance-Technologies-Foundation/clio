dotnet build .\clio\clio.csproj -c Release --no-incremental


dotnet build .\cliogate\cliogate.csproj -c Release -f net472 --no-incremental
dotnet build .\cliogate\cliogate.csproj -c Release -f netstandard2.0 --no-incremental

New-Item ".\cliogate\.temp" -ItemType Directory -Force
dotnet build .\cliogate\cliogate.csproj --output ".\cliogate\.temp" -p:CopyLocalLockFileAssemblies=true
Copy-Item -Path ".\cliogate\.temp\ATF.Repository.dll"  -Destination ".\cliogate\Files\Bin" -Force
Copy-Item -Path ".\cliogate\.temp\ATF.Repository.dll"  -Destination ".\cliogate\Files\Bin\netstandard" -Force
Remove-Item -LiteralPath ".\cliogate\.temp" -Force -Recurse

dotnet .\clio\bin\Release\netcoreapp3.1\clio.dll compress .\cliogate -d .\clio\cliogate\cliogate.gz
dotnet .\clio\bin\Release\netcoreapp3.1\clio.dll compress .\cliogate -d .\clio\cliogate\cliogate_netcore.gz

Get-ChildItem .\cliogate\Files\Bin | Remove-Item -Force -Recurse

dotnet build .\clio\clio.csproj -c Debug --no-incremental
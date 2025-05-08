# format-code.ps1
# Скрипт для автоматического форматирования кода согласно стилям Microsoft

# Установка dotnet-format, если не установлен
if (-not (Get-Command "dotnet-format" -ErrorAction SilentlyContinue)) {
    Write-Host "Установка инструмента dotnet-format..." -ForegroundColor Yellow
    dotnet tool install -g dotnet-format
}

# Полный путь к решению
$solutionPath = "$PSScriptRoot/clio.sln"

# Форматирование кода с применением стилей из .editorconfig
Write-Host "Форматирование кода решения согласно .editorconfig и ruleset..." -ForegroundColor Cyan
dotnet format $solutionPath --fix-style info --fix-analyzers info --verbosity diagnostic

Write-Host "Форматирование завершено!" -ForegroundColor Green
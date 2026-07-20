$projectDirectory = Join-Path $PSScriptRoot "src/HomeAutomationNetDaemon"

Push-Location $projectDirectory
try {
    dotnet tool run nd-codegen
}
finally {
    Pop-Location
}

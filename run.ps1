Push-Location src
try {
    dotnet build
    dotnet run --project .\mtg-commander-tracker.csproj
}
finally {
    Pop-Location
}
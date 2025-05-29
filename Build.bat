dotnet publish -r win-x64 --self-contained
dotnet publish -r linux-x64 --self-contained
cd MagellanicPenguin\vscode && npm run package
# Rebuild the standalone starter from the same protocol and strategy as the repository example.
$ErrorActionPreference = 'Stop'
$arenaRoot = Split-Path -Parent $PSScriptRoot
$starterDirectory = Join-Path $arenaRoot 'tmp/starter-bot'
$starterOutput = Join-Path $arenaRoot 'src/Halloween.Web/wwwroot/starter'
New-Item -ItemType Directory -Force -Path $starterDirectory, $starterOutput, (Join-Path $starterDirectory 'Properties') | Out-Null
Copy-Item -LiteralPath (Join-Path $arenaRoot 'examples/Halloween.SampleBot/Program.cs') -Destination (Join-Path $starterDirectory 'Program.cs')
Copy-Item -LiteralPath (Join-Path $arenaRoot 'src/Halloween.Engine/Contracts.cs') -Destination (Join-Path $starterDirectory 'Contracts.cs')
Copy-Item -LiteralPath (Join-Path $arenaRoot 'examples/Halloween.SampleBot/Properties/launchSettings.json') -Destination (Join-Path $starterDirectory 'Properties/launchSettings.json')
@'
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $starterDirectory 'Halloween.Bot.csproj') -Encoding UTF8
@'
# Mon bot Halloween

Prerequis : SDK .NET 10 stable.

1. Dans ce dossier, lancer `dotnet run`.
2. Dans l'arène, ouvrir Mes bots et connecter http://localhost:5081.
3. Selectionner Mon premier bot et creer une nouvelle partie.
4. Modifier Program.cs pour changer la stratégie.

L'API expose POST /name et POST /move. Contracts.cs contient le format des observations.
Pour rendre le bot accessible depuis un serveur d'arene distant, heberger cette API sur une URL publique.
'@ | Set-Content -LiteralPath (Join-Path $starterDirectory 'README.md') -Encoding UTF8
# Compress only sources, so repeated runs do not ship build outputs.
$starterSources = @('Program.cs', 'Contracts.cs', 'Halloween.Bot.csproj', 'README.md', 'Properties') | ForEach-Object { Join-Path $starterDirectory $_ }
Compress-Archive -LiteralPath $starterSources -DestinationPath (Join-Path $starterOutput 'Halloween.Bot.zip') -Force
Write-Output 'Bot autonome cree : src/Halloween.Web/wwwroot/starter/Halloween.Bot.zip'

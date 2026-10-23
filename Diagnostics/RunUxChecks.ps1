$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$checkFolder = Join-Path $repositoryRoot 'obj\UxSmoke'
New-Item -ItemType Directory -Path $checkFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UxSmoke.cs.txt') -Destination (Join-Path $checkFolder 'Program.cs') -Force
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="../../MyClinic.csproj" /></ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $checkFolder 'UxSmoke.csproj') -Encoding UTF8

Push-Location $repositoryRoot
try {
    dotnet run --project obj/UxSmoke/UxSmoke.csproj -v:q -p:WarningLevel=0
    if ($LASTEXITCODE -ne 0) { throw 'UX regression checks failed.' }
}
finally {
    Pop-Location
}

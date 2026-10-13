$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet run --project tests/Zebex.Checks -c Release
if ($LASTEXITCODE -ne 0) { throw 'Testy nie powiodły się.' }
dotnet run --project tests/Transfer.Checks -c Release
if ($LASTEXITCODE -ne 0) { throw 'Testy bezpieczeństwa transmisji nie powiodły się.' }
dotnet run --project tests/Ekp.Checks -c Release
if ($LASTEXITCODE -ne 0) { throw 'Testy formatów danych EKP nie powiodły się.' }
dotnet run --project tests/Protocol.Checks -c Release
if ($LASTEXITCODE -ne 0) { throw 'Testy protokołu nie powiodły się.' }
dotnet publish src/Zebex.SuzBridge -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o artifacts/windows-x64/suz-bridge
if ($LASTEXITCODE -ne 0) { throw 'Budowa mostu SUZ nie powiodła się.' }
dotnet publish src/Zebex.Simulator -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/windows-x64
if ($LASTEXITCODE -ne 0) { throw 'Publikacja nie powiodła się.' }
Copy-Item artifacts/windows-x64/suz-bridge/ZebexSuzBridge.exe artifacts/windows-x64/suz-bridge/zac_dataReader.exe -Force
Copy-Item artifacts/windows-x64/suz-bridge/ZebexSuzBridge.exe artifacts/windows-x64/suz-bridge/zac_dlookup.exe -Force
dotnet run --project tests/Protocol.Checks -c Release -- --manifest artifacts/windows-x64/suz-bridge
if ($LASTEXITCODE -ne 0) { throw 'Manifest paczki nie powiódł się.' }
Copy-Item docs/START-Windows.md artifacts/windows-x64/INSTRUKCJA.md -Force
Copy-Item README.md artifacts/windows-x64/README.md -Force
Copy-Item examples/produkty.csv artifacts/windows-x64/produkty.csv -Force
Write-Host 'Gotowe: artifacts/windows-x64/ZebexSimulator.exe'

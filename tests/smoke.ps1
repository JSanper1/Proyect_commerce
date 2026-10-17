# SystemData: smoke tests sobre una base SystemDataDB NUEVA/VACÍA.
# Ejecutar con API en http://localhost:5111 y SQL Server configurado.
$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5111/api/commerce'
$testFile = Join-Path $env:TEMP 'commerce_14102026.csv'
$emptyFile = Join-Path $env:TEMP 'commerce_13102026.csv'
$reply = Join-Path $env:TEMP 'SystemData-smoke-response.json'
try {
    [IO.File]::WriteAllBytes($emptyFile, [byte[]]@())
    $httpStatus = & curl.exe --silent --show-error --output $reply --write-out '%{http_code}' --form "file=@$emptyFile" "$base/upload"
    if ($LASTEXITCODE -ne 0 -or $httpStatus -ne '400') { throw "CSV vacío: se esperaba HTTP 400; se recibió $httpStatus" }
    Write-Host '[OK] CSV vacío devuelve 400'

    @'
pc_processdate,pc_nomcomred,pc_numdoc
2026-10-14,Comercio Control,0012345678
2026-10-14,,0955512345
2026-10-14,Papeleria Prueba,
2026-10-14,Servicios Prueba,09AB1234
2026-10-14,,ABC-123
'@ | Set-Content -Path $testFile -Encoding UTF8
    $httpStatus = & curl.exe --silent --show-error --output $reply --write-out '%{http_code}' --form "file=@$testFile" "$base/upload"
    $data = Get-Content $reply -Raw | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $httpStatus -ne '200' -or $data.inserted -ne 5) {
        throw "Carga CSV: esperado HTTP 200 e inserted=5; recibido $httpStatus"
    }
    Write-Host '[OK] Se insertan 5 filas mediante POST /upload'

    $result = Invoke-RestMethod -Method POST -Uri "$base/process" -ContentType 'application/json' -Body '{"processDate":"2026-10-14"}'
    if ($result.quarantined -ne 4) { throw "Proceso: se esperaban 4 rechazados, se recibieron $($result.quarantined)" }
    Write-Host '[OK] POST /process envía 4 filas a cuarentena'
    $repeated = Invoke-RestMethod -Method POST -Uri "$base/process" -ContentType 'application/json' -Body '{"processDate":"2026-10-14"}'
    if ($repeated.quarantined -ne 0) { throw "Segundo procesamiento: se esperaba 0, llegó $($repeated.quarantined)" }
    Write-Host '[OK] Reprocesar sin nuevas cargas devuelve 0'

    $rows = @(Invoke-RestMethod -Method GET -Uri "$base/quarantine")
    $ofDate = @($rows | Where-Object { $_.processDate -eq '2026-10-14' })
    if ($ofDate.Count -ne 4 -or @($ofDate | Where-Object { -not $_.reason }).Count -gt 0) {
        throw "GET cuarentena: se esperaban 4 registros con motivo para la fecha de prueba"
    }
    Write-Host '[OK] GET /quarantine devuelve registros con motivo'
    Write-Host 'Pruebas básicas satisfactorias.'
}
finally {
    Remove-Item $testFile, $emptyFile, $reply -ErrorAction SilentlyContinue
}

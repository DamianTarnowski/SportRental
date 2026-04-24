param(
    [Parameter(Mandatory=$true)][string]$ManifestFile
)

if (-not (Test-Path -LiteralPath $ManifestFile)) {
    Write-Output "[StripClient] Manifest nie istnieje - pomijam"
    exit 0
}

$json = Get-Content -Raw -LiteralPath $ManifestFile | ConvertFrom-Json
$before = $json.Endpoints.Count
$filtered = @($json.Endpoints | Where-Object {
    $route = $_.Route
    -not ($route -eq '_client' -or $route -like '_client/*')
})
$json.Endpoints = $filtered
$after = $filtered.Count
$json | ConvertTo-Json -Depth 100 -Compress | Set-Content -LiteralPath $ManifestFile -NoNewline
$removed = $before - $after
Write-Output "[StripClient] Usunieto $removed wpisow _client z manifestu. Przed: $before Po: $after"

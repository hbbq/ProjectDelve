<#
.SYNOPSIS
Installs this campaign into a running local Project Delve library.
.DESCRIPTION
Uses the existing library API and preserves unrelated entries. An existing campaign
is left untouched unless -Update is specified. Update replaces this campaign and
its matching scenario definitions with the portable content, using revision tokens.
After an interrupted first installation, uniquely named scenarios are reused.
.EXAMPLE
./install.ps1
.EXAMPLE
./install.ps1 -BaseUrl http://127.0.0.1:5081 -Update
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://127.0.0.1:5080',
    [switch] $Update
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$server = [Uri] $BaseUrl
if (-not $server.IsAbsoluteUri -or -not $server.IsLoopback -or
    $server.Scheme -notin @('http', 'https') -or $server.AbsolutePath -ne '/' -or
    $server.Query -or $server.Fragment -or $server.UserInfo) {
    throw 'BaseUrl must be a local HTTP(S) origin, for example http://127.0.0.1:5081.'
}
$origin = $server.GetLeftPart([UriPartial]::Authority)

function Invoke-LibraryApi {
    param([string] $Method, [string] $Path, $Payload)
    $request = @{
        Method = $Method
        Uri = "$origin/api/library$Path"
        TimeoutSec = 60
        UseBasicParsing = $true
    }
    if ($null -ne $Payload) {
        $request.ContentType = 'application/json; charset=utf-8'
        $request.Body = [Text.Encoding]::UTF8.GetBytes(($Payload | ConvertTo-Json -Depth 100))
    }
    Invoke-RestMethod @request
}

$portable = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'library.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($portable.version -ne 1 -or @($portable.campaigns).Count -ne 1) {
    throw 'Expected a version 1 portable library containing one campaign.'
}
$campaign = $portable.campaigns[0]
$definitions = @{}
$names = @{}
foreach ($scenario in $portable.scenarios) {
    if ($definitions.ContainsKey($scenario.id) -or $names.ContainsKey($scenario.name)) {
        throw 'Portable scenario IDs and names must be unique.'
    }
    $definitions[$scenario.id] = $scenario
    $names[$scenario.name] = $true
}
foreach ($id in $campaign.scenarioIds) {
    if (-not $definitions.ContainsKey($id)) { throw "Missing portable scenario: $id" }
}

$catalog = Invoke-LibraryApi GET '/' $null
$matches = @($catalog.campaigns | Where-Object { $_.name -ceq $campaign.name })
if ($matches.Count -gt 1) { throw 'Multiple campaigns have this exact name; resolve the ambiguity in the library before installing.' }
$existing = if ($matches.Count -eq 1) { $matches[0] } else { $null }
$candidates = if ($null -ne $existing) {
    @($catalog.scenarios | Where-Object { $_.id -in $existing.scenarioIds })
} else {
    @($catalog.scenarios)
}

# Resolve every identity before any writes; no file stores machine-specific IDs.
$resolved = @{}
foreach ($id in $campaign.scenarioIds) {
    $source = $definitions[$id]
    $matches = @($candidates | Where-Object { $_.name -ceq $source.name })
    if ($matches.Count -gt 1) { throw "Multiple scenarios are named '$($source.name)'; no changes were made." }
    if ($matches.Count -eq 1) { $resolved[$id] = $matches[0] }
}

if ($null -ne $existing -and -not $Update) {
    Write-Output 'Campaign already installed; existing local edits were preserved. Use -Update to replace its content.'
} else {
    foreach ($id in $campaign.scenarioIds) {
        $source = $definitions[$id]
        $payload = @{ name = $source.name; coverArt = $source.coverArt; definition = $source.definition }
        if ($resolved.ContainsKey($id)) {
            if ($null -ne $existing -and $Update) {
                $previous = $resolved[$id]
                $payload.revision = $previous.revision
                $payload.changeToken = $previous.changeToken
                $resolved[$id] = Invoke-LibraryApi PUT "/scenarios/$($previous.id)" $payload
            }
            # With no campaign yet, reuse matching records without rewriting them.
        } else {
            $resolved[$id] = Invoke-LibraryApi POST '/scenarios' $payload
        }
    }
    $payload = @{
        name = $campaign.name
        description = $campaign.description
        coverArt = $campaign.coverArt
        scenarioIds = @($campaign.scenarioIds | ForEach-Object { $resolved[$_].id })
    }
    if ($null -ne $existing) {
        $payload.revision = $existing.revision
        $payload.changeToken = $existing.changeToken
        $existing = Invoke-LibraryApi PUT "/campaigns/$($existing.id)" $payload
        Write-Output 'Campaign updated from portable content.'
    } else {
        $existing = Invoke-LibraryApi POST '/campaigns' $payload
        Write-Output 'Campaign installed as local library content.'
    }
}

Write-Output "Campaign: $origin/#campaign/$($existing.id)"
foreach ($id in $campaign.scenarioIds) {
    if ($resolved.ContainsKey($id)) {
        Write-Output "$id -> $($resolved[$id].id)"
    } else {
        Write-Output "$id -> no matching entry in the existing campaign (local edits preserved)"
    }
}

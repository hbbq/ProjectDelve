# Protected entry point. Never source scripts or execute commands from the plan.
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true)][ValidateSet('inspect', 'validate')][string]$Action,
    [Parameter(Mandatory = $true)][string]$PlanFile
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try {
    $root = Split-Path $PSScriptRoot -Parent
    if ([IO.Path]::GetFileName($PSScriptRoot) -ne '.codex') { throw 'Run the reviewed entry point in .codex.' }
    $runtime = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'browser-qa.runtime.json') -Raw | ConvertFrom-Json
    foreach ($path in @($runtime.node, $runtime.playwright)) {
        if (-not [IO.Path]::IsPathRooted($path) -or -not (Test-Path -LiteralPath $path)) { throw 'Installed browser runtime is missing; see BROWSER_INSPECTION.md.' }
        if ($path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Browser runtime must be installed outside the writable workspace.' }
    }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $runtime.node
    # All native arguments are fixed, protected paths. Plan data travels on stdin.
    $helper = Join-Path $PSScriptRoot 'browser-qa.cjs'
    $start.Arguments = '"' + $helper + '"'
    $start.WorkingDirectory = $PSScriptRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    # Exclude NODE_OPTIONS, NODE_PATH, Playwright overrides, proxy settings and
    # injected child-process hooks. Preserve only normal Windows launch needs.
    $start.EnvironmentVariables.Clear()
    foreach ($name in @('SystemRoot', 'WINDIR', 'ProgramFiles', 'ProgramFiles(x86)', 'LOCALAPPDATA', 'USERPROFILE', 'TEMP', 'TMP')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($value) { $start.EnvironmentVariables[$name] = $value }
    }
    $start.EnvironmentVariables['PATH'] = (Split-Path $runtime.node -Parent) + ';' + [Environment]::SystemDirectory
    $start.EnvironmentVariables['NO_UPDATE_NOTIFIER'] = '1'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    if (-not $process.Start()) { throw 'Could not start the installed Node runtime.' }
    $process.StandardInput.WriteLine((@{ action = $Action; planFile = $PlanFile } | ConvertTo-Json -Compress))
    $process.StandardInput.Close()
    $process.WaitForExit()
    exit $process.ExitCode
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}

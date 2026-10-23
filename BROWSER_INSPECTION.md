# Browser visual inspection

Builds, Node tests and the isolated localhost host run inside the existing Windows
sandbox. Visible Edge inspection uses `.codex/browser-qa.ps1` and its narrow
project execution rule. Keep `workspace-write`, `on-request`, reviewer `user` and
restricted networking. Do not enable auto-review, expand writable roots or allow
general PowerShell, Node, dotnet or Playwright execution outside the sandbox.

The official skill is `.agents/skills/playwright-cli/SKILL.md`. The dispatcher uses
the Playwright library already installed with CLI 0.1.22, without exposing CLI
JavaScript or daemon attachment. No additional installation is required.

## Resolve installed Node without the nvm shim

Use Node 20+ and .NET 10. If `node` reports **No active Node.js version is
configured** in the sandbox, resolve the existing runtime without invoking its shim:

```powershell
$nodeCandidates = @(Get-Command node.exe -All | ForEach-Object {
    $_.Source
    $installs = Join-Path (Split-Path (Split-Path $_.Source -Parent) -Parent) 'installs'
    if (Test-Path -LiteralPath $installs) {
        Get-ChildItem -LiteralPath $installs -Directory | ForEach-Object {
            Join-Path $_.FullName 'node.exe'
        }
    }
})
$node = $nodeCandidates | Select-Object -Unique | Where-Object {
    (Test-Path -LiteralPath $_) -and
    [Diagnostics.FileVersionInfo]::GetVersionInfo($_).ProductName -eq 'Node.js'
} | Select-Object -First 1
if (-not $node) { throw 'Resolve an existing Node installation in a normal terminal.' }
& $node --version
& $node --test --test-reporter=dot tools/test-browser-qa.cjs ProjectDelve.Web.Tests/*.test.cjs
```

Do not install or globally reconfigure Node to fix sandbox-account resolution.
Do not substitute a downloaded runtime or a workspace copy for the installed one.

## Cached builds, tests and isolated host

Run from the repository root with dependencies already restored:

```powershell
dotnet build ProjectDelve.sln --no-restore --artifacts-path .playwright-cli
dotnet test ProjectDelve.sln --no-build --no-restore --artifacts-path .playwright-cli
```

Keep the artifacts path exactly one directory below the repository root: Web
tests find their content root four parents above their binaries. Isolated output
also avoids binaries locked by the existing host. Use a focused test filter first
when implementing a change. Restore separately when dependencies change or assets
are missing, with the necessary approval. Do not disable auditing, alter feeds,
install packages or enable unrestricted networking to suppress restore prompts.

Preserve the existing port-5080 host. Check listeners with `netstat`, which avoids
the WMI permissions needed by `Get-NetTCPConnection`. Build before starting 5081:

```powershell
netstat -ano -p tcp | Select-String ':508[01]\s+.*LISTENING'
$listeners = netstat -ano -p tcp
if ($listeners -match '^\s*TCP\s+\S+:5081\s+\S+\s+LISTENING\s+\d+\s*$') {
    throw 'Port 5081 is occupied; do not stop its owner.'
}
$contentRoot = Join-Path (Get-Location).Path 'ProjectDelve.Web'
dotnet .playwright-cli/bin/ProjectDelve.Web/debug/ProjectDelve.Web.dll --contentRoot $contentRoot --urls http://127.0.0.1:5081
```

Retain this foreground terminal/Codex exec session. Host startup is a sandbox
operation, separate from the browser exception. Check readiness in another command:

```powershell
(Invoke-WebRequest http://127.0.0.1:5081/ -UseBasicParsing -TimeoutSec 10).StatusCode
```

Stop only your own host session with Ctrl+C. Do not terminate by process name or
stop another process to obtain its port.

## Data-only browser plans

Each call launches headed Microsoft Edge, creates an in-memory context, loads only
`http://127.0.0.1:5081/`, performs the plan and closes its own browser in `finally`.
Steps within a call share the context; drafts do not survive between calls. There
is no session/profile parameter, attachment capability or persistent daemon.

Write a JSON plan below `.playwright-cli/`:

```powershell
New-Item -ItemType Directory -Path .playwright-cli -Force | Out-Null
@'
{
  "steps": [
    { "action": "click", "target": "#mode-designer" },
    { "action": "snapshot", "target": "#designer-main", "name": "designer.yaml" },
    { "action": "hover", "target": "h1" },
    { "action": "screenshot", "name": "desktop.png" },
    { "action": "resize", "width": 600, "height": 850 },
    { "action": "screenshot", "name": "narrow.png" },
    { "action": "console" },
    { "action": "requests" }
  ]
}
'@ | Set-Content -LiteralPath .playwright-cli/qa-plan.json -Encoding UTF8
```

Validate inside the sandbox without launching Edge:

```powershell
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File .codex/browser-qa.ps1 -Action validate -PlanFile .playwright-cli/qa-plan.json
```

For inspection, use the exact absolute executable/script paths recorded in
`.codex/rules/browser-qa.rules`. On this checkout:

```powershell
C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File D:\sc\bq\ProjectDelve\.codex\browser-qa.ps1 -Action inspect -PlanFile .playwright-cli/qa-plan.json
```

Codex should request `require_escalated` for that exact inspection command; the
loaded project rule authorizes it without repeated human review. Do not propose
a broader saved prefix. Keep it separate from variables, output redirection, host
startup and other shell commands. `RemoteSigned` affects only that PowerShell
process; no machine/user execution policy is changed.

| Action | Fields besides `action` |
| --- | --- |
| `click`, `hover` | `target`: simple CSS selector |
| `fill`, `select` | `target`, `value`: plain text |
| `resize` | integer `width` 320..2560 and `height` 320..1800 |
| `snapshot` | optional `target`, optional `.yaml` basename `name` |
| `screenshot` | optional `.png` basename `name`; captures full page |
| `reload`, `console`, `requests` | none |

Targets are CSS, such as `#designer-width` or `[data-designer-tab="board"]`.
Locator expressions, selector engines, JavaScript, URL/session/launch parameters,
extra fields and unknown actions are rejected. Text values are passed only to
fill/select APIs; they are never evaluated. Plans contain 1..40 steps and are at
most 64 KiB. Names cannot contain paths, traversal, streams, device names or
reserved/duplicate names. Output files are never overwritten.

Each run prints its new `.playwright-cli/browser-qa/inspection-*` directory and
writes `final.png`, `final.yaml`, and `report.json` plus requested captures. Open
PNGs with `view_image`; snapshots alone do not establish visual correctness.
Inspect console/request evidence too. Full-page screenshots do not expand
independently scrolling panels. Edits affect only the temporary draft or isolated
5081 host; avoid game replacement controls unless needed for the task.

## Security review and remaining boundaries

The dispatcher was reviewed and tested before adding its allow rule:

- Complete PowerShell/JSON contracts; no argument passthrough, dynamic evaluation,
  command construction from plan data or caller-supplied scripts.
- Fixed QA origin and installed Edge channel; no personal profile, foreign session,
  CDP/extension attachment, uploads or custom browser flags.
- Protected entry point/helper/runtime manifest under `.codex`; absolute installed
  package loading and a clean environment exclude Node preload hooks, workspace
  modules, proxy variables and Playwright overrides.
- Junction/symlink/path-escape rejection, unique output directories and exclusive
  file creation that refuses overwrites, including existing hard links.
- External browser requests, WebSockets and service workers blocked; navigation
  redirects rejected rather than followed. Popups close, downloads cancel,
  dialogs dismiss, and completion/errors close only the new browser.

This is a constrained command exception, not an OS sandbox around Edge. Browser
background traffic and subresource redirects are not proven network confinement.
Concurrent hostile filesystem tampering is outside the dispatcher's guarantee.
Normal development commands retain their sandbox.

Arbitrary CLI `run-code`/`eval`, the existing
`ProjectDelve.Web.Tests/designer.browser.cjs` acceptance script, downloads, uploads,
installations, protected runner/runtime/rule changes, missing dependency restores
and unrelated system/process operations still need separate review where they
cross the sandbox boundary. The acceptance script remains available through a
separately approved official CLI command; the dispatcher never accepts its filename.

## Recovery and portability

If startup fails, read the exact error, check 5081 readiness and validate the plan.
Correct selectors or restart only your own QA host, then retry. Never use
`kill-all`, `close-all`, process-name termination, personal-browser attachment or
security-disabling flags. If an OS crash leaves a QA window, close that specific
temporary window manually; do not guess which other processes to kill.

The ignored `.codex/browser-qa.runtime.json` contains installed absolute `node` and
`playwright` package paths. Resolve Node with the block above; the package normally
is `node_modules/@playwright/cli/node_modules/playwright` beside the runtime. In a
normal terminal, deliberately recreate/review the protected JSON after moving or
updating the runtime. Review versions other than CLI 0.1.22 before changing it.
Never use workspace packages or download replacements automatically.

After moving the checkout, review the exact absolute script path in the project
rule. Restart Codex after configuration/rule changes. Check `/status` and
`/debug-config` for `workspace-write`, `on-request`, reviewer `user`, restricted
networking and protected `.codex`. A trusted project layer is required. Do not
remove its protection to make runner updates automatic.

## Verification

Run `& $node --test tools/test-browser-qa.cjs` for schema, URL, path, junction,
overwrite and PowerShell argument rejection. Repeat a plan in two fresh Edge
contexts and inspect both capture sizes. In a fresh Codex session confirm the
exact rule is loaded and both calls run without human approval, while a different
script/arbitrary CLI code has no matching allow. Stop your 5081 exec session and
confirm the original 5080 listener/PID is unchanged. Rules remain an experimental
Codex feature; recheck after CLI updates.

Verified on Windows with Codex CLI 0.164.0: cached build, 1,778 .NET tests,
353 client tests and seven dispatcher checks passed inside the sandbox. A fresh
interactive Codex session retained `on-request`/reviewer `user` and completed two
separate eight-step Edge plans without human approval. The original port-5080
PID remained unchanged after stopping the test-owned 5081 host. The interactive
session matters: the tested non-interactive `codex exec` selected policy `never`,
so it could not exercise the `on-request` escalation path.

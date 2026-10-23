# Browser visual inspection

Use the official [Playwright CLI](https://playwright.dev/docs/getting-started-cli)
with a named, temporary browser session. No project npm manifest, Playwright test
suite, browser extension, MCP server or permanent service is required. The
official skill is checked in at
[.agents/skills/playwright-cli/SKILL.md](.agents/skills/playwright-cli/SKILL.md).
Codex supports repository skills in `.agents/skills`; a newly installed skill is
available on the next turn. See [OpenAI's skills documentation](https://developers.openai.com/codex/skills).

## Prerequisites and installation

Run from the repository root in PowerShell. Use Node.js 20+ and .NET 10.

```powershell
node --version
npm --version
dotnet --version
npm list -g --depth=0
```

If the CLI is missing, install it once for the active Node version:

```powershell
npm install -g @playwright/cli@0.1.22
```

Version 0.1.22 was verified with Node 22 and npm 12 on Windows. Use an installed
Microsoft Edge browser with `--browser=msedge`; Chromium does not need to be
downloaded separately. The global installation belongs to the
active Node installation, so switching Node versions may require reinstalling it.

Resolve the executable explicitly if a version-manager shim does not expose the
new `playwright-cli` command:

```powershell
$pw = Join-Path (npm prefix -g) 'playwright-cli.cmd'
& $pw --version
& $pw --help
```

Keep `$pw` in each PowerShell session that issues browser commands. To refresh the
vendored skill intentionally, use the official installer and review its diff:

```powershell
& $pw install --skills=agents
```

The installer copies the skill and references and may download browser support
tools into the user's Playwright cache. Keep local dependencies and browser
binaries out of the repository. Put all inspection output, including screenshots,
logs, recordings, downloads and any saved authentication state, under the ignored
`.playwright-cli/` directory. Do not add generated output to the reusable skill.

## Start an isolated host

Inspect listeners before choosing a port. Do not stop a process just to obtain
its port. An existing Project Delve host can be reused for read-only inspection,
but a separate instance keeps its game state independent.

```powershell
$qaPort = 5081
$listeners = Get-NetTCPConnection -State Listen -ErrorAction Stop
if ($listeners.LocalPort -contains $qaPort) {
    throw 'Port 5081 is occupied; choose another unused port and update the URL.'
}
New-Item -ItemType Directory -Path .playwright-cli -Force | Out-Null
dotnet run --project ProjectDelve.Web --no-launch-profile --artifacts-path .playwright-cli/dotnet -- --urls http://127.0.0.1:5081
```

Leave that command running in its own terminal or retain its Codex exec session.
The separate artifacts path avoids rebuilding over another running host's
binaries. Binding to `127.0.0.1` keeps the inspection host local. Check readiness
from a second terminal:

```powershell
(Invoke-WebRequest http://127.0.0.1:5081/ -UseBasicParsing).StatusCode
```

For a background launch, create the output directory first, use
`Start-Process -WindowStyle Hidden -PassThru`, redirect logs into
`.playwright-cli/`, and retain the returned PID. Stop only the processes created
for that launch after checking their command lines. Prefer the foreground exec
session when available because Ctrl+C provides straightforward cleanup.

## Open and edit a temporary draft

Use a unique session name when another inspection may already be running. Designer
and Playtest are now available directly in the persistent application navigation.

```powershell
$pw = Join-Path (npm prefix -g) 'playwright-cli.cmd'
& $pw -s=delve-designer-qa open http://127.0.0.1:5081/ --browser=msedge --headed
& $pw -s=delve-designer-qa resize 1440 1000
& $pw -s=delve-designer-qa click '#mode-designer'
& $pw -s=delve-designer-qa screenshot --filename=.playwright-cli/designer-desktop.png --full-page
```

The Designer is a mode on `/`, reached through **Designer** in the header. Its
inspector sections retain access to every editing capability.

The default profile is temporary and separate from personal browser tabs. Drafts
and unfinished inputs recover from browser-local storage after reload. Use New
and accept the replacement prompt to reset a temporary draft deliberately. This
example changes no scenario file or running game:

```powershell
& $pw -s=delve-designer-qa click '[data-designer-tab=board]'
& $pw -s=delve-designer-qa fill '#designer-width' 8
& $pw -s=delve-designer-qa fill '#designer-height' 6
& $pw -s=delve-designer-qa click '#designer-board-form button'
& $pw -s=delve-designer-qa click '#designer-tools [data-tool=Terrain]'
& $pw -s=delve-designer-qa select '#designer-terrain' Grass
& $pw -s=delve-designer-qa click "#designer-board [data-editor-hit=cell][data-x='1'][data-y='1']"
& $pw -s=delve-designer-qa click '#designer-tools [data-tool=Select]'
& $pw -s=delve-designer-qa click "#designer-board [data-editor-hit=cell][data-x='1'][data-y='1']"
& $pw -s=delve-designer-qa snapshot '#designer-properties'
& $pw -s=delve-designer-qa select '#designer-properties select >> nth=0' Water
& $pw -s=delve-designer-qa click '#designer-properties button'
& $pw -s=delve-designer-qa click '#designer-validate'
& $pw -s=delve-designer-qa snapshot '#designer-status'
```

Quote numeric CSS attribute values as shown. Alternatively, use fresh element
refs from a snapshot; refs can change after rerendering or navigation. Validation
is stateless. **Test scenario**, game start/restart and scenario load controls
replace or mutate the host's game; they are unnecessary for draft inspection.

## Capture, inspect and iterate

```powershell
& $pw -s=delve-designer-qa hover 'h1'
& $pw -s=delve-designer-qa screenshot --filename=.playwright-cli/designer-edited-desktop.png --full-page
& $pw -s=delve-designer-qa resize 900 800
& $pw -s=delve-designer-qa hover 'h1'
& $pw -s=delve-designer-qa screenshot --filename=.playwright-cli/designer-narrow.png --full-page
& $pw -s=delve-designer-qa console warning
& $pw -s=delve-designer-qa requests
```

Move the pointer away from the board before capturing to avoid incidental hover
highlights. Omit `--full-page` for a viewport screenshot. A full-page capture does
not expand independently scrolling containers: scroll the properties sidebar and
capture again when inspecting its lower controls.

For repeatable Phase 1 acceptance with a dedicated test-owned scenario:

```powershell
& $pw -s=delve-designer-qa run-code --filename=ProjectDelve.Web.Tests/designer.browser.cjs
```

This script replaces the temporary draft and the isolated host's game, accepts
draft replacement prompts, and exercises painting/history, Route progress,
atomic rejection, recovery, file download/open, validation navigation and
DELVE1/Playtest return. It captures desktop, 600px and 390px screenshots under
`.playwright-cli/`; inspect them with `view_image` afterwards.

CLI snapshots describe controls and provide element refs, but do not establish
visual correctness. After each material UX change, Codex should open the generated
PNG using its local `view_image` tool and actually inspect it. This was verified
for both the desktop and narrow screenshots. Use partial snapshots such as
`snapshot '#designer-properties'` to avoid dumping hundreds of board targets.

Inspect new browser console messages and failed network requests alongside the
images. In the initial verification, `/favicon.ico` returned 404; all inspected
designer requests succeeded. The 900px layout had no document-level horizontal
overflow, while the sidebar retained its own scrolling area. These are
observations, not comprehensive UX approval.

## Cleanup

```powershell
& $pw -s=delve-designer-qa close
```

Reload now recovers a draft; it does not discard it. Close the temporary session
when done. Stop your host with Ctrl+C in its terminal/exec session. Do not use `close-all`,
`kill-all`, process-name-wide termination, or stop a host you did not launch.
Screenshots, snapshots and isolated build output remain under the ignored
`.playwright-cli/` directory for local review. They are not committed.

## Sandbox restrictions and smallest fixes

Windows sandbox execution may use a separate account without the normal user's
Node configuration or process-inventory permissions. Observed symptoms include
the nvm shim reporting **No active Node.js version is configured** and
`Get-NetTCPConnection` returning **Access denied (0x80041003)**. Use approved
execution outside the sandbox when necessary; do not reinstall or globally
reconfigure Node merely to fix the sandbox account. Browser control was verified
through approved execution outside the sandbox.

Request narrowly scoped approvals for the commands that need normal-user Node,
network access, browser launch/session control or process inventory. Do not
disable Codex sandboxing, change persistent approval policy or add browser
security-disabling flags. If approval is unavailable, report the failed command
and exact error, and ask the user to run that command in a normal local terminal.

If Edge is missing, choose another installed supported browser explicitly, or
request approval for the official CLI's browser installation command shown by
`--help install-browser`. An Edge launch was verified here, so no browser download
was necessary. If Git reports dubious ownership under a sandbox account, resolve
the current checkout dynamically and use a command-scoped exception instead of
changing global Git configuration:

```powershell
$checkoutPath = (Get-Location).Path.Replace('\', '/')
git -c "safe.directory=$checkoutPath" status --short
```

## Phase 1 acceptance coverage

The checked-in acceptance script uses a dedicated test-owned scenario. It checks
atomic paint-stroke Undo/Redo, property commits when selecting another Cell,
rejected compound agency edits, unchanged Route progress, recovery of unfinished
input, validation-error navigation, draft Save/Open, DELVE1 import/export and
return from Playtest. It also exercises mobile painting and zoom, and captures
1440px, 600px and 390px layouts. Run it in a named temporary session against the
isolated host; inspect its screenshots separately. No permanent service or
repository-local browser installation is required.

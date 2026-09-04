# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Windows system-tray utility (.NET 9, WinForms) that replaces OpenDNS's own abandoned
"Dynamic IP Updater". It detects the machine's public IP and pushes it to OpenDNS's
legacy dynamic-update API whenever it changes, so OpenDNS's per-account filtering policy
(applied via its resolvers, configured directly on devices like a phone or PC — not via a
router) keeps following the real home IP. Single project, no tests, no CI, one developer.
See [README.md](README.md) for user-facing setup/troubleshooting; this file is about the
code itself.

## Commands

```
dotnet build OpenDnsUpdater.sln -c Debug          # build
dotnet build src/OpenDnsUpdater/OpenDnsUpdater.csproj -c Debug   # build just the project
.\publish.ps1                                      # produce the distributable exe (see below)
```

There is no test suite and no linter/formatter config in this repo.

To run and observe behavior during development, launch the built exe directly
(`src/OpenDnsUpdater/bin/Debug/net9.0-windows/OpenDnsUpdater.exe`) rather than
`dotnet run` — it's a tray app with no console output, so watch
`%LOCALAPPDATA%\OpenDnsUpdater\log.txt` for what it's doing. Only one instance runs at a
time (a named mutex in `Program.cs` blocks a second launch), so kill the running process
(`taskkill /F /IM OpenDnsUpdater.exe`) before relaunching after a rebuild.

`publish.ps1` deliberately publishes **framework-dependent** (`--self-contained false`),
not trimmed/self-contained: WinForms trimming is not officially supported and can break at
runtime unpredictably, and self-contained-untrimmed would bundle ~150MB of runtime for no
benefit on a machine that already has the .NET 9 Desktop Runtime. This produces a single
~360KB exe instead.

## Architecture

**No main window.** `Program.cs` runs `Application.Run(new TrayAppContext())` —
`TrayAppContext : ApplicationContext` owns the `NotifyIcon` and is the whole app; there is
no `Form` shown to the user. `HiddenForm` exists purely to give background work a
UI-thread-affine handle to marshal onto (`Invoke`/`BeginInvoke`) and to own the Settings
dialog. **Gotcha already hit once:** `Control.CreateControl()` is a no-op when `Visible` is
false (which `HiddenForm` forces permanently) — it does *not* create the window handle.
`TrayAppContext`'s constructor instead forces creation by reading `_hiddenForm.Handle`
directly. Getting this wrong doesn't crash anything visibly — it just makes every
background-thread callback silently fail to reach the UI thread (see next point).

**Fire-and-forget async always goes through `TaskExtensions.FireAndForget`.** An
unobserved exception in a discarded `Task` does not crash a modern .NET process — it just
vanishes. That's exactly what masked the `HiddenForm` bug above during development: the app
ran fine with a lit tray icon while every scheduled IP check silently threw and died. Any
new `_ = SomeAsync()` call site should be `SomeAsync().FireAndForget("description")`
instead, so failures land in the log rather than nowhere.

**One timer drives both event-driven and polling checks.** `IpMonitorService` subscribes to
`NetworkChange.NetworkAddressChanged`/`NetworkAvailabilityChanged` (fires on real network
changes — DHCP renewal, Wi-Fi reconnect, VPN toggle) for fast reaction, and reschedules a
single `System.Threading.Timer` after every check (whether event-triggered or the periodic
fallback) rather than running two separate timers. A `SemaphoreSlim` gate in
`CheckAndUpdateAsync` ensures only one check is ever in flight; a failed check reschedules
itself sooner-but-backed-off rather than at the configured interval.

**IP detection has a primary path and a fallback**, both in `PublicIpResolver`: a DNS query
for `myip.opendns.com` sent directly to OpenDNS's own resolvers (208.67.222.222 /
208.67.220.220, via the `DnsClient` package — plain `Dns.GetHostAddresses` won't work here
since it only asks the OS's configured resolver, and this hostname only resolves specially
against OpenDNS's own nameservers) is tried first; on failure it falls back to plain HTTPS
IP-echo services. `System.Threading.Timer` vs `System.Windows.Forms.Timer` is ambiguous
under WinForms' implicit usings — always qualify as `System.Threading.Timer`.

**`OpenDnsClient` implements OpenDNS's legacy DynDNS-style update protocol**: HTTPS GET to
`updates.opendns.com/nic/update` with HTTP Basic Auth and a `hostname`/`myip` query string.
The response-code-to-`OpenDnsUpdateStatus` mapping in `Parse()` was sourced from OpenDNS's
own archived client source (`opendns/dynamicipupdate` on GitHub, untouched since 2011) and
independently verified live against the real endpoint. `BadAuth` is handled specially
end-to-end (`DescribeLikelyBadAuthCause`, surfaced in both `SettingsForm`'s Test-now flow
and `TrayAppContext`'s warning balloon) because it's almost never actually a wrong
password — it's either a password containing a character (`^ & ~ \` %`) this specific
endpoint has a longstanding bug with, or an account with 2FA that needs a separate
update-only password. Don't reduce that back down to a generic "check your password"
message.

**Settings persistence (`AppSettings`/`AppSettingsStore`)** round-trips to
`%LOCALAPPDATA%\OpenDnsUpdater\settings.json` as plain JSON, except the password: it's
DPAPI-encrypted (`ProtectedData`, `DataProtectionScope.CurrentUser`) before it ever reaches
disk, so the file is meaningless outside the current Windows user account. Anything that
needs the password calls `settings.GetPassword()`, never reads `EncryptedPassword` directly.

**`AutoStartManager`** writes the current exe's path into the per-user
`HKCU\...\Run` key. It resolves that path via `Environment.ProcessPath` /
`Process.GetCurrentProcess().MainModule.FileName` — never `Assembly.Location`, which
silently returns `""` in a single-file publish (caught by the compiler's own IL3000
warning during development; `dotnet publish` is worth watching for warnings like this).

**Tray icons are drawn at runtime** (`TrayIcons`, GDI+ on a `Bitmap`) rather than shipped as
`.ico` assets, so the repo has zero binary assets. The two icons are process-lifetime
statics; the small GDI handle leak from `Icon.FromHandle` is intentional and bounded (two
handles, ever).

**Event history (`EventHistoryStore`/`EventLogForm`)** is a separate, bounded (200-entry),
newest-first JSON log at `%LOCALAPPDATA%\OpenDnsUpdater\history.json`, distinct from
`AppLog`'s free-text `log.txt`. It exists because parsing structured (time/kind/message)
history back out of free text is fragile; `TrayAppContext.OnStatusChanged`/
`OnUpdateCompleted` call both `AppLog` and `_history.Record(...)` side by side for the same
events rather than one being derived from the other. `EventLogForm` (tray menu → "Event
log...") is the one dialog in the app that's resizable and list-driven, so unlike
`SettingsForm` it does *not* use `Form.AutoSize` — see the layout gotcha below for why that
matters.

**Gotcha: WinForms `Button` doesn't auto-scale a hardcoded pixel size to the actual
font/DPI.** `SettingsForm`'s buttons originally set only `Width` (in pixels) and left
`Height` at the WinForms-default 23px (a constant dating to 96 DPI). At this dev machine's
font/DPI, the button's own text needed ~35px to render — so with `Height` stuck at 23, the
label was rendered vertically clipped ("split in half"). Confirmed by comparing
`Button.Bounds.Height` (23) against `Button.PreferredSize.Height` (35) at runtime — the two
diverge whenever you hardcode a pixel dimension WinForms would otherwise compute for you.
Fix: `AutoSize = true` + `AutoSizeMode = AutoSizeMode.GrowAndShrink` + `MinimumSize` (for a
width floor) instead of a fixed `Width`/`Height`, on every `Button` in this codebase.

**Gotcha: `Form.AutoSize` + a screenshot tool that isn't DPI-aware lies to you.**
While chasing the button issue above, `SettingsForm`'s outer `Dock.Fill` panel was
misdiagnosed as broken because PowerShell-driven `GetWindowRect`/screenshot captures came
back visibly clipped. The real cause was that the calling (PowerShell) process wasn't
DPI-aware, so `GetWindowRect` returned coordinates scaled down to a virtualized 96 DPI while
the window's actual content rendered at the monitor's real (150%-scaled) pixel size —
producing a screenshot that was a top-left *crop*, not a faithful capture. Call
`SetProcessDPIAware()` (user32.dll) in the capturing process before `GetWindowRect`/
`PrintWindow` when screenshotting this app for diagnosis, or the geometry will look wrong
even when the app's layout is fine. (The original `Form.AutoSize` + `Dock.Fill` combination
on `SettingsForm`'s root panel turned out to size correctly all along — confirmed by
dumping `Control.Bounds`/`PreferredSize` from inside the app itself, not by screenshot.)

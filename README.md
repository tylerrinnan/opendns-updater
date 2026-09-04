# OpenDNS Updater

A small Windows tray app that replaces the official (long-abandoned — its
[source](https://github.com/opendns/dynamicipupdate) was archived in June 2024 and
hadn't been touched since 2011) OpenDNS Updater.

It watches your public IP address and, whenever it changes, tells OpenDNS so your
account's filtering/policies keep following your actual home network — the same
thing the old updater did for the "Network" associated with your OpenDNS account
(not your router's own DNS settings; this is for devices, like a phone or PC, that
have OpenDNS's resolvers configured directly on them).

## How it works

- **Detecting your IP** — instead of polling a third-party "what's my IP" site, it
  sends a single DNS query for `myip.opendns.com` straight to OpenDNS's own resolvers
  (`208.67.222.222` / `208.67.220.220`). Those resolvers answer that specific name with
  whichever address the query came from — it's OpenDNS's own mechanism for exactly this,
  so a check costs one small UDP round trip, not an HTTP request. If that's ever blocked,
  it falls back to a plain HTTPS IP-echo service.
- **Reacting to changes** — Windows raises a `NetworkAddressChanged` event whenever a
  local adapter's address changes (Wi-Fi reconnect, DHCP renewal, VPN up/down), which is
  what actually causes most public-IP changes. The app reacts to that immediately instead
  of polling on a fixed schedule. A slow periodic check (default every 5 minutes,
  configurable) runs alongside it purely as a safety net, since an ISP can occasionally
  renumber you without any local event firing.
- **Updating OpenDNS** — when the IP has actually changed, it sends the same
  authenticated HTTPS request the official updater used:
  `https://updates.opendns.com/nic/update?hostname=<your network label>&myip=<ip>`,
  confirmed live and unchanged as of this writing.
- **Idle cost** — no window, ~40MB working set, no CPU use between checks, single
  ~360KB executable.

## Before you set it up

You need two things from your OpenDNS account:

1. **Your network label.** Sign in at [dashboard.opendns.com](https://dashboard.opendns.com/),
   open your network's settings, and note its label (this is what ties your public IP to
   your account's filtering policy).
2. **A password the update API will accept.** If your account does **not** use
   two-factor authentication, your normal account password works. If it **does** use 2FA,
   the API can't take your normal password — request an *update-only password* from
   OpenDNS/Cisco support and use that instead.

   Separately, OpenDNS's update endpoint has a long-standing, widely-reported bug: it
   rejects passwords containing `^`, `&`, `~`, `` ` ``, or `%` with `badauth` — even though
   the exact same password works fine for logging into the dashboard, and even though
   OpenDNS's own password page has at times suggested `&` as a valid character. If you get
   `badauth` with a password you're sure is right, this is the first thing to check. The
   app's **Test now** button in Settings detects this and tells you which character is the
   likely culprit.

## Setup

1. Build and publish (see below), or run the debug build directly.
2. Launch `OpenDnsUpdater.exe`. It starts silently in the tray with a blue icon and shows
   a one-time balloon reminding you it isn't configured yet.
3. Right-click the tray icon → **Settings...** and fill in your email, password (or
   update-only password), and network label. Click **Test now** to confirm OpenDNS
   accepts it before saving.
4. Leave **Start automatically when I sign in to Windows** checked (on by default) and
   click **Save**. That's it — it runs hands-off from here.

The tray icon turns orange and a one-time warning balloon appears if an update ever
fails for a reason a retry won't fix (bad credentials, unrecognized network label, etc.).
Right-click → **Event log...** for a readable history of recent checks and the most
recent successful update, or **View raw log file** for the full underlying text log.

## Building

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```
dotnet build OpenDnsUpdater.sln -c Debug
```

To produce the distributable single-file exe:

```
.\publish.ps1
```

This publishes a framework-dependent single file (`publish\OpenDnsUpdater.exe`, ~360KB) —
it needs the .NET 9 Desktop Runtime on the machine it runs on (already present on most
current Windows installs; check with `dotnet --list-runtimes`, or grab it from the link
above). This is deliberately *not* a trimmed self-contained build: trimming isn't
officially supported for WinForms and can break at runtime in ways that are hard to
predict, and bundling the runtime untrimmed would mean a ~150MB exe for no real benefit
on a machine that already has it.

Once published, move `OpenDnsUpdater.exe` wherever you want it to live permanently
(e.g. `%LOCALAPPDATA%\Programs\OpenDnsUpdater\`) **before** turning on auto-start —
the auto-start entry points at the exe's path at the time you save Settings.

## Where its data lives

- `%LOCALAPPDATA%\OpenDnsUpdater\settings.json` — your email, network label, and
  preferences. The password is never stored in plain text: it's encrypted with Windows
  DPAPI, scoped to your Windows user account, so the file is useless if copied elsewhere.
- `%LOCALAPPDATA%\OpenDnsUpdater\log.txt` — rolling log (auto-truncates past ~1MB).
- `%LOCALAPPDATA%\OpenDnsUpdater\history.json` — the last 200 checks/updates, shown in
  the tray's **Event log...** window.
- Auto-start is a normal per-user entry under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` — no admin rights needed, and
  toggling the checkbox in Settings adds/removes it.

## Troubleshooting: update responses

| Response | Meaning | Fix |
|---|---|---|
| `good <ip>` | Updated successfully | — |
| `nochg` | OpenDNS already had this IP | — |
| `badauth` | Usually *not* an actually-wrong password — most often a password containing `^ & ~ \` %` (a known OpenDNS API bug), or 2FA requiring an update-only password | Use **Test now** in Settings for a specific diagnosis |
| `!yours` | Network label isn't on your account | Re-check the label in the dashboard |
| `nohost` | Network label not recognized | Re-check the label in the dashboard |
| `!donator` | Feature needs a paid OpenDNS plan | Check your plan |
| `abuse` | Account/network flagged | Check the dashboard for details |
| `notfqdn` / `numhost` / `badagent` | Malformed request | Shouldn't happen in normal use — check the log |
| `dnserr` / `911` | Transient OpenDNS-side error | The app retries automatically with backoff |

## Notes on the phone

This app only updates *OpenDNS's* record of your current public IP — it can't change
DNS settings on a phone. As long as your phone (and PC) already point at OpenDNS's
resolvers directly and share the same public IP as this PC (i.e. same home network),
they'll pick up the corrected policy automatically once this app updates the network
record — no per-device action needed.

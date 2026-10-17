# Windsurf Gauges

A Macro Deck 3 plugin that shows Windsurf usage on the deck: daily and weekly quota, the extra
usage balance, the plan and its reset dates, and the account email - plus a gauges widget that
draws the value and a label inside the ring, with a title that supports variables.

## What it provides

| Variable | Type | Meaning |
| --- | --- | --- |
| `windsurf_daily_used` | Numeric (%) | Percent of the daily quota used |
| `windsurf_weekly_used` | Numeric (%) | Percent of the weekly quota used |
| `windsurf_extra_balance` | Numeric ($) | Remaining extra usage balance |
| `windsurf_plan` | Text | Plan name |
| `windsurf_last_update` | Text | When the figures were last refreshed |
| `windsurf_plan_reset` | Text | When the plan resets, local time |
| `windsurf_plan_days_left` | Numeric (days) | Whole days until the plan resets, ceiling |
| `windsurf_daily_reset` | Text | When the daily quota resets, local time |
| `windsurf_weekly_reset` | Text | When the weekly quota resets, local time |
| `windsurf_email` | Text | The signed-in account's email address |

Actions: `refresh-usage` polls Windsurf immediately instead of waiting for the next scheduled
refresh. It is also the widget's default short-press action. The poll interval is configurable in
the plugin's settings (30 to 3600 seconds, default 300); completing that one-step settings flow is
what enables the integration.

Widget: **Windsurf gauges** - up to 8 gauges per tile in the built-in Gauges layout (ring or
three-quarter arc), each with the built-in's per-gauge settings (variable, label, icon, colours,
min/max, colour thresholds with the built-in's green/yellow/orange/red defaults, warn above/below)
extended with this plugin's fields: the value and label drawn inside the ring, independent font
sizes, a template-aware value suffix, and a title that supports `{{ vars.name }}` references. A
fresh widget comes pre-configured with the daily and weekly quota gauges and a title showing the
extra usage balance.

## Where the data comes from

The plugin polls Codeium's `GetUserStatus` endpoint with the API key of the signed-in account, and
the quota figures are account-wide: usage from the Windsurf JetBrains plugins, the Windsurf IDE,
Devin sessions and Cascade all draw on the same daily and weekly quota, so the gauges reflect all
of it.

The API key is read automatically from whichever of these is installed and signed in:

- the Windsurf plugin for JetBrains IDEs (`%APPDATA%\JetBrains\*\options\other.xml`)
- Devin Desktop (`%APPDATA%\Devin\User\globalStorage\state.vscdb`)
- the Windsurf IDE (`%APPDATA%\Windsurf\User\globalStorage\state.vscdb`)

Every candidate key is tried until one authenticates, so an account signed in on more than one
client still resolves. Logging out and back in inside any of them rotates the key; the plugin
picks the new one up on its next poll. There is nothing to configure.

## Build

Requires the .NET 10 SDK and the Macro Deck plugin CLI:

```bash
dotnet tool install --global MacroDeck.Plugin.Cli --version 3.0.0-beta.17
dotnet build
dotnet test
macrodeck-plugin test --project src/WindsurfGauges --report markdown --output conformance.md
macrodeck-plugin build --source src/WindsurfGauges --rid win-x64 --output ./artifacts
```

The Macro Deck packages are pinned to `3.0.0-beta.17` in `Directory.Packages.props`; no stable 3.0
SDK exists yet, and SemVer orders `beta` before `preview`, so a floating version would resolve to
the older `3.0.0-preview.10`.

## Prerequisites

A Windsurf account, signed in on the same machine in at least one of: Devin Desktop, a Windsurf
plugin for a JetBrains IDE, or the Windsurf IDE. The plugin reads that client's stored API key; there
is nothing to configure.

The plugin is Windows-only.

## Install

Install from the Macro Deck Store, or build the artifact and install it from file: Macro Deck,
Plugins, install from file, then pick
`artifacts/com.m-hume.windsurf-gauges-1.5.9-win-x64.macroDeckPlugin`.

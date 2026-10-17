# Windsurf Gauges

A Macro Deck 3 plugin that shows Windsurf (JetBrains plugin) usage on the deck: daily and weekly
quota, the extra usage balance and the plan, plus a gauge widget that draws the value and a label
inside the arc and a title that supports variables.

## What it provides

| Variable | Type | Meaning |
| --- | --- | --- |
| `windsurf_daily_used` | Numeric (%) | Percent of the daily quota used |
| `windsurf_weekly_used` | Numeric (%) | Percent of the weekly quota used |
| `windsurf_extra_balance` | Numeric ($) | Remaining extra usage balance |
| `windsurf_plan` | Text | Plan name |
| `windsurf_last_update` | Text | When the figures were last refreshed |

Actions: `refresh-usage` polls Windsurf immediately instead of waiting for the next scheduled
refresh. The poll interval is configurable in the plugin's settings (30 to 3600 seconds, default
300); completing that one-step settings flow is what enables the integration.

Widget: **Windsurf gauge** - bind any numeric variable (the plugin's own or another provider's),
set a maximum, an optional unit, a label drawn inside the arc under the value, and a title that
supports `{{ vars.name }}` references (for example `Windsurf {{ vars.windsurf_plan }}`). The value
gets an optional suffix (also template-aware, for example `%`), and the value's and the label's
font sizes are configured independently, each as a percentage of the tile height.

## Where the data comes from

The plugin polls Codeium's `GetUserStatus` endpoint with the API key the JetBrains Windsurf plugin
stores in each IDE's `options/other.xml`, so on a machine with the IDE plugin signed in it works
with no configuration. Logging out and back in inside the IDE rotates that key; the plugin picks
the new one up on its next poll.

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

## Install locally

Macro Deck, Plugins, install from file, then pick
`artifacts/com.m-hume.windsurf-gauges-1.4.3-win-x64.macroDeckPlugin`. The package is unsigned, which
is fine for local use; publishing to the Store would additionally need a repository and signing.

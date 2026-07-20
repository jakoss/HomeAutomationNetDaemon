# Project template for binary deploy
This is the project template for binary deploy. This allows you to build a binary package and deploy it to NetDaemon.

This is generated using NetDaemon runtime version 5 and .NET 10.

## Getting started
Please see [netdaemon.xyz](https://netdaemon.xyz/docs) more information about getting starting developing apps for Home Assistant using NetDaemon.

Please add code generation features in `src/HomeAutomationNetDaemon/Program.cs` when using code generation features by removing comments!

## Use the code generator
See https://netdaemon.xyz/docs/hass_model/hass_model_codegen

## Work-from-home calendar availability

Configure every ICS feed that should affect the office availability lights in
`src/HomeAutomationNetDaemon/appsettings.json`. Events from all configured calendars are combined: any busy
event results in **Busy**, otherwise any tentative event results in **BusyTentative**,
and a time without events is **Free**.

```json
"WorkFromHome": {
  "CalendarUrls": [
    "https://calendar.example.com/work.ics",
    "https://calendar.example.com/personal.ics"
  ]
}
```

## Issues

- If you have issues or suggestions of improvements to this template, please [add an issue](https://github.com/net-daemon/netdaemon-app-template)
- If you have issues or suggestions of improvements to NetDaemon, please [add an issue](https://github.com/net-daemon/netdaemon/issues)

## Discuss the NetDaemon

Please [join the Discord server](https://discord.gg/K3xwfcX) to get support or if you want to contribute and help others.

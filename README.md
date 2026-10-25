# VikingFactions

Living factions for Valheim, built on **VikingAI**: friendly wayfarers who visit well-kept halls, and raiders who build physical camps, steal from your chests, and run for home.

---

## Features

- **Wayfarers & the Mead Hall**: a lit hearth under a roof with enough comfort draws travelers from the wilds. They warm by the fire, sit, eat from your communal chests, and sleep in free beds. Neglect the hall and they move on.
- **Raiders with a home**: base raids can spawn as a marching warband backed by a real camp (Exile Marauders, Draugr Reavers, Fuling Plunderers). Raiders siphon chest contents, slow down under their load, and flee to camp. Catch them on the road to recover everything; otherwise find the camp (marked on your map) and take it back, plus the Warlord's bounty.
- **Opt-out everywhere**: every system has its own server-authoritative config toggle.
- **Extensible**: other mods can register new raider factions and camp layouts via `RaiderFactionRegistry.RegisterFaction`.

---

## Installation

Install with **r2modman** or the **Thunderstore Mod Manager**. Requires **BepInExPack Valheim**, **Jötunn**, and **VikingAI**.

---

## Configuration

Generated at `BepInEx/config/com.digitalvikings.vikingfactions.cfg`. Edit in-game with the BepInEx Configuration Manager (**F1**). Notable options: `EnableWayfarers`, `MinHospitalityComfort`, `EnableMarauderHeists`, `InterceptVanillaRaids`, `MaxStacksPerRaider`, `RaiderCarrySpeedMultiplier`, `RaidersRespectWards`.

Developer console (cheat mode): `vf spawn_wayfarer`, `vf trigger_raid [marauders|draugr|fulings]`, `vf status`, `vf clear_camps`.

---

## Compatibility

Comfort is read through the vanilla comfort calculation, so comfort-modifying mods (e.g. VikingClimate) apply automatically. Set `InterceptVanillaRaids` to false to keep vanilla raids untouched.

---

## Credits

Developed by **DigitalVikings**. Built with **BepInEx**, **HarmonyX**, and **Jötunn**.

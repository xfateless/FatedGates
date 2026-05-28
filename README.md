# FatedGates

FatedGates is a server-side V Rising mod for creating and managing custom waygates, public waygates, reveal zones, portals, and map icons.

This mod is an extended fork of **KindredPortals**. It includes the original functionality of KindredPortals, while adding public waygate support, persistent reveal zones, save-scoped configuration, and command shorthands for faster admin workflows.

## Installation

Install FatedGates as a server-side BepInEx plugin.

1. Install BepInEx for V Rising Dedicated Server.
2. Place `FatedGates.dll` in `BepInEx/plugins/FatedGates/`.
3. Restart the server.
4. Check `BepInEx/LogOutput.log` for `FatedGates initialized`.

## Features

FatedGates allows server admins to:

- Create custom discoverable waygates.
- Create **public waygates** that are automatically unlocked for all connected and future players.
- Persist public waygates across server restarts.
- Reveal areas of the map for all players.
- Persist reveal zones across server restarts.
- Scope public waygate and reveal-zone config by save name.
- Spawn and manage portals from the original KindredPortals functionality.
- Spawn and manage map icons from the original KindredPortals functionality.
- Use short command aliases for faster administration.

## Why Public Reveal Zones Exist

V Rising treats waypoint unlocking and map exploration as related but separate systems.

When a waygate is unlocked for a player, that waygate can become available on the **respawn map**. However, that does not necessarily make it visible or usable from the standard teleport map when interacting with another waygate.

For a waygate to appear on the normal teleport map, the area around that waygate must also be considered explored/revealed for that player.

That is why reveal-zone functionality was added: it allows public waygates to appear on the standard teleport map before players have naturally explored that area.

Note: reveal zones are applied immediately for future logins. Players who are already online may need to relog before newly revealed areas appear on their client.

```txt
.wg cp "Market"
.rv closest 2
```

This creates a public waygate, then reveals a small area around the closest waygate so players can see and use it on the normal teleport map.

## Map Reveal Accuracy

FatedGates reveals map areas by writing to V Rising's packed map reveal buffer. The reveal buffer uses bitmap-style coordinates, while player and waygate positions use world coordinates.

To make commands like `.rv here` and `.rv closest` work, world coordinates are converted into reveal bitmap coordinates using an approximate mapping equation.

This mapping is functional and has been tested across many locations, but it is not perfectly exact everywhere on the map. In practice, the accuracy of a reveal is good enough for a radius of `1` centered on a waygate to contain the waygate, revealing it to all users.

Expected accuracy:

* Radius `1`: A very tight reveal. Small conversion errors may make it slightly off-center, but the target will still remain inside the revealed area.
* Radius `3+`: useful if you want a larger visible patch.
* Radius `50-100`: can reveal whole regions
* Radius `200-400`: large enough to reveal most of the entire map

The remaining inaccuracy appears to come from the map projection/reveal bitmap mapping rather than from player or waygate positioning. If someone finds a better conversion equation or a more direct way to translate world coordinates to reveal bitmap coordinates, pull requests are welcome.

You can also reach out to me on the **V Rising Modding Community Discord**.

## Configuration

FatedGates stores persistent public waygate and reveal-zone data in save-scoped config folders.

Example:

```txt
BepInEx/config/FatedGates/<SaveName>/PublicWaygates.json
BepInEx/config/FatedGates/<SaveName>/PublicRevealZones.json
```

This means multiple world saves can share the same server install without mixing public waygate or reveal-zone data.

For example:

```txt
BepInEx/config/FatedGates/world1/PublicWaygates.json
BepInEx/config/FatedGates/world2/PublicWaygates.json
```

## Recommended Public Waygate Workflow

### Create a public market waygate

Stand where you want the waygate and run:

```txt
.wg cp "Market"
```

Then reveal the area around the closest waygate:

```txt
.rv closest 2
```

Players should now be able to see and use the waygate from the normal teleport map once the reveal zone has been applied.

### Create a normal discoverable waygate

```txt
.wg c
```

This creates a custom waygate that behaves like a normal discoverable waygate.

### List public waygates

```txt
.wg lp
```

### Delete a public waygate by index

```txt
.wg d 1
```

### Delete the nearest spawned waygate

```txt
.wg d
```

When used without an index, the destroy command targets the closest spawned waygate near the admin.

## Commands

FatedGates uses command groups. Each command has a long form and, where useful, a shorthand.

---

# Waygate Commands

Command group:

```txt
.waygate
```

Shorthand:

```txt
.wg
```

These commands manage spawned waygates and public waygates.

## `.waygate create`

Shorthand:

```txt
.wg c
```

Usage:

```txt
.waygate create [waygatePrefab]
.wg c [waygatePrefab]
```

Creates a normal discoverable waygate at the admin's current location.

If no prefab is provided, the default waygate prefab is used.

Examples:

```txt
.wg c
```

Creates a normal discoverable waygate.

## `.waygate createpublic`

Shorthand:

```txt
.wg cp
```

Usage:

```txt
.waygate createpublic [name] [waygatePrefab]
.wg cp [name] [waygatePrefab]
```

Creates a public waygate at the admin's current location.

Public waygates are:

* Saved to `PublicWaygates.json`.
* Automatically unlocked for connected players.
* Automatically unlocked for future players when they connect.
* Resolved again after server restart.

Defaults:

```txt
name = "Public Waygate"
waygatePrefab = default waygate prefab
```

Examples:

```txt
.wg cp
.wg cp "Market"
.wg cp "Dungeon Entrance"
```

Recommended follow-up:

```txt
.rv closest 2
```

Public waygate unlock alone makes the waygate usable from the respawn map, but the surrounding map area must be revealed for it to reliably appear on the standard teleport map.

## `.waygate teleportclosest`

Shorthand:

```txt
.wg tc
```

Usage:

```txt
.waygate teleportclosest
.wg tc
```

Teleports the admin to the closest spawned waygate.

Example:

```txt
.wg tc
```

## `.waygate destroy`

Shorthand:

```txt
.wg d
```

Usage:

```txt
.waygate destroy [publicWaygateIndex]
.wg d [publicWaygateIndex]
```

Destroys a spawned waygate.

If no index is provided, this destroys the closest spawned waygate near the admin.

If an index is provided, it destroys the saved public waygate with that index from `.waygate listpublic`.

Examples:

```txt
.wg d
```

Destroys the closest spawned waygate.

```txt
.wg d 2
```

Destroys public waygate #2 from the public waygate list.

If the destroyed waygate is a saved public waygate, it is also removed from `PublicWaygates.json`.

## `.waygate listpublic`

Shorthand:

```txt
.wg lp
```

Usage:

```txt
.waygate listpublic
.wg lp
```

Lists saved public waygates.

Example output:

```txt
Saved public waygates (2):
1. Market at X=-1783.07, Y=0.00, Z=-1226.30
2. Arena at X=-1436.18, Y=5.00, Z=-708.79
```

Use these indexes with:

```txt
.wg d <index>
```

---

# Reveal Commands

Command group:

```txt
.reveal
```

Recommended shorthand:

```txt
.rv
```

Reveal commands manage public reveal zones. These zones mark small areas of the map as explored for players.

Reveal zones are saved to:

```txt
BepInEx/config/FatedGates/<SaveName>/PublicRevealZones.json
```

## `.reveal zone`

Shorthand:

```txt
.rv z
```

Usage:

```txt
.reveal zone <x> <y> [radius]
.rv z <x> <y> [radius]
```

Adds a persistent public reveal zone by reveal bitmap coordinates.

Defaults:

```txt
radius = 1
```

Examples:

```txt
.rv z 92 98
.rv z 92 98 2
```

This command expects reveal bitmap coordinates, not world coordinates.

## `.reveal here`

Shorthand:

```txt
.rv h
```

Usage:

```txt
.reveal here [radius]
.rv h [radius]
```

Adds a persistent public reveal zone centered on the admin's current world position.

FatedGates converts the admin's world position into reveal bitmap coordinates.

Defaults:

```txt
radius = 1
```

Examples:

```txt
.rv h
.rv h 2
```

## `.reveal closest`

Shorthand:

```txt
.rv c
```

Usage:

```txt
.reveal closest [radius]
.rv c [radius]
```

Adds a persistent public reveal zone centered on the closest spawned waygate.

This is the recommended reveal command to use after creating a public waygate.

Defaults:

```txt
radius = 1
```

Examples:

```txt
.wg cp "Market"
.rv c 2
```

If this command fails, check:

```txt
BepInEx/LogOutput.log
```

## `.reveal list`

Shorthand:

```txt
.rv l
```

Usage:

```txt
.reveal list
.rv l
```

Lists saved public reveal zones.

Example output:

```txt
Saved public reveal zones (2):
1. Reveal Zone 92,98 at bitmap (92, 98), radius 1
2. Reveal Zone 129,136 at bitmap (129, 136), radius 2
```

## `.reveal remove`

Shorthand:

```txt
.rv rm
```

Usage:

```txt
.reveal remove [index]
.rv rm [index]
```

Removes a saved public reveal zone.

If an index is provided, removes that reveal zone from the list.

If no index is provided, removes the closest reveal zone to the admin's current position.

Examples:

```txt
.rv rm 1
```

Removes reveal zone #1.

```txt
.rv rm
```

Removes the closest reveal zone, if one is close enough.

Removing a reveal zone does not hide the map again for players who already had the area revealed. FatedGates does not attempt to re-fog the map because it cannot safely distinguish areas revealed by this mod from areas a player explored normally.

## `.reveal apply`

Shorthand:

```txt
.rv a
```

Usage:

```txt
.reveal apply
.rv a
```

Applies all saved public reveal zones to currently connected users.

This is mainly useful after manually editing `PublicRevealZones.json`.

## `.reveal pos`

Shorthand:

```txt
.rv p
```

Usage:

```txt
.reveal pos
.rv p
```

Shows the admin's current world position and reveal bitmap position.

Example:

```txt
World position: X=-1783.07, Y=0.00, Z=-1226.30
Reveal bitmap position: X=92, Y=98
Use: .rv zone 92 98 1
```

World coordinates and reveal bitmap coordinates are not the same. Use `.rv here` or `.rv closest` for normal admin workflows.

---

# Portal Commands

Command group:

```txt
.portal
```

Shorthand:

```txt
.port
```

Portal commands are inherited from the original KindredPortals functionality. They let admins create linked portal pairs, teleport to spawned portals, and destroy portal connections.

## `.portal start`

Shorthand:

```txt
.port s
```

Usage:

```txt
.portal start [mapIcon]
.port s [mapIcon]
```

Starts creating a portal connection at the admin's current location.

This creates the first end of a portal pair. After running this command, move to the location where the other end should be placed and run `.portal end`.

Defaults:

```txt
mapIcon = no map icon
```

Examples:

```txt
.port s
.port s SomeMapIconName
```

## `.portal end`

Shorthand:

```txt
.port e
```

Usage:

```txt
.portal end [mapIcon]
.port e [mapIcon]
```

Creates the second end of the portal connection and links it to the portal started with `.portal start`.

Defaults:

```txt
mapIcon = no map icon
```

Examples:

```txt
.port s
# move to the destination
.port e
```

With map icons:

```txt
.port s SomeMapIconName
# move to the destination
.port e SomeMapIconName
```

## `.portal teleportclosest`

Shorthand:

```txt
.port tc
```

Usage:

```txt
.portal teleportclosest
.port tc
```

Teleports the admin to the closest spawned portal.

Example:

```txt
.port tc
```

## `.portal destroy`

Shorthand:

```txt
.port d
```

Usage:

```txt
.portal destroy
.port d
```

Destroys the spawned portal the admin is standing near, along with its linked connection.

After destroying a portal, reconnect to the server to no longer see it.

Example:

```txt
.port d
```

---

# Map Icon Commands

Command group:

```txt
.mapicon
```

Shorthand:

```txt
.mi
```

Map icon commands are inherited from the original KindredPortals functionality. They let admins create, destroy, and list available map icons.

## `.mapicon create`

Shorthand:

```txt
.mi c
```

Usage:

```txt
.mapicon create <mapIcon>
.mi c <mapIcon>
```

Creates a map icon at the admin's current location.

This command requires a map icon prefab name. Use `.mapicon list` / `.mi l` to see available map icons.

Examples:

```txt
.mi c Waypoint
.mi c Castle
```

The exact icon names depend on the available map icon prefabs listed by `.mi l`.

## `.mapicon destroy`

Shorthand:

```txt
.mi d
```

Usage:

```txt
.mapicon destroy
.mi d
```

Destroys the map icon the admin is standing on.

Example:

```txt
.mi d
```

## `.mapicon list`

Shorthand:

```txt
.mi l
```

Usage:

```txt
.mapicon list
.mi l
```

Lists available map icon prefabs that can be used with `.mapicon create`.

The output includes the trimmed icon prefab name and, when available, its localized in-game label.

Example workflow:

```txt
.mi l
.mi c SomeIconName
```

---

# Admin Notes

## Public waygates vs. normal waygates

Normal waygates must be discovered normally by players.

Public waygates are automatically unlocked for players, but map reveal still matters for the standard teleport map.

For public hubs, markets, dungeons, arenas, or event areas, use:

```txt
.wg cp "Name"
.rv c 2
```

## Reveal radius recommendations

Use:

```txt
.rv c 1
```

for a very tight reveal.

Use:

```txt
.rv c 2
```

for a safer public-server reveal.

Use larger values only if you intentionally want a larger patch of the map revealed.

## Logs

FatedGates writes logs to:

```txt
BepInEx/LogOutput.log
```

Check this file if a command says to check the log file.

---

# Credits

FatedGates is an extended fork of **KindredPortals**.

Thanks to the original KindredPortals author for the base functionality this mod builds on, and to the V Rising Modding Community for documentation, examples, and tooling around V Rising server modding.

---

# Contributing

Pull requests are welcome.

I am especially interested in improvements to the world-coordinate-to-reveal-bitmap conversion. The current formula is accurate enough for normal use, even with reveal radius of `1`, but it is not perfect everywhere on the map.

If you find a better equation, a direct mapping method, or a more reliable way to reveal the exact map area around a waygate, feel free to open a pull request or contact me through the V Rising Modding Community Discord.


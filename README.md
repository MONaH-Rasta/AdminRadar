# AdminRadar

Oxide plugin for Rust. Allows admins to have a radar to help detect cheaters and other entities.

**Admin Radar** allows admins to have a radar to help detect cheaters by drawing on your screen the locations of players with their current health and distance from you, among many other entity types listed below. A quick toggle GUI is available to quickly change your filters.

## Permissions

- `adminradar.allowed` -- Allows player to use `/radar` commands - This temporarily grants the admin flag. **USE AT YOUR OWN RISK.**
- `adminradar.auto` -- Automatically turn on radar when you connect and wake up.
- **Also requires**
  - adminradar.allowed permission,
  - be a developer,
  - be in the authorized list,
  - of the required authlevel,
  - OR
  - be able to use radar with FauxAdmin.
- `adminradar.list` - allows players with this permission to use `/radar list`
- `adminradar.bypass` -- Allows users to not be drawn on radar
- `adminradar.bypass.override` -- Override the bypass and see these users also. Useful for server owners.
- **Example usage**:
- oxide.grant group admin adminradar.bypass
- This will make all admins invisible to each other on the radar.
- oxide.grant user nivex adminradar.bypass.override
- In this case, nivex is the Server Owner, and an admin. This will make nivex invisible to all admins on the radar, but allow him to see all users with the `adminradar.bypass` permission.

The permission `adminradar.allowed` by itself will temporarily grant the admin flag to users to allow access to `ddraw`  due to limitations implemented by Facepunch.

Compatibility for *FauxAdmin* users. These users ***MUST*** have `adminradar.allowed` permission. If using the restricted access list then they must also be added to it.

## Chat Commands

- `/radar optional: filter` -- Toggle radar on your self with optional filter
- `/radar online` -- Toggle showing online players boxes only when using the box filter.
- `/radar help` -- Show a list of commands
- `/radar f` -- Use your previous filters
- `/radar ui` -- Turn quick toggle UI on/off
- `/radar tracker` -- **Functionality removed**
- `/radar vision` -- Toggle showing what players are looking at.
- `/radar ext` -- Toggle showing extended information for players (some attachments are shortened. e.g 4x Zoom Scope = 4x, Simple Handmade Sight = Sight)
- `/radar drops` -- Show all dropped items within 150m, including bear traps and landmines
- `/radar list` -- Show all active radar users
- `/radar setanchormin 0.667 0.020` - adjust the min and refresh UI with changes
- `/radar setanchormax 0.810 0.148` - adjust the max and refresh UI with changes
- `/radar anchors_save` - save changes to config
- `/radar anchors_reset` - reset anchors to defaults in config and in game
- `/radar buildings` - draws all buildings on the server that have no TC. Increase Drawing Distances > Tool Cupboard if you want to see buildings with a TC from a further distance using the TC filter.

## Radar Filters

Players, Bradley APC and Helicopters are shown by default. **You may show filters by default that are listed under Additional Tracking by doing the following: 1) disable the UI button for said filter, 2) enable Additional Tracking for said filter.**

- **Bag** -- Show sleeping bags
- **Box** -- Show storage containers, and supply drops
- **Col** -- Show collectibles
- **Dead** -- Show dead players
- **Loot** -- Show loot containers, dropped loot, trash, and backpacks
- **NPC** -- Show animals, Human NPCs, and Scientists
- **Ore** -- Show resource nodes for stone, metal, sulfur
- **Sleeper** -- Show sleeping players
- **Stash** -- Show stashes
- **TC** -- Show tool cupboards
- **Turret** -- Show turrets
- **All** -- Show all of the filters above
- **ht** -- Show hunger/thirst

These are compatible with spectating players.

You may disable tracking of a specific filter entirely by disabling the Show Button for said filter, and if applicable, Additional Tracking.

Filters will automatically disable their functionality entirely if they throw an exception. This exception will be printed to the server console only once.

If using the Restrict Access to Steam64 IDs option then your FauxAdmin user must also be in this list or they will not be allowed to use the radar command.

Dropped item tracker will show dropped items in the world with the exception of items in the config (default exceptions: bottle, planner, rock, torch, can., arrow.)

## Developer API

Check if radar is enabled by player ID string:

```csharp
bool isRadar = AdminRadar.Call<bool>("IsRadar", player.UserIDString);
```

Hook for when radar is activated

```csharp
void OnRadarActivated(BasePlayer player)
```

Hook for when radar is deactivated

```csharp
void OnRadarDeactivated(BasePlayer player)
```

## Configuration

### Map Markers

- REMOVED

### Settings

- Default Distance -> 500.0 (meters)
- Default Refresh Time -> 5.0 (seconds)
- Dropped Item Exceptions (list of items to not show when using `/radar drops`)
- Latency Cap In Milliseconds (0 = no cap) -> 1000.0
- Objects Drawn Limit (0 = unlimited) -> 250
- Restrict Access To Steam64 IDs -> not configured (only users in this list will have access if configured, otherwise auth level will be required)
- Restrict Access To Auth Level -> Auth Level 1
- Chat Command -> radar (Alternate command to use /radar)
- Barebones Performance Mode > false (disable all non-player related functionality)
- *Use Bypass Permission* >  **This setting has been REMOVED**
- User Interface Enabled > true
- Deactivate Radar After X Seconds Inactive -> 300 seconds
- Deactivate Radar After X Minutes -> 0 minutes
- Player Name Text Size (14)
- Player Information Text Size (14)
- Entity Name Text Size (14)
- Entity Information Text Size (14)

### Options

- Show Barrels And Crate Contents -> false
- Show Airdrop Contents -> false
- Show Stash Contents -> false
- Draw Empty Containers -> true
- Show Resource Amounts -> true
- Only Show NPCPlayers At World View -> false (you will only see NPC players below the world when you are below the world, likewise for above the world)
- Show X Items In Backpacks [0 = amount only] -> 3
- Show X Items On Corpses [0 = amount only] -> 0
- Show Authed Count On Cupboards -> true
- Show Bag Count On Cupboards -> true

### Additional Tracking

- Boats -> false
- Bradley APC -> true
- Cars -> false
- CargoShips -> false
- Helicopters -> true
- Helicopter Rotor Health -> false
- MiniCopters -> false
- CH47 -> false
- Ridable Horses -> false
- RHIB -> false

### Drawing Methods

- Draw Arrows On Players -> false
- Draw Boxes -> false
- Draw Text -> true

### Drawing Distances (in meters)

- Airdrop Crates -> 400
- Animals -> 200
- Boats -> 150
- Cars -> 500
- Sleeping Bags -> 250
- Boxes -> 100
- Collectibles -> 100
- Player Corpses -> 200
- Players -> 500
- Loot Containers -> 150
- MiniCopter -> 150
- NPC Players -> 300
- Ridable Horse -> 250
- Resources (Ore) -> 200
- Stashes -> 200
- Tool Cupboards -> 100
- Tool Cupboard Arrows -> 250
- Turrets -> 100
- Vending Machines -> 250
- Radar Drops Command (150 meters) - /radar drops

### Group Limit

- Dead Color -> #ff0000
- Draw Distant Players With X -> true
- Group Color Basic -> #ffff00
- Group Colors (list of colors per group)
- Height Offset [0.0 = disabled] -> 0.0
- Limit -> 4
- Range -> 50.0
- User Group Colors Configuration -> true

### Player Movement Tracker

- Enabled -> false
- Update Tracker Every X Seconds -> 1
- Positions Expire After X Seconds -> 600
- Max Reporting Distance -> 200 meters
- Draw Time -> 60 seconds
- Overlap Reduction Distance -> 5 meters

### Color Hex Codes

- Color hex codes must start with # symbol. Any code not starting with # symbol will be considered a colored word: red, orange, white, etc.

### GUI

- Anchor Min > 0.667 0.020
- Anchor Max > 0.810 0.148
- Color Off > 0.29 0.49 0.69 0.5
- Color On > 0.69 0.49 0.29 0.5

### GUI -> Show Button ->

- Bags -> true
- Boats -> false
- Cars -> false
- CargoShips -> false
- CH47 -> false
- Collectibles -> true
- Dead -> false
- Heli -> false
- Loot -> true
- MiniCopter -> false
- NPC -> true
- Ore -> true
- Ridable Horses -> false
- RigidHullInflatableBoats -> false
- Sleepers -> true
- Stash -> true
- TC -> true
- TC Arrow -> true
- Turrets -> true

### Voice Detection

- Enabled -> true
- Timeout After X Seconds -> 3
- Detection Radius -> 25.0

## Credits

- **Austinv900**, for passing the plugin on to me
- **Reneb**, for the original version of Admin Radar
- **Speedy2M**, for being an amazing co-developer and logo artist
- **nivex**, for donating his custom Admin Radar for us to study

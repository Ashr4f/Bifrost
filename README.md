# Bifrost Portals

Travel between portals by picking the destination on the map. No tag pairing, no portal hub.

Walk into a portal: the map opens with a marker on every portal of the world, click one, travel.
Pressing E on the portal does the same, Shift E renames it like vanilla. On the large map, P
shows or hides every portal at any time.

## Design

Two mods inspired Bifrost and both had a flaw it avoids:

- Destination picking on the map. While picking, other icons are hidden visually frame by
  frame, without ever touching the pins of other mods. Map mods keep restamping their pins
  as much as they want, there is nothing to fight over.
- Fast teleportation, but only the artificial wait is removed. The screen stays faded until the
  destination area is fully loaded, so the half loaded world is never visible.

## Configuration

| Setting | Default | Description |
| --- | --- | --- |
| Lock Configuration | true | Server config is enforced for everyone. |
| Enabled | true | Master switch, off = vanilla portals. |
| Quick Travel | true | Instant arrival when the area is ready, faded screen while it loads. |
| Ignore Teleport Restrictions | false | If on, non teleportable items no longer block travel. |
| Portal Prefabs | portal_wood, portal_stone | Prefabs treated as portals. |
| Open On Enter | true | Walking into a portal opens the destination map. |
| Hide Other Pins | true | Only portal pins are visible while picking a destination. |
| Map Toggle Key | P | Shows or hides every portal on the large map. |
| Show World While Loading | false | No black screen during loading, shows the half loaded world. |
| Skip Loading Objects | false | Arrive once terrain is ready. Warning: can land you on a lower floor. |
| Skip Loading Area | false | Instant arrival, the world loads around you. |
| Extra Load Wait | 1.5 | Seconds waited for cold destinations only, prevents arriving in an empty world. Loaded destinations stay instant. |

The portal list comes from the server, so install Bifrost on the server too.

## Install

BepInEx plugin: `BepInEx/plugins/Bifrost.dll`, or via r2modman.

## Credits

Built for the "Les Fous du Bus" server.

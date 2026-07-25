# Bifrost Portals

Travel between portals by picking the destination on the map. No tag pairing, no portal hub.

Walk into a portal: the map opens with a marker on every portal of the world, click one, travel.
Pressing E on the portal does the same, holding E renames it like vanilla.

## Design

Two mods inspired Bifrost and both had a flaw it avoids:

- Destination picking on the map, but Bifrost draws its own marker layer instead of hiding the
  pins of other mods. Map mods keep their icons, Bifrost keeps its markers, nobody fights.
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

The portal list comes from the server, so install Bifrost on the server too.

## Install

BepInEx plugin: `BepInEx/plugins/Bifrost.dll`, or via r2modman.

## Credits

Built for the "Les Fous du Bus" server.

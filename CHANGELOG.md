# Changelog

## 1.0.9

- Arrival now has a real completion condition instead of a delay. At departure the client asks the server how many objects the destination sector holds, then waits until it has received them, with a small tolerance because the last objects are sent by priority and can lag behind. On a host or in single player the count is read directly, without a round trip.
- Without an answer, because the server does not run Bifrost or has not replied yet, arrival watches the object count of the sector and completes once it stops moving. A sector that receives nothing at all is treated as empty after a short grace.
- This replaces the old behaviour, which trusted the game's area check. That check answers yes as soon as everything the client knows about is built, so on a sector never visited it answered yes on an empty world and the first jump landed in the void.
- Extra Load Wait now defaults to zero. It is no longer the mechanism, only a margin for a slow connection.

## 1.0.8

- Faster arrival on an open world destination. The fixed floor before the first readiness check drops from one second to a third, and disappears entirely as soon as the destination zone is loaded. Interiors were already instant because they live in a zone that is loaded before the jump.
- Extra Load Wait now defaults to half a second instead of one and a half.
- The loading veil is found again on current game versions. It was looked up under a field name that no longer exists, so the view was not held at all and the log carried a warning at every start. The lookup now tries several names, falls back to a scan, and accepts a canvas group as well as a plain object.

## 1.0.7

- Fixed an endless portal loading screen. Holding back the teleport itself deadlocked the wait: the player has to move for the destination to start loading. The teleport now always completes and only the view is held, with a 12 second hard cap.
- Pings, other players, death and bed markers stay visible while choosing a destination. New setting Always Visible Pins lists the pin types that are never hidden.

## 1.0.6

- Arrival is now blocked at the source instead of only holding the fade, and waits a settle delay after the destination reports ready. Terrain is generated locally and reports ready almost instantly while server objects are still arriving, which is what caused arrivals in an empty world.
- New setting Extra Load Wait (1.5s), applied only to destinations that were not loaded when leaving. Already loaded destinations stay instant. Skip Loading Objects and Skip Loading Area bypass the gate as expected.

## 1.0.5

- Arrival waits for the destination zone to be confirmed loaded over several frames. Previously an unvisited area could be entered while it was still streaming.

## 1.0.4

- Browse pins are hidden while the picker is open so nothing stacks on the picker pins, and the toggle key is ignored during picking.

## 1.0.3

- Unnamed portals show no label instead of a placeholder.
- Portals keep their flames and glow: the connected state is forced since tag pairing is pointless with Bifrost.

## 1.0.2

- Destroyed pin elements no longer crash the hiding pass, which let icons reappear on zoom and fast reopen.

## 1.0.1

- Pins are inserted directly, bypassing AddPin patches from other mods.
- Pin hiding is alpha based and survives zooming and fast reopening.
- Optional loading skips (objects, area).
- Player is frozen while the picker is open, Shift E renames.

## 1.0.0

- Initial release: map based destination picking with an independent marker layer, quick travel that waits for the area to load behind the fade, server synced configuration.

# Changelog

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

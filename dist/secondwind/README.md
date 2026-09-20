# SecondWind

After you die, getting the Rested buff back takes a quarter of the usual fireside wait, for two minutes. Built for
Valheim 1.0.15.

> **Client-only. Install it on your own game; the server does not need it and never sees it.** There is no server
> sync and no version check, so it cannot stop you or anyone else joining a server, with or without the mod.
> (Hexium shows "Client-only" on every package; here it is true.)

Dying in a hard fight costs a corpse run, three foods and a wait by the fire. The wait is the dull part: you stand
under a roof next to a fire until "Resting" turns into "Rested", and if anything interrupts it (an enemy notices you,
you step away, you are wet) the count starts again from zero.

With SecondWind, while you have recently died, Rested arrives once a fraction of that wait has passed. Nothing else
changes: your food is still gone, your tombstone is still where you fell, and the Rested buff lasts as long as the
comfort of the place you rested in says it should. After the window closes the wait is vanilla again.

"Recently died" uses the game's own time-since-death clock, the same one that gives you the no-skill-drain grace
period. It starts when you die and keeps running while you respawn and walk back.

## Config (`BepInEx/config/bruceirons.SecondWind.cfg`)

- `Enabled` (On)
- `Resting time after a death (x)` (0.25): multiplier on the fireside wait. 1 = vanilla, 0 = Rested the moment you
  are resting.
- `Counts as just died for (seconds)` (120)

Each player sets their own values; the server has no say. The first time you rest, the log prints the game's
fireside wait and what it becomes.

## License

MIT. Written for the bruceirons LAN server.

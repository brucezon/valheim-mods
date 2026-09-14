# ServerBasedRanch

Server-only. Install on the dedicated server; players install nothing and vanilla clients work.
Built for Valheim 1.0.7.

Vanilla only advances taming and breeding while a player has the pen loaded, and the animals only eat
while their AI runs. The dedicated server never loads a zone by itself, so a pen at an outpost does
nothing until someone stands there. This mod ticks the unloaded pens on the server, directly on the
animals' saved state, with the vanilla rules and numbers:

- A hungry animal eats one item of the food it accepts lying within **Feed radius** of it. The dropped
  stack really goes down, so leave enough.
- A fed wild animal's taming timer runs down; at zero it becomes tame.
- A fed tamed animal gains love points on the vanilla schedule, conceives at the vanilla count, and
  gives birth after the vanilla pregnancy time. Births respect **Max animals per pen** (adults plus
  young within 10 m) and the partner rule.
- A newborn is created as a saved object with a spawn time equal to its birth, so the game's own
  grow-up timer raises it when someone loads the pen.

Zones a player has loaded are left to that player's client, which runs the real thing. Only animals
that are tamed or have been fed at least once are tracked, so wild herds cost nothing.

## Config (`BepInEx/config/bruceirons.ServerBasedRanch.cfg`)
- **Enabled** (true).
- **Tick interval (seconds)** (30): how often the server sweeps the world. Each tracked animal is
  advanced by the time since its last tick, in 10-second steps.
- **Feed radius (metres)** (8).
- **Catch-up limit (hours)** (12): the most one tick will advance an animal, so server downtime does
  not empty the pen or fill it in one go.
- **Unloaded speed (%)** (50): how fast an unloaded pen progresses compared to a loaded one. 100 = real
  time; 50 = an hour away counts as half an hour of eating, taming and breeding. Newborns still grow up
  on the game's own world clock.
- **Max animals per pen** (4, vanilla): server-side breeding cap.
- **Partner radius while unloaded (metres)** (10): a tame partner within this distance counts. Vanilla
  uses 3 m but re-checks while the animals wander; unloaded animals stand still.
- **Log activity** (true): one line per sweep that changed something, with why anything was blocked
  (loaded by a client, hungry without food in reach, no partner, pen full).
- **Log every sweep** (false): log that line even when idle, to confirm a pen is being ticked.

Compatible with BruceQoL's client-side passive taming: both stamp the same "last simulated" time on
the animal, so nothing is counted twice.

## License
MIT. Written for the bruceirons LAN server.

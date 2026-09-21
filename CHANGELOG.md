# Changelog

All notable changes to BuffBot. Versions follow [SemVer](https://semver.org).

## 0.1.0-beta.3 — 2026-09-21

### Fixed

- **Buffing works on OpenAC 0.1.13.** The client's list of self buffs now leaves out spells cast
  on someone else, which is every buff the bot gives a player. On 0.1.13 test builds the bot
  answered every request with "I haven't learned anything for buff yet" and cast nothing. It now
  builds its spell list from everything the character knows, on 0.1.11 and later alike.

## 0.1.0-beta.2 — 2026-09-17

First public build. Everything below is new.

### Buffing

- **Archetype keywords.** `heavy`, `light`, `finesse`, `2h`, `bow` (and `xbow`, `thrown`, `missile`),
  `void`, `mage`, `dual`, `tink`, `trades` and `prots` each resolve to a composed spell chain, built
  from layered profiles rather than one flat list.
- **A chain is cast in survival order:** trade and utility buffs first, then offence, then personal
  skills, then protections and regeneration — so the buffs a player can most afford to lose are the
  first to expire.
- **Tier fallback.** A cast refused for skill or components steps down a tier rather than failing
  the run, with a floor so it never quietly casts a useless tier.
- **Mana window.** The bot bounces its own mana between a low and a high mark instead of reacting to
  a single unaffordable spell.
- **Fizzles are treated as luck**, not failure: a fizzle retries, and only a run of them drops a
  tier.
- **Queueing.** One player served at a time, the rest in a queue that answers `line`, `position`,
  `cancel` and `remove`, and a `help` built from the live vocabulary.
- **Self-upkeep**, including re-buffing after death and topping up mana between requests.

### Components

- **Pea splitting.** When a reagent runs low the bot splits a matching pea, and it will split
  mid-chain to finish a cast that was refused for components.
- **The component report** understands that a caster holding a focus uses the scarab-only formula,
  so Prismatic Tapers are counted as needed.
- `contribute` tells a player what the bot is short of.

### Donations

- **Donations come through the secure trade window only.** The bot accepts a trade where everything
  staged is something it uses, resets and explains when it is not, and never stages anything of its
  own.
- **Direct gives are refused by the server**, not cleaned up afterwards: the bot never hands an item
  back and never drops one.
- **A contributors list** records who donated what, and survives restarts.

### Operating it

- **An in-game panel** with the current spell, mana, queue, counters and mute controls.
- **A web console** on loopback: live view, settings, component stocks and contributors.
- **Owner commands** from local chat only, for inventory, logout and diagnostics.
- **Runs headless**, with no window, as well as on the graphical client.

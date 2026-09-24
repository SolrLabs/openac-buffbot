# Changelog

All notable changes to BuffBot. Versions follow [SemVer](https://semver.org).

## 0.1.0-beta.6.1 — 2026-09-24

### Changed

- **BuffBot now needs OpenAC 0.1.17 or newer.** That release reorganised where the client keeps
  everything into one install folder. Earlier versions are no longer supported, because the floor
  beta.6 claimed had never actually been tested against.

### Documentation

- The README now states the MIT licence, and describes where BuffBot actually keeps your settings
  under the new install layout.

## 0.1.0-beta.6 — 2026-09-24

### Fixed

- **Your settings survive the move to OpenAC 0.1.17.** That release keeps plugin data somewhere
  new and does not carry the old data across, so updating the client used to lose your buff sets,
  your mutes and which characters had the bot switched on. BuffBot now brings its own over the
  first time it starts. If you already gave up and re-made a setting by hand, yours is kept — only
  the genuinely missing pieces are filled in, one at a time. Your old files are read and never
  changed, moved or deleted, so nothing is at risk either way.

## 0.1.0-beta.5.1 — 2026-09-23

### Fixed

- **Asking three different things no longer gets you muted.** The bot counted how often it had
  repeated *itself*, so three distinct questions that happened to share one answer looked like
  flooding and earned a ten-minute silence. It now counts what you actually said, five of the same
  thing in a row rather than three, and a first mute lasts a minute instead of ten — two on the
  next offence, four after that, all forgotten a day later.
- **A portal description you type is kept.** The setting only saved when the text box lost focus,
  and the console's own refresh would put the old text back before that happened, so an edit could
  vanish without a word. It now saves as you type, and says "Saved." when it has.
- **The bot gives up on someone who isn't there.** A player who logged off or walked out of range
  while waiting used to reach the front of the queue and have a whole chain started on them. They
  are told once, and the line moves on.

### Added

- **A pause between two people.** After someone is buffed, the bot waits a few seconds before
  taking the next person, so the one it just finished has room to open a trade. Five to ten
  seconds, seven by default, on the Settings tab as "Queue pause".

### Changed

- **Both portal ties share one block** on the Settings tab, a row each, with a wider description.

## 0.1.0-beta.5 — 2026-09-23

### Fixed

- **The game no longer stutters every ten seconds.** The bot re-reads its spell components on a
  ten-second beat, and on a graphical client that read ran inside the frame the client was drawing
  — so the game froze for a quarter of a second, over and over, for anyone playing in a window.
  The longer the bot's spell list, the longer the freeze. It now does that read in a hundredth of
  the time, and the beat passes unnoticed. Nothing about what the bot casts, or which tier it
  picks, has changed.

## 0.1.0-beta.4 — 2026-09-21

### Added

- **Portal summoning.** Tell the bot `where` (or `whereto`) to hear where its portals go, then
  `primary` or `secondary` to have it summon one. The bot casts the strongest Summon Portal it
  knows, announces it in local chat, and turns back the way it was facing afterwards.
- **Portal settings** on the web console's Settings tab: a description for each tie, stated to
  players exactly as you write it (so "Aerlinthe — dangerous drop" works), and which side of the
  bot the portal appears on: front, right, behind or left.
- **A portal request cuts into a buff.** Someone asking for a portal while the bot is buffing
  another player gets it between two spells; the buff then carries on where it left off, without
  recasting anything.
- **The web console link on a headless bot.** A bot running without a window prints its console
  link, token included, at the console when it comes up, and again whenever it changes.
  `/buffbot console` prints it on demand, on either host.

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

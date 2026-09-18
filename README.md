# BuffBot

BuffBot is a plugin for [OpenAC](https://github.com/eriknihlen/OpenAC), the open-source
Asheron's Call client that runs on Windows, Linux, and Mac. It targets OpenAC's plugin API version 1.

Ask it for buffs by tell and it casts them. A player sends it a keyword for how they
fight — `heavy`, `bow`, `mage`, `void` and the rest — and the bot works out the spell chain for
that style, queues them if somebody else is already being served, and casts until they are buffed
or it runs out of what it needs.

It is self-maintaining: it re-buffs itself, bounces its own mana, drops a spell tier when a cast is
refused, and splits component peas when a reagent runs low. Donations come through the secure trade
window, and it limits what can be given.

The in-game interface is light, but the plugin provides a local web URL where you can see analytics and configure the bot. This supports both headed and headless bot mode.

## Installing

You can find Buffbot listed in OpenAC's plugins listing inside the launcher. Just press 'install' to get started.


## BETA Status

The behaviour above works and is tested, but it has been exercised on one server by a handful
of characters, so expect rough edges and tell me what you find through an issue or submit a PR.

See [CHANGELOG.md](CHANGELOG.md) for what changed, and [CONTRIBUTING.md](CONTRIBUTING.md) before
opening a pull request.

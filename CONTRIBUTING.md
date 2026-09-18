# Contributing

## Licensing and ownership

BuffBot is published by SolrLabs LLC under the MIT license in [LICENSE](LICENSE).

**Inbound is outbound.** Anything you contribute is contributed under that same MIT license, and
you keep your own copyright in it. Nobody signs copyright over to anybody, and SolrLabs LLC claims
no ownership of your contribution beyond the rights the MIT license grants.

**Every commit must carry a `Signed-off-by` line.** That line is your statement that you have the
right to contribute the code, in the words of the [Developer Certificate of Origin](DCO.txt).
`git commit -s` adds it:

```
Signed-off-by: Your Name <your.email@example.com>
```

Use your real name and a working address. A pull request whose commits are not signed off will be
asked to amend them.

**Because the project is MIT and contributions stay under it, the license cannot be changed later
without every contributor's agreement.** That is deliberate: it keeps BuffBot open for everyone,
including its authors.

## What may not be contributed

- **No code from other Asheron's Call tools.** Decal plugins, VTank, DoThingsBot and MossTank are
  behavior references only: study what they do and re-express it in your own words. Copying their
  source into this repository, in any amount, is not acceptable.
- **No decompiled or disassembled game client code**, and no assets from the game.
- **Nothing you are not free to publish** — work owned by an employer, or covered by an agreement
  that conflicts with the MIT license.

If a behavior was learned by watching the game, say so in the commit message. That is fine and
welcome; a copied file is not.

## Agent-written code

**Agentic and AI-assisted contributions are welcome.** Much of this project was built that way.
There is no penalty for using Claude Code, Copilot, Cursor or anything else, and no requirement to
declare it. What matters is what lands in the diff.

The rules do not soften because an agent wrote the code. They tighten in three places:

- **You sign off, not the agent.** `Signed-off-by` takes a human name and a working address. The
  DCO is a statement about what you know, and you cannot certify code you have not read. If you
  would not defend a line in review, do not submit it.
- **Agents reproduce their training data, and their training data includes VTank.** The prohibition
  on copying from other Asheron's Call tools applies whether you typed the code or a model emitted
  it. Before you open a pull request, search the diff for anything that looks recalled rather than
  reasoned: unusual constant names, comments in a voice that is not yours, structure you did not
  ask for. If you cannot explain where a block came from, cut it.
- **A live run means a human watched it.** Agent output is not evidence. Nobody can attest to the
  inventory before and after except the person who was looking at the screen.

Two practical asks. Keep pull requests small enough that a human can review them in one sitting; an
afternoon of agent output is not one pull request. And check that generated tests assert behavior
rather than restating the implementation, because a test written from the code it is testing passes
for the wrong reason.

## The bar for a change

- **Warnings are errors.** `dotnet build BuffBot.slnx -c Release` must produce zero warnings.
- **Comments explain the code, not the circumstances that produced it.** Write the fact about the
  API, the format or the invariant that makes the next line necessary — not the bug report, the
  date, the discussion or the plan. One line, two if the reason needs it. `scripts/check-comments.py`
  enforces the limits and runs in CI.
- **Tests pass:** `dotnet test BuffBot.slnx -c Release --no-build`.
- **New behavior comes with tests.** The core is host-free on purpose: parsing, queueing and
  decision logic take plain values and return plain values, so they can be tested without a running
  client. Keep it that way, and keep host-touching code thin.
- **Player-facing phrases live in `Vocabulary/DefaultVocabulary.cs`** and nowhere else.
- **Anything that moves an item needs a watched live run**, described in the pull request: what you
  ran, what the bot held before and after, and what the log showed. A bot that gives, drops or
  splits the wrong thing costs somebody real hours.

CI runs the build, the tests and the packaging step on every pull request. It needs an OpenAC
checkout beside this one; see the [README](README.md).

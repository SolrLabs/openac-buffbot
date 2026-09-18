## What this changes

<!-- One or two sentences. Why, not just what. -->

## How it was checked

- [ ] `dotnet build BuffBot.slnx -c Release` — zero warnings
- [ ] `dotnet test BuffBot.slnx -c Release --no-build` — all pass
- [ ] New behaviour has tests
- [ ] If this can move an item (give, drop, split, trade): a watched live run, described below

<!-- For a live run: what you ran, what the bot held before and after, what the log showed. -->

## Declarations

- [ ] Every commit is signed off (`git commit -s`), per [DCO.txt](../DCO.txt)
- [ ] No source copied from Decal plugins, VTank, DoThingsBot, MossTank, or a decompiled client

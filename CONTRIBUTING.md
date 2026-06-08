# Contributing to SplitPlay

First off: thanks. SplitPlay is an early MVP, which is a polite way of saying there
is a *lot* of low-hanging fruit. You don't have to be a Win32 wizard to help.

## The single most useful thing you can do

**Tell us whether it works on a game.** SplitPlay has been properly verified on
exactly one game so far. Every honest "it works on X" / "it breaks on Y, here's what
happened" report is gold. Open an issue with the
[bug report template](.github/ISSUE_TEMPLATE/bug_report_app.yml) and include:

- The game (and its Steam App ID if you have it).
- What you picked (split orientation, display, controllers).
- What happened vs. what you expected — and **logs/screenshots** if you can.
- Whether the native XInput proxy was present (the app tells you if isolation is on).

## Ways to contribute

- 🧪 **Game verification reports** — see above. Seriously, this is the big one.
- 🐛 **Bug reports** — clear repro steps beat a paragraph of vibes.
- ✨ **Features / fixes** — check the [Roadmap](README.md#roadmap) first so we're
  rowing in the same direction.
- 📖 **Docs** — if something tripped you up, it'll trip up the next person too.

## Development setup

You'll need the **.NET 8 SDK** and **Windows** (this is WPF + Win32; there's no
cross-platform build).

```powershell
# build everything
dotnet build SplitPlay.sln

# run the app
dotnet run --project src/SplitPlay.App/SplitPlay.App.csproj
```

For controller isolation, build the native proxy once (needs the C++ build tools):

```cmd
native\build-proxy.cmd
```

`Core` is plain `net8.0` and has no UI/OS dependencies, so its logic (layout math,
models) is the easiest place to start and the easiest to test.

## Code style

- Settings are centralized in [`Directory.Build.props`](Directory.Build.props) and
  [`.editorconfig`](.editorconfig) — nullable reference types are **on**, so respect
  the `?`.
- Match the surrounding code: comment density, naming, and the dependency rule
  (everything points **toward** `Core`, never the other way).
- View models depend on `Core` abstractions, not concrete implementations. If you're
  newing-up a service inside a view model, take a step back.

## Pull requests

1. Branch from the current development branch.
2. Keep PRs focused — one logical change is easier to review (and revert) than ten.
3. Make sure `dotnet build SplitPlay.sln -c Release` is clean; CI builds the whole
   thing on a real Windows runner and will tattle on you otherwise.
4. Describe **what** changed and **why**. If you verified it on a game, say which.

## Licensing of contributions

SplitPlay is **GPL-3.0**. By contributing, you agree your contribution is licensed
under the same terms. Don't paste in code you don't have the rights to — if you're
adapting something, flag it in the PR so we can credit it properly in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Be decent

We have a [Code of Conduct](CODE_OF_CONDUCT.md). The TL;DR: be kind, assume good
faith, and don't be a jerk. Building couch co-op tooling is supposed to be fun.

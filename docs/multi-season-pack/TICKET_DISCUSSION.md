# Multi-season pack import support — discussion draft

Prepared as input for a GitHub issue/discussion, per `CONTRIBUTING.md`'s "discuss before building large features" requirement — both prior attempts at this were told exactly that. Sources below are pulled directly from the referenced issues/PRs; verify exact wording before quoting anyone in the actual ticket, since it was extracted via automated summarization, not copy-pasted by hand.

## Problem statement

A torrent/NZB named like `Show.S01-S05.1080p...` currently:
- imports only season 1 when added directly to a download client (the rest of the files are silently ignored or the whole folder is blocked from import), and
- is rejected outright by RSS, automatic search, and interactive search, with no path to grab it through the UI.

Goal: make such packs import completely, and give users an honest, opt-in way to grab one from interactive search — without changing RSS/automatic-search behavior (still reject) and without the destructive behavior that got the import gate added in the first place.

## Prior art

| # | Type | Author | Opened | Status | Summary |
|---|------|--------|--------|--------|---------|
| [#3678](https://github.com/Sonarr/Sonarr/issues/3678) | Issue | samip5 | Apr 2020 | Closed | Original feature request: `Alias S01-05` folders only import season 1. |
| [#8133](https://github.com/Sonarr/Sonarr/issues/8133) | Issue | BreiteSeite | Oct 2025 | Closed (via #8417) | Data-loss report: Sonarr auto-deleted a multi-season torrent after a bad partial import, because "Remove Completed" defaults on and multi-season handling was unreliable. **This is why the current import gate exists** — any redesign has to explicitly not reopen this. |
| [#8508](https://github.com/Sonarr/Sonarr/pull/8508) | PR (draft) | Nikamura | Apr 2026 | Auto-closed, inactivity (Aug 2026) | `SeasonNumbers` array on `ParsedEpisodeInfo`, resolve episodes for every season, drop the import-time rejection. Functionally the closest prior attempt to what we're proposing. |
| [#8668](https://github.com/Sonarr/Sonarr/pull/8668) | PR (draft) | VerleneDodds | May 2026 | Auto-closed, inactivity (Aug 2026) | Wider "Experimental multi-season release support" toggle: new regexes, season subfolder handling, `MultiSeasonPack` release type, frontend work. Drew the most pushback. |

Both PRs stalled the same way: converted to draft after review, feedback never fully addressed, auto-closed after ~90 days. Maintainer **markus101** closed #8508 saying not to open a new PR for it — "comment and we will reopen this one" was the ask. **That's worth deciding explicitly in this ticket**: do we try to revive #8508 / coordinate with Nikamura, or is a fresh PR acceptable now that it's been closed a while? Don't default to "just open PR #3" without asking.

## What maintainers actually pushed back on

Pulled from the review threads on #8668 and #8508. Organized by theme so the ticket can address each one directly rather than re-litigating them one at a time as review comments.

**Process**
- markus101 (#8668): should have been discussed first per CONTRIBUTING.md — "a substantial feature affecting many parsing components." This ticket exists to close that gap.
- markus101 (#8668): flagged the PR as AI-authored "without human review" per its own submission checklist, and separately called out "low quality 🤖 comments" in the diff. **This is directly relevant to us** — see note at the bottom.

**Data model (`SeasonNumber` vs `SeasonNumbers`)**
- markus101 (#8508): "This is going to get confusing with `SeasonNumber` and `SeasonNumbers`" — having both side by side wasn't acceptable; asked whether `SeasonNumber` should just return the first entry of `SeasonNumbers`, and whether `IsMultiSeason` should be computed (`SeasonNumbers.Count > 1`) instead of a separately-set flag.
- markus101 (#8508): the Queue model specifically should store the season numbers, not a single season number.

**Non-contiguous seasons**
- markus101 (#8508) and gavincrawford (#8668) both raised this independently: what happens with a pack containing seasons 1 and 3 but not 2? #8508 tried to keep a discrete list; #8668's range-expansion approach risked *fabricating* a season 2 that isn't in the release, with no test coverage for that case. **This needs an explicit decision recorded in the ticket** — the working assumption so far has been "contiguous ranges only, same as we don't support non-contiguous multi-episode files," but that's an assumption, not something a maintainer confirmed.

**Scope creep (specific to #8668)**
- New parser regexes for `S01S02S03` / `Seasons 1-3` forms — pushback on expanding parsing scope.
- A separate `ProcessMultiSeasonFolder` that walks season subfolders — markus101 asked "why do we *need* season subfolders?", and gavincrawford noted it duplicated existing `ProcessFolder` cleanup logic.
- A new `MultiSeasonPack` `ReleaseType` enum value — not asked for, widened the diff.
- An "Experimental Features" settings toggle gating the whole thing — markus101 questioned the concept itself ("why introduce this umbrella? what promotes something out of it?"), and gavincrawford found a real bug where multi-season downloads bypassed rejection entirely when the toggle was *off*.

**Code quality nitpicks** (worth listing so they don't get relitigated as "surprises" later)
- Double ternary in `QueueResource` mapping logic — "shouldn't live in the resource class."
- `EpisodeCountBySeason` / `EpisodesWithFilesCountBySeason` running two separate `GroupBy` queries that could be consolidated.
- Frontend sorting season arrays client-side instead of asking the API to return them sorted.

**Download-loop / data-safety risk**
- mynameisbogdan (#8508), referencing #8133 directly: a multi-season pack with mixed quality across seasons (e.g., part Bluray, part WEB-DL) could, if auto-grabbed, cause perpetual "upgrade" loops once some episodes are already imported at a different quality. This is the direct throughline back to the original data-loss issue and should be addressed head-on in the ticket, not just in a PR description nobody reads during review.

## Open questions to settle in the ticket (before code)

1. **Revive #8508 or open fresh?** Given markus101's explicit ask not to open a new PR, and that the closure was for inactivity rather than a rejection on principle.
2. **Non-contiguous seasons** — reject the whole release as unparseable, or support a discrete (non-range) season list? Contiguous-range-only is the simpler implementation but hasn't been confirmed by a maintainer as acceptable.
3. **Scope for v1**: is "keep the decision-engine rejection everywhere, fix the message, make grab-from-interactive-search actually work" an acceptable minimal scope, or do maintainers want the interactive-search acceptance path (from the #8668 follow-up idea) in the same PR?
4. **Mixed-quality/download-loop risk** — confirm the mitigation (rejection stays on for RSS/automatic search; only a manual interactive grab opts in) is sufficient, referencing #8133 and mynameisbogdan's comment explicitly.
5. **Queue/API shape** — confirm `SeasonNumbers: number[]` on the queue resource (already partially shipped in V5) is the agreed shape, so it isn't relitigated in review.

## A note on AI authorship, given the above

markus101 flagged *both* prior PRs partly for being AI-assisted without adequate human review — this isn't a hypothetical concern, it's the exact thing that contributed to both predecessors stalling. Worth stating explicitly in the ticket (and later the PR) how this round differs: a human is driving the ticket discussion, reviewing the diff line by line, and owns the PR — not asking maintainers to review AI output on faith. That framing matters more here than the code itself, based on how the last two rounds went.

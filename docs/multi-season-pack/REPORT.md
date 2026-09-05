# Multi-Season Pack Grab & Import — Implementation Report

Companion to `PLAN.md` in this directory. Summarizes what was actually changed, why, and what still needs human review before this goes anywhere near a PR (see `PLAN.md` §9 — a human must own, commit and open the PR; this report is written from the implementation agent's side of that handoff).

## Status

All of Steps 1–7 from the plan are implemented, plus the `SeasonNumbers` part of the optional Step 8 API exposure. Frontend (Step 8 TSX/localization) was **not** touched — see "Not done" below. Nothing has been built or run: this environment has no .NET SDK, so `dotnet build` / `dotnet test` / `yarn build` from the plan's verification checklist have **not** been executed. Every change below was verified by manual code reading, not by compiling.

## Files changed

Production code:
- `src/NzbDrone.Core/Parser/Model/ParsedEpisodeInfo.cs` — `SeasonNumbers` is now the source of truth; `SeasonNumber` is a facade getter/setter; `IsMultiSeason` is computed (`SeasonNumbers.Length > 1`); `ToString()` renders a season range.
- `src/NzbDrone.Core/Parser/Parser.cs` — populates `SeasonNumbers` (contiguous range for multi-season packs, single-element array otherwise) instead of setting the old `IsMultiSeason` bool directly.
- `src/NzbDrone.Core/Parser/ParsingService.cs` — `GetEpisodes` resolves episodes across every season in a multi-season pack, reapplying any scene-mapping offset uniformly, and skips scene-season lookup for packs.
- `src/NzbDrone.Core/DecisionEngine/Specifications/MultiSeasonSpecification.cs` — reworded rejection message only; still rejects everywhere.
- `src/NzbDrone.Core/DecisionEngine/Specifications/Search/SeasonMatchSpecification.cs` — season-in-range check for multi-season packs instead of exact equality, so a valid pack doesn't also show a false "Wrong season".
- `src/NzbDrone.Core/MediaFiles/DownloadedEpisodesImportService.cs` — removed the blanket "reject the whole folder if multi-season" import gate; per-file decisions now do the work.
- `src/NzbDrone.Core/Download/CompletedDownloadService.cs` — removed the now-dead `ImportRejectionReason.MultiSeason` branch left over from the deleted gate.
- `src/NzbDrone.Core/Queue/Queue.cs`, `src/NzbDrone.Core/Queue/QueueService.cs`, `src/Sonarr.Api.V5/Queue/QueueResource.cs` — Queue model now stores `List<int> SeasonNumbers` (derived from the episodes actually in the queue item, falling back to `MappedSeasonNumber`) instead of a single nullable season.
- `src/Sonarr.Api.V5/Release/ParsedEpisodeInfoResource.cs` — exposes `SeasonNumbers` alongside the existing `SeasonNumber` (Step 8, backend half only).

Tests:
- `src/NzbDrone.Core.Test/ParserTests/SeasonParserFixture.cs` — multi-season cases now assert the full `SeasonNumbers` range; added a single-season case asserting `IsMultiSeason == false`.
- `src/NzbDrone.Core.Test/ParserTests/ParsingServiceTests/GetEpisodesFixture.cs` — new cases: episodes returned span every season in a pack; a scene-mapping offset is reapplied to every season, not just the first.
- `src/NzbDrone.Core.Test/ParserTests/ParsingServiceTests/MapFixture.cs` — one test fixed (see "Bugs found" below).
- `src/NzbDrone.Core.Test/DecisionEngineTests/MultiSeasonSpecificationFixture.cs` — updated to construct multi-season state via `SeasonNumbers` now that `IsMultiSeason` is read-only.
- `src/NzbDrone.Core.Test/DecisionEngineTests/Search/SeasonMatchSpecificationFixture.cs` **(new)** — season-in-range accept/reject cases for multi-season packs.
- `src/NzbDrone.Core.Test/DecisionEngineTests/Search/SingleEpisodeSearchMatchSpecificationTests/StandardEpisodeSearch.cs` — three tests fixed (see "Bugs found" below).
- `src/NzbDrone.Core.Test/MediaFiles/DownloadedEpisodesImportServiceFixture.cs` — the old "reject the whole folder" test is inverted to assert the folder is now processed normally.
- `src/NzbDrone.Core.Test/Download/CompletedDownloadServiceTests/ImportFixture.cs` — new cases: a download is only marked `Imported` once every season's episodes have imported, not after the first season.
- `src/NzbDrone.Core.Test/Datastore/Converters/EmbeddedDocumentConverterFixture.cs` **(new)** — round-trips `ParsedEpisodeInfo.SeasonNumbers` through the real `EmbeddedDocumentConverter`/STJ options used for `PendingReleases`, and checks legacy `{"seasonNumber":2}` JSON still populates `SeasonNumbers`.

## A design risk in the plan that needed fixing along the way

The plan's Step 1 makes `SeasonNumber` a facade setter that is a **no-op once `SeasonNumbers` is already populated** (this is deliberate — it's what makes new-format JSON round-trip correctly instead of a redundant `seasonNumber` field clobbering the real array on deserialize). The plan calls this out only as a compile concern ("all keep compiling via the setter").

In practice this setter is also a runtime footgun: any code that constructs a `ParsedEpisodeInfo` once and then **reassigns** `.SeasonNumber` to a *different* value later on the same instance now silently does nothing, because the guard (`SeasonNumbers.Length == 0`) is already false. I grepped every `.SeasonNumber = ` and `IsMultiSeason = ` in `src/` (production and tests) and found this pattern in three test fixtures, seven call sites, all fixed by assigning `SeasonNumbers` directly instead of going through the legacy setter:

- `GetEpisodesFixture.cs` — three `[TestCase(2)] [TestCase(20)]` tests reassigned `_parsedEpisodeInfo.SeasonNumber` from the fixture's default of `1` to `2`/`20` inside the test body. Left as-is, the season would have silently stayed `1`, and the mocked `IEpisodeService` calls (set up for season `2`/`20`) would never have matched — these would have started failing outright.
- `MapFixture.cs` — `should_not_use_scene_season_number_from_xem_mapping_if_alias_matches_a_specific_season_number_but_did_not_parse_season_1` reassigns to `2` specifically to exercise the "did not parse season 1" branch. Left as-is, the season would have silently stayed `1`, the test would have gone through the *other* code path than the one it names, and the assertion would have passed for the wrong reason — a false-positive, not a build break.
- `StandardEpisodeSearch.cs` — three tests reassign the fixture's default season `5` to `10` to test season-mismatch and scene-mapping cases. Same false-positive risk as above.

None of these are hypothetical — I traced each one against the actual `Setup()`/test body to confirm the value really does change under the old settable-bool/plain-int design and really would have gotten stuck under the new facade. Every other `.SeasonNumber = ` assignment in the repo (about 120 files matched `SeasonNumber = ` in a raw grep) turned out to be an unrelated type — `Episode.SeasonNumber`, `SeasonSearchCriteria.SeasonNumber`, `ManualImportItem.SeasonNumber`, migration-local model classes — and needed no change.

## Deliberate scope trims vs. the plan

- **Step 8 frontend/localization** (`InteractiveSearchRow.tsx`, `ParseResult.tsx`, new localization key) was left undone. The plan marks this step "optional, independent," and there's no way to run `yarn build` or exercise the UI in this environment to verify a frontend change actually works — shipping unverified TSX felt worse than leaving it for the human reviewer to add (or ask for) separately. The backend half of Step 8 (`SeasonNumbers` on `ParsedEpisodeInfoResource`) was done since it's a pure additive C# field.
- **PR/issue process** (plan §9) — not attempted. No issue was opened, no PR created, nothing pushed anywhere. This is local, uncommitted work for human review, per the task instructions.

## What to double-check before trusting this (no compiler was available)

1. **Build the solution.** `EnforceCodeStyleInBuild`/`IDE0005` (unused usings) are build errors in this repo (`Directory.Build.props`, `.editorconfig`); I checked every touched file's `using` list by hand but a real `dotnet build` is the only real confirmation.
2. **Run the full test suite**, not just the fixtures touched here — the `SeasonNumber` facade's no-op-after-first-set behavior is the kind of thing that can bite a fixture I didn't think to grep for (I searched for `SeasonNumber = ` and `IsMultiSeason = ` specifically, but a differently-named local variable holding a `ParsedEpisodeInfo` wouldn't show up in that pattern).
3. **The JSON round-trip test** (`EmbeddedDocumentConverterFixture.cs`) is new ground for this codebase (no prior test exercised `EmbeddedDocumentConverter` directly) — worth a second look to confirm it actually protects the declaration-order invariant the way it's meant to and not something more incidental to how Moq's `IDbDataParameter` mock behaves.
4. **Manual verification checklist in `PLAN.md` §6** — grabbing a real multi-season pack, watching queue/import/history — is completely unexercised; this was a static-analysis-only pass.

## Suggested commit split

The plan's own scope note ("5 core files + 1 message string + 3-4 test fixtures + optional small UI tweaks") suggests one PR, but if it's easier to review in pieces:
1. `ParsedEpisodeInfo` facade + `Parser.cs` + parser tests (Steps 1–2, 7 parser tests) — self-contained, no behavior change outside parsing.
2. `ParsingService` + decision engine specs + import service + tests (Steps 3–5, 7 remaining tests) — the actual behavior change.
3. Queue model (Step 6b) — independent of the rest; only touches how the queue displays season numbers.

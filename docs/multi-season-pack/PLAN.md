# Sonarr: Multi-Season Pack Grab & Import — Implementation Plan

Target branch: `v5-develop` (analysis done against the current tree, Sept 2026).
Audience: implementation agent. All paths are relative to repo root.

---

## 0. Goal

1. Externally added (or previously grabbed) torrents named like `Show.S01-S05.1080p...` import **every** season automatically instead of being blocked / treated as season 1 only.
2. Interactive search still lists multi-season packs as **rejected** (honest, opt-in UX), but the rejection reason is accurate (no false "Wrong season"), and when a user grabs one anyway it tracks, queues, imports and completes correctly for all seasons.
3. No change to the decision engine's accept/reject outcome anywhere: RSS, automatic search and interactive search all keep rejecting multi-season packs. Only the rejection *message* changes.

This is exactly the scope proposed in the discussion ticket opened for this work: keep ignoring multi-season packs in RSS/automatic search/auto-upgrades, but stop treating a manually-added or interactively-grabbed pack as unsupported — allow it to track and import completely, with a warning shown up front. See §4 for how that maps to a PR sequence, and §9 for the discussion/process status.

## 1. Root-cause summary (verified in code)

| # | Where | What |
|---|-------|------|
| A | `src/NzbDrone.Core/Parser/Parser.cs` ~L1060-1080 | The multi-season regex (L208) captures exactly two `season` groups (first & last). Parser sets `IsMultiSeason = true` and `SeasonNumber = seasons.First()`; the range is discarded. `FullSeason` is `true` for these titles. |
| B | `src/NzbDrone.Core/Parser/Model/ParsedEpisodeInfo.cs` | No field to carry the season list. `ToString()` prints `Season 01`. |
| C | `src/NzbDrone.Core/Parser/ParsingService.cs` L259-274 (`GetEpisodes`, `FullSeason` branch) | Single `GetEpisodesBySeason(series.Id, mappedSeasonNumber)` call → `RemoteEpisode.Episodes` only contains season 1. Everything downstream (grab history, queue, `CompletedDownloadService.VerifyImport`, `TrackedDownloadAlreadyImported`) keys off `RemoteEpisode.Episodes`, so a pack is "complete" as soon as S01 is imported. |
| D | `src/NzbDrone.Core/DecisionEngine/Specifications/MultiSeasonSpecification.cs` | Permanent reject on `IsMultiSeason` with message "not supported". Runs for RSS **and** search. The rejection itself is *not* the blocker: interactive search already lets users grab rejected rows and `ReleaseController.DownloadRelease` never re-runs specs. The blocker is that the cached `RemoteEpisode.Episodes` only holds season 1 (C). |
| E | `src/NzbDrone.Core/DecisionEngine/Specifications/Search/SeasonMatchSpecification.cs` | Season search compares `SeasonNumber` (== first season) to the searched season → "Wrong season" for a S01-S05 pack when searching S03. |
| F | `src/NzbDrone.Core/MediaFiles/DownloadedEpisodesImportService.cs` L206-214 (`ProcessFolder`) | Explicit early-return when `downloadClientItemInfo.IsMultiSeason` → `ImportRejectionReason.MultiSeason` before any per-file decision is made. `CompletedDownloadService.Import` (L181) turns that into `ImportBlocked`. |

What already works and must be reused, not rewritten:

- Per-file episode resolution: `ImportDecisionMaker.GetDecision` calls `Parser.ParsePath(file)` and `AggregateEpisodes` prefers `FileEpisodeInfo`; it only falls back to folder/download-client info when those are **not** `FullSeason`. So `S03E04` files in a multi-season folder already resolve to S03E04. `ParsePath` also combines `Season 3/` folder + `E04` file (L589-615).
- Interactive search UI already lets the user grab a rejected release (`frontend/src/InteractiveSearch/InteractiveSearchRow.tsx`, `handleGrabPress`). `DownloadAllowed` is derived from `Episodes.Any()` (`DownloadDecisionMaker.cs` L123), not from rejections, so a rejected-but-mapped pack grabs in one click with no confirm modal. `ReleaseController.DownloadRelease` uses the cached `RemoteEpisode` and does not re-run specs.
- Manual Import UI is unaffected and remains the fallback for files that can't be parsed.

## 2. Design decision — minimal change, best UX

**Chosen approach:** make `ParsedEpisodeInfo` carry the full season range, make episode resolution span all seasons, remove the import gate (F), and fix the misleading "Wrong season" rejection (E). Keep `MultiSeasonSpecification` (D) rejecting everywhere — only reword its message. Users opt in by grabbing the rejected row in interactive search; from that point the pack is one tracked download, one queue row, one history grab per episode — exactly how single-season packs behave today.

Why keep the rejection rather than accept in interactive search:
- Zero change to decision-engine outcomes → nothing for maintainers to debate about auto-grab risk, no new spec branch, existing `MultiSeasonSpecificationFixture` stays valid.
- Rejected rows sort below approved ones and are hidden by the "approved only" filter (`frontend/src/InteractiveSearch/useReleases.ts`), so a 40 GB pack is never the default pick; the ⚠ popover tells the user what will happen.
- Accepting for `InteractiveSearch: true` can be a trivial follow-up PR once the import path has proven itself.

Rejected alternatives:
- *Split a multi-season pack into N virtual tracked downloads* — touches TrackedDownloadService, queue, history, download-client mapping; far more code and risk.
- *Config toggle "allow multi-season import"* — adds a setting for something that should just work; the per-file import already guards against wrong files (unparseable files are rejected individually and surface as ImportBlocked messages). It's also what #8668 tried ("Experimental Features" toggle) and drew explicit pushback for the concept itself, on top of a real bug where the toggle-off path didn't actually reject.
- *Only remove the import gate (F) without fixing C* — imports all files but `VerifyImport` would mark the download complete after S01, leaving the torrent removed/mis-tracked. C is mandatory.
- *Accept multi-season packs in interactive search (`InteractiveSearch: true` gate in `MultiSeasonSpecification`)* — functionally identical grab/import path, but changes decision outcomes; deferred as a follow-up.

Scope of the change: **5 core files + 1 message string + 3-4 test fixtures + optional small UI tweaks.**

## 3. Implementation steps (do in order)

Each step below is tagged with the PR stage it belongs to (see §4). Stages are designed to be merged independently, in order, each leaving the tree green (build + tests pass) on its own.

### Step 1 — `ParsedEpisodeInfo`: `SeasonNumbers` becomes the source of truth — **Stage 1**
File: `src/NzbDrone.Core/Parser/Model/ParsedEpisodeInfo.cs`

Maintainer feedback on PR #8508 (markus101): having both `SeasonNumber` and `SeasonNumbers` "is going to get confusing… make `SeasonNumber` return the first entry from `SeasonNumbers`", and "should `IsMultiSeason` be `=> SeasonNumbers.Count > 1`?" Do exactly that:

```csharp
// Declare BEFORE SeasonNumber: System.Text.Json (used by EmbeddedDocumentConverter for
// PendingReleases) serializes/deserializes in declaration order, so on new JSON the array
// is populated first and the legacy SeasonNumber setter becomes a no-op.
public int[] SeasonNumbers { get; set; } = Array.Empty<int>();

// Facade kept for the ~2 assignment sites in Parser.cs, the API resources and legacy JSON.
public int SeasonNumber
{
    get => SeasonNumbers.Length > 0 ? SeasonNumbers[0] : 0;
    set { if (SeasonNumbers.Length == 0 && value != 0) { SeasonNumbers = new[] { value }; } }
}

public bool IsMultiSeason => SeasonNumbers.Length > 1;   // was a settable bool
```
- Only two real assignments to `ParsedEpisodeInfo.SeasonNumber` exist (`Parser.cs` L1081, L1086) plus object initialisers in `ParsingService.cs` (~L389) and test fixtures; all keep compiling via the setter. Grep `SeasonNumber =` / `IsMultiSeason =` in `src/` and tests and fix any assignment to `IsMultiSeason` (now read-only) — `Parser.cs` L1075 and migration `161_remove_plex_hometheater.cs` (the migration has its own private model; leave it alone).
- **Careful with the facade setter**: it's a no-op once `SeasonNumbers` is already non-empty (that's what makes new-format JSON round-trip correctly instead of a redundant `seasonNumber` field clobbering the real array). Any existing test that constructs a `ParsedEpisodeInfo` once and then *reassigns* `.SeasonNumber` to a different value later on the same instance needs to be changed to assign `.SeasonNumbers` directly instead, or the reassignment silently does nothing. This bit at least three fixtures in practice (`GetEpisodesFixture`, `MapFixture`, `StandardEpisodeSearch`) — grep for the pattern before assuming "it still compiles" means "it still behaves the same."
- Legacy JSON in `PendingReleases` only has `seasonNumber` → setter populates `[n]`. New JSON writes both; array wins because it is declared first.
- `ToString()` (L125-131): when `FullSeason && IsMultiSeason` → `$"Season {SeasonNumbers.First():00}-{SeasonNumbers.Last():00}"`.
- Do **not** add a `MultiSeasonPack` `ReleaseType` (both prior PRs did; it was not asked for and widens the diff).

### Step 2 — `Parser.cs`: populate `SeasonNumbers` — **Stage 1**
File: `src/NzbDrone.Core/Parser/Parser.cs`, block at ~L1060-1086.

```csharp
var distinctSeasons = seasons.Distinct().OrderBy(s => s).ToList();

if (distinctSeasons.Count > 1)
{
    // The multi-season regex (L208) only captures the endpoints (S01-S05 → 1, 5).
    // Contiguous ranges only; non-contiguous packs are intentionally unsupported
    // (maintainer position on #8508/#8668: "same as we don't support a single
    // episode file with episodes that aren't contiguous").
    result.SeasonNumbers = Enumerable.Range(distinctSeasons.First(), distinctSeasons.Last() - distinctSeasons.First() + 1).ToArray();
}
else if (distinctSeasons.Count == 1)
{
    result.SeasonNumbers = new[] { distinctSeasons[0] };
}
else if (!result.AbsoluteEpisodeNumbers.Any() && result.EpisodeNumbers.Any())
{
    result.SeasonNumbers = new[] { 1 };   // mini-series, unchanged semantics
    result.IsMiniSeries = true;
}
```
- Remove the old `result.IsMultiSeason = true;` line (property is now computed).
- Do **not** add new regexes (#8668 added `S01S02S03` / `Seasons 1-3` forms and got pushback for scope). Existing regex + test corpus (`SeasonParserFixture.should_parse_multi_season_release`) define supported forms.
- Daily/absolute-only parses leave `SeasonNumbers` empty.

**Stage 1 is purely internal**: no decision-engine or import behavior changes, because every consumer still reads the same `IsMultiSeason`/`SeasonNumber` names. It's mergeable on its own and is the cleanest way to resolve the #8508 "confusing data model" objection before anything else is reviewed.

### Step 3 — `ParsingService.GetEpisodes`: resolve across seasons — **Stage 2**
File: `src/NzbDrone.Core/Parser/ParsingService.cs`, private `GetEpisodes(...)` L259.

```csharp
if (parsedEpisodeInfo.FullSeason)
{
    if (parsedEpisodeInfo.IsMultiSeason)
    {
        // Preserve any scene-mapping season offset that Map() applied to the first season.
        var offset = mappedSeasonNumber - parsedEpisodeInfo.SeasonNumber;

        return parsedEpisodeInfo.SeasonNumbers
            .Select(s => s + offset)
            .SelectMany(s => _episodeService.GetEpisodesBySeason(series.Id, s))
            .ToList();
    }

    // existing single-season logic (scene season lookup + fallback) unchanged
}
```
- Keep this inside the existing `FullSeason` branch rather than a separate `GetMultiSeasonEpisodes` method — #8668 was asked "does this actually need to be handled separately or can we re-use existing methods?"
- Skip `GetEpisodesBySceneSeason` for multi-season; scene season numbering is per-season and XEM offsets for whole-series packs are not meaningful. Document this in a comment.
- `Map()` (L177-250) needs no change: `EpisodeRequested` is computed from the full `Episodes` list, so a S03 search request will now intersect.

### Step 5 — `SeasonMatchSpecification`: season-in-range — **Stage 2**
File: `src/NzbDrone.Core/DecisionEngine/Specifications/Search/SeasonMatchSpecification.cs`

Replace the equality check with:
```csharp
var parsed = remoteEpisode.ParsedEpisodeInfo;
var matches = parsed.IsMultiSeason
    ? parsed.SeasonNumbers.Contains(singleEpisodeSpec.SeasonNumber)
    : singleEpisodeSpec.SeasonNumber == parsed.SeasonNumber;
if (!matches) { /* existing reject */ }
```
This step matters even though the pack stays rejected: without it, a Season 3 search shows the pack with **two** reasons ("Multi-season…" and "Wrong season"), and the second one is false — users conclude the pack doesn't contain S03. `EpisodeRequestedSpecification` will also now correctly report the pack as containing the requested episodes.

(`EpisodeRequestedSpecification`, `MonitoredEpisodeSpecification`, `AcceptableSizeSpecification`, `QueueSpecification`, `HistorySpecification`, `UpgradeDiskSpecification` all iterate `Episodes` and need no change.)

**Stage 2 is still behaviorally inert for RSS/automatic search** — `MultiSeasonSpecification` (Step 4, Stage 3) still hard-rejects everywhere, so nothing new auto-grabs. It *does* make an interactive-search grab of a rejected pack track and queue correctly across all seasons, since interactive search doesn't re-run specs — but the import will still be blocked until Stage 3 removes the import gate. Merge order matters: Stage 2 before Stage 3, never after.

### Step 4 — `MultiSeasonSpecification`: message only — **Stage 3**
File: `src/NzbDrone.Core/DecisionEngine/Specifications/MultiSeasonSpecification.cs`

Keep the logic (permanent reject on `IsMultiSeason` for every context). Change only the message so the interactive-search popover explains the opt-in:

```csharp
return DownloadSpecDecision.Reject(DownloadRejectionReason.MultiSeason,
    "Multi-season release. Not grabbed automatically; grab it from interactive search and all seasons will be imported");
```
- No behavioural change for RSS, automatic search or interactive search listing.
- `DownloadRejectionReason.MultiSeason` unchanged (frontend filter `rejections` uses `reason`).
- This is also where the ticket's "display warning in interactive search results" bullet is satisfied — the existing rejection popover already surfaces this string; no new UI plumbing needed for the warning itself, only the copy change here.

### Step 6 — `DownloadedEpisodesImportService.ProcessFolder`: remove the gate — **Stage 3**
File: `src/NzbDrone.Core/MediaFiles/DownloadedEpisodesImportService.cs` L206-214.

Delete the `if (downloadClientItemInfo is { IsMultiSeason: true })` block entirely. Rationale:
- Per-file decisions already do the right thing (see §1). Files that cannot be parsed to a season/episode get an individual `InvalidSeasonOrEpisode` rejection, which `CompletedDownloadService.Import` (L192-208) turns into per-file `ImportBlocked` messages — better UX than a blanket block.
- `folderInfo` for the pack is `FullSeason=true`, so `AggregateEpisodes` will never use it to assign episodes.

Do **not** add a `ProcessMultiSeasonFolder` that walks season sub-folders (#8668 did; maintainer: "Why do we *need* season subfolders?" / duplicated cleanup logic). `_diskScanService.GetVideoFiles` is already recursive and `ParsePath` already handles `Season N/` parents.

Also in `src/NzbDrone.Core/Download/CompletedDownloadService.cs` L181-186: leave the `ImportRejectionReason.MultiSeason` branch (harmless, keeps enum stable) or delete it — either is fine; prefer delete to avoid dead code, keep the enum value.

**Stage 3 is where the ticket's actual ask ships**: a multi-season pack added directly to a download client, or grabbed from interactive search, now imports completely instead of being blocked. This is the PR that will get (and deserves) the most scrutiny — it's the one that touches the exact code path #8133 was filed against. Its description should lead with the safety story from §6 edge case 5 (mixed-quality/download-loop risk), not bury it. Keep this PR to exactly Steps 4 and 6 plus their tests; resist folding in Stage 4 or any UI work.

### Step 6b — Queue model: store season numbers, not a single season — **Stage 4**
Maintainer request on #8508: "The model should store the season numbers, not a single season number." Files:
- `src/NzbDrone.Core/Queue/Queue.cs` L17: replace `int? SeasonNumber` with `List<int> SeasonNumbers`.
- `src/NzbDrone.Core/Queue/QueueService.cs` L63: populate from `trackedDownload.RemoteEpisode?.Episodes.Select(e => e.SeasonNumber).Distinct().OrderBy(s => s)` (falls back to `MappedSeasonNumber` if `Episodes` is empty, to preserve today's behaviour for unmapped items).
- `src/Sonarr.Api.V5/Queue/QueueResource.cs` L57: `SeasonNumbers = model.SeasonNumbers` (the resource already exposes a list). Check the V3 queue resource for the equivalent mapping and keep it returning the first entry.
- Update `QueueServiceFixture` / V5 queue tests accordingly. No frontend change needed (V5 already receives a list).

**Stage 4 is independent of Stages 2-3** in the sense that it's purely how the queue *displays* season coverage — it doesn't change import behavior. It could technically be reordered earlier, but it's placed last because it's the lowest-priority piece of the ticket's ask and the most likely to get bikeshedded on API shape; better to have the import behavior already reviewed and settled first.

### Step 7 — Tests
Ship each test change in the same stage as the production code it covers (see §4 for the exact per-stage file list). Summary of what's needed:
- `src/NzbDrone.Core.Test/ParserTests/SeasonParserFixture.cs` `should_parse_multi_season_release`: extend test cases with `lastSeason` and assert `result.SeasonNumbers.Should().Equal(Enumerable.Range(first, last-first+1))` and `IsMultiSeason.Should().BeTrue()`. Add a single-season case asserting `SeasonNumbers == [n]` and `IsMultiSeason == false`. *(Stage 1)*
- `src/NzbDrone.Core.Test/DecisionEngineTests/MultiSeasonSpecificationFixture.cs`: fix the `IsMultiSeason = true/false` assignments (now read-only) to set `SeasonNumbers` instead; update the message assertion if any. *(Stage 1 for the compile fix if done there, Stage 3 if the message text is asserted — do both together to avoid a fixture failing between stages)*
- New `SeasonMatchSpecificationFixture` case: S01-S05 pack accepted for season 3 search, rejected for season 7. *(Stage 2)*
- `src/NzbDrone.Core.Test/MediaFiles/DownloadedEpisodesImportServiceFixture.cs` ~L518: the test asserting `ImportRejectionReason.MultiSeason` must be inverted — a multi-season download client item should now call `GetImportDecisions` with all video files. *(Stage 3)*
- `ParsingServiceTests`: new fixture asserting `GetEpisodes` for `SeasonNumbers=[1,2,3]`, `FullSeason=true` returns the union of `GetEpisodesBySeason(1..3)` and that a scene offset is applied uniformly. *(Stage 2)*
- `CompletedDownloadServiceFixture`: verify a tracked download whose `RemoteEpisode.Episodes` spans 3 seasons is only marked `Imported` when all imported. *(Stage 3)*
- `PendingReleaseService` / JSON round-trip: serialize a `ParsedEpisodeInfo` with `SeasonNumbers=[1,2,3]` via the same STJ options `EmbeddedDocumentConverter` uses, deserialize, assert array intact; deserialize legacy `{"seasonNumber":2,...}` JSON, assert `SeasonNumbers==[2]`. *(Stage 1)*

### Step 8 — Optional UI polish (small, independent, **not in the initial PR sequence**)
Deliberately excluded from Stages 1-4 to match the ticket's own scope restraint. Revisit as a follow-up once Stage 3 has shipped and been used for a while:
- `src/Sonarr.Api.V5/Release/ReleaseResource.cs` / `ParsedEpisodeInfoResource`: expose `seasonNumbers` alongside `seasonNumber`.
- `frontend/src/InteractiveSearch/InteractiveSearchRow.tsx` + `ReleaseSceneIndicator`/`EpisodeFormats`: render `S01-S05` for `seasonNumbers.length > 1` instead of `Season 1`.
- `frontend/src/Parse/ParseResult.tsx` (L128-131): show the season range next to the existing "Multi-Season: True" row.
- Localization: add `MultiSeasonReleaseInteractiveOnly` to `src/NzbDrone.Core/Localization/Core/en.json` if the rejection message is localized (check how other `DownloadRejectionReason` messages are surfaced; currently they are plain strings).

## 4. Proposed PR split

Both prior attempts (#8508, #8668) were reviewed as one large diff each and stalled without resolving every comment. The ticket for this round explicitly asks for a narrower scope; splitting the implementation into four small, sequentially-mergeable PRs is how that scope discipline gets enforced in review, not just asserted in a description. Each stage below is designed to build and pass its own tests standing alone — none of them should ever be merged out of order.

| Stage | PR title (suggested) | Plan steps | Production files | Test files | Ships user-visible behavior? |
|---|---|---|---|---|---|
| **1** | Parser: track full season range on multi-season packs | 1, 2 | `ParsedEpisodeInfo.cs`, `Parser.cs` | `SeasonParserFixture.cs`, `MultiSeasonSpecificationFixture.cs` (compile fix only), new `EmbeddedDocumentConverterFixture.cs` | No. Pure data-model change; `IsMultiSeason`/`SeasonNumber` still read the same by every consumer. |
| **2** | Resolve episodes across every season in a pack | 3, 5 | `ParsingService.cs`, `SeasonMatchSpecification.cs` | `GetEpisodesFixture.cs`, new `SeasonMatchSpecificationFixture.cs` | Partially. Interactive-search grabs of a (still-rejected) pack now track/queue against all seasons; import is still blocked until Stage 3. Search UI stops showing the false "Wrong season" reason. |
| **3** | Allow multi-season packs to import completely | 4, 6 | `MultiSeasonSpecification.cs`, `DownloadedEpisodesImportService.cs`, `CompletedDownloadService.cs` | `DownloadedEpisodesImportServiceFixture.cs`, `ImportFixture.cs` (multi-season completion cases), `MultiSeasonSpecificationFixture.cs` (message text) | **Yes — this is the PR that satisfies the ticket.** Externally-added and interactively-grabbed multi-season packs import fully; RSS/automatic search still reject unconditionally. |
| **4** | Queue: report every season a download covers | 6b | `Queue.cs`, `QueueService.cs`, `QueueResource.cs` (V5) | `QueueServiceFixture` / relevant V5 queue tests | Cosmetic/API only — the queue row shows the real season range instead of just the first season. |

Notes on ordering and independence:
- **1 → 2 → 3 is a hard dependency chain** (each stage's tests assume the previous stage's production code is in). Stage 4 can land anywhere after Stage 1, including in parallel with Stage 2/3 review, since it touches an entirely different model.
- If a maintainer wants even smaller units, Stage 2 could be split further into "episode resolution" (Step 3) and "search rejection accuracy" (Step 5) — they touch unrelated files and don't depend on each other, only both depend on Stage 1.
- Step 8 (frontend/localization) is intentionally not a stage. Only add it as a Stage 5 if a maintainer asks for it during review of Stage 3; don't pre-emptively widen scope the ticket didn't ask for.
- Every stage's PR description should link back to the discussion ticket and name which stage it is (e.g. "Stage 1 of N — see issue for the full sequence"), so a reviewer seeing PR 2 in isolation has the context without re-deriving it.

## 5. Resulting user experience

| Scenario | Before | After |
|----------|--------|-------|
| Torrent `Show.S01-S05` added directly in qBittorrent | Queue → *Import blocked: Multi-season download* (or nothing) | All parseable files import; queue completes; torrent removed per client settings. Unparseable leftovers listed under Import blocked → Manual Import. |
| Interactive search on Season 3 | Pack listed with ⚠ "Multi-season releases are not supported" + "Wrong season"; grabbing it only tracks S01 | Pack still listed with ⚠ but a single accurate reason explaining it can be grabbed; one-click grab records history for every episode in S01-S05; queue shows all episodes; import completes when all imported (or history shows all). |
| RSS / automatic search | Rejected | Unchanged (rejected; only the message differs). |
| Pack contains a season not in TVDB | n/a | `GetEpisodesBySeason` returns empty for it; files for that season → per-file "Invalid season or episode" → ImportBlocked message, rest imports. |

## 6. Edge cases to check during implementation

1. **Existing higher-quality files in some seasons** — `UpgradeDiskSpecification` / import `UpgradeSpecification` operate per episode; the grab may be rejected if any episode isn't an upgrade (same as single season packs). Acceptable; interactive search still allows forced grab.
2. **Anime with absolute numbering** — `SeasonNumbers` stays empty for absolute-only parses; multi-season branch never triggers. Verify with `AnimeParserFixture`.
3. **Titles like `Show S01 04`** (already in the test corpus as multi-season) — confirm range expansion 1..4 is intended; matches current `IsMultiSeason` behaviour.
4. **`PendingReleases` JSON** stored before this change deserializes via the `SeasonNumber` setter into `SeasonNumbers=[n]`; covered by the round-trip test above.
5. **Mixed-quality packs (download-loop concern raised on #8508 by mynameisbogdan)** — e.g. `S01-S09 … Bluray/WEB-DL-Mixed` parses as Bluray-1080p; if such packs were auto-grabbed, WEB-DL-imported episodes would see the pack as an upgrade forever. This plan keeps RSS/automatic search rejecting multi-season packs, and an interactive grab is a one-off user action, so no loop is introduced. Import quality is per file (`AggregateQuality` takes the highest-confidence source, normally the file name), so history records the real per-episode quality. State this explicitly in the Stage 3 PR description — it's the one PR where this argument needs to hold up to scrutiny.
6. **`AcceptableSizeSpecification`** now sees the true episode count, so per-episode size limits behave correctly for packs (previously it compared a 5-season torrent against 1 season's episode count → large-size rejections).

## 7. Verification checklist

```
yarn build && dotnet build src/Sonarr.sln
dotnet test src/NzbDrone.Core.Test --filter "FullyQualifiedName~Parser|FullyQualifiedName~DecisionEngine|FullyQualifiedName~MediaFiles|FullyQualifiedName~Download"
```
Run this after every stage lands, not just once at the end — each stage is expected to be independently green.

Manual (only fully exercisable once Stage 3 has landed):
- [ ] Add an S01-S03 torrent (with `Season 0X/…E0X` sub-folders and with flat `S0XE0X` files) directly to the download client → all import, queue row completes, no "Import blocked".
- [ ] Interactive search Season 2 → S01-S03 pack shown with a single ⚠ reason (no "Wrong season"); one-click grab (no confirm modal) → history has one Grabbed row per episode across seasons → queue row lists all episodes → import completes.
- [ ] RSS sync log shows multi-season packs still rejected.
- [ ] `Parse` page (Settings → Parse) shows season range for a multi-season title *(only if Step 8 is picked up later)*.

## 8. Relationship to prior PRs #8508 and #8668

Both were drafts auto-closed for inactivity after "changes requested" — neither was rejected on principle. #8508 (Nikamura, Apr 2026) is functionally the same idea as this plan (season array + resolve all seasons + drop import gate, search rejection kept). #8668 (May 2026) went much wider and drew the most pushback.

| Maintainer feedback | #8508 | #8668 | This plan |
|---|---|---|---|
| `SeasonNumber` + `SeasonNumbers` side by side is confusing; derive one from the other | ✗ both stored | ✗ both stored | ✓ Stage 1 facade, `IsMultiSeason` computed |
| Queue model should store season numbers, not one | ✗ | ✗ | ✓ Stage 4 |
| Non-contiguous season sets: don't support | kept discrete lists | range-expanded | ✓ contiguous range only |
| Don't add a separate multi-season code path; reuse `GetEpisodes` / `ProcessFolder` | mostly ok | ✗ `GetMultiSeasonEpisodes`, `ProcessMultiSeasonFolder` | ✓ |
| No "experimental features" toggle | n/a | ✗ (and the toggle-off path had a real bug that bypassed rejection) | ✓ none |
| New `ReleaseType.MultiSeasonPack` | added | added | not added |
| New parser regexes | none | added | none |
| Discuss before building (CONTRIBUTING.md) | not done | not done | discussion ticket opened — see §9 |
| Reviewed as one large diff | yes | yes | ✗ — split into 4 stages, see §4 |
| Download-loop risk from auto-grabbing mixed-quality packs | addressed by keeping rejection | conditional accept when all monitored+aired | rejection kept everywhere |

Practical consequence: the maintainers asked #8508's author to "comment here and we will reopen this one — please do not open a new PR". Once the discussion ticket settles the open questions in §9, the cleanest route may still be to revive #8508 or coordinate with its author for Stage 1/2, rather than opening a cold PR — confirm this in the ticket before opening anything.

## 9. Contribution notes and current process status

The maintainers require a human to own every line: an agent can produce the diff and a plain PR description, but a human must review, commit, push and open the PR — no AI attribution footers. Both prior attempts were also criticized in review for being AI-authored without adequate human review of the substance (not just the diff); that criticism should not be repeatable this time.

**Discussion status**: a scoped-down proposal has been opened as a discussion issue, deliberately narrower than either prior PR — ignore multi-season packs in RSS/automatic search/auto-upgrades (unchanged from today), but stop blocking import for a pack added manually or grabbed via interactive search, with a warning shown up front. It references #3678 (original request), #8133 (the data-loss report that motivated the current import gate), and #8508/#8668 (prior attempts) directly. As of this writing it has no maintainer replies yet.

Before opening **Stage 1's PR**:
- Wait for at least initial maintainer signal on the discussion ticket — the two open questions most likely to change this plan are (a) whether to revive #8508 instead of opening fresh, and (b) whether non-contiguous season packs need any support at all (this plan says no, matching #8508's and #8668's own review outcomes, but it's still an assumption until confirmed).
- If maintainer feedback changes the design, update this file first, then the code — don't let the two drift.

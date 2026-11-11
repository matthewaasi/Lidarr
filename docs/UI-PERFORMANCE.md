# Library and artist page performance

## Read-only live baseline

Measured on November 10, 2026 against `http://10.0.0.101:8687` (Lidarr 3.2.1.5196). These are HTTP request timings, not browser interaction/render timings. No scans, configuration changes, restarts, or deployments were performed.

| Request | Total time | JSON size | Records |
| --- | ---: | ---: | ---: |
| Artist list, first measured request | 17.968 s | 26,217,327 bytes | 2,211 artists |
| Artist list, repeat request | 4.075 s | 26,217,327 bytes | 2,211 artists |
| Sample large artist's albums | 0.522 s | 3,518,992 bytes | 362 albums |
| Sample large artist's track files | 0.164 s | 1,423,676 bytes | 1,742 files |
| Sample median artist's albums | 0.046 s | 176,909 bytes | 26 albums |
| Sample median artist's track files | 0.028 s | 82,291 bytes | 108 files |

The artist list was already compressed to approximately 4.16 MB on the network. Most of its delay preceded response headers (17.735 s on the first measured request, 3.853 s on the repeat). More compression alone would not remove that server preparation time. Two sampled artists do not establish timings for every artist or release.

The response's artist summaries reported 78,731 albums and 415,494 catalog tracks. Catalog totals can differ from downloaded file counts or the library's approximate track count.

## Changes

1. Index the album/file statistics merge by `(ArtistId, AlbumId)` instead of repeatedly scanning the complete file-statistics list for every album. Preserve missing-file behavior, track counts, ordering, and duplicate-match validation.
2. Add `includeAlbumDetails=false` to artist-list requests made by the web UI. Last/next album summaries retain IDs, titles, disambiguation, dates, and images without loading or embedding complete release lists and another copy of the artist. Existing API consumers retain the previous full response by default.
3. Index the last/next album lookup by artist metadata ID.
4. When an album-list request concerns one artist, use that artist's statistics instead of whole-library statistics. Build the album-statistics lookup once per response.
5. Preserve the artist library's memoized selector across renders and correct its equality comparison. Loading/error/filter/order changes still propagate.
6. Cache sorting of artist/release navigation independently of command and file updates.
7. Show an artist's albums as soon as album data is ready. Track-file loading continues separately; file tools retain their existing loaded-file availability checks and errors remain visible.

Offline reserialization of the saved live artist response using the application's resource types and JSON settings measured 27,213,307 bytes before summary projection and 12,021,493 bytes afterward: 55.8% smaller. Both sides use the same serializer. Their baseline differs slightly from the original live response because it is a reserialization through local resource types. This is a projected response-size comparison, not a live endpoint or browser timing measurement.

No download behavior, monitoring, music files, database schema, or queue behavior changes are introduced by this performance patch. It is separate from Library Audit.

## Validation

- Production webpack build passed.
- ESLint passed for changed frontend modules.
- API and core test projects built with StyleCop/.NET analyzers enabled, zero warnings/errors.
- 39 frontend tests passed, including collection cache invalidation, progressive artist loading, and navigation sort reuse.
- 90 .NET tests passed: 84 core tests covering statistics/database behavior, Library Audit and matching/Singles regressions; 6 API tests covering lightweight summaries, unchanged API defaults, and scoped/bulk statistics mapping.
- A synthetic in-memory 20,000-album equivalence test measured the previous merge at 1,440.5 ms and the indexed merge at 4.1 ms. This is a benchmark of the merge only, not a live page speedup.

The tests used installed .NET SDK 8.0.418 through a scratch SDK selection without changing the repository's 8.0.421 pin. NUnitLite ran the tests because VSTest testhost connectivity is unavailable in this environment.

## Deployment and follow-up

This document records pre-release local validation. See the GitHub release and `distribution/unraid/README.md` for packaged builds. Build and deploy the updated backend and UI together before using the lightweight artist query. An older backend ignores the added query parameter and continues serving the larger response.

After deployment, compare repeated `/api/v1/artist?includeAlbumDetails=false` requests and actual main-library, artist, and release navigation in the same browser. Measure both first visit and repeat navigation. Browser profiling is still required to attribute any remaining long render delays; no end-to-end speed target is claimed from the local benchmark.

# Progressive interactive release search

Album and artist interactive searches display fully evaluated results in batches, with processed/total counts and a provisional ranking notice. The final list uses the existing GUID deduplication (fewest rejections, then indexer priority) and download-decision prioritization. No matching thresholds, result caps, specification rules or automatic-search behavior are changed. Rows are displayed in pages of 100; every returned result remains available through pagination and filtering.

The provider interface returns a whole result batch. Lidarr still waits for provider fetching to finish, preserving the original report evaluation order. Streaming improves time to review during the evaluation phase and bounds the number of rendered rows; it is not a promise of reduced provider latency or total evaluation time.

Closing the album/artist search window aborts its request and signals cancellation to the server. Checks before and between release evaluations prevent additional evaluations. Cancellation also stops waiting for provider results. The existing provider API does not accept cancellation tokens, so an in-flight provider call can finish internally, but its results are not subsequently evaluated by this search. A currently running synchronous specification or final sorting operation can finish before cancellation is observed. Already queued downloads are unaffected. Reopening starts a new search; partial results are not cached for resumption.

## Transport and UI

`GET /api/v1/release/stream?albumId=<id>` or `?artistId=<id>` accepts exactly one positive ID. Authentication and URL-base handling use Lidarr's existing AJAX helper. The response is newline-delimited JSON (`application/x-ndjson`), with a flushed initial `searching` frame, `batch` frames containing `processed`, `total` and changed evaluated `records`, and a `complete` frame containing the final deduplicated, prioritized records. An `error` frame explicitly marks incomplete work. A truncated connection is never treated as completion.

The original `GET /api/v1/release` API remains available. Streaming sets `Cache-Control: no-store` and `X-Accel-Buffering: no`. Reverse proxies must permit streaming and propagate client disconnects for prompt cancellation. Some proxies buffer despite these headers and may delay display or cancellation until their next write.

The browser coalesces frames received in one progress event, replaces provisional GUID winners without duplicate rows, ignores late responses after cancellation, and retains download-button state when final ranking arrives. Partial results remain visible with an error if the search fails; they are not labelled complete.

## Verification

Tests compare 15,000 synthetic reports through the regular and progressive decision paths, including acceptance/rejection reasons, qualities, custom format scores and ordering. Additional tests cover final duplicate winners and priority, artist album scope, cancellation during fetching and between evaluated batches, exact final API ordering/rejections, incremental parsing, truncated/error streams, late responses after cancellation, and bounded rendering with access to all 15,000 rows.

These are synthetic and mocked-provider checks. They do not replace a captured real Soulseek replay or live browser/proxy verification. No real-provider timing improvement is claimed.

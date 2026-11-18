# Search processing performance work after ai.17

The ai.17 server run on November 18, 2026 evaluated 6,491 releases with eight workers in 427.786 seconds (15.2/sec). Cumulative, nested worker timings included 775.5 seconds parsing, 497.2 seconds mapping, 226.4 seconds fuzzy artist lookup and 233.5 seconds in the simple quality-profile rule. These numbers overlap, include logging and waiting, and cannot be added as wall time or attributed solely to CPU or SQL.

## Changes

- Debug and trace file targets now use bounded asynchronous batching (500-message batches, 10,000-message queue, blocking overflow). The existing cleansing file target still formats/redacts the messages and handles rotation. Info logging remains synchronous. Graceful flushing preserves queued messages; an abrupt process kill can lose messages still in memory. Logging rotation settings also update wrapped targets.
- A fresh artist catalog is read once per search, then indexed for exact-name matching. Fuzzy matching retains all candidates and the same four scoring passes, thresholds, aliases, case rules and ambiguity behavior. It tracks the two highest scores instead of sorting the full catalog when only a unique result is requested. Normalized alias strings are calculated once per artist per search, on demand. General candidate-list APIs retain their original implementation.
- Album exact matches use a per-artist catalog index with the same cleaned-title OR raw-title condition and ambiguous-match behavior. Year matching still runs after exact matching. No candidate cap, relaxed threshold, title stripping or search-criteria-first reinterpretation is introduced.
- Search-criteria regexes are reused within a search, keyed by the full pattern and current culture. Parsing still follows the same path and produces a fresh result per title.
- Search timing summaries appear approximately every five seconds at completed window boundaries, and also when evaluation finishes or stops. A slow individual window can delay a heartbeat. Stopped counts describe fully evaluated windows; partially evaluated windows are not included.

The read-once catalogs can differ from a changing live library during a search. New searches reload the artist catalog rather than inheriting the older global artist-list cache. Production accuracy still needs comparison against the same actual library snapshot.

## Captured-input replay

`SearchIndexFixture` accepts optional local JSON arrays through:

- `LIDARR_REPLAY_TITLES`: captured raw release titles.
- `LIDARR_REPLAY_ARTISTS`: captured inexact artist lookup strings.

Run the fixture with the repository's .NET SDK and normal test prerequisites. The corpus stays outside the repository. Without the variables, the fixture uses representative public examples.

The initial private corpus contains 5,168 distinct Soulseek titles and 2,419 distinct artist lookup strings. With additional fixture examples, comparisons cover 5,172 title parses and 2,428 artist lookups. Parsing uses the production parser and compares every serialized parsed field. Artist matching compares the original scoring implementation against the indexed path using a 134-artist fixture catalog with aliases, accents and ambiguous names. This is not the user's database.

A local run recorded 0.230 versus 0.148 seconds for criteria parsing, and 3.197 versus 1.355 seconds for fuzzy artist lookups. These are component-level measurements in a test process, not production search throughput. Synchronous production log-file overhead is not represented by those matching timings.

Regression checks also cover exact database lookup parity on SQLite, duplicate artist names, ambiguous album titles, independent per-search caches, cancellation timing summaries, full ordered decisions, and preservation/redaction/rotation of every test log entry. PostgreSQL, external plugins and the 500 releases/sec goal require deployment-host validation. No running server configuration is changed by these source changes.

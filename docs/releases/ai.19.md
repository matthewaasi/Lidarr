# Lidarr 3.2.1.5196-ai.19 — Album-wide AI reconciliation

AI identification previously stopped when file batches chose different metadata releases, or when one identified a release and another returned no release. This could produce `File stages did not agree on a single release; manual review required` without a final album-wide AI attempt.

## Changed behavior

- If the other batches agree on one release and a batch contains only `no_match` decisions, keep the identified release and the unmatched files' independent no-match results. Do not force those files to match.
- Genuine conflicting or uncertain file-stage identities trigger one album-wide reconciliation request containing every original file and candidate release, plus bounded prior assessment reasons. AI can select any original candidate, including one not selected by a file stage. This is not majority voting.
- After a sufficiently confident album selection, discard the previous mappings and remap every original file against the complete selected release track list. Existing staging can still split these mapping requests. Global validation rejects duplicate or cross-release track assignments before applying any result.
- Individually uncertain files remain review items; unrelated files remain unmatched. Review mode still requires confirmation and does not automatically accept proposed file matches. Album confidence is capped by the reconciliation confidence and the identified remapping stages.
- No match for every batch remains a no-match result. Indistinguishable editions, low confidence, refusals, truncated answers, invalid IDs and failed remapping still require review. A failure in a later stage cannot apply an earlier partial success.

Reconciliation is attempted at most once per identification. Its small album-only response does not require the normal per-stage file-count limit, but the complete request must fit `LIDARR_AI_MAX_REQUEST_CHARACTERS` and the model's actual context. Original evidence is never silently truncated. Size/context failures provide an actionable review reason. The shared 128-request staging budget remains enforced. Conflict cases can require an additional album-selection call plus a fresh round of file-mapping calls, increasing LM Studio work and latency.

## Install and retry

1. Back up appdata and any external PostgreSQL database.
2. Load the attached image with `docker load -i lidarr-ai-3.2.1.5196-ai.19-amd64.tar.gz`.
3. Set the existing container image to `lidarr-ai:3.2.1.5196-ai.19`, preserving volumes, paths, ports, network, plugins, database settings and LM Studio configuration, then recreate/start it.
4. For existing failed imports, use **Retry Matching** to obtain a fresh decision. Opening a saved Manual Import preview may show the previous cached decision until fresh matching runs.
5. Look for `AI album reconciliation stage` in the logs. If its review reason reports context size, narrow candidates in Manual Import or increase LM Studio context; confidence thresholds are unchanged.

No database migration or new setting is required. Existing `LIDARR_RELEASE_WORKERS` configuration and ai.18 search-performance improvements are retained. Assembly version remains 3.2.1.5196 for plugin compatibility. The Docker-loadable Linux amd64 archive uses the same pinned LinuxServer runtime. No registry image or server deployment is implied.

## Verification and limits

497 backend tests passed with two existing skips, covering AI identification, client validation, saved decisions, manual previews, queue actions, search matching and concurrency regressions. New reconciliation cases cover selecting a previously unchosen edition, Automatic and Review modes, no-match-only batches, per-file uncertainty, low confidence, invalid candidate IDs, duplicate tracks across stages, later remapping failures, context/request limits, refusals and incomplete outputs.

AI responses in these tests are mocked. They verify orchestration and validation, not the accuracy of a particular LM Studio model on a live library. Real model behavior and context capacity require deployment-host verification. The change does not guarantee every import can be resolved automatically.

Frontend tests/production compilation, analyzer-enabled Linux Console/Mono publishing, source checksums and Docker archive checks are recorded in the attached verification report. No Docker daemon or Linux runtime is available on the build host; Docker loading and container startup have not been executed here. The running server is not modified by publishing this release.

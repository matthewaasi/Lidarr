# Native-first matching with LM Studio fallback

In Settings > Identification, select **Lidarr first, LM Studio fallback** and save. No restart is required. Existing installations keep their selected engine until changed.

The native stage uses the existing configurable album and track thresholds (80% and 60% by default). Native candidate selection and allowed fingerprinting run first. Native identification must resolve the input files, meet both score thresholds, and pass new-download completeness checks. If it passes, no LM Studio request is made. Native matches follow normal import behavior even when LM Studio Mode is Review.

If identification fails, the whole release is sent through the existing LM Studio matcher. Its confidence threshold, Review/Automatic mode, staging, cache, and failure handling remain in effect. A failed AI attempt does not fall back to a weak native result. Quality-profile rejections, duplicates, path problems, and other non-identification import rules do not trigger AI fallback; those checks still run after matching.

Manual import preview tries native identification and reads saved AI results for unresolved releases. Opening the menu does not request new AI matching. Map Tracks and artist/album changes request fresh native-first matching. The Queue's Retry Matching command also uses the selected engine; changing the engine alone does not retry held downloads.

The AI Summary column describes AI results. A successful native match has no AI assessment and can display Not assessed.

No database migration or new environment variable is required. The setting API accepts matchingEngine=Hybrid. Native thresholds continue to use originalAlbumMatchPercent and originalTrackMatchPercent. Hybrid requires a valid LM Studio URL and model so fallback is configured.

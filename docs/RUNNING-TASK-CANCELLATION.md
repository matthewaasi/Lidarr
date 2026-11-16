# Running task cancellation

System → Tasks offers Cancel for queued tasks and supported running tasks. Initial running-task support covers Album Search, Artist Search, Missing Album Search, Cutoff Unmet Album Search and Library Audit. Other running task types have a disabled control with an explanation; they are not forcibly interrupted.

A running cancellation request keeps the task in Started state and displays Cancelling… until its handler reaches a safe point. Only then does it become Cancelled with an end time. The worker and exclusivity rules remain occupied until acknowledgement, preventing overlap with a still-running disk task. Queued cancellations are persisted as Cancelled. Requests for unsupported, finished or finalizing tasks are rejected; they do not claim that work stopped.

Search cancellation stops waiting for results and subsequent release evaluation, album iterations and download-decision dispatch. A provider call already in flight or a current download dispatch can finish. Already queued downloads and completed work remain. Supported tasks use a per-command cancellation source, so stopping one task does not stop other workers or requests. Cancelling the current run does not disable its future schedule.

Library audit checks cancellation between files, after fingerprint lookup and between streamed hashing chunks. Cancelled scans never publish partial findings or resolve old findings. At final saving, cancellation is no longer accepted: that short phase completes without intentional interruption. Prior findings remain unchanged if cancellation is accepted before that point.

The existing `DELETE /api/v1/command/{id}` route now returns the current command resource. `canCancel` identifies available cancellation and `cancellationRequested` distinguishes an accepted request from terminal cancellation. The response and SignalR updates keep the row visible; a late Started response cannot overwrite an already received terminal state. Unsupported plugin task handlers remain non-cancellable until they implement cooperative cancellation.

No database migration or thread termination is added. Task matching, prioritization and interactive search result evaluation remain unchanged. Live providers and deployment-host UI behavior still need runtime verification; automated checks exercise the real command executor, per-task isolation, cancellation status, preservation of audit findings, chunked hashing cancellation, named waiting indexers and processing/ranking messages.

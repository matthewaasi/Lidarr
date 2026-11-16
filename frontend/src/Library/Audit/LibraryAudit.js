import React, { Component } from 'react';
import Alert from 'Components/Alert';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import { featureError } from 'Settings/FeatureSettings';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import styles from './LibraryAudit.css';

const labels = { version: 'Version mismatch', duplicate: 'Exact duplicate files', versionError: 'File inspection failed', duplicateError: 'Duplicate inspection failed', fingerprintError: 'Fingerprint unavailable' };
const confidenceLabels = { exact: 'Exact file match', supported: 'Fingerprint-supported mismatch', uncertain: 'Version uncertain', unavailable: 'Evidence unavailable' };
const fileSize = (bytes) => `${bytes.toLocaleString()} bytes (${(bytes / 1024 / 1024).toFixed(2)} MiB)`;
const duration = (seconds) => (seconds > 0 ? `${Math.floor(seconds / 60)}:${(seconds % 60).toFixed(1).padStart(4, '0')}` : 'Unknown');

class LibraryAudit extends Component {
  state = { records: [], total: 0, page: 1, status: 'pending', kind: 'all', versions: true, duplicates: true, fingerprints: false, loading: true, busy: false, scanning: false, scanProgress: null, scanStatus: null, progressError: '', error: '', confirmId: null, selectedIds: [], deleteTarget: null, deleteFileIds: [], deletionPreview: null, confirmAllCopies: false };

  componentDidMount() {
    this.mounted = true;
    this.onLoad();
    this.request({ url: '/command', dataType: 'json' }, (commands) => {
      const audits = commands.filter((command) => command.name === 'ScanLibraryAudit');
      const active = audits.find((command) => command.status === 'started') || audits.find((command) => command.status === 'queued');
      if (active && !this.state.scanning) {
        this.setState({ scanning: true, scanStatus: active.status, scanProgress: active.body?.progress || null, progressError: '' });
        this.poll(active.id);
      }
    }, (xhr) => this.setState({ error: featureError(xhr) }));
  }

  componentWillUnmount() {
    this.mounted = false;
    clearTimeout(this.timer);
    this.requests.forEach((request) => request.abortRequest());
  }

  mounted = false;
  requests = new Set();
  timer = null;
  generation = 0;

  request = (options, success, failure) => {
    const request = createAjaxRequest(options);
    this.requests.add(request);
    request.request.then((data) => {
      this.requests.delete(request);
      if (this.mounted) {
        success(data);
      }
    }, (xhr) => {
      this.requests.delete(request);
      if (this.mounted) {
        failure(xhr);
      }
    });
  };

  onLoad = () => {
    const { page, status, kind } = this.state;
    const generation = ++this.generation;
    this.setState({ loading: true, confirmId: null, selectedIds: [], deleteTarget: null, deleteFileIds: [], deletionPreview: null, confirmAllCopies: false });
    this.request({ url: `/libraryaudit?status=${status}&kind=${kind}&page=${page}&pageSize=25`, dataType: 'json' },
      (data) => {
        if (generation === this.generation) {
          this.setState({ records: data.records, total: data.totalRecords, page: data.page, loading: false });
        }
      }, (xhr) => {
        if (generation === this.generation) {
          this.setState({ loading: false, error: featureError(xhr) });
        }
      });
  };

  onFilter = (event) => this.setState({ [event.target.name]: event.target.value, page: 1, error: '' }, this.onLoad);

  onDecide = (row, action) => {
    this.setState({ busy: true, error: '', confirmId: null });
    this.request({ url: `/libraryaudit/${row.id}`, method: 'POST', dataType: 'json', data: JSON.stringify({ action, evidenceKey: row.evidenceKey }) },
      () => this.setState({ busy: false }, this.onLoad),
      (xhr) => this.setState({ busy: false, error: featureError(xhr) }, this.onLoad));
  };

  onSelect = (id, checked) => this.setState(({ selectedIds }) => ({
    selectedIds: checked ? [...selectedIds, id] : selectedIds.filter((selectedId) => selectedId !== id),
    deleteTarget: null, deleteFileIds: [], deletionPreview: null, confirmAllCopies: false
  }));

  onConfirmDelete = (deleteTarget) => this.setState({ deleteTarget, deleteFileIds: [], deletionPreview: null, confirmAllCopies: false, confirmId: null });

  onChangeDeleteFile = (id, checked) => this.setState(({ deleteFileIds }) => ({
    deleteFileIds: checked ? [...deleteFileIds, id] : deleteFileIds.filter((fileId) => fileId !== id),
    deletionPreview: null, confirmAllCopies: false
  }));

  onDelete = () => {
    const { deleteTarget, selectedIds, deleteFileIds, records, busy, scanning, deletionPreview, confirmAllCopies } = this.state;
    if (!deleteTarget || busy || scanning) {
      return;
    }
    let url = `/libraryaudit/${deleteTarget}`;
    if (deleteTarget === 'selected') {
      url = '/libraryaudit/bulk';
    }
    let method = 'DELETE';
    let data = deleteTarget === 'selected' ? JSON.stringify(selectedIds) : undefined;
    if (deleteTarget !== 'all' && deleteFileIds.length) {
      if (deletionPreview?.duplicateGroups.length && !confirmAllCopies) {
        return;
      }
      url = deletionPreview ? '/libraryaudit/deletefiles' : '/libraryaudit/deletefiles/preview';
      method = 'POST';
      data = JSON.stringify({
        findings: records.filter((row) => (deleteTarget === 'selected' ? selectedIds.includes(row.id) : row.id === deleteTarget)).map((row) => ({ id: row.id, evidenceKey: row.evidenceKey })),
        fileIds: deleteFileIds,
        ...(deletionPreview ? { previewKey: deletionPreview.previewKey, confirmAllCopies } : {})
      });
    }
    this.setState({ busy: true, error: '' });
    this.request({ url, method, dataType: 'json', ...(data ? { data } : {}) },
      (result) => {
        if (url === '/libraryaudit/deletefiles/preview') {
          this.setState({ busy: false, deletionPreview: result, confirmAllCopies: false });
        } else {
          this.setState({ busy: false }, this.onLoad);
        }
      },
      (xhr) => this.setState({ busy: false, error: featureError(xhr), deletionPreview: null, confirmAllCopies: false }));
  };

  deleteButtonLabel = () => {
    const { deleteFileIds, deleteTarget, deletionPreview } = this.state;
    if (deleteTarget === 'all' || !deleteFileIds.length) {
      return 'Confirm delete findings';
    }
    return deletionPreview ? 'Confirm delete findings and selected files' : 'Preview file deletion';
  };

  poll = (id) => {
    clearTimeout(this.timer);
    this.request({ url: `/command/${id}`, dataType: 'json' }, (command) => {
      const scanState = { scanStatus: command.status, scanProgress: command.body?.progress || null, progressError: '' };
      if (['completed', 'failed', 'cancelled', 'aborted', 'orphaned'].includes(command.status)) {
        this.setState({ ...scanState, scanning: false, error: ['completed', 'cancelled'].includes(command.status) ? '' : 'Audit did not finish. Check System → Tasks and the logs.' }, this.onLoad);
      } else {
        this.setState(scanState);
        this.timer = setTimeout(() => this.poll(id), 2000);
      }
    }, () => {
      this.setState({ progressError: 'Unable to refresh progress. The scan may still be running. Retrying…' });
      this.timer = setTimeout(() => this.poll(id), 5000);
    });
  };

  renderProgress = () => {
    const { scanning, scanStatus, scanProgress, progressError } = this.state;
    if (!scanning && !scanStatus) {
      return null;
    }
    const phaseLabels = { queued: 'Waiting to start', loading: 'Loading library files', inspection: 'Inspecting files', duplicates: 'Checking duplicates', saving: 'Saving findings', completed: 'Audit completed' };
    const stopped = ['cancelled', 'failed', 'aborted', 'orphaned'].includes(scanStatus);
    let phase = scanProgress?.phase || (scanStatus === 'queued' ? 'queued' : 'loading');
    let percentage = scanProgress?.percentage ?? null;
    if (scanStatus === 'completed') {
      phase = 'completed';
      percentage = 100;
    } else if (phase === 'completed') {
      phase = 'saving';
      percentage = null;
    }
    const label = phaseLabels[phase] || 'Audit progress';
    return (
      <section className={styles.confirm} aria-label="Audit progress">
        <p role="status" aria-live="polite">
          {stopped ? `${scanStatus === 'cancelled' ? 'Cancelled' : 'Stopped'} · ` : ''}
          {scanProgress?.stage > 0 ? `Stage ${scanProgress.stage} of ${scanProgress.stages} · ` : ''}
          <strong>{label}{percentage == null ? '' : ` — ${percentage.toFixed(1)}%`}</strong>
          {progressError ? ' · Last received progress' : ''}
        </p>
        <progress aria-label={`${label} progress`} max={100}
          value={percentage == null ? undefined : percentage} style={{ width: '100%' }}
        />
        {['inspection', 'duplicates'].includes(phase) && scanProgress?.total != null ? <p>{scanProgress.processed.toLocaleString()} / {scanProgress.total.toLocaleString()} {phase === 'inspection' ? 'files inspected' : 'duplicate candidate files hashed or checked'}</p> : null}
        {scanProgress?.activity && !stopped ? <p>{scanProgress.activity}</p> : null}
        {scanProgress?.updated && phase !== 'queued' ? <small>Last progress update: {new Date(scanProgress.updated).toLocaleTimeString()}</small> : null}
        <p>Percentage applies to the current phase, not time remaining. Inspection and duplicate checks can take different amounts of time.</p>
        {scanStatus === 'cancelled' ? <Alert>Audit cancelled. Findings from the unfinished scan were not saved.</Alert> : null}
        {progressError ? <Alert kind={kinds.WARNING}>{progressError}</Alert> : null}
      </section>
    );
  };

  onScan = () => {
    const { scanning, versions, duplicates, fingerprints } = this.state;
    if (scanning || (!versions && !duplicates)) {
      return;
    }
    this.setState({ scanning: true, scanProgress: null, scanStatus: 'queued', progressError: '', error: '', confirmId: null });
    this.request({ url: '/command', method: 'POST', dataType: 'json', data: JSON.stringify({ name: 'ScanLibraryAudit', versions, duplicates, fingerprints: versions && fingerprints }) },
      (command) => this.setState({ scanStatus: command.status || 'queued', scanProgress: command.body?.progress || null }, () => this.poll(command.id)), (xhr) => this.setState({ scanning: false, scanStatus: null, scanProgress: null, error: featureError(xhr) }));
  };

  render() {
    const { records, total, page, status, kind, versions, duplicates, fingerprints, loading, busy, scanning, error, confirmId, selectedIds, deleteTarget, deleteFileIds, deletionPreview, confirmAllCopies } = this.state;
    const deleteRows = records.filter((row) => (deleteTarget === 'selected' ? selectedIds.includes(row.id) : row.id === deleteTarget));
    const deleteFiles = [...new Map(deleteRows.filter((row) => row.status !== 'resolved').flatMap((row) => row.evidence.files).map((file) => [file.id, file])).values()];
    let deleteMessage = 'Delete this audit finding?';
    if (deleteTarget === 'all') {
      deleteMessage = 'Delete all audit findings across every status, type and page?';
    } else if (deleteTarget === 'selected') {
      deleteMessage = `Delete ${selectedIds.length} selected audit findings?`;
    }
    return (
      <PageContent title="Library Audit">
        <PageContentBody>
          <h2>Library Audit</h2>
          <p>Find conflicting mix labels and exact duplicate files in your existing library. Scans read files and save findings; they do not modify tags, delete files or change monitoring.</p>
          <div className={styles.toolbar}>
            <label>
              <input type="checkbox" checked={versions}
                disabled={scanning || busy} onChange={(event) => this.setState({ versions: event.target.checked })}
              /> Version audit
            </label>
            <label>
              <input type="checkbox" checked={duplicates}
                disabled={scanning || busy} onChange={(event) => this.setState({ duplicates: event.target.checked })}
              /> Exact duplicates
            </label>
            <label>
              <input type="checkbox" checked={fingerprints}
                disabled={!versions || scanning || busy} onChange={(event) => this.setState({ fingerprints: event.target.checked })}
              /> Deep version check (AcoustID)
            </label>
            <button disabled={scanning || busy || (!versions && !duplicates)} onClick={this.onScan}>{scanning ? 'Scanning…' : 'Scan library'}</button>
            <button disabled={loading || busy} onClick={this.onLoad}>Refresh findings</button>
          </div>
          <p>Version checks compare filename, embedded title and assigned catalog duration. Suggested versions come from the same artist’s local catalog. These are clues, not proof of the audio version. Duplicate checks read complete files of equal size and require identical SHA-256 hashes; differently tagged or encoded copies will not match. Large scans can take time.</p>
          <p>Optional deep checks require fpcalc and send audio fingerprints and durations to AcoustID using Lidarr’s existing lookup. This checks every assigned single-track file and can be slow. Fingerprint associations support review; they do not prove a mix label.</p>
          {this.renderProgress()}
          {scanning ? <Alert>The scan continues if you leave this page. Cancel a running audit under System → Tasks. Findings appear when the scan finishes.</Alert> : null}
          {error ? <Alert kind={kinds.DANGER}>{error}</Alert> : null}
          <div className={styles.toolbar}>
            <label>Status
              <select name="status" value={status}
                disabled={busy || loading} onChange={this.onFilter}
              >
                <option value="pending">Needs review</option><option value="ignored">Ignored</option><option value="resolved">No longer detected</option><option value="all">All findings</option>
              </select></label>
            <label>Type
              <select name="kind" value={kind}
                disabled={busy || loading} onChange={this.onFilter}
              >
                <option value="all">All types</option><option value="version">Version mismatches</option><option value="duplicate">Exact duplicates</option><option value="errors">Inspection errors</option>
              </select></label>
            <span>{total} findings</span>
          </div>
          <div className={styles.toolbar}>
            <label>
              <input type="checkbox" aria-label="Select all findings on this page"
                checked={records.length > 0 && selectedIds.length === records.length}
                disabled={loading || busy || scanning || !records.length} onChange={(event) => this.setState({ selectedIds: event.target.checked ? records.map((row) => row.id) : [], deleteTarget: null })}
              /> Select page
            </label>
            <span>{selectedIds.length} selected</span>
            <button disabled={loading || busy || scanning || !selectedIds.length} onClick={() => this.onConfirmDelete('selected')}>Delete selected findings…</button>
            <button disabled={loading || busy || scanning} onClick={() => this.onConfirmDelete('all')}>Delete all findings…</button>
          </div>
          {deleteTarget ? <div className={styles.confirm}>
            <p>{deleteMessage} {deleteFileIds.length ? `Also delete ${deleteFileIds.length} selected music files from disk and remove their file records from Lidarr. Artist and album entries remain. Monitoring is unchanged, so missing tracks may be downloaded again. Lidarr’s recycle-bin settings apply.` : 'This only removes audit findings. Your library, music files, tags and monitoring are unchanged. A later scan can rediscover these findings.'}</p>
            {deleteTarget !== 'all' && deleteFiles.length ? <div>
              <p>Optional: also delete specific music files. Select only the copies you want removed; no files are selected by default.</p>
              {deleteFiles.map((file) => (<div key={file.id}>
                <label>
                  <input type="checkbox" checked={deleteFileIds.includes(file.id)}
                    disabled={busy || scanning} onChange={(event) => this.onChangeDeleteFile(file.id, event.target.checked)}
                  /> Also delete file: {file.path}
                </label>
              </div>))}
            </div> : null}
            {deletionPreview ? <div>
              <h4>Deletion preview</h4>
              <p>{deletionPreview.files.length} files selected · Total file size: {fileSize(deletionPreview.totalBytes)}. Space may remain in Lidarr’s recycle bin until it is emptied.</p>
              <div className={styles.comparison}>
                <table className={styles.table}>
                  <thead><tr><th>File to delete</th><th>Size</th></tr></thead>
                  <tbody>{deletionPreview.files.map((file) => <tr key={file.id}><td>{file.path}</td><td>{fileSize(file.size)}</td></tr>)}</tbody>
                </table>
              </div>
              {deletionPreview.duplicateGroups.length ? <Alert kind={kinds.DANGER}>
                <p>This will delete every audited copy in {deletionPreview.duplicateGroups.length} duplicate group(s). No copy in these groups will remain.</p>
                {deletionPreview.duplicateGroups.map((group) => (<div key={group.findingId}>
                  <strong>Duplicate finding {group.findingId}</strong>
                  <ul>{group.files.map((file) => <li key={file.id}>{file.path}</li>)}</ul>
                </div>))}
                <label>
                  <input type="checkbox" checked={confirmAllCopies}
                    disabled={busy || scanning} onChange={(event) => this.setState({ confirmAllCopies: event.target.checked })}
                  /> I understand and want to delete every copy in the duplicate groups listed above.
                </label>
              </Alert> : null}
            </div> : null}
            <button disabled={busy || scanning || Boolean(deletionPreview?.duplicateGroups.length && !confirmAllCopies)} onClick={this.onDelete}>{this.deleteButtonLabel()}</button>{' '}
            <button disabled={busy} onClick={() => this.setState({ deleteTarget: null })}>Cancel</button>
          </div> : null}
          {loading ? <LoadingIndicator /> : null}
          {!loading && !records.length ? <p>No findings in this view. Run a scan or change the filters. A clear audit does not verify every mix label.</p> : null}
          {!loading && records.map((row) => (
            <section key={row.id} className={styles.card}>
              <label>
                <input type="checkbox" aria-label={`Select finding ${row.id}`}
                  checked={selectedIds.includes(row.id)}
                  disabled={busy || scanning} onChange={(event) => this.onSelect(row.id, event.target.checked)}
                /> Select finding
              </label>
              <h3>{labels[row.kind] || row.kind} · {confidenceLabels[row.evidence.confidence]}</h3>
              <p>{row.status} · Last observed {new Date(row.updated).toLocaleString()}</p>
              <p>{row.evidence.reason}</p>
              <div className={styles.comparison}>
                <table className={styles.table}>
                  <thead><tr><th>File</th><th>Titles</th><th>Duration / recording</th></tr></thead>
                  <tbody>{row.evidence.files.map((file) => (
                    <tr key={file.id}>
                      <td>{file.foreignArtistId ? <Link to={`/artist/${file.foreignArtistId}`}>{file.artist}</Link> : file.artist || 'Unmapped file'}<small>{file.path}</small><small>{file.size.toLocaleString()} bytes</small>{file.foreignAlbumId ? <Link to={`/album/${file.foreignAlbumId}`}>Review album and track assignment</Link> : <Link to="/unmapped">Review unmapped files</Link>}</td>
                      <td>Tags: {file.tagTitle || 'Not read / unavailable'}<small>Catalog: {file.catalogTitle || 'Unassigned / multi-track file'}</small></td>
                      <td>File: {duration(file.durationSeconds)}<small>Catalog: {duration(file.catalogDurationSeconds)}</small><small>Recording: {file.recordingId || 'Unknown'}</small><small>Fingerprint: {file.fingerprintStatus}</small>{file.audioRecordingIds.map((id) => <small key={id}>Audio association: {id}</small>)}</td>
                    </tr>
                  ))}</tbody>
                </table>
              </div>
              {row.evidence.candidates.length ? <div><h4>Possible catalog versions (not verified)</h4>{row.evidence.candidates.map((candidate) => <p key={candidate.recordingId}>{candidate.title} · {duration(candidate.durationSeconds)} · Recording: {candidate.recordingId}</p>)}</div> : null}
              <p>{(row.evidence.suggestedAction || '').replace('Audit never removes files or changes monitoring.', 'Scans never remove files or change monitoring. File deletion requires explicit selection and confirmation.')}</p>
              <div className={styles.toolbar}>
                <button disabled={busy || scanning} onClick={() => this.onConfirmDelete(row.id)}>Delete finding…</button>
                {row.status === 'pending' ? <button disabled={busy || scanning} onClick={() => this.setState({ confirmId: row.id })}>Ignore finding…</button> : null}
                {row.status === 'ignored' ? <button disabled={busy || scanning} onClick={() => this.onDecide(row, 'restore')}>Return to review</button> : null}
              </div>
              {confirmId === row.id ? <div className={styles.confirm}>
                <p>Hide this finding from Needs review? It stays ignored while its evidence is unchanged. Changed evidence creates a new finding after a scan.</p>
                <button disabled={busy} onClick={() => this.onDecide(row, 'ignore')}>Confirm ignore</button>{' '}
                <button onClick={() => this.setState({ confirmId: null })}>Cancel</button>
              </div> : null}
            </section>
          ))}
          <div className={styles.toolbar}>
            <button disabled={page <= 1 || loading || busy} onClick={() => this.setState({ page: page - 1 }, this.onLoad)}>Previous</button>
            <span>Page {page} of {Math.max(1, Math.ceil(total / 25))}</span>
            <button disabled={page * 25 >= total || loading || busy} onClick={() => this.setState({ page: page + 1 }, this.onLoad)}>Next</button>
          </div>
        </PageContentBody>
      </PageContent>
    );
  }
}

export default LibraryAudit;

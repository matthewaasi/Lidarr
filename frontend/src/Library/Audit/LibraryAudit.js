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
const duration = (seconds) => (seconds > 0 ? `${Math.floor(seconds / 60)}:${(seconds % 60).toFixed(1).padStart(4, '0')}` : 'Unknown');

class LibraryAudit extends Component {
  state = { records: [], total: 0, page: 1, status: 'pending', kind: 'all', versions: true, duplicates: true, fingerprints: false, loading: true, busy: false, scanning: false, error: '', confirmId: null };

  componentDidMount() {
    this.mounted = true;
    this.onLoad();
    this.request({ url: '/command', dataType: 'json' }, (commands) => {
      const active = commands.find((command) => command.name === 'ScanLibraryAudit' && ['queued', 'started'].includes(command.status));
      if (active && !this.state.scanning) {
        this.setState({ scanning: true });
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
    this.setState({ loading: true, confirmId: null });
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

  poll = (id) => {
    this.request({ url: `/command/${id}`, dataType: 'json' }, (command) => {
      if (['completed', 'failed', 'cancelled', 'aborted', 'orphaned'].includes(command.status)) {
        this.setState({ scanning: false, error: command.status === 'completed' ? '' : 'Audit did not finish. Check System → Tasks and the logs.' }, this.onLoad);
      } else {
        this.timer = setTimeout(() => this.poll(id), 2000);
      }
    }, (xhr) => this.setState({ scanning: false, error: featureError(xhr) }));
  };

  onScan = () => {
    const { scanning, versions, duplicates, fingerprints } = this.state;
    if (scanning || (!versions && !duplicates)) {
      return;
    }
    this.setState({ scanning: true, error: '', confirmId: null });
    this.request({ url: '/command', method: 'POST', dataType: 'json', data: JSON.stringify({ name: 'ScanLibraryAudit', versions, duplicates, fingerprints: versions && fingerprints }) },
      (command) => this.poll(command.id), (xhr) => this.setState({ scanning: false, error: featureError(xhr) }));
  };

  render() {
    const { records, total, page, status, kind, versions, duplicates, fingerprints, loading, busy, scanning, error, confirmId } = this.state;
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
          {scanning ? <Alert>Audit is running. Progress is available under System → Tasks. Results appear when the scan finishes.</Alert> : null}
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
          {loading ? <LoadingIndicator /> : null}
          {!loading && !records.length ? <p>No findings in this view. Run a scan or change the filters. A clear audit does not verify every mix label.</p> : null}
          {!loading && records.map((row) => (
            <section key={row.id} className={styles.card}>
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
              <p>{row.evidence.suggestedAction}</p>
              <div className={styles.toolbar}>
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

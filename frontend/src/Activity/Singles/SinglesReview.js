import React, { Component } from 'react';
import Alert from 'Components/Alert';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import { featureError } from 'Settings/FeatureSettings';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import styles from './SinglesReview.css';

const statuses = ['pending', 'cleaned', 'kept', 'restored', 'error', 'cleaning', 'notCovered', 'all'];
const statusLabels = { pending: 'Needs review', cleaned: 'Cleaned', kept: 'Kept', restored: 'Re-monitored', error: 'Cleanup failed', cleaning: 'Interrupted cleanup', notCovered: 'Not covered', all: 'All decisions' };

class SinglesReview extends Component {
  state = { records: [], total: 0, page: 1, status: 'pending', enabled: false, loading: true, busy: false, scanning: false, confirmId: null, error: '' };

  componentDidMount() {
    this.mounted = true;
    this.onLoad();
  }

  componentWillUnmount() {
    this.mounted = false;
    clearTimeout(this.timer);
    this.requests.forEach((request) => request.abortRequest());
  }

  requests = [];
  timer = null;
  mounted = false;

  onLoad = () => {
    const { page, status } = this.state;
    this.setState({ loading: true, confirmId: null });
    const request = createAjaxRequest({ url: `/singlesreview?status=${encodeURIComponent(status)}&page=${page}&pageSize=25`, dataType: 'json' });
    this.requests.push(request);
    request.request.then((data) => this.setState({ records: data.records, total: data.totalRecords, enabled: data.enabled, loading: false }),
      (xhr) => this.setState({ loading: false, error: featureError(xhr) }));
  };

  onFilter = (event) => {
    this.setState({ status: event.target.value, page: 1, error: '' }, this.onLoad);
  };

  onPage = (offset) => {
    this.setState(({ page }) => ({ page: page + offset }), this.onLoad);
  };

  onDecide = (row, action) => {
    if (this.state.busy) {
      return;
    }
    this.setState({ busy: true, error: '', confirmId: null });
    const request = createAjaxRequest({ url: `/singlesreview/${row.id}`, method: 'POST', dataType: 'json', data: JSON.stringify({ action, fingerprint: row.evidence.fingerprint }) });
    this.requests.push(request);
    request.request.then(() => this.setState({ busy: false }, this.onLoad), (xhr) => this.setState({ busy: false, error: featureError(xhr) }, this.onLoad));
  };

  poll = (id) => {
    if (!this.mounted) {
      return;
    }
    const request = createAjaxRequest({ url: `/command/${id}`, dataType: 'json' });
    this.requests.push(request);
    request.request.then((command) => {
      if (['completed', 'failed', 'cancelled', 'aborted'].includes(command.status)) {
        this.setState({ scanning: false, error: command.status === 'completed' ? '' : 'Scan did not finish successfully. Check System → Tasks and the logs.' }, this.onLoad);
      } else {
        this.timer = setTimeout(() => this.poll(id), 2000);
      }
    }, (xhr) => this.setState({ scanning: false, error: featureError(xhr) }));
  };

  onScan = () => {
    if (this.state.scanning || !this.state.enabled) {
      return;
    }
    this.setState({ scanning: true, error: '' });
    const request = createAjaxRequest({ url: '/command', method: 'POST', dataType: 'json', data: JSON.stringify({ name: 'ScanUniqueSingles' }) });
    this.requests.push(request);
    request.request.then((command) => this.poll(command.id), (xhr) => this.setState({ scanning: false, error: featureError(xhr) }));
  };

  render() {
    const { records, total, page, status, enabled, loading, busy, scanning, confirmId, error } = this.state;
    return (
      <PageContent title="Singles Review">
        <PageContentBody>
          <h2>Singles Review</h2>
          <p>Review possible duplicates separately from downloads. Every single track must have a downloaded album/EP counterpart before cleanup can be approved.</p>
          <p><Link to="/settings/uniquesingles">Unique Singles settings</Link></p>
          {enabled ? null : <Alert>Unique Singles is off. Saved decisions are available, but scanning, deletion and automatic monitoring changes are disabled.</Alert>}
          {error ? <Alert kind={kinds.DANGER}>{error}</Alert> : null}
          <div className={styles.toolbar}>
            <button onClick={this.onLoad} disabled={loading || busy}>Refresh list</button>
            <button onClick={this.onScan} disabled={!enabled || scanning || busy}>{scanning ? 'Scanning…' : 'Scan now'}</button>
            <label>
              Status
              <select value={status} onChange={this.onFilter}
                disabled={busy || loading}
              >
                {statuses.map((item) => <option key={item} value={item}>{statusLabels[item]}</option>)}
              </select>
            </label>
            <span>{total} decisions</span>
          </div>
          {enabled ? <p>Scan now can automatically unmonitor and delete strong matches. Review approvals also delete single files using your recycle-bin settings.</p> : null}
          {loading ? <LoadingIndicator /> : null}
          {!loading && records.length === 0 ? <p>No decisions in this view. New uncertain matches appear after an import or scan.</p> : null}
          {records.map((row) => (
            <section className={styles.card} key={row.id}>
              <h3>
                {row.evidence.foreignArtistId ? <Link to={`/artist/${row.evidence.foreignArtistId}`}>{row.evidence.artist}</Link> : row.evidence.artist}
                {' — '}
                {row.evidence.foreignAlbumId ? <Link to={`/album/${row.evidence.foreignAlbumId}`}>{row.evidence.single}</Link> : row.evidence.single}
              </h3>
              <p><strong>{statusLabels[row.status] || row.status}</strong> · {new Date(row.updated).toLocaleString()}</p>
              <p>{row.evidence.reason}</p>
              <div className={styles.comparison}>
                <table className={styles.table}>
                  <thead><tr><th>Single track</th><th>Album / EP counterpart</th><th>Evidence</th></tr></thead>
                  <tbody>{row.evidence.tracks.map((track) => (
                    <tr key={track.trackId}>
                      <td>{track.title}<small>{(track.durationMs / 1000).toFixed(1)}s · Recording: {track.recordingId || 'unknown'}</small></td>
                      <td>{track.sourceTitle || 'No counterpart'}<small>{track.sourceForeignAlbumId ? <Link to={`/album/${track.sourceForeignAlbumId}`}>{track.sourceAlbum}</Link> : track.sourceAlbum || '—'} · {(track.sourceDurationMs / 1000).toFixed(1)}s</small><small>Recording: {track.sourceRecordingId || 'unknown'}</small></td>
                      <td>{track.reason}</td>
                    </tr>
                  ))}</tbody>
                </table>
              </div>
              <p>{row.evidence.singleFileIds.length} single files {row.status === 'cleaned' ? 'removed by this cleanup' : 'in this comparison'}. {row.autoUnmonitored ? 'Monitoring restoration is managed by Unique Singles.' : ''}</p>
              <div className={styles.toolbar}>
                {row.status === 'pending' ? <button disabled={!enabled || busy || loading || scanning || !row.evidence.canCleanup} onClick={() => this.setState({ confirmId: row.id })}>Approve cleanup…</button> : null}
                {['pending', 'error', 'cleaning'].includes(row.status) ? <button disabled={busy || loading || scanning} onClick={() => this.onDecide(row, 'keep')}>Keep / preserve my monitoring choice</button> : null}
                <button disabled={!enabled || busy || loading || scanning} onClick={() => this.onDecide(row, 'recheck')}>Recheck evidence</button>
              </div>
              {confirmId === row.id ? (
                <div className={styles.confirm} role="alert">
                  <p>Unmonitor this single and delete its {row.evidence.singleFileIds.length} files? The album/EP files will be kept. Current evidence is checked again before deletion.</p>
                  <button disabled={busy} onClick={() => this.onDecide(row, 'approve')}>Confirm cleanup</button>
                  <button disabled={busy} onClick={() => this.setState({ confirmId: null })}>Cancel</button>
                </div>
              ) : null}
            </section>
          ))}
          <div className={styles.toolbar}>
            <button disabled={page <= 1 || loading || busy} onClick={() => this.onPage(-1)}>Previous</button>
            <span>Page {page} of {Math.max(1, Math.ceil(total / 25))}</span>
            <button disabled={page * 25 >= total || loading || busy} onClick={() => this.onPage(1)}>Next</button>
          </div>
        </PageContentBody>
      </PageContent>
    );
  }
}

export default SinglesReview;

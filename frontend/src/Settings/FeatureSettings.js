import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import styles from './FeatureSettings.css';

export function featureError(xhr) {
  const body = xhr.responseJSON;
  if (Array.isArray(body)) {
    return body.map((entry) => entry.errorMessage || entry.message).filter(Boolean).join(' ');
  }
  return body?.message || 'The request failed. Check the server connection and logs, then retry.';
}

class FeatureSettings extends Component {
  state = { data: null, busy: true, error: '', saved: false };

  componentDidMount() {
    this.onLoad();
  }

  componentWillUnmount() {
    this.requests.forEach((request) => request.abortRequest());
  }

  requests = [];

  onLoad = () => {
    const request = createAjaxRequest({ url: `/config/${this.props.feature}`, dataType: 'json' });
    this.requests.push(request);
    request.request.then((data) => this.setState({ data, busy: false }), (xhr) => this.setState({ busy: false, error: featureError(xhr) }));
  };

  onChange = (event) => {
    const { name, type, checked, value } = event.target;
    let nextValue = value;
    if (type === 'checkbox') {
      nextValue = checked;
    } else if (type === 'number' && value !== '') {
      nextValue = Number(value);
    }
    this.setState(({ data }) => ({ data: { ...data, [name]: nextValue }, saved: false }));
  };

  onSave = (event) => {
    event.preventDefault();
    if (this.state.busy) {
      return;
    }
    this.setState({ busy: true, error: '', saved: false });
    const request = createAjaxRequest({ url: `/config/${this.props.feature}/1`, method: 'PUT', dataType: 'json', data: JSON.stringify(this.state.data) });
    this.requests.push(request);
    request.request.then(() => this.setState({ busy: false, saved: true }), (xhr) => this.setState({ busy: false, error: featureError(xhr) }));
  };

  field(name, label, options = {}, help = '') {
    const { data, busy } = this.state;
    const checkbox = options.type === 'checkbox';
    return (
      <label className={styles.field} key={name}>
        <span>{label}</span>
        <input name={name} type="text"
          {...options} value={checkbox ? undefined : data[name]}
          checked={checkbox ? data[name] : undefined} disabled={busy}
          onChange={this.onChange}
        />
        {help ? <small>{help}</small> : null}
      </label>
    );
  }

  render() {
    const { data, busy, error, saved } = this.state;
    const singles = this.props.feature === 'uniquesingles';
    const title = singles ? 'Unique Singles' : 'Identification';
    return (
      <PageContent title={title}>
        <PageContentBody>
          <div className={styles.settings}>
            <h2>{title}</h2>
            {error ? <Alert kind={kinds.DANGER}>{error}</Alert> : null}
            {saved ? <Alert kind={kinds.SUCCESS}>Settings saved. Existing held imports are unchanged.</Alert> : null}
            {!data && busy ? <LoadingIndicator /> : null}
            {!data && !busy ? <button onClick={this.onLoad}>Retry loading settings</button> : null}
            {data ? (
              <form onSubmit={this.onSave}>
                {singles ? (
                  <>
                    <Alert kind={kinds.WARNING}>When enabled, strong matches can unmonitor singles and delete their files using your recycle-bin settings. Disable the external UniqueSingles plugin before enabling this native feature.</Alert>
                    {this.field('uniqueSinglesEnabled', 'Enable automatic single cleanup', { type: 'checkbox' }, 'Off disables scans, cleanup, and automatic monitoring changes. Existing decisions remain available.')}
                    {this.field('uniqueSinglesTitleDuration', 'Allow normalized title and duration matches', { type: 'checkbox' }, 'When off, only recording-ID matches can clean automatically. Title-only matches and version conflicts require review.')}
                    {this.field('uniqueSinglesToleranceMs', 'Duration tolerance (milliseconds)', { type: 'number', min: 0, max: 10000, required: true })}
                    <p>Every track must be covered by downloaded tracks on monitored albums or EPs. Exclusive B-sides are kept. Singles unmonitored by this feature are re-monitored when coverage disappears, unless you override the decision.</p>
                    <p>Coverage is checked after imports and deletions, and daily. After enabling, use Scan now on the <Link to="/activity/singles">Singles Review page</Link>. Scanning can clean strong matches automatically.</p>
                  </>
                ) : (
                  <>
                    <label className={styles.field}>
                      <span>Matching engine</span>
                      <select name="matchingEngine" value={data.matchingEngine}
                        disabled={busy} onChange={this.onChange}
                      >
                        <option value="Original">Original Lidarr</option>
                        <option value="LmStudio">LM Studio</option>
                        <option value="Hybrid">Lidarr first, LM Studio fallback</option>
                      </select>
                    </label>
                    <p>Engine changes apply to future identification and explicit retries. Saved AI proposals keep their original model label. Original Lidarr does not contact LM Studio. Hybrid tries native matching first using the thresholds below (80% album by default), then sends the whole release to LM Studio only if identification or completeness checks fail. Other import rejections do not trigger AI.</p>
                    <p>Manual import preview in Hybrid mode tries native matching and reuses saved AI results without calling LM Studio. Map Tracks and Retry Matching run fresh native-first matching. AI fallback uses the Review or Automatic setting below; native matches are not held by AI Review mode.</p>
                    <h3>Native Lidarr thresholds (Original and Hybrid)</h3>
                    {this.field('originalAlbumMatchPercent', 'Minimum album match score (%)', { type: 'number', min: 1, max: 100, required: true }, 'Default 80%. A weighted metadata similarity score, not an AI probability.')}
                    {this.field('originalTrackMatchPercent', 'Minimum track match score (%)', { type: 'number', min: 1, max: 100, required: true }, 'Default 60%, matching stock Lidarr. Missing-track protection applies with either engine.')}
                    <h3>LM Studio</h3>
                    {this.field('lmStudioBaseUrl', 'Server API URL', { type: 'url', required: data.matchingEngine !== 'Original' }, 'Include /v1, for example http://10.0.0.88:1234/v1.')}
                    {this.field('lmStudioModel', 'Model identifier', { required: data.matchingEngine !== 'Original' }, 'Use the exact model ID shown by LM Studio.')}
                    <label className={styles.field}>
                      <span>LM Studio import mode</span>
                      <select name="lmStudioMode" value={data.lmStudioMode}
                        disabled={busy} onChange={this.onChange}
                      >
                        <option value="Review">Review</option>
                        <option value="Automatic">Automatic</option>
                      </select>
                    </label>
                    {this.field('lmStudioConfidencePercent', 'Minimum AI confidence (%)', { type: 'number', min: 1, max: 100, required: true })}
                    <p>Saved server, model, mode and confidence values override the corresponding Docker environment defaults. Staging, timeout and API-key environment settings remain available.</p>
                  </>
                )}
                <button className={styles.button} type="submit"
                  disabled={busy}
                >{busy ? 'Saving…' : 'Save settings'}</button>
              </form>
            ) : null}
          </div>
        </PageContentBody>
      </PageContent>
    );
  }
}

FeatureSettings.propTypes = { feature: PropTypes.oneOf(['identification', 'uniquesingles']).isRequired };

export default FeatureSettings;

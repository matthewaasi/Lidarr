import React, {
  createContext,
  ReactNode,
  useCallback,
  useContext,
  useEffect,
  useState,
} from 'react';
import { useSelect } from 'App/SelectContext';
import Artist from 'Artist/Artist';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import { icons, kinds } from 'Helpers/Props';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';

type Operation = 'rename' | 'retag';

const labels = {
  upToDate: 'Up to date',
  changesPending: 'Changes pending',
  needsRecheck: 'Needs recheck',
  notChecked: 'Not checked',
  failed: 'Partial / failed',
};

interface Status {
  artistId: number;
  operation: Operation;
  state: keyof typeof labels;
  pending: number;
  lastChecked?: string;
  lastApplied?: string;
}

interface Target {
  operation: Operation;
  artists: Pick<Artist, 'id' | 'artistName'>[];
}

interface Preview {
  key?: string;
  skipped: string[];
  renames: {
    trackFileId: number;
    existingPath: string;
    newPath: string;
  }[];
  retags: {
    trackFileId: number;
    path: string;
    changes: { field: string; oldValue?: string; newValue?: string }[];
  }[];
}

type PreviewRow = Target['artists'][number] & Preview;

function previewTitle(operation: Operation) {
  return translate(operation === 'rename' ? 'PreviewRename' : 'PreviewRetag');
}

const MaintenanceContext = createContext<{
  statuses: Map<string, Status>;
  unavailable: boolean;
  open: (target: Target) => void;
} | null>(null);

export function ArtistFileMaintenanceProvider({ children }: { children: ReactNode }) {
  const [statuses, setStatuses] = useState(new Map<string, Status>());
  const [unavailable, setUnavailable] = useState(false);
  const [target, setTarget] = useState<Target | null>(null);
  const [queuedId, setQueuedId] = useState<number | null>(null);
  const [jobMessage, setJobMessage] = useState('');
  const [revision, setRevision] = useState(0);

  const close = useCallback(() => {
    setTarget(null);
    setRevision((value) => value + 1);
  }, []);

  useEffect(() => {
    let active = true;
    let request: ReturnType<typeof createAjaxRequest> | undefined;
    let timer: ReturnType<typeof setTimeout>;

    function refresh() {
      request = createAjaxRequest({ url: '/artistfilemaintenance', dataType: 'json' });
      request.request.then((records: Status[]) => {
        if (active) {
          setStatuses(new Map(records.map((record) => [
            `${record.operation}:${record.artistId}`, record,
          ])));
          setUnavailable(false);
        }
      }, () => {
        if (active) {
          setUnavailable(true);
        }
      }).always(() => {
        if (active) {
          timer = setTimeout(refresh, 15000);
        }
      });
    }

    refresh();

    return () => {
      active = false;
      clearTimeout(timer);
      request?.abortRequest();
    };
  }, [revision]);

  useEffect(() => {
    if (queuedId === null) {
      return;
    }

    let active = true;
    let timer: ReturnType<typeof setTimeout>;
    let request: ReturnType<typeof createAjaxRequest>;

    function poll() {
      request = createAjaxRequest({ url: `/command/${queuedId}`, dataType: 'json' });
      request.request.then((command: { status: string; message?: string }) => {
        if (!active) {
          return;
        }

        setJobMessage(command.message || `Library maintenance: ${command.status}`);
        if (['completed', 'failed', 'cancelled', 'aborted', 'orphaned'].includes(command.status)) {
          setQueuedId(null);
          setRevision((value) => value + 1);
        } else {
          timer = setTimeout(poll, 2000);
        }
      }, () => {
        if (active) {
          setJobMessage('Unable to read progress. Check System → Tasks for the queued job.');
          setQueuedId(null);
        }
      });
    }

    poll();

    return () => {
      active = false;
      clearTimeout(timer);
      request?.abortRequest();
    };
  }, [queuedId]);

  function onQueued(id: number) {
    setJobMessage('Library maintenance queued');
    setQueuedId(id);
    close();
  }

  return (
    <MaintenanceContext.Provider value={{ statuses, unavailable, open: setTarget }}>
      {children}
      {jobMessage ? <Alert>{jobMessage}</Alert> : null}
      {target ? <ArtistFilePreviewModal target={target} close={close} onQueued={onQueued} /> : null}
    </MaintenanceContext.Provider>
  );
}

export function ArtistFileMaintenanceButtons({ artists, isSelectMode }: {
  artists: Artist[];
  isSelectMode: boolean;
}) {
  const [selection] = useSelect();
  const maintenance = useContext(MaintenanceContext);
  const selected = isSelectMode
    ? artists.filter((artist) => selection.selectedState[artist.id])
    : [];
  const scope = selected.length ? selected : artists;

  return (
    <>
      <PageToolbarButton
        label={previewTitle('rename')}
        iconName={icons.ORGANIZE}
        isDisabled={!scope.length}
        onPress={() => maintenance?.open({ operation: 'rename', artists: scope })}
      />
      <PageToolbarButton
        label={previewTitle('retag')}
        iconName={icons.RETAG}
        isDisabled={!scope.length}
        onPress={() => maintenance?.open({ operation: 'retag', artists: scope })}
      />
    </>
  );
}

export function ArtistFileStatusLabel({ artistId, artistName, operation }: {
  artistId: number;
  artistName: string;
  operation: Operation;
}) {
  const maintenance = useContext(MaintenanceContext);
  const status = maintenance?.statuses.get(`${operation}:${artistId}`);
  const details = [
    status?.lastChecked ? `Checked: ${new Date(status.lastChecked).toLocaleString()}` : '',
    status?.lastApplied ? `Applied: ${new Date(status.lastApplied).toLocaleString()}` : '',
  ].filter(Boolean).join('\n');

  return (
    <button
      type="button"
      title={details}
      onClick={() => maintenance?.open({ operation, artists: [{ id: artistId, artistName }] })}
    >
      {maintenance?.unavailable ? 'Status unavailable' : labels[status?.state ?? 'notChecked']}
      {status?.state === 'changesPending' ? ` (${status.pending})` : ''}
    </button>
  );
}

function preventClose() {
  return undefined;
}

function ArtistFilePreviewModal({ target, close, onQueued }: {
  target: Target;
  close: () => void;
  onQueued: (id: number) => void;
}) {
  const { operation, artists } = target;
  const [rows, setRows] = useState<PreviewRow[]>([]);
  const [checking, setChecking] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState(false);
  const [started, setStarted] = useState(false);
  const requests = React.useRef(new Set<ReturnType<typeof createAjaxRequest>>());
  const active = React.useRef(true);

  useEffect(() => {
    active.current = true;
    const pending = requests.current;
    return () => {
      active.current = false;
      pending.forEach((request) => request.abortRequest());
    };
  }, []);

  async function check() {
    setStarted(true);
    setChecking(true);
    const results: PreviewRow[] = [];

    for (const artist of artists) {
      const request = createAjaxRequest({
        url: `/artistfilemaintenance/preview?artistId=${artist.id}&operation=${operation}`,
        dataType: 'json',
        timeout: 120000,
      });
      requests.current.add(request);
      let preview: Preview;
      try {
        preview = await request.request;
      } catch {
        preview = { renames: [], retags: [], skipped: ['Unable to load preview.'] };
      } finally {
        requests.current.delete(request);
      }

      if (!active.current) {
        return;
      }

      results.push({ ...artist, ...preview });
      setRows([...results]);
    }

    setChecking(false);
  }

  const ready = rows.filter((row) => row.key && (row.renames.length || row.retags.length));
  const issues = rows.filter((row) => row.skipped.length).length;
  const current = rows.filter((row) => !row.renames.length && !row.retags.length && !row.skipped.length).length;
  const fileCount = ready.reduce((count, row) => count + row.renames.length + row.retags.length, 0);

  async function apply() {
    setSubmitting(true);
    setSubmitError(false);
    const request = createAjaxRequest({
      url: '/artistfilemaintenance/apply',
      method: 'POST',
      dataType: 'json',
      data: JSON.stringify({
        operation,
        reviews: Object.fromEntries(ready.map((row) => [row.id, row.key])),
      }),
    });

    try {
      const { id } = await request.request;
      if (active.current) {
        onQueued(id);
      }
    } catch {
      if (active.current) {
        setSubmitting(false);
        setSubmitError(true);
      }
    }
  }

  return (
    <Modal isOpen={true} onModalClose={submitting ? preventClose : close}>
      <ModalContent onModalClose={submitting ? preventClose : close}>
        <ModalHeader>{previewTitle(operation)}</ModalHeader>
        <ModalBody>
          <p>{artists.length} {artists.length === 1 ? 'artist' : 'artists'} in scope</p>
          {checking ? <p>Checking {rows.length} of {artists.length} artists…</p> : null}
          {started && !checking ? (
            <p>{ready.length} with changes · {current} up to date · {issues} with skips or errors</p>
          ) : null}
          {submitError ? <Alert kind={kinds.DANGER}>Unable to queue the reviewed changes.</Alert> : null}
          {rows.map((row) => (
            <details key={row.id}>
              <summary>
                {row.artistName} · {row.renames.length + row.retags.length} changes
                {row.skipped.length ? ` · ${row.skipped.length} skipped or failed` : ''}
              </summary>
              <ul>
                {row.renames.map((file) => (
                  <li key={file.trackFileId}>{file.existingPath} → {file.newPath}</li>
                ))}
                {row.retags.map((file) => (
                  <li key={file.trackFileId}>
                    {file.path}
                    <ul>
                      {file.changes.map((change) => (
                        <li key={change.field}>
                          {change.field}: {change.oldValue || '∅'} → {change.newValue || '∅'}
                        </li>
                      ))}
                    </ul>
                  </li>
                ))}
                {row.skipped.map((detail, index) => <li key={`${detail}:${index}`}>{detail}</li>)}
              </ul>
            </details>
          ))}
        </ModalBody>
        <ModalFooter>
          <Button isDisabled={submitting} onPress={close}>{translate('Close')}</Button>
          {!started ? <Button kind={kinds.PRIMARY} onPress={check}>{'Check files'}</Button> : null}
          {fileCount > 0 && !checking ? (
            <Button kind={kinds.WARNING} isDisabled={submitting} onPress={apply}>
              Apply {fileCount} file changes
            </Button>
          ) : null}
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

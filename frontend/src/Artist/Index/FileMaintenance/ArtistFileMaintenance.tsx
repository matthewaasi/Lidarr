import React, {
  createContext,
  ReactNode,
  useCallback,
  useContext,
  useEffect,
  useState,
} from 'react';
import { useSelector } from 'react-redux';
import { useSelect } from 'App/SelectContext';
import Artist from 'Artist/Artist';
import Alert from 'Components/Alert';
import CheckInput from 'Components/Form/CheckInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarOverflowMenuItem from 'Components/Page/Toolbar/PageToolbarOverflowMenuItem';
import OrganizePreviewRow from 'Organize/OrganizePreviewRow';
import organizeStyles from 'Organize/OrganizePreviewModalContent.css';
import createAllArtistSelector from 'Store/Selectors/createAllArtistSelector';
import { icons, kinds } from 'Helpers/Props';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';
import styles from './ArtistFileMaintenance.css';

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

interface ArtistFileMaintenanceButtonProps {
  artists: Artist[];
  isSelectMode: boolean;
  label: string;
  operation: Operation;
  overflowComponent?: typeof ArtistFileMaintenanceMenuItem;
}

function useMaintenanceButton({
  artists,
  isSelectMode,
  operation,
  label,
}: ArtistFileMaintenanceButtonProps) {
  const [selection] = useSelect();
  const libraryArtists = useSelector(createAllArtistSelector());
  const maintenance = useContext(MaintenanceContext);
  const selected = isSelectMode
    ? artists.filter((artist) => selection.selectedState[artist.id])
    : [];
  const scope = selected.length ? selected : artists;

  return {
    label,
    iconName: operation === 'rename' ? icons.ORGANIZE : icons.RETAG,
    isDisabled: !scope.length,
    onPress: () => {
      const names = new Map(libraryArtists.map((artist) => [artist.id, artist.artistName]));
      maintenance?.open({
        operation,
        artists: scope.map(({ id }) => ({ id, artistName: names.get(id) ?? `Artist ${id}` })),
      });
    },
  };
}

export function ArtistFileMaintenanceButton(props: ArtistFileMaintenanceButtonProps) {
  const button = useMaintenanceButton(props);
  return <PageToolbarButton {...button} />;
}

export function ArtistFileMaintenanceMenuItem(props: ArtistFileMaintenanceButtonProps) {
  const button = useMaintenanceButton(props);
  return <PageToolbarOverflowMenuItem {...button} />;
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

  const state = maintenance?.unavailable ? 'unavailable' : status?.state ?? 'notChecked';
  const label = state === 'unavailable' ? 'Status unavailable' : labels[state];
  const pending = state === 'changesPending' ? ` (${status?.pending})` : '';
  const statusIcons = {
    upToDate: icons.CHECK_CIRCLE,
    changesPending: icons.RETAG,
    needsRecheck: icons.HISTORY,
    notChecked: icons.CHECK_INDETERMINATE,
    failed: icons.WARNING,
    unavailable: icons.WARNING,
  };

  return (
    <button
      type="button"
      className={styles.statusButton}
      title={[label + pending, details].filter(Boolean).join('\n')}
      onClick={() => maintenance?.open({ operation, artists: [{ id: artistId, artistName }] })}
    >
      <Icon className={styles[state]} name={statusIcons[state]} size={12} />
      <span className={styles.statusText}>{label}{pending}</span>
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
  const isRename = operation === 'rename';
  const [naming, setNaming] = useState<{ renameTracks: boolean; standardTrackFormat: string; multiDiscTrackFormat: string }>();
  const [deselected, setDeselected] = useState<Set<number>>(new Set());
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

  const check = useCallback(async (signal?: AbortSignal) => {
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

      if (!active.current || signal?.aborted) {
        return;
      }

      results.push({ ...artist, ...preview });
      setRows([...results]);
    }

    setChecking(false);
  }, [artists, operation]);

  useEffect(() => {
    if (!isRename) {
      return;
    }

    const controller = new AbortController();
    const request = createAjaxRequest({ url: '/config/naming', dataType: 'json' });
    request.request.then((config: NonNullable<typeof naming>) => {
      if (!controller.signal.aborted) {
        setNaming(config);
      }
    }, () => {});
    void check(controller.signal);

    return () => {
      controller.abort();
      request.abortRequest();
    };
  }, [check, isRename]);

  const ready = rows.filter((row) => row.key && (row.renames.length || row.retags.length));
  const issues = rows.filter((row) => row.skipped.length).length;
  const current = rows.filter((row) => !row.renames.length && !row.retags.length && !row.skipped.length).length;
  const renameIds = ready.flatMap((row) => row.renames.map((file) => file.trackFileId));
  const selectedRenameCount = renameIds.filter((id) => !deselected.has(id)).length;
  const fileCount = isRename ? selectedRenameCount : ready.reduce((count, row) => count + row.retags.length, 0);

  function onRenameSelected({ id, value }: { id: number; value: boolean }) {
    setDeselected((previous) => {
      if (previous.has(id) === !value) {
        return previous;
      }

      const next = new Set(previous);
      if (value) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  async function apply() {
    setSubmitting(true);
    setSubmitError(false);
    const selectedFiles = Object.fromEntries(ready.map((row) => [
      row.id,
      row.renames.filter((file) => !deselected.has(file.trackFileId)).map((file) => file.trackFileId),
    ]));
    const reviewed = isRename ? ready.filter((row) => selectedFiles[row.id].length) : ready;
    const request = createAjaxRequest({
      url: '/artistfilemaintenance/apply',
      method: 'POST',
      dataType: 'json',
      data: JSON.stringify({
        operation,
        reviews: Object.fromEntries(reviewed.map((row) => [row.id, row.key])),
        selectedFiles: isRename
          ? Object.fromEntries(reviewed.map((row) => [row.id, selectedFiles[row.id]]))
          : undefined,
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
        <ModalHeader>{isRename ? 'Organize & Rename' : previewTitle(operation)}</ModalHeader>
        <ModalBody>
          <p>{artists.length} {artists.length === 1 ? 'artist' : 'artists'} in scope</p>
          {checking ? <p>Checking {rows.length} of {artists.length} artists…</p> : null}
          {started && !checking ? (
            <p>{ready.length} with changes · {current} up to date · {issues} with skips or errors</p>
          ) : null}
          {submitError ? <Alert kind={kinds.DANGER}>Unable to queue the reviewed changes.</Alert> : null}
          {isRename && naming && renameIds.length > 0 ? (
            <Alert>
              {naming.renameTracks ? (
                <>
                  <div>Naming pattern: <span className={organizeStyles.trackFormat}>{naming.standardTrackFormat}</span></div>
                  {naming.multiDiscTrackFormat !== naming.standardTrackFormat ? (
                    <div>Multi-disc: <span className={organizeStyles.trackFormat}>{naming.multiDiscTrackFormat}</span></div>
                  ) : null}
                </>
              ) : 'Track renaming is disabled in Naming settings.'}
            </Alert>
          ) : null}
          {isRename && started && !checking && !renameIds.length && !issues ? (
            <div>{translate('SuccessMyWorkIsDoneNoFilesToRename')}</div>
          ) : null}
          {isRename ? rows.filter((row) => row.renames.length || row.skipped.length).map((row) => (
            <section key={row.id} className={organizeStyles.previews}>
              {artists.length > 1 ? <h3>{row.artistName}</h3> : null}
              {row.renames.map((file) => (
                <OrganizePreviewRow
                  key={file.trackFileId}
                  id={file.trackFileId}
                  existingPath={file.existingPath}
                  newPath={file.newPath}
                  isSelected={!deselected.has(file.trackFileId)}
                  onSelectedChange={onRenameSelected}
                />
              ))}
              {row.skipped.length ? (
                <details>
                  <summary>{row.skipped.length} skipped or failed</summary>
                  <ul>{row.skipped.map((detail, index) => <li key={`${detail}:${index}`}>{detail}</li>)}</ul>
                </details>
              ) : null}
            </section>
          )) : rows.map((row) => (
            <details key={row.id}>
              <summary>
                {row.artistName} · {row.renames.length + row.retags.length} changes
                {row.skipped.length ? ` · ${row.skipped.length} skipped or failed` : ''}
              </summary>
              <ul>
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
          {isRename && renameIds.length > 0 && !checking ? (
            <CheckInput
              className={organizeStyles.selectAllInput}
              containerClassName={organizeStyles.selectAllInputContainer}
              name="selectAll"
              value={selectedRenameCount === renameIds.length ? true : selectedRenameCount === 0 ? false : null}
              isDisabled={submitting}
              onChange={({ value }: { value: boolean }) => setDeselected(new Set(value ? [] : renameIds))}
            />
          ) : null}
          <Button isDisabled={submitting} onPress={close}>{translate(isRename ? 'Cancel' : 'Close')}</Button>
          {!isRename && !started ? <Button kind={kinds.PRIMARY} onPress={() => check()}>Check files</Button> : null}
          {fileCount > 0 && !checking ? (
            <Button kind={isRename ? kinds.PRIMARY : kinds.WARNING} isDisabled={submitting} onPress={apply}>
              {isRename ? translate('Organize') : `Apply ${fileCount} file changes`}
            </Button>
          ) : null}
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

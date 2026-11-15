import createAjaxRequest from 'Utilities/createAjaxRequest';

// One request owns the search lifetime. Aborting it also cancels server evaluation.
export default function createProgressiveReleaseSearch(payload, onUpdate, onError) {
  let active = true;
  let terminal = false;
  let offset = 0;
  let pending = '';
  let processed = 0;
  let total = null;
  let phase = 'searching';
  let records = new Map();
  let request = null;

  const fail = (error) => {
    if (active && !terminal) {
      terminal = true;
      onError(error);
      request?.abortRequest();
    }
  };

  const consume = (text, final = false) => {
    if (!active || terminal) {
      return;
    }
    try {
      pending += text.slice(offset);
      offset = text.length;
      let changed = false;
      let newline = pending.indexOf('\n');
      while (newline >= 0) {
        const line = pending.slice(0, newline);
        pending = pending.slice(newline + 1);
        newline = pending.indexOf('\n');
        if (!line.trim()) {
          throw new Error('Empty search response frame');
        }
        const message = JSON.parse(line);
        if (message.type === 'error') {
          throw new Error(message.message);
        }
        if (message.type === 'batch') {
          for (const record of message.records) {
            records.set(record.guid, record);
          }
          processed = message.processed;
          total = message.total;
          phase = 'processing';
        } else if (message.type === 'complete') {
          records = new Map(message.records.map((record) => [record.guid, record]));
          phase = 'complete';
          terminal = true;
        } else if (message.type !== 'searching') {
          throw new Error('Unexpected search response');
        }
        changed = true;
        if (terminal) {
          break;
        }
      }
      if (changed) {
        onUpdate({ items: Array.from(records.values()), processed, total, phase });
      }
      if (final && !terminal) {
        throw new Error('Search ended before completion. Partial results are incomplete.');
      }
    } catch (error) {
      fail({ responseJSON: { message: error.message || 'Unable to read search results' } });
    }
  };

  request = createAjaxRequest({
    url: '/release/stream',
    data: payload,
    traditional: true,
    dataType: 'text',
    xhrFields: {
      onprogress: (event) => {
        if (event.target.status === 200) {
          consume(event.target.responseText);
        }
      }
    }
  });
  request.request.done((text) => consume(text, true));
  request.request.fail((xhr) => fail(xhr));

  return () => {
    active = false;
    request.abortRequest();
  };
}

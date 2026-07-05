import { parseJsonResponse } from '../services/apiClient.js';

let activeSyncQueue = [];

export async function processSyncAction(action, boardId, itemId, payload, {
  fetchWithCredentials,
  setBoards,
  showSyncBanner,
  refreshStudioDisplay
}) {
  const timestamp = new Date().toISOString();

  activeSyncQueue.push({
    action,
    boardId,
    itemId,
    payload,
    timestamp
  });

  try {
    const res = await fetchWithCredentials('/api/sync', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        queue: activeSyncQueue,
        clientTimestamp: timestamp
      })
    });

    const data = await parseJsonResponse(res, 'Sync returned an unexpected response.');

    if (res.ok) {
      if (data.success) {
        activeSyncQueue = [];
        setBoards(data.boards || []);
        const skippedCount = data.skippedCount || 0;
        if (skippedCount > 0) {
          const firstWarning = data.warnings?.[0] || `${skippedCount} sync action(s) were skipped.`;
          showSyncBanner(`Sync completed with ${skippedCount} warning(s): ${firstWarning}`, true);
        } else {
          showSyncBanner(`Sync completed. ${data.appliedCount || 0} cloud update(s) verified.`, false);
        }
        refreshStudioDisplay();
      }
    } else {
      throw new Error(data?.error || 'Sync returned error status.');
    }
  } catch (err) {
    const offlinePill = document.getElementById('offline-pill');
    offlinePill?.classList.remove('hidden');
    offlinePill?.classList.add('flex');
    showSyncBanner('Sync is pending in this browser session. Keep this tab open and retry when the server is reachable.', true);
  }
}

export function showSyncBanner(msg, isError) {
  const banner = document.getElementById('sync-status-banner');
  const txt = document.getElementById('sync-banner-text');
  if (!banner) return;

  banner.classList.remove('d-none', 'status-ok', 'status-warn');
  banner.classList.add('d-flex', 'status-banner', 'fade-in-up');

  txt.textContent = msg;
  banner.classList.toggle('status-warn', !!isError);
  banner.classList.toggle('status-ok', !isError);

  setTimeout(() => {
    banner.classList.add('d-none');
    banner.classList.remove('flex');
  }, 4500);
}

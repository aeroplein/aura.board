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

    if (res.ok) {
      const data = await res.json();
      if (data.success) {
        activeSyncQueue = [];
        setBoards(data.boards || []);
        showSyncBanner('Sync completed. Cloud database update verified.', false);
        refreshStudioDisplay();
      }
    } else {
      throw new Error('Sync returned error status.');
    }
  } catch (err) {
    document.getElementById('offline-pill').classList.remove('hidden');
    document.getElementById('offline-pill').classList.add('flex');
    showSyncBanner('Offline mode toggled. Action saved in local cache.', true);
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

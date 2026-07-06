import { escapeHtml } from '../utils/html.js';
import { parseJsonResponse } from '../services/apiClient.js';

export async function fetchCollaborationActivity({
  getCurrentUser,
  fetchWithCredentials
}) {
  const feed = document.getElementById('collab-updates-list');
  if (!getCurrentUser() || !feed) return;

  try {
    const res = await fetchWithCredentials('/api/boards/activity');

    if (res.ok) {
      const logs = await parseJsonResponse(res, 'Could not load collaboration activity.') || [];
      if (logs.length === 0) {
        feed.innerHTML = `
          <li class="feed-note p-2.5 rounded-xl bg-white dark:bg-[#1E1B2E] border border-[#C8B6FF]/20 text-center font-mono text-[10px] text-[#9F86C0]">
            No recent collaboration activity yet.
          </li>
        `;
        return;
      }

      feed.innerHTML = logs.map(log => {
        const timeStr = new Date(log.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        const namePart = escapeHtml(log.actorLabel || (log.userEmail || 'collaborator').split('@')[0]);
        const actionDescription = escapeHtml(log.actionDescription || 'Updated a board.');
        return `
          <li class="feed-note p-2.5 rounded-xl bg-white dark:bg-[#1E1B2E] border border-[#C8B6FF]/20 font-mono shadow-xs mb-2">
            <div class="d-flex justify-content-between text-[10px] text-[#9F86C0]">
              <span>@${namePart}</span>
              <span>${timeStr}</span>
            </div>
            <p class="mb-0 mt-1 font-sans text-[#5E548E] dark:text-cream leading-normal">${actionDescription}</p>
          </li>
        `;
      }).join('');
    }
  } catch (err) {
    console.warn('Failed to fetch workspace activity logs:', err);
  }
}

export function startCollaborationTicker(context) {
  let inFlight = false;

  const tick = async () => {
    if (document.hidden || inFlight) return;
    inFlight = true;
    try {
      await fetchCollaborationActivity(context);
    } finally {
      inFlight = false;
    }
  };

  tick();
  return setInterval(tick, 30000);
}

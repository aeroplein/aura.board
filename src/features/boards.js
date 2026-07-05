import { escapeHtml } from '../utils/html.js';
import { parseJsonResponse } from '../services/apiClient.js';

export async function fetchUserBoards({
  fetchWithCredentials,
  setBoards,
  renderUserBoardsList,
  populatePushSelection,
  openPendingBoardLink,
  showSyncBanner
}) {
  try {
    const res = await fetchWithCredentials('/api/boards');
    if (res.ok) {
      setBoards(await parseJsonResponse(res, 'Could not load boards from the server.') || []);
      renderUserBoardsList();
      populatePushSelection();
      openPendingBoardLink();
    } else {
      const data = await parseJsonResponse(res, 'Could not load boards from the server.');
      showSyncBanner(data?.error || 'Could not load boards from the server.', true);
    }
  } catch (e) {
    showSyncBanner(e.message || 'Could not load boards from the server. Check your connection and try again.', true);
  }
}

export function openPendingBoardLink({
  getPendingBoardLinkId,
  setPendingBoardLinkId,
  getCurrentBoardId,
  getBoards,
  enterBoardStudio,
  showSyncBanner
}) {
  const pendingBoardLinkId = getPendingBoardLinkId();
  if (!pendingBoardLinkId || getCurrentBoardId()) return;

  const linkedBoard = getBoards().find(board => board.id === pendingBoardLinkId);
  if (linkedBoard) {
    const openedBoardId = pendingBoardLinkId;
    setPendingBoardLinkId(null);
    enterBoardStudio(openedBoardId);
    showSyncBanner(`Opened shared board "${linkedBoard.title}".`, false);
  } else {
    showSyncBanner('Sign in with the invited email address to open this shared board link.', true);
  }
}

export function renderUserBoardsList({
  getBoards,
  getBoardsSearchQuery,
  enterBoardStudio,
  openBoardEditor,
}) {
  const grid = document.getElementById('boards-grid-list');
  const countSpan = document.getElementById('board-count-indicator');
  if (!grid) return;

  const boards = getBoards();
  const boardsSearchQuery = getBoardsSearchQuery();

  if (boards.length === 0) {
    countSpan.textContent = '0 boards saved';
    grid.innerHTML = `
      <div class="col-12 state-empty py-12 text-center text-[#9F86C0] bg-white/40 dark:bg-black/10 rounded-3xl border border-dashed border-[#C8B6FF]/35">
        <i data-lucide="folder-open" class="w-10 h-10 text-[#C8B6FF] mx-auto mb-2 opacity-80"></i>
        <h4 class="font-bold text-sm mb-1 text-[#5E548E]">No boards yet</h4>
        <p class="text-xs leading-relaxed max-w-xs mx-auto">Create your first vision board with the button above. It will appear here when saved.</p>
      </div>
    `;
    lucide.createIcons();
    return;
  }

  const query = (boardsSearchQuery || '').toLowerCase().trim();
  const filteredBoards = boards.filter(b => {
    if (!query) return true;
    const matchesTitle = b.title && b.title.toLowerCase().includes(query);
    const matchesCategory = b.category && b.category.toLowerCase().includes(query);
    return matchesTitle || matchesCategory;
  });

  if (filteredBoards.length === 0) {
    countSpan.textContent = `0 of ${boards.length} grids matching`;
    grid.innerHTML = `
      <div class="col-12 state-empty py-12 text-center text-[#9F86C0] bg-white/40 dark:bg-black/10 rounded-3xl border border-dashed border-[#C8B6FF]/35 font-sans">
        <i data-lucide="search" class="w-10 h-10 text-[#9F86C0] mx-auto mb-2 opacity-80"></i>
        <h4 class="font-bold text-sm mb-1 text-[#5E548E]">Nothing matched</h4>
        <p class="text-xs leading-relaxed max-w-xs mx-auto">No boards titled or tagged like "${escapeHtml(boardsSearchQuery)}". Try a shorter phrase or clear the search.</p>
      </div>
    `;
    lucide.createIcons();
    return;
  }

  countSpan.textContent = query 
    ? `${filteredBoards.length} of ${boards.length} board${boards.length !== 1 ? 's' : ''} filtered` 
    : `${boards.length} grid${boards.length > 1 ? 's' : ''} loaded`;

  grid.innerHTML = '';

  filteredBoards.forEach((b, idx) => {
    const totalItems = b.items ? b.items.length : 0;
    const safeCategory = escapeHtml(b.category || 'General');
    const safeTitle = escapeHtml(b.title || 'Untitled board');
    const safeDescription = escapeHtml(b.description || 'No description yet.');
    const card = document.createElement('div');
    card.className = 'col-md-6 board-grid-item';
    card.style.animationDelay = `${Math.min(idx * 0.07, 0.42)}s`;
    card.innerHTML = `
      <div class="board-preview-card glass-card p-4 h-100 d-flex flex-column justify-content-between">
        <div class="space-y-2">
          <div class="d-flex justify-content-between align-items-start gap-2">
            <div>
              <span class="board-card-category font-mono uppercase bg-lilac/30 text-[#5E548E] px-2 py-0.5 rounded-full font-bold">
                ${safeCategory}
              </span>
              <h4 class="board-card-title font-extrabold text-[#5E548E] mb-1">${safeTitle}</h4>
            </div>
            
            <button type="button" class="p-1 px-2 border text-[#5E548E] rounded-lg btn-edit-board-cfg" data-id="${b.id}" title="Edit board settings" aria-label="Edit board settings">
              <i data-lucide="edit-3" class="w-3.5 h-3.5"></i>
            </button>
          </div>
          <p class="board-card-desc line-clamp-2 pr-2 mb-0">${safeDescription}</p>
        </div>

        <div class="board-card-footer border-top border-[#C8B6FF]/20 d-flex justify-content-between align-items-center">
          <span class="board-card-meta font-mono d-flex align-items-center gap-1">
            <i data-lucide="palette" class="w-3.5 h-3.5"></i> ${totalItems} item${totalItems !== 1 ? 's' : ''}
          </span>
          <button type="button" class="px-3.5 py-1.5 bg-[#5E548E] text-white text-xs font-bold rounded-xl btn-enter-studio" data-id="${b.id}" aria-label="Open ${safeTitle}">
            Open board
          </button>
        </div>
      </div>
    `;
    grid.appendChild(card);
  });

  document.querySelectorAll('.btn-enter-studio').forEach(btn => {
    btn.addEventListener('click', (e) => {
      const bid = e.currentTarget.getAttribute('data-id');
      enterBoardStudio(bid);
    });
  });

  document.querySelectorAll('.btn-edit-board-cfg').forEach(btn => {
    btn.addEventListener('click', (e) => {
      e.stopPropagation();
      const bid = e.currentTarget.getAttribute('data-id');
      const targ = boards.find(o => o.id === bid);
      if (targ) {
        openBoardEditor(targ);
      }
    });
  });

  lucide.createIcons();
}

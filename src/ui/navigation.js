export function showTab(tabId, { renderUserBoardsList, refreshStudioDisplay, renderUserSettings }) {
  ['home', 'studio', 'gallery', 'settings', 'user-settings'].forEach(id => {
    document.getElementById(`view-${id}`)?.classList.add('d-none');
    document.getElementById(`btn-tab-${id}`)?.classList.remove('bg-white', 'shadow-sm', 'text-[#9F86C0]');
  });

  document.getElementById(`view-${tabId}`)?.classList.remove('d-none');

  const btn = document.getElementById(`btn-tab-${tabId}`);
  if (btn) {
    btn.classList.add('bg-white', 'shadow-sm', 'text-[#9F86C0]');
  }

  const navLinks = document.getElementById('nav-links');
  const menuToggle = document.getElementById('nav-menu-toggle');
  navLinks?.classList.remove('is-open');
  menuToggle?.setAttribute('aria-expanded', 'false');

  if (tabId === 'home') {
    renderUserBoardsList();
  } else if (tabId === 'studio') {
    refreshStudioDisplay();
  } else if (tabId === 'user-settings') {
    renderUserSettings?.();
  }
}

export function setupNavTriggers({
  getCurrentUser,
  getBoards,
  getCurrentBoardId,
  showTab,
  authModalObj,
  boardModalObj,
  exportPdfModalObj,
  showSyncBanner
}) {
  const navLinks = document.getElementById('nav-links');
  const menuToggle = document.getElementById('nav-menu-toggle');

  menuToggle?.addEventListener('click', () => {
    const isOpen = navLinks?.classList.toggle('is-open') || false;
    menuToggle.setAttribute('aria-expanded', String(isOpen));
  });

  ['home', 'studio', 'gallery', 'settings'].forEach(tab => {
    document.getElementById(`btn-tab-${tab}`).addEventListener('click', () => {
      if (!getCurrentUser()) {
        authModalObj.show();
        return;
      }
      showTab(tab);
    });
  });

  document.getElementById('btn-nav-gallery-promo').addEventListener('click', () => showTab('gallery'));
  document.getElementById('btn-new-board-trigger').addEventListener('click', () => {
    document.getElementById('board-form').reset();
    document.getElementById('board-field-id').value = '';
    document.getElementById('btn-delete-board').classList.add('d-none');
    document.getElementById('boardModalHeaderTitle').textContent = 'Curate A New Workspace';
    boardModalObj.show();
  });

  document.getElementById('btn-darkMode').addEventListener('click', () => {
    document.documentElement.classList.toggle('dark');
    const isDark = document.documentElement.classList.contains('dark');
    localStorage.setItem('aura-dark-mode', isDark);
  });

  if (localStorage.getItem('aura-dark-mode') === 'true') {
    document.documentElement.classList.add('dark');
  }

  const exportBtn = document.getElementById('export-pdf-studio-btn');
  if (exportBtn) {
    exportBtn.addEventListener('click', () => {
      const activeBoard = getBoards().find(o => o.id === getCurrentBoardId());
      if (!activeBoard) {
        showSyncBanner('Please open an active vision board first to export.', true);
        return;
      }
      exportPdfModalObj.show();
    });
  }

  const btnBack = document.getElementById('btn-studio-back');
  if (btnBack) {
    btnBack.addEventListener('click', () => {
      showTab('home');
    });
  }
}

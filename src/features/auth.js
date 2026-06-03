import { escapeHtml } from '../utils/html.js';

export async function loadCachedSession({
  fetchWithCredentials,
  setCurrentUser,
  renderUserProfileUI,
  fetchUserBoards,
  showTab,
  authModalObj
}) {
  try {
    const res = await fetchWithCredentials('/api/auth/session');
    if (res.ok) {
      const data = await res.json();
      setCurrentUser(data.user);
      renderUserProfileUI();
      fetchUserBoards();
      showTab('home');
      return;
    }
  } catch (e) {
    console.warn('Unable to restore cookie session:', e);
  }

  setCurrentUser(null);
  toggleAuthMode(true);
  authModalObj.show();
}

export async function handleLogout({
  fetchWithCredentials,
  clearSessionState,
  renderUserProfileUI,
  showTab,
  authModalObj
}) {
  try {
    await fetchWithCredentials('/api/auth/logout', { method: 'POST' });
  } catch (e) {
    console.warn('Logout request failed:', e);
  }

  clearSessionState();

  const sInput = document.getElementById('board-search-input');
  if (sInput) sInput.value = "";
  const sClear = document.getElementById('board-search-clear-btn');
  if (sClear) sClear.classList.add('hidden');

  renderUserProfileUI();
  showTab('home');
  authModalObj.show();
}

export function renderUserProfileUI({
  getCurrentUser,
  handleLogout,
  openUserSettings,
  authModalObj
}) {
  const container = document.getElementById('session-profile');
  if (!container) return;

  const currentUser = getCurrentUser();

  if (currentUser) {
    const safeName = escapeHtml(currentUser.name || 'User');
    const safeEmail = escapeHtml(currentUser.email || 'Signed in');
    const initials = (currentUser.name || 'US').substring(0, 2).toUpperCase();
    container.innerHTML = `
      <div class="session-card d-flex align-items-center gap-2">
        <button id="btn-profile-settings" type="button" class="session-profile-button d-flex align-items-center gap-2 border-0 bg-transparent p-0 cursor-pointer" aria-label="Open user settings for ${safeName}" title="${safeEmail}">
          <span class="session-copy text-right">
            <span class="session-name text-xs font-bold text-plum dark:text-cream mb-0 leading-tight d-block">${safeName}</span>
            <span class="session-status text-[9px] font-mono text-dusty uppercase d-block">Curating Active</span>
          </span>
          <span class="session-avatar h-8 w-8 rounded-full bg-gradient-to-tr from-lilac to-lavender d-flex align-items-center justify-content-center text-[#5E548E] font-bold text-xs ring-2 ring-[#C8B6FF]/30 select-none">
            ${escapeHtml(initials)}
          </span>
        </button>
        <button id="btn-logout" type="button" class="p-1.5 rounded-lg border hover:bg-red-50 text-red-500 cursor-pointer align-middle ml-1" title="Logout Session" aria-label="Log out">
          <i data-lucide="log-out" class="w-3.5 h-3.5"></i>
        </button>
      </div>
    `;

    document.getElementById('btn-profile-settings').addEventListener('click', openUserSettings);
    document.getElementById('btn-logout').addEventListener('click', handleLogout);
    lucide.createIcons();
  } else {
    container.innerHTML = `
      <button id="btn-login-trigger" type="button" class="px-3.5 py-1.5 bg-[#5E548E] hover:bg-[#9F86C0] text-white text-xs font-bold rounded-xl transition-all cursor-pointer">
        Sign In
      </button>
    `;
    document.getElementById('btn-login-trigger').addEventListener('click', () => {
      toggleAuthMode(false);
      authModalObj.show();
    });
  }
}

export function setupAuthForm({
  fetchWithCredentials,
  setCurrentUser,
  authModalObj,
  renderUserProfileUI,
  fetchUserBoards,
  showTab
}) {
  const form = document.getElementById('auth-form');
  const alert = document.getElementById('auth-alert');
  const toggleBtn = document.getElementById('btn-toggle-auth-mode');

  let isRegisterMode = true;

  toggleBtn.addEventListener('click', () => {
    isRegisterMode = !isRegisterMode;
    toggleAuthMode(isRegisterMode);
  });

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    alert.classList.add('d-none');
    const submitBtn = document.getElementById('btn-auth-submit');
    const originalSubmitText = submitBtn?.textContent || '';
    if (submitBtn) {
      submitBtn.disabled = true;
      submitBtn.textContent = isRegisterMode ? 'Creating workspace...' : 'Signing in...';
    }

    const email = document.getElementById('auth-input-email').value;
    const password = document.getElementById('auth-input-password').value;
    const name = document.getElementById('auth-input-name').value;

    const endpoint = isRegisterMode ? '/api/auth/register' : '/api/auth/login';
    const bodyObj = isRegisterMode ? { email, password, name } : { email, password };

    try {
      const res = await fetchWithCredentials(endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(bodyObj)
      });

      const data = await res.json();
      if (!res.ok) {
        throw new Error(data.error || 'Identity verification failed.');
      }

      setCurrentUser(data.user);

      authModalObj.hide();
      renderUserProfileUI();
      fetchUserBoards();
      showTab('home');

    } catch (err) {
      alert.textContent = err.message || 'Could not complete sign-in. Check your details and try again.';
      alert.classList.remove('d-none');
    } finally {
      if (submitBtn) {
        submitBtn.disabled = false;
        submitBtn.textContent = originalSubmitText;
      }
    }
  });
}

export function toggleAuthMode(regMode) {
  const nameGroup = document.getElementById('group-auth-name');
  const title = document.getElementById('authModalTitle');
  const toggleBtn = document.getElementById('btn-toggle-auth-mode');
  const submitBtn = document.getElementById('btn-auth-submit');

  if (regMode) {
    nameGroup.classList.remove('d-none');
    title.textContent = 'Curate in aura.board';
    toggleBtn.innerHTML = 'Already curating? <strong class="text-[#5E548E]">Sign In</strong>';
    submitBtn.textContent = 'Assemble Workspace';
  } else {
    nameGroup.classList.add('d-none');
    title.textContent = 'Welcome back to aura.';
    toggleBtn.innerHTML = 'New to the studio? <strong class="text-[#5E548E]">Sign Up</strong>';
    submitBtn.textContent = 'Unlock Canvas Gate';
  }
}

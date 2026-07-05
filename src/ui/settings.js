import { parseJsonResponse } from '../services/apiClient.js';
import { showConfirmDialog } from './confirmDialog.js';

const DEFAULT_PREFERENCES = {
  darkMode: false,
  notificationsEnabled: true,
  highContrast: false
};

function getUserPreferences(user) {
  return {
    ...DEFAULT_PREFERENCES,
    ...(user?.preferences || {})
  };
}

function setState(visibleState) {
  ['loading', 'error', 'empty'].forEach(state => {
    document.getElementById(`settings-${state}-state`)?.classList.toggle('d-none', visibleState !== state);
  });

  document.getElementById('settings-content')?.classList.toggle('d-none', visibleState !== 'content');
}

function applyPreferenceEffects(preferences) {
  document.documentElement.classList.toggle('dark', Boolean(preferences.darkMode));
  document.body.classList.toggle('grayscale', Boolean(preferences.highContrast));
  localStorage.setItem('aura-dark-mode', String(Boolean(preferences.darkMode)));
}

function setToggleChecked(id, value) {
  const input = document.getElementById(id);
  if (input) input.checked = Boolean(value);
}

export function renderUserSettings({ getCurrentUser }) {
  const user = getCurrentUser();
  if (!user) {
    setState('empty');
    return;
  }

  const preferences = getUserPreferences(user);
  setState('content');

  const nameEl = document.getElementById('settings-profile-name');
  const emailEl = document.getElementById('settings-profile-email');
  if (nameEl) nameEl.textContent = user.name || 'Aura user';
  if (emailEl) emailEl.textContent = user.email || 'No email available';
  setToggleChecked('user-settings-toggle-notif', preferences.notificationsEnabled);
  setToggleChecked('user-settings-toggle-theme', preferences.darkMode);
  setToggleChecked('user-settings-toggle-contrast', preferences.highContrast);
  setToggleChecked('settings-toggle-notif', preferences.notificationsEnabled);
  setToggleChecked('settings-toggle-contrast', preferences.highContrast);

  applyPreferenceEffects(preferences);
  lucide.createIcons();
}

export function setupSettingsHandlers({
  getCurrentUser,
  setCurrentUser,
  fetchWithCredentials,
  showSyncBanner,
  handleLogout
}) {
  const toggleNotif = document.getElementById('settings-toggle-notif');
  const toggleContr = document.getElementById('settings-toggle-contrast');
  const userToggleNotif = document.getElementById('user-settings-toggle-notif');
  const userToggleTheme = document.getElementById('user-settings-toggle-theme');
  const userToggleContr = document.getElementById('user-settings-toggle-contrast');
  const logoutBtn = document.getElementById('btn-purge-caches');
  const userLogoutBtn = document.getElementById('btn-user-settings-logout');

  async function savePreferences(nextPreferences, successMessage) {
    const user = getCurrentUser();
    if (!user) {
      setState('empty');
      return;
    }

    setState('loading');

    try {
      const res = await fetchWithCredentials('/api/auth/preferences', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(nextPreferences)
      });

      const data = await parseJsonResponse(res, 'Preference update failed.');
      if (!res.ok) {
        throw new Error(data?.error || 'Preference update failed.');
      }

      const savedPreferences = data.preferences || nextPreferences;
      setCurrentUser({
        ...user,
        preferences: savedPreferences
      });
      applyPreferenceEffects(savedPreferences);
      renderUserSettings({ getCurrentUser });
      showSyncBanner(successMessage, false);
    } catch (error) {
      console.warn('Preference update failed:', error);
      setState('error');
      showSyncBanner(error.message || 'Could not save settings. Please try again.', true);
    }
  }

  function handlePreferenceChange(changedValues, message) {
    const user = getCurrentUser();
    if (!user) {
      setState('empty');
      return;
    }

    const nextPreferences = {
      ...getUserPreferences(user),
      ...changedValues
    };

    applyPreferenceEffects(nextPreferences);
    savePreferences(nextPreferences, message);
  }

  toggleNotif?.addEventListener('change', () => {
    handlePreferenceChange(
      { notificationsEnabled: toggleNotif.checked },
      `Preference saved. Collaborative alerts: ${toggleNotif.checked ? 'Enabled' : 'Disabled'}.`
    );
  });

  userToggleNotif?.addEventListener('change', () => {
    handlePreferenceChange(
      { notificationsEnabled: userToggleNotif.checked },
      `Preference saved. Collaborative alerts: ${userToggleNotif.checked ? 'Enabled' : 'Disabled'}.`
    );
  });

  userToggleTheme?.addEventListener('change', () => {
    handlePreferenceChange(
      { darkMode: userToggleTheme.checked },
      `Preference saved. Theme: ${userToggleTheme.checked ? 'Dark' : 'Light'}.`
    );
  });

  toggleContr?.addEventListener('change', () => {
    handlePreferenceChange(
      { highContrast: toggleContr.checked },
      `Preference saved. Contrast: ${toggleContr.checked ? 'Enabled' : 'Disabled'}.`
    );
  });

  userToggleContr?.addEventListener('change', () => {
    handlePreferenceChange(
      { highContrast: userToggleContr.checked },
      `Preference saved. Contrast: ${userToggleContr.checked ? 'Enabled' : 'Disabled'}.`
    );
  });

  logoutBtn?.addEventListener('click', async () => {
    const confirmed = await showConfirmDialog({
      eyebrow: 'Local session cleanup',
      title: 'Wipe local cache?',
      message: 'This clears temporary session keys from this browser. Your saved boards stay in the cloud database.',
      confirmText: 'Wipe cache',
      cancelText: 'Not now'
    });

    if (confirmed) {
      handleLogout();
    }
  });

  userLogoutBtn?.addEventListener('click', () => {
    handleLogout();
  });

  document.getElementById('settings-content')?.setAttribute('aria-live', 'polite');
}

import { parseJsonResponse } from '../services/apiClient.js';
import { escapeHtml } from '../utils/html.js';
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

function getUserInitials(user) {
  const source = String(user?.name || user?.username || 'US').trim();
  const parts = source.split(/\s+/).filter(Boolean);
  if (parts.length >= 2) {
    return `${parts[0][0]}${parts[1][0]}`.toUpperCase();
  }

  return source.substring(0, 2).toUpperCase();
}

function renderAvatarPreview(user, avatarUrl = user?.avatarUrl) {
  const preview = document.getElementById('settings-avatar-preview');
  if (!preview) return;

  const cleanAvatarUrl = String(avatarUrl || '').trim();
  if (cleanAvatarUrl) {
    preview.innerHTML = `<img src="${escapeHtml(cleanAvatarUrl)}" alt="${escapeHtml(user?.name || 'Profile')} profile picture preview" class="w-full h-full object-cover" referrerpolicy="no-referrer" />`;
    return;
  }

  preview.textContent = getUserInitials(user);
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
  const usernameEl = document.getElementById('settings-profile-username');
  const emailEl = document.getElementById('settings-profile-email');
  if (nameEl) nameEl.textContent = user.name || 'Aura user';
  if (usernameEl) usernameEl.textContent = user.username ? `@${user.username}` : 'No username yet';
  if (emailEl) emailEl.textContent = user.email || 'No email available';
  const displayNameInput = document.getElementById('settings-profile-display-name');
  const usernameInput = document.getElementById('settings-profile-username-input');
  const avatarUrlInput = document.getElementById('settings-profile-avatar-url');
  const avatarFileInput = document.getElementById('settings-profile-avatar-file');
  if (displayNameInput) displayNameInput.value = user.name || '';
  if (usernameInput) usernameInput.value = user.username ? `@${user.username}` : '';
  if (avatarUrlInput) avatarUrlInput.value = user.avatarUrl || '';
  if (avatarFileInput) avatarFileInput.value = '';
  renderAvatarPreview(user);
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
  const profileForm = document.getElementById('settings-profile-form');
  const avatarFileInput = document.getElementById('settings-profile-avatar-file');
  const avatarUrlInput = document.getElementById('settings-profile-avatar-url');
  const clearAvatarBtn = document.getElementById('settings-profile-clear-avatar');
  const profileSaveBtn = document.getElementById('settings-profile-save');

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

  async function uploadAvatarFile(file) {
    if (!file) return;
    if (!['image/jpeg', 'image/png', 'image/webp', 'image/gif'].includes(file.type)) {
      showSyncBanner('Profile picture must be JPG, PNG, WebP, or GIF.', true);
      return;
    }

    if (file.size > 5 * 1024 * 1024) {
      showSyncBanner('Profile picture is larger than 5MB. Choose a smaller image.', true);
      return;
    }

    const reader = new FileReader();
    reader.onload = async () => {
      const submitText = profileSaveBtn?.textContent || '';
      if (profileSaveBtn) {
        profileSaveBtn.disabled = true;
        profileSaveBtn.textContent = 'Uploading...';
      }

      try {
        const res = await fetchWithCredentials('/api/upload', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            base64Data: reader.result,
            mimeType: file.type,
            fileName: file.name
          })
        });

        const data = await parseJsonResponse(res, 'Profile picture upload failed.');
        if (!res.ok || !data.url) {
          throw new Error(data?.error || 'Profile picture upload failed.');
        }

        if (avatarUrlInput) avatarUrlInput.value = data.url;
        renderAvatarPreview(getCurrentUser(), data.url);
        showSyncBanner('Profile picture uploaded. Save profile to keep it.', false);
      } catch (error) {
        console.warn('Profile picture upload failed:', error);
        showSyncBanner(error.message || 'Profile picture upload failed.', true);
      } finally {
        if (profileSaveBtn) {
          profileSaveBtn.disabled = false;
          profileSaveBtn.textContent = submitText;
        }
      }
    };

    reader.onerror = () => {
      showSyncBanner('Could not read that profile picture file.', true);
    };

    reader.readAsDataURL(file);
  }

  async function saveProfile() {
    const user = getCurrentUser();
    if (!user) {
      setState('empty');
      return;
    }

    const displayNameInput = document.getElementById('settings-profile-display-name');
    const usernameInput = document.getElementById('settings-profile-username-input');
    const nextName = String(displayNameInput?.value || '').trim();
    const nextUsername = String(usernameInput?.value || '').trim().replace(/^@+/, '');
    const nextAvatarUrl = String(avatarUrlInput?.value || '').trim();

    if (nextName.length < 2) {
      showSyncBanner('Display name needs at least 2 characters.', true);
      displayNameInput?.focus();
      return;
    }

    if (nextUsername && !/^[A-Za-z0-9_.]{3,30}$/.test(nextUsername)) {
      showSyncBanner('Username can use 3-30 letters, numbers, underscores, or dots.', true);
      usernameInput?.focus();
      return;
    }

    const submitText = profileSaveBtn?.textContent || '';
    if (profileSaveBtn) {
      profileSaveBtn.disabled = true;
      profileSaveBtn.textContent = 'Saving...';
    }

    try {
      const res = await fetchWithCredentials('/api/auth/profile', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: nextName,
          username: nextUsername || null,
          avatarUrl: nextAvatarUrl || null
        })
      });

      const data = await parseJsonResponse(res, 'Profile update failed.');
      if (!res.ok) {
        throw new Error(data?.error || 'Profile update failed.');
      }

      setCurrentUser(data.user || {
        ...user,
        name: nextName,
        username: nextUsername || null,
        avatarUrl: nextAvatarUrl || null
      });
      renderUserSettings({ getCurrentUser });
      showSyncBanner('Profile saved.', false);
    } catch (error) {
      console.warn('Profile update failed:', error);
      showSyncBanner(error.message || 'Could not save profile. Please try again.', true);
    } finally {
      if (profileSaveBtn) {
        profileSaveBtn.disabled = false;
        profileSaveBtn.textContent = submitText;
      }
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

  avatarFileInput?.addEventListener('change', () => {
    uploadAvatarFile(avatarFileInput.files?.[0]);
  });

  clearAvatarBtn?.addEventListener('click', () => {
    const user = getCurrentUser();
    if (avatarUrlInput) avatarUrlInput.value = '';
    if (avatarFileInput) avatarFileInput.value = '';
    renderAvatarPreview(user, '');
  });

  profileForm?.addEventListener('submit', (event) => {
    event.preventDefault();
    saveProfile();
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

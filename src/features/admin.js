import { escapeHtml } from '../utils/html.js';
import { showConfirmDialog } from '../ui/confirmDialog.js';

export function setupAdminPanel({ fetchWithCredentials, parseJsonResponse, getCurrentUser, showTab }) {
  let currentPage = 1;
  let totalPages = 0;
  let searchQuery = '';

  const adminButton = document.getElementById('btn-tab-admin');
  const inviteForm = document.getElementById('admin-invite-form');
  const searchForm = document.getElementById('admin-user-search-form');
  const usersBody = document.getElementById('admin-users-body');

  adminButton?.addEventListener('click', () => {
    if (!getCurrentUser()?.isAdmin) return;
    showTab('admin');
    loadAdminPanel();
  });

  document.getElementById('admin-refresh')?.addEventListener('click', loadAdminPanel);
  document.getElementById('admin-users-prev')?.addEventListener('click', () => {
    if (currentPage <= 1) return;
    currentPage--;
    loadUsers();
  });
  document.getElementById('admin-users-next')?.addEventListener('click', () => {
    if (currentPage >= totalPages) return;
    currentPage++;
    loadUsers();
  });

  searchForm?.addEventListener('submit', event => {
    event.preventDefault();
    searchQuery = document.getElementById('admin-user-search').value.trim();
    currentPage = 1;
    loadUsers();
  });

  inviteForm?.addEventListener('submit', async event => {
    event.preventDefault();
    const name = document.getElementById('admin-invite-name').value.trim();
    const email = document.getElementById('admin-invite-email').value.trim();
    try {
      await adminMutation('/api/admin/users/invite', {
        method: 'POST',
        body: JSON.stringify({ name, email })
      });
      inviteForm.reset();
      showFeedback(`A secure invitation was sent to ${email}.`);
      await loadAdminPanel();
    } catch (error) {
      showFeedback(error.message, true);
    }
  });

  usersBody?.addEventListener('click', async event => {
    const button = event.target.closest('[data-admin-action]');
    if (!button) return;

    const { adminAction: action, userId, userEmail } = button.dataset;
    let url = `/api/admin/users/${encodeURIComponent(userId)}`;
    let options = { method: 'POST' };

    if (action === 'delete') {
      const confirmed = await showConfirmDialog({
        eyebrow: 'Permanent account action',
        title: 'Delete this user?',
        message: 'This permanently removes the account, owned boards, and unshared uploads. This action cannot be undone.',
        confirmText: 'Delete user',
        cancelText: 'Keep user',
        requiredText: userEmail,
        inputLabel: 'Type this email to confirm'
      });
      if (!confirmed) return;
      url = `${url}`;
      options = { method: 'DELETE', body: JSON.stringify({ confirmationEmail: userEmail }) };
    } else if (action === 'suspend' || action === 'reactivate') {
      const isSuspending = action === 'suspend';
      const confirmed = await showConfirmDialog({
        eyebrow: isSuspending ? 'Account access' : 'Restore access',
        title: isSuspending ? 'Suspend this user?' : 'Reactivate this user?',
        message: isSuspending
          ? `${userEmail} will be signed out and unable to access their account until reactivated.`
          : `${userEmail} will be able to sign in and access their account again.`,
        confirmText: isSuspending ? 'Suspend user' : 'Reactivate user',
        cancelText: 'Cancel',
        tone: isSuspending ? 'danger' : 'neutral'
      });
      if (!confirmed) return;
      url = `${url}/${action}`;
    } else if (action === 'grant-admin' || action === 'remove-admin') {
      const isAdmin = action === 'grant-admin';
      const confirmed = await showConfirmDialog({
        eyebrow: 'Administrator role',
        title: isAdmin ? 'Make this user an admin?' : 'Remove admin access?',
        message: `${userEmail} ${isAdmin ? 'will gain access to user management and audit tools' : 'will lose access to administrative tools'}. Active sessions will be revoked.`,
        confirmText: isAdmin ? 'Make admin' : 'Remove access',
        cancelText: 'Cancel',
        tone: isAdmin ? 'neutral' : 'danger'
      });
      if (!confirmed) return;
      url = `${url}/role`;
      options.body = JSON.stringify({ isAdmin });
    } else {
      return;
    }

    button.disabled = true;
    try {
      await adminMutation(url, options);
      showFeedback(`Admin action completed for ${userEmail}.`);
      await loadAdminPanel();
    } catch (error) {
      showFeedback(error.message, true);
    } finally {
      button.disabled = false;
    }
  });

  async function loadAdminPanel() {
    if (!getCurrentUser()?.isAdmin) {
      showTab('home');
      return;
    }

    try {
      await Promise.all([loadDashboard(), loadUsers(), loadAudit()]);
      window.lucide?.createIcons();
    } catch (error) {
      showFeedback(error.message, true);
    }
  }

  async function loadDashboard() {
    const data = await adminGet('/api/admin/dashboard');
    setText('admin-total-users', data.totalUsers);
    setText('admin-verified-users', data.verifiedUsers);
    setText('admin-pending-invites', data.pendingInvitations);
    setText('admin-suspended-users', data.suspendedUsers);
    setText('admin-admin-users', data.adminUsers);
    setText('admin-total-boards', data.totalBoards);
  }

  async function loadUsers() {
    const query = new URLSearchParams({ page: String(currentPage), pageSize: '20' });
    if (searchQuery) query.set('search', searchQuery);
    const data = await adminGet(`/api/admin/users?${query}`);
    totalPages = data.totalPages;
    currentPage = data.page;
    renderUsers(data.users || []);
    setText('admin-users-page-label', `${data.totalCount} users | page ${data.page} of ${Math.max(data.totalPages, 1)}`);
    document.getElementById('admin-users-prev').disabled = currentPage <= 1;
    document.getElementById('admin-users-next').disabled = totalPages === 0 || currentPage >= totalPages;
  }

  async function loadAudit() {
    const events = await adminGet('/api/admin/audit?limit=30');
    const list = document.getElementById('admin-audit-list');
    list.innerHTML = events.length
      ? events.map(event => `
          <article class="admin-audit-entry">
            <div class="d-flex flex-wrap justify-content-between gap-2">
              <strong>${escapeHtml(formatAction(event.action))}</strong>
              <time>${escapeHtml(formatDate(event.timestamp))}</time>
            </div>
            <div class="mt-1">${escapeHtml(event.adminEmail)} -> ${escapeHtml(event.targetEmail || 'system')}</div>
            ${event.details ? `<div class="admin-user-email mt-1">${escapeHtml(event.details)}</div>` : ''}
          </article>`).join('')
      : '<p class="text-xs text-[#9F86C0]">No administrative actions have been recorded yet.</p>';
  }

  function renderUsers(users) {
    const signedInUserId = String(getCurrentUser()?.id || '');
    usersBody.innerHTML = users.length
      ? users.map(user => {
          const isSelf = String(user.id) === signedInUserId;
          const status = [
            user.isAdmin ? badge('Admin') : '',
            user.isSuspended ? badge('Suspended', 'danger') : '',
            user.invitationPending ? badge('Invite pending') : '',
            user.isEmailVerified ? badge('Verified', 'success') : badge('Unverified')
          ].join('');
          const actions = isSelf
            ? '<span class="admin-user-email">Current account</span>'
            : [
                !user.isAdmin
                  ? actionButton(user, user.isSuspended ? 'reactivate' : 'suspend', user.isSuspended ? 'Reactivate' : 'Suspend')
                  : '',
                actionButton(user, user.isAdmin ? 'remove-admin' : 'grant-admin', user.isAdmin ? 'Remove admin' : 'Make admin'),
                actionButton(user, 'delete', 'Delete', true)
              ].join('');

          return `
            <tr>
              <td><strong>${escapeHtml(user.name)}</strong><div class="admin-user-email">${escapeHtml(user.email)}</div></td>
              <td>${status}</td>
              <td>${Number(user.ownedBoardCount) || 0}</td>
              <td class="text-end">${actions}</td>
            </tr>`;
        }).join('')
      : '<tr><td colspan="4" class="text-center text-[#9F86C0] py-4">No matching users.</td></tr>';
  }

  function actionButton(user, action, label, danger = false) {
    return `<button type="button" class="admin-action-button${danger ? ' is-danger' : ''}" data-admin-action="${action}" data-user-id="${escapeHtml(user.id)}" data-user-email="${escapeHtml(user.email)}">${escapeHtml(label)}</button>`;
  }

  function badge(label, variant = '') {
    return `<span class="admin-status-badge${variant ? ` is-${variant}` : ''}">${escapeHtml(label)}</span>`;
  }

  async function adminGet(url) {
    const response = await fetchWithCredentials(url);
    const data = await parseJsonResponse(response, 'Unable to load admin data.');
    if (!response.ok) throw new Error(data?.error || 'Unable to load admin data.');
    return data;
  }

  async function adminMutation(url, options) {
    const response = await fetchWithCredentials(url, {
      ...options,
      headers: {
        'Content-Type': 'application/json',
        'X-Admin-Request': 'true',
        ...(options.headers || {})
      }
    });
    const data = await parseJsonResponse(response, 'Admin action failed.');
    if (!response.ok) throw new Error(data?.error || 'Admin action failed.');
    return data;
  }

  function showFeedback(message, isError = false) {
    const panel = document.getElementById('admin-feedback');
    panel.textContent = message;
    panel.classList.remove('d-none', 'state-panel-success', 'state-panel-error');
    panel.classList.add(isError ? 'state-panel-error' : 'state-panel-success');
  }
}

function setText(id, value) {
  const element = document.getElementById(id);
  if (element) element.textContent = String(value ?? '--');
}

function formatAction(action) {
  return String(action || 'admin action').replaceAll('_', ' ');
}

function formatDate(value) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Unknown time' : date.toLocaleString();
}

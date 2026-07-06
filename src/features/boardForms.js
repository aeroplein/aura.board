import { parseJsonResponse } from '../services/apiClient.js';
import { showConfirmDialog } from '../ui/confirmDialog.js';

const BOARD_TITLE_MAX = 120;
const BOARD_DESCRIPTION_MAX = 500;
const BOARD_CATEGORY_MAX = 80;
const BOARD_COLLABORATORS_MAX = 25;

function isLikelyEmail(value) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(String(value || '').trim());
}

function getSaveErrorMessage(responseBody) {
  if (responseBody?.error) {
    return responseBody.error;
  }

  const validationErrors = responseBody?.errors;
  if (validationErrors && typeof validationErrors === 'object') {
    const firstMessages = Object.values(validationErrors)
      .flat()
      .filter(Boolean);
    if (firstMessages.length > 0) {
      return firstMessages[0];
    }
  }

  return 'Could not save this board. Please check the required fields and try again.';
}

export function setupBoardForm({
  fetchWithCredentials,
  boardModalObj,
  fetchUserBoards,
  showSyncBanner,
  getCurrentBoardId,
  setCurrentBoardId,
  showTab
}) {
  const form = document.getElementById('board-form');
  const sharedBox = document.getElementById('board-field-shared');
  const collabGroup = document.getElementById('board-collaborators-group');

  sharedBox.addEventListener('change', () => {
    if (sharedBox.checked) {
      collabGroup.classList.remove('d-none');
    } else {
      collabGroup.classList.add('d-none');
    }
  });

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const submitBtn = form.querySelector('button[type="submit"]');
    const originalSubmitText = submitBtn?.textContent || '';
    const bid = document.getElementById('board-field-id').value;
    const title = document.getElementById('board-field-title').value.trim();
    const description = document.getElementById('board-field-desc').value.trim();
    const category = document.getElementById('board-field-category').value.trim();
    const isShared = sharedBox.checked;
    const collaborators = document.getElementById('board-field-collabs').value
      .split(',')
      .map(s => s.trim())
      .filter(Boolean);
    const invalidCollaborators = collaborators.filter(email => !isLikelyEmail(email));

    if (isShared && invalidCollaborators.length > 0) {
      showSyncBanner(`Please fix collaborator email: ${invalidCollaborators[0]}`, true);
      return;
    }

    if (!title) {
      showSyncBanner('Please add a canvas title before saving.', true);
      return;
    }

    if (title.length > BOARD_TITLE_MAX) {
      showSyncBanner(`Canvas title must be ${BOARD_TITLE_MAX} characters or fewer.`, true);
      return;
    }

    if (description.length > BOARD_DESCRIPTION_MAX) {
      showSyncBanner(`Theme description must be ${BOARD_DESCRIPTION_MAX} characters or fewer.`, true);
      return;
    }

    if (category.length > BOARD_CATEGORY_MAX) {
      showSyncBanner(`Aesthetic category must be ${BOARD_CATEGORY_MAX} characters or fewer.`, true);
      return;
    }

    if (collaborators.length > BOARD_COLLABORATORS_MAX) {
      showSyncBanner(`Share with at most ${BOARD_COLLABORATORS_MAX} collaborators at a time.`, true);
      return;
    }

    let method = 'POST';
    let url = '/api/boards';
    
    const payload = {
      title,
      description,
      category,
      isShared,
      collaborators,
    };

    if (bid) {
      method = 'PUT';
      url = `/api/boards/${bid}`;
    } else {
      payload.id = crypto.randomUUID();
      payload.items = [];
    }

    try {
      if (submitBtn) {
        submitBtn.disabled = true;
        submitBtn.textContent = bid ? 'Saving board...' : 'Creating board...';
      }
      const res = await fetchWithCredentials(url, {
        method,
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(payload)
      });
      const resultBoard = await parseJsonResponse(res, 'Could not save this board. Please check the required fields and try again.');

      if (res.ok) {
        let inviteMessage = '';
        if (isShared && collaborators.length > 0 && resultBoard?.id) {
          inviteMessage = await sendBoardInvites(resultBoard.id, { fetchWithCredentials });
        }

        boardModalObj.hide();
        fetchUserBoards();
        showSyncBanner(inviteMessage || 'Workspace changes pushed successfully.', false);
      } else {
        showSyncBanner(getSaveErrorMessage(resultBoard), true);
      }
    } catch (e) {
      showSyncBanner(e.message || 'Could not reach the server to save this board. Try again when you are online.', true);
    } finally {
      if (submitBtn) {
        submitBtn.disabled = false;
        submitBtn.textContent = originalSubmitText;
      }
    }
  });

  document.getElementById('btn-delete-board').addEventListener('click', async () => {
    const bid = document.getElementById('board-field-id').value;
    const deleteBtn = document.getElementById('btn-delete-board');
    if (!bid) return;

    const confirmed = await showConfirmDialog({
      eyebrow: 'Permanent canvas action',
      title: 'Wipe this board?',
      message: 'This will permanently delete the board and every card inside it. This action cannot be undone.',
      confirmText: 'Wipe board',
      cancelText: 'Keep editing'
    });
    if (!confirmed) return;

    try {
      if (deleteBtn) {
        deleteBtn.disabled = true;
        deleteBtn.textContent = 'Deleting...';
      }
      const res = await fetchWithCredentials(`/api/boards/${bid}`, {
        method: 'DELETE'
      });
      const data = await parseJsonResponse(res, 'Could not delete this board. Please try again.');

      if (res.ok) {
        boardModalObj.hide();
        fetchUserBoards();
        if (getCurrentBoardId() === bid) setCurrentBoardId(null);
        showTab('home');
        showSyncBanner('Board deleted.', false);
      } else {
        showSyncBanner(data?.error || 'Could not delete this board. Please try again.', true);
      }
    } catch (e) {
      showSyncBanner(e.message || 'Could not reach the server to delete this board.', true);
    } finally {
      if (deleteBtn) {
        deleteBtn.disabled = false;
        deleteBtn.textContent = 'Wipe Board Entirely';
      }
    }
  });
}

export async function sendBoardInvites(boardId, { fetchWithCredentials }) {
  try {
    const res = await fetchWithCredentials(`/api/boards/${boardId}/invite`, {
      method: 'POST'
    });
    const data = await parseJsonResponse(res, 'Invitation email could not be prepared.');

    if (!res.ok) {
      return data?.error || 'Workspace saved, but invitation email could not be prepared.';
    }

    if (!data.configured) {
      return `Workspace saved. Invite link ready: ${data.boardUrl}`;
    }

    if (data.success) {
      return `Workspace saved and invitation email sent to ${data.sentCount} collaborator(s).`;
    }

    return `Workspace saved. Email invite failed, but invite link is ready: ${data.boardUrl}`;
  } catch (e) {
    return 'Workspace saved. Email invite could not be sent from this environment.';
  }
}

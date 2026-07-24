import { escapeHtml } from '../utils/html.js';

let activeConfirmDialog = null;
let dialogSequence = 0;

function closeActiveDialog(result) {
  if (!activeConfirmDialog) return;

  const { overlay, previousFocus, resolve } = activeConfirmDialog;
  activeConfirmDialog = null;
  overlay.classList.add('app-confirm-overlay-leaving');

  window.setTimeout(() => {
    overlay.remove();
    previousFocus?.focus?.();
    resolve(result);
  }, 140);
}

export function showConfirmDialog({
  title = 'Confirm action',
  message,
  eyebrow = 'Careful step',
  confirmText = 'Confirm',
  cancelText = 'Cancel',
  tone = 'danger',
  requiredText = null,
  inputLabel = 'Type the requested value to confirm'
}) {
  if (activeConfirmDialog) {
    closeActiveDialog(false);
  }

  const previousFocus = document.activeElement;
  const overlay = document.createElement('div');
  const dialogId = `app-confirm-${++dialogSequence}`;
  const hasTextConfirmation = typeof requiredText === 'string' && requiredText.length > 0;
  const safeTone = tone === 'danger' ? 'danger' : 'neutral';
  overlay.className = 'app-confirm-overlay';
  overlay.setAttribute('role', 'presentation');

  overlay.innerHTML = `
    <div class="app-confirm-dialog app-confirm-dialog--${safeTone}" role="dialog" aria-modal="true" aria-labelledby="${dialogId}-title" aria-describedby="${dialogId}-message">
      <button type="button" class="app-confirm-close" aria-label="Close confirmation">
        <i data-lucide="x" aria-hidden="true"></i>
      </button>
      <div class="app-confirm-mark" aria-hidden="true">
        <i data-lucide="${safeTone === 'danger' ? 'trash-2' : 'sparkles'}"></i>
      </div>
      <p class="app-confirm-eyebrow">${escapeHtml(eyebrow)}</p>
      <h3 id="${dialogId}-title">${escapeHtml(title)}</h3>
      <p id="${dialogId}-message">${escapeHtml(message)}</p>
      ${hasTextConfirmation ? `
        <div class="app-confirm-field">
          <label for="${dialogId}-input">${escapeHtml(inputLabel)}</label>
          <code>${escapeHtml(requiredText)}</code>
          <input id="${dialogId}-input" type="text" autocomplete="off" autocapitalize="none" spellcheck="false" aria-describedby="${dialogId}-hint">
          <span id="${dialogId}-hint" aria-live="polite">The confirmation must match exactly.</span>
        </div>
      ` : ''}
      <div class="app-confirm-actions">
        <button type="button" class="app-confirm-cancel">${escapeHtml(cancelText)}</button>
        <button type="button" class="app-confirm-confirm"${hasTextConfirmation ? ' disabled' : ''}>${escapeHtml(confirmText)}</button>
      </div>
    </div>
  `;

  document.body.appendChild(overlay);
  window.lucide?.createIcons?.();

  const cancelBtn = overlay.querySelector('.app-confirm-cancel');
  const confirmBtn = overlay.querySelector('.app-confirm-confirm');
  const closeBtn = overlay.querySelector('.app-confirm-close');
  const dialog = overlay.querySelector('.app-confirm-dialog');
  const confirmationInput = overlay.querySelector('.app-confirm-field input');

  const updateConfirmationState = () => {
    if (!confirmationInput) return;
    confirmBtn.disabled = confirmationInput.value !== requiredText;
  };

  const onKeyDown = (event) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      closeActiveDialog(false);
    }

    if (event.key === 'Enter' && confirmationInput && !confirmBtn.disabled) {
      event.preventDefault();
      closeActiveDialog(true);
    }
  };

  overlay.addEventListener('click', (event) => {
    if (!dialog.contains(event.target)) {
      closeActiveDialog(false);
    }
  });
  closeBtn.addEventListener('click', () => closeActiveDialog(false));
  cancelBtn.addEventListener('click', () => closeActiveDialog(false));
  confirmBtn.addEventListener('click', () => closeActiveDialog(true));
  confirmationInput?.addEventListener('input', updateConfirmationState);
  document.addEventListener('keydown', onKeyDown);

  return new Promise((resolve) => {
    activeConfirmDialog = {
      overlay,
      previousFocus,
      resolve: (result) => {
        document.removeEventListener('keydown', onKeyDown);
        resolve(result);
      }
    };

    window.requestAnimationFrame(() => {
      overlay.classList.add('app-confirm-overlay-visible');
      (confirmationInput || confirmBtn).focus();
    });
  });
}

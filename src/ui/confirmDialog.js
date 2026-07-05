let activeConfirmDialog = null;

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
  tone = 'danger'
}) {
  if (activeConfirmDialog) {
    closeActiveDialog(false);
  }

  const previousFocus = document.activeElement;
  const overlay = document.createElement('div');
  overlay.className = 'app-confirm-overlay';
  overlay.setAttribute('role', 'presentation');

  overlay.innerHTML = `
    <div class="app-confirm-dialog app-confirm-dialog--${tone}" role="dialog" aria-modal="true" aria-labelledby="app-confirm-title" aria-describedby="app-confirm-message">
      <button type="button" class="app-confirm-close" aria-label="Close confirmation">
        <i data-lucide="x" aria-hidden="true"></i>
      </button>
      <div class="app-confirm-mark" aria-hidden="true">
        <i data-lucide="${tone === 'danger' ? 'trash-2' : 'sparkles'}"></i>
      </div>
      <p class="app-confirm-eyebrow">${eyebrow}</p>
      <h3 id="app-confirm-title">${title}</h3>
      <p id="app-confirm-message">${message}</p>
      <div class="app-confirm-actions">
        <button type="button" class="app-confirm-cancel">${cancelText}</button>
        <button type="button" class="app-confirm-confirm">${confirmText}</button>
      </div>
    </div>
  `;

  document.body.appendChild(overlay);
  window.lucide?.createIcons?.();

  const cancelBtn = overlay.querySelector('.app-confirm-cancel');
  const confirmBtn = overlay.querySelector('.app-confirm-confirm');
  const closeBtn = overlay.querySelector('.app-confirm-close');
  const dialog = overlay.querySelector('.app-confirm-dialog');

  const onKeyDown = (event) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      closeActiveDialog(false);
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
      confirmBtn.focus();
    });
  });
}

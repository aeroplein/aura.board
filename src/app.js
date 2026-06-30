import './index.css';
import {
  handleLogout as handleLogoutFeature,
  loadCachedSession as loadCachedSessionFeature,
  renderUserProfileUI as renderUserProfileUIFeature,
  setupAuthForm as setupAuthFormFeature,
  toggleAuthMode as toggleAuthModeFeature
} from './features/auth.js';
import { setupBoardForm as setupBoardFormFeature } from './features/boardForms.js';
import {
  fetchUserBoards as fetchUserBoardsFeature,
  openPendingBoardLink as openPendingBoardLinkFeature,
  renderUserBoardsList as renderUserBoardsListFeature
} from './features/boards.js';
import { startCollaborationTicker as startCollaborationTickerFeature } from './features/collaboration.js';
import { processSyncAction as processSyncActionFeature, showSyncBanner as showSyncBannerFeature } from './features/sync.js';
import { fetchWithCredentials, parseJsonResponse } from './services/apiClient.js';
import { showTab as showTabView, setupNavTriggers as setupNavigationTriggers } from './ui/navigation.js';
import { setupSearchHandlers as setupBoardSearchHandlers } from './ui/search.js';
import { renderUserSettings as renderUserSettingsView, setupSettingsHandlers as setupPreferenceHandlers } from './ui/settings.js';
import { decryptText, encryptText, setupCryptoLab as setupCryptoLabHandlers } from './utils/cryptoLab.js';
import { escapeHtml } from './utils/html.js';
import { imageDataAttributes, imageFallbackMarkup, resolveImageSource } from './utils/images.js';

// aura.board • Creative Vision Studio (Vanilla JavaScript Core Platform)

// 1. STATE & CACHE REGISTRY
let currentUser = null;
let boards = [];
let currentBoardId = null;
let boardsSearchQuery = "";
let pendingBoardLinkId = new URLSearchParams(window.location.search).get('board');
let pendingCustomCardColor = null;
const resolvedImageCache = new Map();

function normalizeBoardItemType(type) {
  const normalized = String(type || '').trim().toLowerCase();
  return ['note', 'quote', 'image', 'text'].includes(normalized) ? normalized : 'note';
}

function parseChecklistLine(line) {
  const rawLine = String(line || '');
  const match = rawLine.match(/^\s*(?:[-*]\s*)?\[(x|X| )\]\s*(.*)$/);
  if (!match) {
    return { text: rawLine.replace(/^\s*(?:[-*]|\d+[\).]|[☐□])\s+/, ''), checked: false };
  }

  return { text: match[2], checked: match[1].toLowerCase() === 'x' };
}

function serializeChecklistLine(text, checked) {
  return `[${checked ? 'x' : ' '}] ${String(text || '').trim()}`;
}

function normalizeChecklistContent(value) {
  let text = String(value || '').replace(/\r\n?/g, '\n').trim();
  if (!text) return '';

  if (!text.includes('\n')) {
    text = text
      .replace(/([^\n])\s*(?=\d+[\).]\s+)/g, '$1\n')
      .replace(/\s+(?=(?:[-*]|\[[ xX]\]|[☐□])\s+)/g, '\n');
  }

  return text
    .split('\n')
    .map(line => line.trim())
    .filter(Boolean)
    .map(line => {
      if (/^\[(?:x|X| )\]\s+/.test(line)) return line;
      return line
        .replace(/^\d+[\).]\s+/, '[ ] ')
        .replace(/^[☐□]\s+/, '[ ] ')
        .replace(/^[-*]\s+/, '[ ] ');
    })
    .join('\n');
}

function getChecklistSteps(value) {
  return normalizeChecklistContent(value)
    .split('\n')
    .filter(Boolean)
    .map(parseChecklistLine);
}

function getQuoteParts(content, caption) {
  let quoteText = String(content || '').trim();
  let author = String(caption || '').trim();
  if (/^(source|source concept|concept focus)$/i.test(author)) {
    author = '';
  }

  if (!author) {
    const attribution = quoteText.match(/^(.*?)\s+[-–—]\s+([^–—-]{2,80})$/);
    if (attribution) {
      quoteText = attribution[1].trim().replace(/^["“]+|["”]+$/g, '');
      author = attribution[2].trim();
    }
  }

  return { quoteText, author };
}

function normalizeGeneratedItemForCanvas(item) {
  if (!item || typeof item !== 'object') {
    return null;
  }

  const normalizedType = normalizeBoardItemType(item?.type);
  const normalized = {
    ...item,
    type: normalizedType,
    title: String(item.title || 'Generated idea').trim().slice(0, 120),
    content: String(item.content || '').trim().slice(0, 4000),
    caption: String(item.caption || '').trim().slice(0, 500),
    color: String(item.color || '').trim().slice(0, 200),
    width: Math.min(Math.max(Number(item.width) || 25, 5), 100),
    height: Math.min(Math.max(Number(item.height) || 22, 5), 100)
  };

  if (!normalized.content && normalizedType !== 'image') {
    return null;
  }

  if (normalizedType === 'note') {
    normalized.content = normalizeChecklistContent(normalized.content);
  }

  if (normalizedType === 'quote') {
    const quote = getQuoteParts(normalized.content, normalized.caption);
    normalized.content = quote.quoteText;
    normalized.caption = quote.author;
  }

  return normalized;
}

function getValidatedGeneratedItems(items) {
  return (Array.isArray(items) ? items : [])
    .map(normalizeGeneratedItemForCanvas)
    .filter(Boolean);
}

function normalizeHexColor(value) {
  const raw = String(value || '').trim();
  const match = raw.match(/^#?([0-9a-fA-F]{6})$/);
  return match ? `#${match[1].toUpperCase()}` : '#C8B6FF';
}

function isCustomCardColor(value) {
  return String(value || '').startsWith('custom:');
}

function getCardColorValue(value) {
  return normalizeHexColor(String(value || '').replace(/^custom:/, ''));
}

function hexToRgb(hex) {
  const normalized = normalizeHexColor(hex).slice(1);
  return {
    r: parseInt(normalized.slice(0, 2), 16),
    g: parseInt(normalized.slice(2, 4), 16),
    b: parseInt(normalized.slice(4, 6), 16)
  };
}

function relativeLuminance({ r, g, b }) {
  const toLinear = (channel) => {
    const value = channel / 255;
    return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  };

  return (0.2126 * toLinear(r)) + (0.7152 * toLinear(g)) + (0.0722 * toLinear(b));
}

function getReadableCardTextColors(hex) {
  const isLight = relativeLuminance(hexToRgb(hex)) > 0.45;
  return isLight
    ? {
        text: '#2F275A',
        muted: '#4E4578',
        chipBg: 'rgba(47, 39, 90, 0.12)',
        iconBg: 'rgba(47, 39, 90, 0.14)'
      }
    : {
        text: '#FFFFFF',
        muted: 'rgba(255, 255, 255, 0.82)',
        chipBg: 'rgba(255, 255, 255, 0.18)',
        iconBg: 'rgba(255, 255, 255, 0.16)'
      };
}

function getCardColorPresentation(value) {
  if (!isCustomCardColor(value)) {
    return {
      className: value || 'bg-white border-[#C8B6FF]/30 text-[#5E548E]',
      style: {}
    };
  }

  const hex = getCardColorValue(value);
  const readableColors = getReadableCardTextColors(hex);
  return {
    className: 'custom-color-card border',
    style: {
      backgroundColor: hex,
      borderColor: hex,
      color: readableColors.text,
      '--card-bg-color': hex,
      '--card-border-color': hex,
      '--card-text-color': readableColors.text,
      '--card-muted-color': readableColors.muted,
      '--card-chip-bg': readableColors.chipBg,
      '--card-icon-bg': readableColors.iconBg,
      boxShadow: '0 8px 22px rgba(94, 84, 142, 0.12)'
    }
  };
}

function applyCardColorPresentation(cardEl, colorPresentation) {
  Object.entries(colorPresentation.style).forEach(([key, value]) => {
    if (key.startsWith('--')) {
      cardEl.style.setProperty(key, value);
    } else {
      cardEl.style[key] = value;
    }
  });
}

function setCustomColorSelection(hex) {
  const normalizedHex = normalizeHexColor(hex);
  const customRadio = document.getElementById('item-color-custom-radio');
  const colorInput = document.getElementById('item-field-custom-color');
  const colorText = document.getElementById('item-field-custom-color-text');
  if (customRadio) customRadio.checked = true;
  if (colorInput) colorInput.value = normalizedHex;
  if (colorText) colorText.value = normalizedHex;
}

function getSelectedCardColorValue() {
  const colorRadio = document.querySelector('input[name="color-preset"]:checked');
  if (!colorRadio) return 'bg-white border-[#C8B6FF]/30 text-[#5E548E]';
  if (colorRadio.value !== '__custom__') return colorRadio.value;

  const colorText = document.getElementById('item-field-custom-color-text')?.value;
  const colorInput = document.getElementById('item-field-custom-color')?.value;
  return `custom:${normalizeHexColor(colorText || colorInput)}`;
}

function isImageKeywordQuery(value) {
  const trimmed = String(value || '').trim();
  return Boolean(trimmed) &&
    !trimmed.startsWith('/data/') &&
    !trimmed.startsWith('data:image') &&
    !trimmed.startsWith('/api/images/') &&
    !trimmed.startsWith('http');
}

function refreshResolvedImageForItem(item) {
  if (!item || item.type !== 'image' || item.isEncrypted) return;
  if (isImageKeywordQuery(item.content)) {
    resolvedImageCache.set(item.content, resolveImageSource(item.content));
  } else {
    resolvedImageCache.delete(item.content);
  }
}

window.handleImageLoadError = function handleImageLoadError(img) {
  const container = img.closest('[data-image-frame]') || img.parentElement;
  if (!container) return;
  container.innerHTML = imageFallbackMarkup(img.dataset.fallbackTitle, img.dataset.fallbackCaption);
  lucide.createIcons();
};


// Drag & Drop specific trackers
let isDragging = false;
let draggedElement = null;
let activeItemId = null;
let dragStartX = 0;
let dragStartY = 0;
let elementStartX = 0;
let elementStartY = 0;
let activeCanvasOffsetWidth = 1000;
let activeCanvasOffsetHeight = 580;
const CARD_WIDTH_UNIT = 8;
const CARD_HEIGHT_UNIT = 7;
const CANVAS_EDGE_GAP = 10;

function getCanvasPixelBounds() {
  const container = document.getElementById('canvas-area-wrapper');
  return {
    width: container?.clientWidth || activeCanvasOffsetWidth,
    height: container?.clientHeight || activeCanvasOffsetHeight
  };
}

function getRenderedCardSize(item, canvasWidth, canvasHeight) {
  const baseWidth = item.width ? item.width * CARD_WIDTH_UNIT : 200;
  const baseHeight = item.height ? item.height * CARD_HEIGHT_UNIT : 140;
  const maxWidth = Math.max(120, canvasWidth - (CANVAS_EDGE_GAP * 2));
  const maxHeight = Math.max(90, canvasHeight - (CANVAS_EDGE_GAP * 2));

  return {
    width: Math.min(baseWidth, maxWidth),
    height: Math.min(baseHeight, maxHeight)
  };
}

function getClampedCardPosition(item, renderedSize, canvasWidth, canvasHeight) {
  const rawLeft = (Number(item.x) || 0) / 100 * canvasWidth;
  const rawTop = (Number(item.y) || 0) / 100 * canvasHeight;
  const maxLeft = Math.max(CANVAS_EDGE_GAP, canvasWidth - renderedSize.width - CANVAS_EDGE_GAP);
  const maxTop = Math.max(CANVAS_EDGE_GAP, canvasHeight - renderedSize.height - CANVAS_EDGE_GAP);
  const left = Math.max(CANVAS_EDGE_GAP, Math.min(rawLeft, maxLeft));
  const top = Math.max(CANVAS_EDGE_GAP, Math.min(rawTop, maxTop));

  return {
    leftPercent: (left / canvasWidth) * 100,
    topPercent: (top / canvasHeight) * 100
  };
}

// Access bootstrap modals
let authModalObj = null;
let itemModalObj = null;
let boardModalObj = null;
let exportPdfModalObj = null;

// Presets list of items on active workspace
let selectedItemToEdit = null;

// 2. DOCUMENT LIFE HANDLERS
document.addEventListener('DOMContentLoaded', () => {
  // Initialize bootstrap modals safely
  authModalObj = new bootstrap.Modal(document.getElementById('authScreenModal'));
  itemModalObj = new bootstrap.Modal(document.getElementById('editItemModal'));
  boardModalObj = new bootstrap.Modal(document.getElementById('editBoardModal'));
  exportPdfModalObj = new bootstrap.Modal(document.getElementById('exportPdfModal'));

  // Run dynamic Lucide rendering
  lucide.createIcons();

  // Load cache keys and preferences
  loadCachedSession();

  // Attach UI event listeners
  setupNavTriggers();
  setupAuthValidationGuard();
  setupAuthForm();
  setupBoardForm();
  setupItemForm();
  setupSettingsHandlers();
  setupGalleryHandlers();
  setupCryptoLab();
  setupExportPdfHandlers();
  setupSearchHandlers();

  // Periodically pull fresh collaborator suggestions
  startCollaborationTicker();
});

// 3. SESSION MANAGEMENT
async function loadCachedSession() {
  return loadCachedSessionFeature({
    fetchWithCredentials,
    setCurrentUser: (user) => {
      currentUser = user;
    },
    renderUserProfileUI,
    fetchUserBoards,
    showTab,
    authModalObj
  });
}

async function handleLogout() {
  return handleLogoutFeature({
    fetchWithCredentials,
    clearSessionState: () => {
      currentUser = null;
      boards = [];
      currentBoardId = null;
      boardsSearchQuery = "";
      resolvedImageCache.clear();
    },
    renderUserProfileUI,
    showTab,
    authModalObj
  });
}

function renderUserProfileUI() {
  renderUserProfileUIFeature({
    getCurrentUser: () => currentUser,
    handleLogout,
    openUserSettings: () => {
      showTab('user-settings');
    },
    authModalObj
  });
}

// 4. ROUTER LAYOUT CONTROLS
function showTab(tabId) {
  showTabView(tabId, {
    renderUserBoardsList,
    refreshStudioDisplay,
    renderUserSettings
  });
}

function setupNavTriggers() {
  setupNavigationTriggers({
    getCurrentUser: () => currentUser,
    getBoards: () => boards,
    getCurrentBoardId: () => currentBoardId,
    showTab,
    authModalObj,
    boardModalObj,
    exportPdfModalObj,
    showSyncBanner
  });
}

// 5. REST GRAPH API fetchers
async function fetchUserBoards() {
  return fetchUserBoardsFeature({
    fetchWithCredentials,
    setBoards: (nextBoards) => {
      boards = nextBoards;
    },
    renderUserBoardsList,
    populatePushSelection,
    openPendingBoardLink,
    showSyncBanner
  });
}

function openPendingBoardLink() {
  openPendingBoardLinkFeature({
    getPendingBoardLinkId: () => pendingBoardLinkId,
    setPendingBoardLinkId: (value) => {
      pendingBoardLinkId = value;
    },
    getCurrentBoardId: () => currentBoardId,
    getBoards: () => boards,
    enterBoardStudio,
    showSyncBanner
  });
}

function renderUserBoardsList() {
  renderUserBoardsListFeature({
    getBoards: () => boards,
    getBoardsSearchQuery: () => boardsSearchQuery,
    enterBoardStudio,
    openBoardEditor: (targ) => {
      document.getElementById('board-field-id').value = targ.id;
      document.getElementById('board-field-title').value = targ.title;
      document.getElementById('board-field-desc').value = targ.description || '';
      document.getElementById('board-field-category').value = targ.category || 'Personal Growth';
      document.getElementById('board-field-shared').checked = targ.isShared || false;
      document.getElementById('board-field-collabs').value = (targ.collaborators || []).join(', ');
      document.getElementById('board-collaborators-group').classList.toggle('d-none', !targ.isShared);
      
      document.getElementById('btn-delete-board').classList.remove('d-none');
      document.getElementById('boardModalHeaderTitle').textContent = 'Edit Canvas Assembly Settings';
      boardModalObj.show();
    }
  });
}
function enterBoardStudio(id) {
  const isSwitchingBoards = currentBoardId && currentBoardId !== id;
  currentBoardId = id;
  if (isSwitchingBoards) {
    closeAiDrawer();
    activeAiSuggestionsBoardId = null;
    activeAiSuggestions = null;
    addedAiSuggestionKeys.clear();
  }
  showTab('studio');
}

// 6. CREATION CANVAS PLAYGROUND (Vanishing components)
const canvasDraggableStage = document.getElementById('canvas-draggable-stage');
const noElementsBanner = document.getElementById('no-elements-banner');

function refreshStudioDisplay() {
  const b = boards.find(o => o.id === currentBoardId);
  if (!b) {
    document.getElementById('studio-board-title').textContent = 'Select Canvas Workspace';
    document.getElementById('studio-board-desc').textContent = 'Open your active designs from dashboard first.';
    canvasDraggableStage.innerHTML = '';
    noElementsBanner.classList.remove('d-none');
    return;
  }

  document.getElementById('studio-board-title').textContent = b.title;
  document.getElementById('studio-board-desc').textContent = b.description || 'Flexible digital curation stage.';

  // Clear workspace stage
  canvasDraggableStage.innerHTML = '';
  
  if (!b.items || b.items.length === 0) {
    noElementsBanner.classList.remove('d-none');
    return;
  }

  noElementsBanner.classList.add('d-none');
  const canvasBounds = getCanvasPixelBounds();

  // Render cards absolute positions using viewport percentages scaled to bounds
  b.items.forEach(it => {
    const cardEl = document.createElement('div');
    const colorPresentation = getCardColorPresentation(it.color);
    cardEl.className = `draggable-card glass-card p-3 flex flex-col justify-between ${colorPresentation.className}`;
    cardEl.id = `card-${it.id}`;
    applyCardColorPresentation(cardEl, colorPresentation);
    let tiltHash = 0;
    const idStr = String(it.id || '');
    for (let ci = 0; ci < idStr.length; ci++) tiltHash += idStr.charCodeAt(ci);
    const cardTilt = ((tiltHash % 7) - 3) * 0.14;
    cardEl.style.setProperty('--card-tilt', `${cardTilt}deg`);
    
    // Scale percentages to layout coordinates
    const renderedSize = getRenderedCardSize(it, canvasBounds.width, canvasBounds.height);
    const renderedPosition = getClampedCardPosition(it, renderedSize, canvasBounds.width, canvasBounds.height);
    cardEl.style.left = `${renderedPosition.leftPercent.toFixed(2)}%`;
    cardEl.style.top = `${renderedPosition.topPercent.toFixed(2)}%`;
    cardEl.style.width = `${renderedSize.width}px`;
    cardEl.style.minHeight = `${renderedSize.height}px`;
    cardEl.style.zIndex = it.zIndex || 10;
    cardEl.setAttribute('data-id', it.id);

    // Local reversible obfuscation indicator check
    const isEncrypted = it.isEncrypted || it.content?.startsWith('shield_v15_');
    const revealedPayload = isEncrypted ? decryptText(it.content) : it.content;
    const safeTitle = escapeHtml(it.title || 'Untitled element');
    const safeCaption = escapeHtml(it.caption || 'Concept focus');
    const safeType = escapeHtml(it.type || 'note');
    const safePayload = escapeHtml(revealedPayload || '');

    let subHtml = '';
    if (it.type === 'quote') {
      const quote = getQuoteParts(revealedPayload, it.caption);
      const quoteAuthor = quote.author ? `<span class="text-[9px] font-mono text-dusty uppercase text-right block">- ${escapeHtml(quote.author)}</span>` : '';
      subHtml = `<p class="italic text-xs my-2 text-[#5E548E]/90">"${escapeHtml(quote.quoteText)}"</p>
                 ${quoteAuthor}`;
    } else if (it.type === 'note') {
      // Map multiple checklist items if multi line
      const steps = getChecklistSteps(revealedPayload);
      let listItems = steps.map((step, index) => `
        <li class="d-flex align-items-center gap-1 px-1 py-0.5 text-[11px] font-mono">
          <input type="checkbox" class="form-check-input mt-0 board-note-check" data-id="${it.id}" data-index="${index}" ${step.checked ? 'checked' : ''} />
          <span class="line-clamp-1 pr-1">${escapeHtml(step.text)}</span>
        </li>
      `).join('');
      subHtml = `<ul class="list-unstyled space-y-1 my-1.5">${listItems || '<li>Checklist Empty</li>'}</ul>`;
    } else if (it.type === 'image') {
      const queryKey = revealedPayload || '';
      const imgSource = resolvedImageCache.get(queryKey) || resolveImageSource(queryKey);
      resolvedImageCache.set(queryKey, imgSource);
      subHtml = `
        <div class="my-1.5 relative rounded-lg overflow-hidden flex-grow min-h-0 flex flex-col justify-center items-center bg-[#C8B6FF]/10 dark:bg-white/5" style="flex-grow: 1; min-height: 80px;">
          <img id="img-asset-${it.id}" src="${imgSource}" alt="${safeTitle}" class="w-full h-full object-cover" referrerPolicy="no-referrer" style="max-height: 100%; object-fit: cover;" ${imageDataAttributes(it.title, it.caption)} onerror="window.handleImageLoadError(this)" />
          <div class="absolute bottom-0 inset-x-0 p-1 bg-black/45 text-[9px] text-white italic truncate text-center">${safeCaption}</div>
        </div>
      `;
    } else {
      subHtml = `<p class="text-xs my-2 leading-normal break-words">${safePayload}</p>`;
    }

    const isImage = it.type === 'image';
    cardEl.innerHTML = `
      <div class="h-100 flex flex-col justify-between">
        <div class="${isImage ? 'flex flex-col flex-grow min-h-0' : 'space-y-1 flex-shrink-0'}" style="${isImage ? '' : 'flex-shrink: 0;'}">
          <div class="d-flex justify-content-between align-items-center border-b border-[#C8B6FF]/20 pb-1.5 flex-shrink-0" style="flex-shrink: 0;">
            <span class="text-[9px] font-mono uppercase bg-[#C8B6FF]/20 px-1.5 py-0.5 rounded text-[#5E548E] font-bold">
              ${safeType}
            </span>
            <div class="d-flex gap-1">
              ${isEncrypted ? '<i data-lucide="lock" class="w-3.5 h-3.5 text-emerald-500" title="Obfuscated locally"></i>' : ''}
              <button type="button" class="p-0.5 border-0 hover:bg-[#C8B6FF]/20 text-[#5E548E] rounded btn-card-edit-action" data-id="${it.id}" aria-label="Edit ${safeTitle}">
                <i data-lucide="edit-3" class="w-3 h-3"></i>
              </button>
            </div>
          </div>
          <h5 class="text-[11px] font-extrabold tracking-tight text-[#5E548E] uppercase pt-1 mb-0 flex-shrink-0" style="flex-shrink: 0;">${safeTitle}</h5>
          <div class="stage-card-body mt-2 ${isImage ? 'flex-grow min-h-0 flex flex-col' : 'flex-shrink-0'}" style="${isImage ? '' : 'flex-shrink: 0;'}">${subHtml}</div>
        </div>
        
        <div class="text-[8px] font-mono text-dusty uppercase pt-2 flex items-center justify-between select-none flex-shrink-0" style="flex-shrink: 0; width: 100%;">
          <div class="flex items-center gap-1.5">
            <i data-lucide="move" class="w-3 h-3"></i>
            <span class="card-size-indicator text-[8px] font-mono text-dusty lowercase d-none">${Math.round(renderedSize.width)}x${Math.round(renderedSize.height)}px</span>
          </div>
          <div class="d-flex items-center gap-1">
            <button type="button" class="p-0.5 border-0 hover:bg-[#C8B6FF]/20 text-[#5E548E] rounded btn-card-send-back" data-id="${it.id}" title="Send to Back" aria-label="Send ${safeTitle} to back" style="cursor: pointer;">
              <i data-lucide="chevrons-down" class="w-3 h-3"></i>
            </button>
            <button type="button" class="p-0.5 border-0 hover:bg-[#C8B6FF]/20 text-[#5E548E] rounded btn-card-bring-front" data-id="${it.id}" title="Bring to Front" aria-label="Bring ${safeTitle} to front" style="cursor: pointer;">
              <i data-lucide="chevrons-up" class="w-3 h-3"></i>
            </button>
          </div>
        </div>
      </div>
      <div class="card-resize-handle" style="position: absolute; right: 6px; bottom: 6px; z-index: 30; cursor: se-resize; opacity: 0.6; transition: opacity 0.2s;">
        <svg width="12" height="12" viewBox="0 0 12 12" style="display: block;">
          <line x1="10" y1="2" x2="2" y2="10" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
          <line x1="10" y1="6" x2="6" y2="10" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
        </svg>
      </div>
    `;

    canvasDraggableStage.appendChild(cardEl);
    attachCardDragEvents(cardEl);
    attachCardResizeEvents(cardEl);
  });

  // Attach dynamic clicks to edit particular elements
  canvasDraggableStage.querySelectorAll('.btn-card-edit-action').forEach(btn => {
    btn.addEventListener('click', (e) => {
      e.stopPropagation();
      const itemid = e.currentTarget.getAttribute('data-id');
      const targetBoard = boards.find(o => o.id === currentBoardId);
      const targetItem = targetBoard?.items.find(i => i.id === itemid);
      if (targetItem) {
        openEditCardModal(targetItem);
      }
    });
  });

  // Attach quick layering actions
  canvasDraggableStage.querySelectorAll('.btn-card-send-back').forEach(btn => {
    btn.addEventListener('click', (e) => {
      e.stopPropagation();
      const itemid = e.currentTarget.getAttribute('data-id');
      sendCardToBack(itemid);
    });
  });

  canvasDraggableStage.querySelectorAll('.btn-card-bring-front').forEach(btn => {
    btn.addEventListener('click', (e) => {
      e.stopPropagation();
      const itemid = e.currentTarget.getAttribute('data-id');
      bringCardToFront(itemid);
    });
  });

  canvasDraggableStage.querySelectorAll('.board-note-check').forEach(input => {
    input.addEventListener('change', handleChecklistToggle);
  });

  lucide.createIcons();
}

function handleChecklistToggle(e) {
  e.stopPropagation();

  const checkbox = e.currentTarget;
  const itemId = checkbox.getAttribute('data-id');
  const lineIndex = Number(checkbox.getAttribute('data-index'));
  const bIndex = boards.findIndex(b => b.id === currentBoardId);
  if (bIndex === -1 || Number.isNaN(lineIndex)) return;

  const itemIndex = boards[bIndex].items?.findIndex(it => it.id === itemId);
  if (itemIndex === -1 || itemIndex === undefined) return;

  const item = boards[bIndex].items[itemIndex];
  if (item.type !== 'note') return;

  const isEncrypted = item.isEncrypted || item.content?.startsWith('shield_v15_');
  let contentToUpdate = isEncrypted ? decryptText(item.content) : item.content;
  const lines = normalizeChecklistContent(contentToUpdate).split('\n').filter(Boolean);
  const parsedLines = lines.map(parseChecklistLine);
  if (!parsedLines[lineIndex]) return;

  parsedLines[lineIndex].checked = checkbox.checked;
  contentToUpdate = parsedLines
    .map(line => serializeChecklistLine(line.text, line.checked))
    .join('\n');

  item.content = isEncrypted ? encryptText(contentToUpdate, currentUser.id) : contentToUpdate;
  item.updatedAt = new Date().toISOString();
  boards[bIndex].items[itemIndex] = item;

  processSyncAction('upsert_item', currentBoardId, item.id, item);
}

function bringCardToFront(itemId) {
  const bIndex = boards.findIndex(b => b.id === currentBoardId);
  if (bIndex === -1) return;
  const items = boards[bIndex].items;
  if (!items || items.length === 0) return;

  const targetItem = items.find(it => it.id === itemId);
  if (targetItem) {
    const maxZ = items.reduce((max, it) => {
      if (it.id === itemId) return max;
      return Math.max(max, Number(it.zIndex || 10));
    }, 10);

    targetItem.zIndex = maxZ + 1;
    targetItem.updatedAt = new Date().toISOString();
    refreshStudioDisplay();
    processSyncAction('upsert_item', currentBoardId, itemId, targetItem);
  }
}

function sendCardToBack(itemId) {
  const bIndex = boards.findIndex(b => b.id === currentBoardId);
  if (bIndex === -1) return;
  const items = boards[bIndex].items;
  if (!items || items.length === 0) return;

  const targetItem = items.find(it => it.id === itemId);
  if (targetItem) {
    const minZ = items.reduce((min, it) => {
      if (it.id === itemId) return min;
      return Math.min(min, Number(it.zIndex || 10));
    }, 10);

    targetItem.zIndex = minZ - 1;
    targetItem.updatedAt = new Date().toISOString();
    refreshStudioDisplay();
    processSyncAction('upsert_item', currentBoardId, itemId, targetItem);
  }
}

// DRAG MECHANICS (Calculations scaled fluidly to percentages)
function attachCardDragEvents(el) {
  const container = document.getElementById('canvas-area-wrapper');
  let pendingTouchDrag = false;
  let rafDragFrame = null;
  let nextDragPosition = null;
  
  el.addEventListener('mousedown', dragStart);
  el.addEventListener('touchstart', dragStart, { passive: true });

  function dragStart(e) {
    if (
      e.target.closest('.btn-card-edit-action') ||
      e.target.closest('.btn-card-send-back') ||
      e.target.closest('.btn-card-bring-front') ||
      e.target.closest('input[type="checkbox"]') ||
      e.target.closest('.card-resize-handle')
    ) {
      return; 
    }

    // Bounds dimensions mapping
    activeCanvasOffsetWidth = container.offsetWidth;
    activeCanvasOffsetHeight = container.offsetHeight;

    const clientX = e.type === 'touchstart' ? e.touches[0].clientX : e.clientX;
    const clientY = e.type === 'touchstart' ? e.touches[0].clientY : e.clientY;

    const rect = el.getBoundingClientRect();
    const parentRect = container.getBoundingClientRect();

    dragStartX = clientX;
    dragStartY = clientY;

    // Relative offsets inside parent
    elementStartX = rect.left - parentRect.left;
    elementStartY = rect.top - parentRect.top;

    if (e.type === 'touchstart') {
      pendingTouchDrag = true;
    } else {
      beginCardDrag();
    }

    document.addEventListener('mousemove', dragMove);
    document.addEventListener('mouseup', dragEnd);
    document.addEventListener('touchmove', dragMove, { passive: false });
    document.addEventListener('touchend', dragEnd);
    document.addEventListener('touchcancel', dragEnd);
  }

  function beginCardDrag() {
    isDragging = true;
    pendingTouchDrag = false;
    draggedElement = el;
    activeItemId = el.getAttribute('data-id');
    el.classList.add('is-dragging');
  }

  function dragMove(e) {
    if (!isDragging && !pendingTouchDrag) return;

    const clientX = e.type === 'touchmove' ? e.touches[0].clientX : e.clientX;
    const clientY = e.type === 'touchmove' ? e.touches[0].clientY : e.clientY;

    // Compute pixel delta changes
    const dx = clientX - dragStartX;
    const dy = clientY - dragStartY;
    if (pendingTouchDrag) {
      const absX = Math.abs(dx);
      const absY = Math.abs(dy);
      const dragThreshold = 8;

      if (absY > dragThreshold && absY > absX * 1.25) {
        pendingTouchDrag = false;
        cleanupDragListeners();
        return;
      }

      if (Math.max(absX, absY) < dragThreshold) {
        return;
      }

      beginCardDrag();
    }

    if (!isDragging || draggedElement !== el) return;
    if (e.cancelable) e.preventDefault();

    let targetLeftX = elementStartX + dx;
    let targetTopY = elementStartY + dy;

    // Stay inside stage margins boundary checks
    const maxLeftX = Math.max(CANVAS_EDGE_GAP, activeCanvasOffsetWidth - el.offsetWidth - CANVAS_EDGE_GAP);
    const maxTopY = Math.max(CANVAS_EDGE_GAP, activeCanvasOffsetHeight - el.offsetHeight - CANVAS_EDGE_GAP);

    targetLeftX = Math.max(CANVAS_EDGE_GAP, Math.min(targetLeftX, maxLeftX));
    targetTopY = Math.max(CANVAS_EDGE_GAP, Math.min(targetTopY, maxTopY));

    // Convert pixels delta coordinates to percentage ratios dynamically
    const percentX = (targetLeftX / activeCanvasOffsetWidth) * 100;
    const percentY = (targetTopY / activeCanvasOffsetHeight) * 100;

    nextDragPosition = {
      left: `${percentX.toFixed(2)}%`,
      top: `${percentY.toFixed(2)}%`
    };

    if (!rafDragFrame) {
      rafDragFrame = requestAnimationFrame(() => {
        rafDragFrame = null;
        if (!nextDragPosition) return;
        el.style.left = nextDragPosition.left;
        el.style.top = nextDragPosition.top;
      });
    }
  }

  function dragEnd() {
    pendingTouchDrag = false;
    if (rafDragFrame) {
      cancelAnimationFrame(rafDragFrame);
      rafDragFrame = null;
      if (nextDragPosition) {
        el.style.left = nextDragPosition.left;
        el.style.top = nextDragPosition.top;
      }
    }
    nextDragPosition = null;

    if (isDragging && draggedElement === el) {
      // Fetch dynamic coordinates to sync state database
      const finalPercentX = parseFloat(el.style.left);
      const finalPercentY = parseFloat(el.style.top);

      updateBoardItemPosition(activeItemId, finalPercentX, finalPercentY);

      isDragging = false;
      draggedElement = null;
      el.classList.remove('is-dragging');
    }

    el.classList.remove('is-dragging');
    cleanupDragListeners();
  }

  function cleanupDragListeners() {
    document.removeEventListener('mousemove', dragMove);
    document.removeEventListener('mouseup', dragEnd);
    document.removeEventListener('touchmove', dragMove);
    document.removeEventListener('touchend', dragEnd);
    document.removeEventListener('touchcancel', dragEnd);
  }
}

function updateBoardItemPosition(itemId, px, py) {
  const bIndex = boards.findIndex(b => b.id === currentBoardId);
  if (bIndex !== -1) {
    const itemIndex = boards[bIndex].items.findIndex(it => it.id === itemId);
    if (itemIndex !== -1) {
      boards[bIndex].items[itemIndex].x = parseFloat(px.toFixed(1));
      boards[bIndex].items[itemIndex].y = parseFloat(py.toFixed(1));
      boards[bIndex].items[itemIndex].updatedAt = new Date().toISOString();
      
      // Auto compile sync request
      processSyncAction('upsert_item', currentBoardId, itemId, boards[bIndex].items[itemIndex]);
    }
  }
}

function attachCardResizeEvents(el) {
  const container = document.getElementById('canvas-area-wrapper');
  const handle = el.querySelector('.card-resize-handle');
  if (!handle) return;

  let isResizing = false;
  let resizeStartX = 0;
  let resizeStartY = 0;
  let cardStartWidth = 0;
  let cardStartHeight = 0;
  let itemId = el.getAttribute('data-id');

  handle.addEventListener('mousedown', resizeStart);
  handle.addEventListener('touchstart', resizeStart, { passive: false });

  function resizeStart(e) {
    e.stopPropagation();
    e.preventDefault();

    isResizing = true;
    itemId = el.getAttribute('data-id');

    activeCanvasOffsetWidth = container.offsetWidth;
    activeCanvasOffsetHeight = container.offsetHeight;

    const clientX = e.type === 'touchstart' ? e.touches[0].clientX : e.clientX;
    const clientY = e.type === 'touchstart' ? e.touches[0].clientY : e.clientY;

    resizeStartX = clientX;
    resizeStartY = clientY;

    cardStartWidth = el.offsetWidth;
    cardStartHeight = el.offsetHeight;

    // Show visual size indicator text during active resizing
    const sizeIndicator = el.querySelector('.card-size-indicator');
    if (sizeIndicator) {
      sizeIndicator.classList.remove('d-none');
    }

    document.addEventListener('mousemove', resizeMove);
    document.addEventListener('mouseup', resizeEnd);
    document.addEventListener('touchmove', resizeMove, { passive: false });
    document.addEventListener('touchend', resizeEnd);
  }

  function resizeMove(e) {
    if (!isResizing) return;
    if (e.cancelable) e.preventDefault();

    const clientX = e.type === 'touchmove' ? e.touches[0].clientX : e.clientX;
    const clientY = e.type === 'touchmove' ? e.touches[0].clientY : e.clientY;

    const dx = clientX - resizeStartX;
    const dy = clientY - resizeStartY;

    let newWidth = cardStartWidth + dx;
    let newHeight = cardStartHeight + dy;

    // Get card position relative to canvas wrapper
    const parentRect = container.getBoundingClientRect();
    const cardRect = el.getBoundingClientRect();
    const leftPx = cardRect.left - parentRect.left;
    const topPx = cardRect.top - parentRect.top;

    // Boundary constraints: Card cannot exceed canvas boundaries
    let maxWidth = activeCanvasOffsetWidth - leftPx;
    let maxHeight = activeCanvasOffsetHeight - topPx;

    // Max limits: absolute canvas sizes
    maxWidth = Math.min(maxWidth, activeCanvasOffsetWidth);
    maxHeight = Math.min(maxHeight, activeCanvasOffsetHeight);

    // Min limits (card shouldn't shrink too small)
    const absoluteMinWidth = Math.min(120, maxWidth);
    const absoluteMinHeight = Math.min(80, maxHeight);

    newWidth = Math.max(absoluteMinWidth, Math.min(newWidth, maxWidth));
    newHeight = Math.max(absoluteMinHeight, Math.min(newHeight, maxHeight));

    // Update style dynamically
    el.style.width = `${newWidth}px`;
    el.style.minHeight = `${newHeight}px`;
    el.style.height = `${newHeight}px`;

    // Calculate new size and update in the left bottom corner
    const sizeIndicator = el.querySelector('.card-size-indicator');
    if (sizeIndicator) {
      sizeIndicator.textContent = `${Math.round(newWidth)}x${Math.round(newHeight)}px`;
    }
  }

  function resizeEnd() {
    if (isResizing) {
      isResizing = false;

      // Convert pixels to database coordinates (Width / 8, Height / 7)
      const dbWidth = Math.round(el.offsetWidth / CARD_WIDTH_UNIT);
      const dbHeight = Math.round(el.offsetHeight / CARD_HEIGHT_UNIT);

      updateBoardItemSize(itemId, dbWidth, dbHeight);

      // Snap the visual size text
      const sizeIndicator = el.querySelector('.card-size-indicator');
      if (sizeIndicator) {
        sizeIndicator.textContent = `${dbWidth * CARD_WIDTH_UNIT}x${dbHeight * CARD_HEIGHT_UNIT}px`;
        sizeIndicator.classList.add('d-none');
      }
    }

    document.removeEventListener('mousemove', resizeMove);
    document.removeEventListener('mouseup', resizeEnd);
    document.removeEventListener('touchmove', resizeMove);
    document.removeEventListener('touchend', resizeEnd);
  }
}

function updateBoardItemSize(itemId, width, height) {
  const bIndex = boards.findIndex(b => b.id === currentBoardId);
  if (bIndex !== -1) {
    const itemIndex = boards[bIndex].items.findIndex(it => it.id === itemId);
    if (itemIndex !== -1) {
      boards[bIndex].items[itemIndex].width = width;
      boards[bIndex].items[itemIndex].height = height;
      boards[bIndex].items[itemIndex].updatedAt = new Date().toISOString();
      
      // Auto compile sync request
      processSyncAction('upsert_item', currentBoardId, itemId, boards[bIndex].items[itemIndex]);
    }
  }
}

// 7. BOARD FORMS ASSEMBLY
function setupBoardForm() {
  setupBoardFormFeature({
    fetchWithCredentials,
    boardModalObj,
    fetchUserBoards,
    showSyncBanner,
    getCurrentBoardId: () => currentBoardId,
    setCurrentBoardId: (value) => {
      currentBoardId = value;
    },
    showTab
  });
}

// 8. ELEMENT CARD CUSTOMIZER FORM
function setupItemForm() {
  const form = document.getElementById('item-form');
  const typeSelect = document.getElementById('item-field-type');
  const labelContent = document.getElementById('item-label-content');
  const labelCaption = document.getElementById('item-label-caption');
  const extraCaptionWrap = document.getElementById('item-extra-caption-wrapper');
  const delBtn = document.getElementById('btn-delete-card');

  // Adjust card labels matching selected type
  typeSelect.addEventListener('change', () => {
    const val = typeSelect.value;
    const imageUploadWrapper = document.getElementById('item-image-upload-wrapper');
    if (val === 'image') {
      imageUploadWrapper?.classList.remove('d-none');
      labelContent.textContent = 'Image Keyword, Image URL, or Pinterest Image Link';
      labelCaption.textContent = 'Aesthetic Photo Tagline / Caption';
      extraCaptionWrap.classList.remove('d-none');
    } else {
      imageUploadWrapper?.classList.add('d-none');
      if (val === 'quote') {
        labelContent.textContent = 'Quote Text Body';
        labelCaption.textContent = 'Quote Author / Originator';
        extraCaptionWrap.classList.remove('d-none');
      } else if (val === 'note') {
        labelContent.textContent = 'Checklist Steps (One bullet per line)';
        extraCaptionWrap.classList.add('d-none');
      } else {
        labelContent.textContent = 'Plain Memo Paragraph';
        extraCaptionWrap.classList.add('d-none');
      }
    }
  });

  const fileInput = document.getElementById('item-field-image-file');
  const uploadStatus = document.getElementById('item-image-upload-status');
  const uploadStatusText = document.getElementById('item-image-upload-status-text');
  const previewContainer = document.getElementById('item-image-preview-container');
  const previewImg = document.getElementById('item-image-preview');
  const removeImgBtn = document.getElementById('btn-remove-uploaded-image');
  const contentTextarea = document.getElementById('item-field-content');
  const customColorInput = document.getElementById('item-field-custom-color');
  const customColorText = document.getElementById('item-field-custom-color-text');
  const customColorRadio = document.getElementById('item-color-custom-radio');
  const submitBtn = form.querySelector('button[type="submit"]');

  customColorInput?.addEventListener('input', () => {
    setCustomColorSelection(customColorInput.value);
  });

  customColorText?.addEventListener('input', () => {
    const raw = customColorText.value.trim();
    if (/^#?[0-9a-fA-F]{6}$/.test(raw)) {
      setCustomColorSelection(raw);
    } else if (customColorRadio) {
      customColorRadio.checked = true;
    }
  });

  if (fileInput) {
    fileInput.addEventListener('change', async (e) => {
      const file = e.target.files[0];
      if (!file) return;
      const supportedImageTypes = ['image/jpeg', 'image/png', 'image/webp', 'image/gif'];

      if (!supportedImageTypes.includes(file.type)) {
        showSyncBanner('Only JPG, PNG, WebP, and GIF images are supported.', true);
        fileInput.value = '';
        return;
      }

      // 15MB limit check
      if (file.size > 15 * 1024 * 1024) {
        showSyncBanner('Image exceeds the 15MB upload limit. Choose a smaller image.', true);
        fileInput.value = '';
        return;
      }

      // Show upload status spinner, disable form submit
      if (uploadStatus) {
        uploadStatus.classList.remove('d-none');
        uploadStatus.classList.add('d-flex');
      }
      if (uploadStatusText) {
        uploadStatusText.textContent = 'Uploading image to server...';
      }
      if (submitBtn) submitBtn.disabled = true;

      // Read file as base64
      const reader = new FileReader();
      reader.onload = async () => {
        const base64Data = reader.result;

        try {
          const res = await fetchWithCredentials('/api/upload', {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json'
            },
            body: JSON.stringify({
              base64Data,
              mimeType: file.type,
              fileName: file.name
            })
          });
          const data = await parseJsonResponse(res, 'Upload failed on server.');

          if (res.ok) {
            if (data.url) {
              contentTextarea.value = data.url;
              if (previewImg) previewImg.src = data.url;
              if (previewContainer) previewContainer.classList.remove('d-none');
              showSyncBanner('Image uploaded successfully.', false);
            } else {
              throw new Error('No url returned from server');
            }
          } else {
            throw new Error(data?.error || 'Upload failed on server');
          }
        } catch (err) {
          // Fallback to local base64 on failure or offline
          contentTextarea.value = base64Data;
          if (previewImg) previewImg.src = base64Data;
          if (previewContainer) previewContainer.classList.remove('d-none');
          showSyncBanner(`${err.message || 'Image upload failed.'} Using a session-local image payload fallback.`, true);
        } finally {
          // Hide status spinner, re-enable form submit
          if (uploadStatus) {
            uploadStatus.classList.remove('d-flex');
            uploadStatus.classList.add('d-none');
          }
          if (submitBtn) submitBtn.disabled = false;
        }
      };

      reader.onerror = () => {
        showSyncBanner('Failed to read local file.', true);
        if (uploadStatus) {
          uploadStatus.classList.remove('d-flex');
          uploadStatus.classList.add('d-none');
        }
        if (submitBtn) submitBtn.disabled = false;
      };

      reader.readAsDataURL(file);
    });
  }

  if (removeImgBtn) {
    removeImgBtn.addEventListener('click', () => {
      if (fileInput) fileInput.value = '';
      if (previewImg) previewImg.src = '';
      if (previewContainer) previewContainer.classList.add('d-none');
      if (contentTextarea.value.startsWith('data:image') || contentTextarea.value.startsWith('/data/uploads/') || contentTextarea.value.startsWith('/api/images/')) {
        contentTextarea.value = '';
      }
    });
  }

  document.getElementById('add-item-board-btn').addEventListener('click', () => {
    if (!currentBoardId) {
      showSyncBanner('Please open or curate a vision board grid to add elements!', true);
      showTab('home');
      return;
    }
    selectedItemToEdit = null;
    form.reset();
    document.getElementById('item-field-id').value = '';
    if (pendingCustomCardColor) {
      setCustomColorSelection(pendingCustomCardColor);
    }
    
    // Reset file input and preview container for new item
    if (fileInput) fileInput.value = '';
    if (previewContainer) previewContainer.classList.add('d-none');
    if (previewImg) previewImg.src = '';
    if (uploadStatus) {
      uploadStatus.classList.remove('d-flex');
      uploadStatus.classList.add('d-none');
    }

    const zSelect = document.getElementById('item-field-z-index');
    if (zSelect) zSelect.value = 'keep';

    typeSelect.dispatchEvent(new Event('change'));
    delBtn.classList.add('d-none');
    document.getElementById('itemModalHeaderTitle').textContent = 'Curate A New Card';
    itemModalObj.show();
  });

  form.addEventListener('submit', (e) => {
    e.preventDefault();
    const itemId = document.getElementById('item-field-id').value;
    const title = document.getElementById('item-field-title').value;
    const type = normalizeBoardItemType(typeSelect.value);
    let content = document.getElementById('item-field-content').value;
    const caption = document.getElementById('item-field-caption').value;
    const isSecureChecked = document.getElementById('item-field-secure').checked;
    const zAction = document.getElementById('item-field-z-index').value;

    const bIndex = boards.findIndex(b => b.id === currentBoardId);
    if (bIndex === -1) return;

    // Apply reversible local obfuscation prior to saving.
    if (isSecureChecked) {
      content = encryptText(content, currentUser.id);
    }

    const colorVal = getSelectedCardColorValue();

    let itemObj = null;
    let finalZIndex = 10;

    if (itemId) {
      // Edit mode
      const itemIdx = boards[bIndex].items.findIndex(i => i.id === itemId);
      if (itemIdx !== -1) {
        const currentItem = boards[bIndex].items[itemIdx];
        finalZIndex = currentItem.zIndex || 10;

        if (zAction === 'front') {
          let maxZ = 10;
          boards[bIndex].items.forEach(it => {
            if (it.id !== itemId && it.zIndex && it.zIndex > maxZ) maxZ = it.zIndex;
          });
          finalZIndex = maxZ + 1;
        } else if (zAction === 'back') {
          let minZ = 10;
          boards[bIndex].items.forEach(it => {
            if (it.id !== itemId && it.zIndex && it.zIndex < minZ) minZ = it.zIndex;
          });
          finalZIndex = Math.max(1, minZ - 1);
        }

        itemObj = {
          ...currentItem,
          title,
          type,
          content,
          caption,
          color: colorVal,
          isEncrypted: isSecureChecked,
          zIndex: finalZIndex,
          updatedAt: new Date().toISOString()
        };
        boards[bIndex].items[itemIdx] = itemObj;
      }
    } else {
      // Add mode
      const freshId = `item-${Date.now()}`;

      if (zAction === 'front') {
        let maxZ = 10;
        if (boards[bIndex].items) {
          boards[bIndex].items.forEach(it => {
            if (it.zIndex && it.zIndex > maxZ) maxZ = it.zIndex;
          });
        }
        finalZIndex = maxZ + 1;
      } else if (zAction === 'back') {
        let minZ = 10;
        if (boards[bIndex].items) {
          boards[bIndex].items.forEach(it => {
            if (it.zIndex && it.zIndex < minZ) minZ = it.zIndex;
          });
        }
        finalZIndex = Math.max(1, minZ - 1);
      }

      itemObj = {
        id: freshId,
        title,
        type,
        content,
        caption,
        color: colorVal,
        isEncrypted: isSecureChecked,
        zIndex: finalZIndex,
        x: Math.round(15 + Math.random() * 45),
        y: Math.round(20 + Math.random() * 35),
        width: 25,
        height: 22,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };
      
      if (!boards[bIndex].items) boards[bIndex].items = [];
      boards[bIndex].items.push(itemObj);
    }

    refreshResolvedImageForItem(itemObj);
    itemModalObj.hide();
    refreshStudioDisplay();

    if (itemObj) {
      processSyncAction('upsert_item', currentBoardId, itemObj.id, itemObj);
    }
  });

  delBtn.addEventListener('click', () => {
    const itemId = document.getElementById('item-field-id').value;
    if (!itemId) return;

    const bIndex = boards.findIndex(b => b.id === currentBoardId);
    if (bIndex !== -1) {
      boards[bIndex].items = boards[bIndex].items.filter(i => i.id !== itemId);
      
      // Submit deletion log
      processSyncAction('delete_item', currentBoardId, itemId, null);
    }

    itemModalObj.hide();
    refreshStudioDisplay();
  });
}

function openEditCardModal(item) {
  selectedItemToEdit = item;
  document.getElementById('item-field-id').value = item.id;
  document.getElementById('item-field-title').value = item.title;
  document.getElementById('item-field-type').value = item.type;
  document.getElementById('item-field-type').dispatchEvent(new Event('change'));

  const zSelect = document.getElementById('item-field-z-index');
  if (zSelect) zSelect.value = 'keep';

  // Reveal decrypted version for editing
  const isEncrypted = item.isEncrypted || item.content?.startsWith('shield_v15_');
  const valToDisplay = isEncrypted ? decryptText(item.content) : item.content;
  document.getElementById('item-field-secure').checked = isEncrypted;

  document.getElementById('item-field-content').value = valToDisplay;
  document.getElementById('item-field-caption').value = item.caption || '';
  
  // Set checked bg preset
  const inputCheck = document.querySelector(`input[name="color-preset"][value="${item.color}"]`);
  if (inputCheck) {
    inputCheck.checked = true;
  } else if (isCustomCardColor(item.color)) {
    setCustomColorSelection(getCardColorValue(item.color));
  } else {
    document.querySelector('input[name="color-preset"][value="bg-white border-[#C8B6FF]/30 text-[#5E548E]"]').checked = true;
  }

  // Populate preview if type is image and has content
  const fileInput = document.getElementById('item-field-image-file');
  if (fileInput) fileInput.value = '';
  
  const previewContainer = document.getElementById('item-image-preview-container');
  const previewImg = document.getElementById('item-image-preview');
  const uploadStatus = document.getElementById('item-image-upload-status');
  if (uploadStatus) {
    uploadStatus.classList.remove('d-flex');
    uploadStatus.classList.add('d-none');
  }

  if (item.type === 'image' && valToDisplay) {
    if (previewImg) previewImg.src = resolveImageSource(valToDisplay);
    if (previewContainer) previewContainer.classList.remove('d-none');
  } else {
    if (previewContainer) previewContainer.classList.add('d-none');
    if (previewImg) previewImg.src = '';
  }

  document.getElementById('btn-delete-card').classList.remove('d-none');
  document.getElementById('itemModalHeaderTitle').textContent = 'Customise Visual Card Details';
  itemModalObj.show();
}

// 9. AUTOMATED CLOUD DATA SYNCHRONIZER
async function processSyncAction(action, boardId, itemId, payload) {
  return processSyncActionFeature(action, boardId, itemId, payload, {
    fetchWithCredentials,
    setBoards: (nextBoards) => {
      boards = nextBoards;
    },
    showSyncBanner,
    refreshStudioDisplay
  });
}

document.getElementById('save-studio-btn').addEventListener('click', () => {
  const targ = boards.find(b => b.id === currentBoardId);
  if (targ) {
    processSyncAction('update', currentBoardId, null, targ);
  }
});

function showSyncBanner(msg, isError) {
  showSyncBannerFeature(msg, isError);
}

// 10. AUTH FLOW BACKEND REQUESTS
function setupAuthValidationGuard() {
  const form = document.getElementById('auth-form');
  const alert = document.getElementById('auth-alert');
  if (!form || !alert) return;

  form.noValidate = true;

  form.addEventListener('input', (event) => {
    event.target?.classList?.remove('auth-field-invalid');
    if (!form.querySelector('.auth-field-invalid')) {
      alert.classList.add('d-none');
    }
  });

  form.addEventListener('submit', (event) => {
    clearAuthFieldErrors(form);
    alert.classList.add('d-none');

    const invalidState = getAuthValidationState();
    if (!invalidState) return;

    event.preventDefault();
    event.stopImmediatePropagation();
    showAuthValidationMessage(alert, invalidState.message);
    invalidState.input?.classList.add('auth-field-invalid');
    invalidState.input?.focus();
  }, true);
}

function getAuthValidationState() {
  const nameInput = document.getElementById('auth-input-name');
  const emailInput = document.getElementById('auth-input-email');
  const passwordInput = document.getElementById('auth-input-password');
  const isRegisterMode = !document.getElementById('group-auth-name')?.classList.contains('d-none');

  if (isRegisterMode && !nameInput?.value.trim()) {
    return { input: nameInput, message: 'Add your name to create the workspace.' };
  }

  if (!emailInput?.value.trim()) {
    return { input: emailInput, message: 'Enter your email address to continue.' };
  }

  if (!emailInput.checkValidity()) {
    return { input: emailInput, message: 'Enter a valid email address.' };
  }

  if (!passwordInput?.value) {
    return { input: passwordInput, message: 'Enter your Aura Passkey to continue.' };
  }

  return null;
}

function clearAuthFieldErrors(form) {
  form.querySelectorAll('.auth-field-invalid').forEach(input => {
    input.classList.remove('auth-field-invalid');
  });
}

function showAuthValidationMessage(alert, message) {
  alert.textContent = message;
  alert.classList.remove('d-none');
}

function setupAuthForm() {
  setupAuthFormFeature({
    fetchWithCredentials,
    setCurrentUser: (user) => {
      currentUser = user;
    },
    authModalObj,
    renderUserProfileUI,
    fetchUserBoards,
    showTab
  });
}

function toggleAuthMode(regMode) {
  toggleAuthModeFeature(regMode);
}

// 11. GEMINI BOARD RECOMMENDATIONS (AURA DRAWER INSIGHTS)
const aiDrawer = document.getElementById('studio-ai-drawer');
const aiDrawerLoader = document.getElementById('ai-drawer-loader');
const aiDrawerContent = document.getElementById('ai-drawer-content');
const aiAnalysisFeedback = document.getElementById('ai-analysis-feedback');
const aiPaletteFeedback = document.getElementById('ai-color-palette-feedback');
const aiAssetsList = document.getElementById('ai-recommended-assets-list');
const regenerateAiSuggestionsBtn = document.getElementById('regenerate-ai-suggestions-btn');
let activeAiSuggestionsBoardId = null;
let activeAiSuggestions = null;
const addedAiSuggestionKeys = new Set();

document.getElementById('studio-ai-recommender-btn').addEventListener('click', async () => {
  aiDrawer.classList.toggle('d-none');
  if (aiDrawer.classList.contains('d-none')) return;

  const b = boards.find(o => o.id === currentBoardId);
  if (!b) {
    aiAnalysisFeedback.textContent = 'Open a board before asking for AI suggestions.';
    return;
  }

  if (activeAiSuggestions && activeAiSuggestionsBoardId === currentBoardId) {
    renderAiBoardInsights(activeAiSuggestions, { storeResult: false });
    return;
  }

  await requestAiBoardInsights({ forceRefresh: false });
});

regenerateAiSuggestionsBtn?.addEventListener('click', async () => {
  aiDrawer.classList.remove('d-none');
  await requestAiBoardInsights({ forceRefresh: true });
});

async function requestAiBoardInsights({ forceRefresh = false } = {}) {
  const b = boards.find(o => o.id === currentBoardId);
  if (!b) {
    aiAnalysisFeedback.textContent = 'Open a board before asking for AI suggestions.';
    return;
  }

  const triggerBtn = document.getElementById('studio-ai-recommender-btn');
  const originalButtonHtml = triggerBtn?.innerHTML;
  const originalRegenerateHtml = regenerateAiSuggestionsBtn?.innerHTML;

  aiDrawerLoader.classList.remove('d-none');
  aiDrawerContent.classList.add('opacity-40');
  if (triggerBtn) {
    triggerBtn.disabled = true;
    triggerBtn.innerHTML = '<i data-lucide="refresh-cw" class="w-3.5 h-3.5 animate-spin"></i> Loading suggestions';
  }
  if (regenerateAiSuggestionsBtn) {
    regenerateAiSuggestionsBtn.disabled = true;
    regenerateAiSuggestionsBtn.innerHTML = forceRefresh ? 'Regenerating' : 'Loading';
  }
  lucide.createIcons();

  try {
    const res = await fetchWithCredentials('/api/board/recommendations', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        title: b.title,
        description: b.description,
        category: b.category,
        items: b.items || []
      })
    });

    const data = await parseJsonResponse(res, 'AI suggestions are unavailable right now.');
    if (res.ok) {
      if (forceRefresh || activeAiSuggestionsBoardId !== currentBoardId) {
        addedAiSuggestionKeys.clear();
      }
      activeAiSuggestionsBoardId = currentBoardId;
      activeAiSuggestions = data;
      renderAiBoardInsights(data);
    } else {
      throw new Error(data?.error || 'AI suggestions are unavailable right now.');
    }
  } catch (err) {
    console.warn('AI recommendations unavailable; using local fallback suggestions.', err);
    const fallback = buildLocalAiBoardFallback(b);
    if (forceRefresh || activeAiSuggestionsBoardId !== currentBoardId) {
      addedAiSuggestionKeys.clear();
    }
    activeAiSuggestionsBoardId = currentBoardId;
    activeAiSuggestions = fallback;
    renderAiBoardInsights(fallback);
  } finally {
    aiDrawerLoader.classList.add('d-none');
    aiDrawerContent.classList.remove('opacity-40');
    if (triggerBtn) {
      triggerBtn.disabled = false;
      triggerBtn.innerHTML = originalButtonHtml;
    }
    if (regenerateAiSuggestionsBtn) {
      regenerateAiSuggestionsBtn.disabled = false;
      regenerateAiSuggestionsBtn.innerHTML = originalRegenerateHtml || 'Regenerate';
    }
    lucide.createIcons();
  }
}

function buildLocalAiBoardFallback(board) {
  const title = board?.title || 'this board';
  const category = board?.category || 'your current vision';
  return {
    analysis: `Live AI suggestions are unavailable, so Aura prepared a local starter set for "${title}" based on ${category}. You can add these now and refine them later.`,
    suggestedColorPalette: ['#c4b5fd', '#a5b4fc', '#f8fafc', '#fbcfe8'],
    recommendedItems: [
      {
        type: 'quote',
        title: 'Daily Action Catalyst',
        content: 'Continuous improvement is better than delayed perfection.',
        color: 'bg-indigo-50 dark:bg-indigo-950 border-indigo-200 dark:border-indigo-800',
        width: 25,
        height: 25
      },
      {
        type: 'note',
        title: 'Growth Intentions Checklist',
        content: '[ ] Identify one micro-habit you can start today\n[ ] Block out 15 minutes in your calendar\n[ ] Capture one visual reference that matches the mood',
        color: 'bg-emerald-50 dark:bg-emerald-950 border-emerald-200 dark:border-emerald-800',
        width: 25,
        height: 30
      },
      {
        type: 'image',
        title: 'Focus Mindset Symbol',
        content: 'focused workspace soft light growth mindset',
        caption: 'A visual anchor for structured growth and personal momentum.',
        width: 30,
        height: 35
      }
    ]
  };
}

function getAiSuggestionKey(item) {
  return [
    item?.type || '',
    item?.title || '',
    item?.content || '',
    item?.caption || ''
  ].join('|');
}

function setAiSuggestionButtonAdded(button) {
  if (!button) return;
  button.textContent = 'Added';
  button.disabled = true;
  button.className = 'w-full py-1.5 bg-emerald-100 text-emerald-800 text-[9px] font-bold rounded-lg cursor-not-allowed';
}

function renderAiBoardInsights(data) {
  aiAnalysisFeedback.textContent = data.analysis || "Analysis formatted successfully.";
  
  // Suggested color swatches
  aiPaletteFeedback.innerHTML = '';
  if (data.suggestedColorPalette) {
    data.suggestedColorPalette.forEach(hex => {
      const colorDiv = document.createElement('div');
      colorDiv.className = "h-6 flex-1 rounded-lg border border-black/5 hover:scale-105 transition-transform cursor-pointer relative";
      colorDiv.style.backgroundColor = hex;
      colorDiv.title = `Suggested Color: ${hex}`;
      colorDiv.addEventListener('click', () => {
        pendingCustomCardColor = normalizeHexColor(hex);
        setCustomColorSelection(pendingCustomCardColor);
        navigator.clipboard.writeText(hex);
        showSyncBanner(`Selected ${pendingCustomCardColor} for your next custom card tint.`, false);
      });
      aiPaletteFeedback.appendChild(colorDiv);
    });
  }

  // Recommended new elements lists
  aiAssetsList.innerHTML = '';
  const recommendedItems = getValidatedGeneratedItems(data.recommendedItems);
  if (recommendedItems.length > 0) {
    recommendedItems.forEach((normalizedRecommendation, idx) => {
      const assetCard = document.createElement('div');
      assetCard.className = "p-2.5 rounded-xl border border-[#C8B6FF]/30 bg-white/70 text-xs text-[#5E548E] space-y-2 card-ai-suggestion shadow-xs";
      const suggestionKey = getAiSuggestionKey(normalizedRecommendation);
      const safeTitle = escapeHtml(normalizedRecommendation.title || 'Suggested element');
      const safeType = escapeHtml(normalizedRecommendation.type || 'note');
      const safeCaption = escapeHtml(normalizedRecommendation.caption || 'Concept Query');
      const safeContent = escapeHtml(normalizedRecommendation.content || '');
      
      let preHtml = '';
      if (normalizedRecommendation.type === 'image') {
        const urlToUse = resolveImageSource(normalizedRecommendation.content);
        preHtml = `<div class="relative h-16 rounded-lg overflow-hidden bg-[#F8F7FF] dark:bg-[#1E1B2E]">
                     <img src="${urlToUse}" alt="${safeTitle}" class="w-full h-full object-cover" referrerPolicy="no-referrer" ${imageDataAttributes(normalizedRecommendation.title, normalizedRecommendation.caption)} onerror="window.handleImageLoadError(this)" />
                   </div>
                   <p class="text-[10px] text-dusty italic mb-0">${safeCaption}</p>`;
      } else {
        preHtml = `<p class="italic font-mono text-[10.5px] bg-[#F8F7FF] p-1.5 rounded text-plum border border-black/5 leading-normal whitespace-pre-line truncate-3-lines">${safeContent}</p>`;
      }

      assetCard.innerHTML = `
        <div class="d-flex justify-content-between align-items-center">
          <span class="text-[8px] font-mono uppercase bg-[#C8B6FF] text-white px-1.5 py-0.5 rounded font-bold">${safeType}</span>
          <span class="text-[10px] font-bold text-[#5E548E] truncate max-w-[120px]">${safeTitle}</span>
        </div>
        ${preHtml}
        <button type="button" class="w-full py-1.5 bg-[#5E548E] hover:bg-[#9F86C0] text-white text-[9px] font-semibold rounded-lg btn-inject-ai-item mt-1 select-none" data-idx="${idx}">
          + Add to Canvas
        </button>
      `;
      
      aiAssetsList.appendChild(assetCard);

      const addButton = assetCard.querySelector('.btn-inject-ai-item');
      if (addedAiSuggestionKeys.has(suggestionKey)) {
        setAiSuggestionButtonAdded(addButton);
      }

      addButton.addEventListener('click', () => {
        injectRecommendedItem(normalizedRecommendation);
        addedAiSuggestionKeys.add(suggestionKey);
        setAiSuggestionButtonAdded(addButton);
      });
    });
  } else {
    aiAssetsList.innerHTML = `
      <div class="p-3 rounded-xl border border-dashed border-[#C8B6FF]/40 bg-white/50 text-center text-[10px] text-[#9F86C0]">
        No AI recommendations returned for this board yet.
      </div>
    `;
  }

  lucide.createIcons();
}

function injectRecommendedItem(item) {
  const bIndex = boards.findIndex(b => b.id === currentBoardId);
  if (bIndex === -1) return;

  const normalizedItem = normalizeGeneratedItemForCanvas(item);
  if (!normalizedItem) {
    showSyncBanner('This AI suggestion was skipped because it was incomplete.', true);
    return;
  }
  const freshId = `ai-item-${Date.now()}`;
  const newItem = {
    id: freshId,
    title: normalizedItem.title,
    type: normalizedItem.type,
    content: normalizedItem.content,
    caption: normalizedItem.caption || '',
    color: normalizedItem.color || 'bg-white border-[#C8B6FF]/30 text-[#5E548E]',
    x: Math.round(20 + Math.random() * 40),
    y: Math.round(25 + Math.random() * 30),
    width: normalizedItem.width || 25,
    height: normalizedItem.height || 22,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString()
  };

  boards[bIndex].items.push(newItem);
  refreshStudioDisplay();
  processSyncAction('upsert_item', currentBoardId, freshId, newItem);
}

function closeAiDrawer() {
  aiDrawer.classList.add('d-none');
}
document.getElementById('close-ai-drawer-btn').addEventListener('click', closeAiDrawer);

// 12. GEMINI AI IDEA FEED GENERATOR GALLERY
function setupGalleryHandlers() {
  const gTheme = document.getElementById('input-gallery-theme');
  const gLoader = document.getElementById('gallery-loader');
  const gEmpty = document.getElementById('gallery-empty');
  const gResultsBox = document.getElementById('gallery-results-box');
  const gError = document.getElementById('gallery-error');

  const gTitle = document.getElementById('gallery-result-title');
  const gDesc = document.getElementById('gallery-result-desc');
  const gBadges = document.getElementById('gallery-badge-palette');
  const gItemsGrid = document.getElementById('gallery-items-container');
  const gGenerateBtn = document.getElementById('btn-gallery-generate');

  let activeGalleryResult = null;

  gGenerateBtn.addEventListener('click', async () => {
    const val = gTheme.value.trim();
    if (!val) {
      gError.textContent = 'Enter a theme first, then generate vision elements.';
      gError.classList.remove('d-none');
      return;
    }
    triggerGallerySynthesis(val);
  });

  // Action presets click
  document.querySelectorAll('.click-preset').forEach(btn => {
    btn.addEventListener('click', (e) => {
      const pText = e.target.textContent;
      gTheme.value = pText;
      triggerGallerySynthesis(pText);
    });
  });

  async function triggerGallerySynthesis(themeStr) {
    gError.classList.add('d-none');
    gEmpty.classList.add('d-none');
    gResultsBox.classList.add('d-none');
    gLoader.classList.remove('d-none');
    const originalGenerateHtml = gGenerateBtn.innerHTML;
    gGenerateBtn.disabled = true;
    gGenerateBtn.innerHTML = '<i data-lucide="refresh-cw" class="w-4 h-4 animate-spin"></i> Synthesizing...';
    lucide.createIcons();

    try {
      const res = await fetchWithCredentials('/api/inspiration', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({ theme: themeStr })
      });

      const data = await parseJsonResponse(res, 'Could not generate vision elements right now.');
      if (!res.ok) {
        throw new Error(data?.error || 'Could not generate vision elements right now.');
      }

      activeGalleryResult = data;
      renderGalleryOutput(data);

    } catch (err) {
      console.warn('Gallery inspiration unavailable; using local fallback suggestions.', err);
      activeGalleryResult = buildLocalGalleryFallback(themeStr);
      renderGalleryOutput(activeGalleryResult);
      return;
      gError.textContent = err.message || "Couldn’t reach the inspiration service. Check that you’re signed in and the server is running.";
      gError.classList.remove('d-none');
      gError.classList.add('state-panel');
    } finally {
      gLoader.classList.add('d-none');
      gGenerateBtn.disabled = false;
      gGenerateBtn.innerHTML = originalGenerateHtml;
      lucide.createIcons();
    }
  }

  function buildLocalGalleryFallback(themeStr) {
    const cleanTheme = String(themeStr || 'dream board').trim();
    const seed = Array.from(cleanTheme.toLowerCase()).reduce((sum, char) => sum + char.charCodeAt(0), cleanTheme.length);
    const paletteSets = [
      ['#f8fafc', '#ecfdf5', '#fffbeb', '#fff1f2'],
      ['#faf5ff', '#e0f2fe', '#fef3c7', '#dcfce7'],
      ['#f5f3ff', '#cffafe', '#fce7f3', '#f7fee7'],
      ['#eef2ff', '#f0fdfa', '#fff7ed', '#fdf2f8']
    ];
    const toneWords = ['soft', 'focused', 'playful', 'minimal', 'bright', 'calm'];
    const verbs = ['shape', 'curate', 'build', 'anchor', 'sketch', 'refine'];
    const objects = ['workspace', 'mood board', 'daily ritual', 'visual system', 'inspiration corner', 'planning desk'];
    const palette = paletteSets[seed % paletteSets.length];
    const tone = toneWords[seed % toneWords.length];
    const verb = verbs[(seed + 2) % verbs.length];
    const object = objects[(seed + 4) % objects.length];

    return {
      theme: cleanTheme,
      description: `Live AI generation is unavailable, so Aura prepared a local ${tone} concept kit for "${cleanTheme}". Use it as a starting point, then regenerate after Gemini is connected for fully bespoke ideas.`,
      quote: `Small details make the vision feel real: ${verb} one ${object} at a time.`,
      colorPalette: palette,
      suggestedItems: [
        {
          type: 'quote',
          title: `${cleanTheme} Mantra`,
          content: `Design the next version of "${cleanTheme}" through one visible detail, one useful habit, and one space that makes starting easy.`,
          color: 'bg-indigo-50 dark:bg-indigo-950 border-indigo-200 dark:border-indigo-800',
          width: 30,
          height: 25
        },
        {
          type: 'note',
          title: `${cleanTheme} Actions`,
          content: `[ ] Save three references for the ${tone} look\n[ ] Choose one color and one material cue\n[ ] Add a tiny daily ritual that belongs in this vision`,
          color: 'bg-emerald-50 dark:bg-emerald-950 border-emerald-200 dark:border-emerald-800',
          width: 25,
          height: 30
        },
        {
          type: 'image',
          title: `${tone} ${object}`,
          content: `${cleanTheme} ${tone} ${object} natural light aesthetic`,
          caption: `A visual direction for ${cleanTheme}.`,
          width: 40,
          height: 45
        }
      ]
    };
  }

  function renderGalleryOutput(data) {
    gResultsBox.classList.remove('d-none');
    gTitle.textContent = data.theme || data.themeTitle || "Bespoke Design Aspiration";
    
    let descText = data.description || data.quoteSynthesis || "Curated color parameters mapping.";
    if (data.quote && data.quote !== descText) {
      descText += ` — "${data.quote}"`;
    }
    gDesc.textContent = descText;

    // Swatches
    gBadges.innerHTML = '';
    const palette = data.colorPalette || data.hexaPalette;
    if (palette) {
      palette.forEach(hx => {
        const chip = document.createElement('span');
        chip.className = "px-2 py-1 rounded bg-white text-[10px] font-mono border border-black/10 text-plum shadow-xs flex items-center gap-1 cursor-pointer";
        chip.innerHTML = `<span class="h-3 w-3 rounded d-inline-block" style="background-color: ${hx}"></span> ${hx}`;
        chip.addEventListener('click', () => {
          pendingCustomCardColor = normalizeHexColor(hx);
          setCustomColorSelection(pendingCustomCardColor);
          navigator.clipboard.writeText(hx);
          showSyncBanner(`Selected ${pendingCustomCardColor} for your next custom card tint.`, false);
        });
        gBadges.appendChild(chip);
      });
    }

    // Recommendation Cards elements
    gItemsGrid.innerHTML = '';
    const items = getValidatedGeneratedItems(data.suggestedItems || data.itemsToCreate);
    if (items.length > 0) {
      items.forEach((normalizedRecommendation, index) => {
        const itemCol = document.createElement('div');
        itemCol.className = index % 5 === 0 ? 'col-md-6' : 'col-md-4';
        const safeTitle = escapeHtml(normalizedRecommendation.title || 'Suggested element');
        const safeType = escapeHtml(normalizedRecommendation.type || 'note');
        const safeCaption = escapeHtml(normalizedRecommendation.caption || 'Concept tag');
        const safeContent = escapeHtml(normalizedRecommendation.content || '');
        
        let preHtml = '';
        if (normalizedRecommendation.type === 'image') {
          const finalUrl = resolveImageSource(normalizedRecommendation.content);
          preHtml = `
            <div class="my-2 rounded-lg overflow-hidden relative h-24 bg-[#F8F7FF] dark:bg-[#1E1B2E]">
              <img src="${finalUrl}" alt="${safeTitle}" class="w-full h-full object-cover" referrerPolicy="no-referrer" ${imageDataAttributes(normalizedRecommendation.title, normalizedRecommendation.caption)} onerror="window.handleImageLoadError(this)" />
              <div class="absolute bottom-0 inset-x-0 bg-black/40 text-white p-1 text-[9px] text-center italic">${safeCaption}</div>
            </div>
          `;
        } else {
          preHtml = `<p class="italic text-[11px] bg-[#F8F7FF] dark:bg-[#1E1B2E] border p-2 rounded text-plum dark:text-cream leading-relaxed font-mono whitespace-pre-line">${safeContent}</p>`;
        }

        itemCol.innerHTML = `
          <div class="glass-card p-3 h-100 flex flex-col justify-between whitespace-normal">
            <div>
              <div class="d-flex justify-content-between align-items-center mb-2">
                <span class="text-[8px] font-mono uppercase bg-lilac/30 text-plum px-1.5 py-0.5 rounded font-bold">${safeType}</span>
                <span class="text-[10px] font-bold text-plum max-w-[140px] truncate">${safeTitle}</span>
              </div>
              ${preHtml}
            </div>
            
            <span class="text-[9px] text-emerald-600 font-mono mt-2 block uppercase text-right">✓ Synapse Ready</span>
          </div>
        `;
        gItemsGrid.appendChild(itemCol);
      });
    } else {
      gItemsGrid.innerHTML = `
        <div class="col-12">
          <div class="state-empty py-8 text-center text-[#9F86C0] bg-white/40 dark:bg-black/10 rounded-3xl border border-dashed border-[#C8B6FF]/35">
            <h4 class="font-bold text-sm mb-1 text-[#5E548E]">No ideas returned</h4>
            <p class="text-xs leading-relaxed max-w-xs mx-auto">Try a more specific theme or generate again.</p>
          </div>
        </div>
      `;
    }

    lucide.createIcons();
  }

  // Push integration into Selected Board
  document.getElementById('btn-gallery-push-all').addEventListener('click', () => {
    if (!activeGalleryResult) {
      showSyncBanner('No elements available to push. Please generate inspiration first.', true);
      return;
    }

    const items = getValidatedGeneratedItems(activeGalleryResult.suggestedItems || activeGalleryResult.itemsToCreate);
    if (!items || items.length === 0) {
      showSyncBanner('The generated theme has no elements to push.', true);
      return;
    }
    
    const select = document.getElementById('gallery-push-board-select');
    const bId = select.value;
    if (!bId) {
      showSyncBanner('Please select an active target board first. If you have no boards, create one on the home dashboard.', true);
      return;
    }

    const bIndex = boards.findIndex(b => b.id === bId);
    if (bIndex === -1) {
      showSyncBanner('Target board not found. Please refresh or create a board first.', true);
      return;
    }

    items.forEach((normalizedItem, idx) => {
      const freshId = `gallery-item-${Date.now()}-${idx}`;
      const newItem = {
        id: freshId,
        title: normalizedItem.title,
        type: normalizedItem.type,
        content: normalizedItem.content,
        caption: normalizedItem.caption || '',
        color: normalizedItem.color || 'bg-white border-[#C8B6FF]/30 text-[#5E548E]',
        x: Math.round(15 + Math.random() * 50),
        y: Math.round(20 + Math.random() * 40),
        width: normalizedItem.width || 25,
        height: normalizedItem.height || 22,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };

      if (!boards[bIndex].items) boards[bIndex].items = [];
      boards[bIndex].items.push(newItem);

      // Save position item
      processSyncAction('upsert_item', bId, freshId, newItem);
    });

    showSyncBanner(`Injected ${items.length} elements to your "${boards[bIndex].title}" board!`, false);
    enterBoardStudio(bId);
  });
}

function populatePushSelection() {
  const select = document.getElementById('gallery-push-board-select');
  if (!select) return;

  select.innerHTML = boards.map(b => `<option value="${b.id}">${escapeHtml(b.title || 'Untitled board')}</option>`).join('');
}

// 13. LOCAL OBFUSCATION LAB TESTING
function setupCryptoLab() {
  setupCryptoLabHandlers({
    getCurrentUser: () => currentUser
  });
}

// 14. PREFERENCES PANEL TOGGLES
function setupSettingsHandlers() {
  setupPreferenceHandlers({
    getCurrentUser: () => currentUser,
    setCurrentUser: (user) => {
      currentUser = user;
      renderUserProfileUI();
    },
    fetchWithCredentials,
    showSyncBanner,
    handleLogout
  });
}

function renderUserSettings() {
  renderUserSettingsView({
    getCurrentUser: () => currentUser
  });
}

// 15. PERIODIC COLLABORATION UPDATES
function startCollaborationTicker() {
  startCollaborationTickerFeature({
    getCurrentUser: () => currentUser,
    fetchWithCredentials
  });
}

// 13. OFFLINE PDF & PRINT EXPORT ENGINE
function setupExportPdfHandlers() {
  const btnPrint = document.getElementById('btn-export-print');
  if (btnPrint) {
    btnPrint.addEventListener('click', () => {
      exportPdfModalObj.hide();
      setTimeout(() => {
        window.print();
      }, 450);
    });
  }

  const btnJsPdf = document.getElementById('btn-export-jspdf');
  if (btnJsPdf) {
    btnJsPdf.addEventListener('click', () => {
      exportPdfModalObj.hide();
      generateProgrammaticPdf();
    });
  }

  const btnVisualPdf = document.getElementById('btn-export-visual-pdf');
  if (btnVisualPdf) {
    btnVisualPdf.addEventListener('click', () => {
      generateVisualBoardPdf();
    });
  }
}

async function generateVisualBoardPdf() {
  const b = boards.find(o => o.id === currentBoardId);
  if (!b) {
    showSyncBanner('Please select an active vision board to export.', true);
    return;
  }

  const canvasWrapper = document.getElementById('canvas-area-wrapper');
  if (!canvasWrapper) {
    showSyncBanner('Canvas wrapper not found.', true);
    return;
  }

  exportPdfModalObj.hide();
  canvasWrapper.classList.add('printing-in-progress');

  let jsPDF;
  try {
    ({ jsPDF } = await import('jspdf'));
  } catch (error) {
    canvasWrapper.classList.remove('printing-in-progress');
    console.error("jsPDF load error:", error);
    showSyncBanner('Failed to load the PDF exporter. Please try again.', true);
    return;
  }

  // Wait a small delay to ensure modal hides completely and classes apply
  setTimeout(() => {
    html2canvas(canvasWrapper, {
      scale: 2, // high quality
      useCORS: true, // fetch cross-origin images
      allowTaint: false,
      backgroundColor: null // transparent/preset background
    }).then(canvas => {
      canvasWrapper.classList.remove('printing-in-progress');

      const imgData = canvas.toDataURL('image/png');
      const doc = new jsPDF({
        orientation: 'landscape',
        unit: 'mm',
        format: 'a4'
      });

      const pageWidth = doc.internal.pageSize.getWidth(); // 297
      const pageHeight = doc.internal.pageSize.getHeight(); // 210

      const canvasRatio = canvas.width / canvas.height;
      const pageRatio = pageWidth / pageHeight;

      let imgWidth = pageWidth;
      let imgHeight = pageHeight;
      let xOffset = 0;
      let yOffset = 0;

      if (canvasRatio > pageRatio) {
        imgHeight = pageWidth / canvasRatio;
        yOffset = (pageHeight - imgHeight) / 2;
      } else {
        imgWidth = pageHeight * canvasRatio;
        xOffset = (pageWidth - imgWidth) / 2;
      }

      doc.addImage(imgData, 'PNG', xOffset, yOffset, imgWidth, imgHeight);

      const cleanFileName = b.title.toLowerCase().replace(/[^a-z0-9]+/g, '_') + '_visual.pdf';
      doc.save(cleanFileName);
      showSyncBanner('Successfully downloaded visual landscape PDF!', false);
    }).catch(err => {
      canvasWrapper.classList.remove('printing-in-progress');
      console.error("html2canvas error:", err);
      showSyncBanner('Failed to render visual board canvas.', true);
    });
  }, 100);
}


async function generateProgrammaticPdf() {
  const b = boards.find(o => o.id === currentBoardId);
  if (!b) {
    showSyncBanner('Please select an active vision board to export.', true);
    return;
  }

  try {
    const { jsPDF } = await import('jspdf');
    const doc = new jsPDF({
      orientation: 'portrait',
      unit: 'mm',
      format: 'a4'
    });

    const pageWidth = doc.internal.pageSize.getWidth();
    const pageHeight = doc.internal.pageSize.getHeight();

    // 1. Sleek Colored Header Banner
    doc.setFillColor(94, 84, 142); // Plum #5E548E (RGB: 94, 84, 142)
    doc.rect(0, 0, pageWidth, 40, 'F');

    doc.setTextColor(255, 255, 255);
    doc.setFont("Helvetica", "bold");
    doc.setFontSize(22);
    doc.text("AURA VISION BOARD DOSSIER", 15, 18);
    
    doc.setFont("Helvetica", "normal");
    doc.setFontSize(10);
    doc.setTextColor(200, 182, 255); // Lilac #C8B6FF (RGB: 200, 182, 255)
    doc.text("Offline Creator Archive • Created via aura.board studio", 15, 26);
    doc.text(`Generated: ${new Date().toLocaleDateString()}`, pageWidth - 15, 26, { align: 'right' });

    // 2. Metadata Section (Board Info)
    doc.setTextColor(40, 40, 40);
    doc.setFont("Helvetica", "bold");
    doc.setFontSize(18);
    doc.text(b.title.toUpperCase(), 15, 55);

    // Category Badge
    doc.setFillColor(200, 182, 255); // Lilac #C8B6FF
    doc.rect(15, 60, 45, 6, 'F');
    doc.setTextColor(94, 84, 142); // Plum #5E548E
    doc.setFont("Helvetica", "bold");
    doc.setFontSize(8);
    doc.text((b.category || "General").toUpperCase(), 17, 64);

    // Board Description
    doc.setTextColor(100, 100, 100);
    doc.setFont("Helvetica", "normal");
    doc.setFontSize(11);
    const descText = b.description || "No specific themes or category descriptions are set.";
    const splitDesc = doc.splitTextToSize(descText, pageWidth - 30);
    doc.text(splitDesc, 15, 75);

    let currentY = 75 + (splitDesc.length * 5) + 12;

    doc.setDrawColor(200, 182, 255);
    doc.setLineWidth(0.5);
    doc.line(15, currentY - 5, pageWidth - 15, currentY - 5);

    const items = b.items || [];
    
    if (items.length === 0) {
      doc.setFont("Helvetica", "italic");
      doc.setFontSize(12);
      doc.setTextColor(150, 150, 150);
      doc.text("No elements currently curated on this vision board grid.", 15, currentY + 10);
    } else {
      const notes = items.filter(it => it.type === 'note');
      const quotes = items.filter(it => it.type === 'quote');
      const images = items.filter(it => it.type === 'image');
      const standard = items.filter(it => !['note', 'quote', 'image'].includes(it.type));

      // 3a. Aspirational Quotes
      if (quotes.length > 0) {
        doc.setFont("Helvetica", "bold");
        doc.setFontSize(13);
        doc.setTextColor(94, 84, 142);
        doc.text("🪐 ASPIRATIONAL QUOTES & PHRASES", 15, currentY);
        currentY += 8;

        quotes.forEach(it => {
          if (currentY > pageHeight - 35) {
            doc.addPage();
            currentY = 25;
          }

          const isEncrypted = it.isEncrypted || it.content?.startsWith('shield_v15_');
          const revealedPayload = isEncrypted ? decryptText(it.content) : it.content;
          const quote = getQuoteParts(revealedPayload, it.caption);

          doc.setFillColor(248, 247, 255); // #F8F7FF
          doc.rect(15, currentY, pageWidth - 30, 20, 'F');
          
          doc.setFont("Helvetica", "italic");
          doc.setFontSize(10);
          doc.setTextColor(60, 60, 60);

          const quoteText = `"${quote.quoteText}"`;
          const splitQuote = doc.splitTextToSize(quoteText, pageWidth - 42);
          doc.text(splitQuote, 20, currentY + 7);

          if (quote.author) {
            doc.setFont("Helvetica", "bold");
            doc.setFontSize(8);
            doc.setTextColor(159, 134, 192);
            doc.text(`- ${quote.author}`.toUpperCase(), pageWidth - 22, currentY + 16, { align: 'right' });
          }

          currentY += 25;
        });
        currentY += 5;
      }

      // 3b. Action Checklists
      if (notes.length > 0) {
        if (currentY > pageHeight - 40) {
          doc.addPage();
          currentY = 25;
        }

        doc.setFont("Helvetica", "bold");
        doc.setFontSize(13);
        doc.setTextColor(94, 84, 142);
        doc.text("✅ INTENTIONS & ACTION CHECKLISTS", 15, currentY);
        currentY += 8;

        notes.forEach(it => {
          const isEncrypted = it.isEncrypted || it.content?.startsWith('shield_v15_');
          const revealedPayload = isEncrypted ? decryptText(it.content) : it.content;
          const steps = getChecklistSteps(revealedPayload);

          const approxHeight = 8 + (steps.length * 6);
          if (currentY + approxHeight > pageHeight - 20) {
            doc.addPage();
            currentY = 25;
          }

          doc.setFont("Helvetica", "bold");
          doc.setFontSize(11);
          doc.setTextColor(40, 40, 40);
          doc.text(it.title.toUpperCase(), 15, currentY);
          currentY += 5;

          steps.forEach(step => {
            doc.setFont("Helvetica", "normal");
            doc.setFontSize(10);
            doc.setTextColor(80, 80, 80);
            
            doc.setDrawColor(159, 134, 192);
            doc.rect(17, currentY - 3, 3.5, 3.5);
            doc.text(step.text, 24, currentY);
            currentY += 6;
          });
          currentY += 4;
        });
        currentY += 5;
      }

      // 3c. Visual Themes
      if (images.length > 0) {
        if (currentY > pageHeight - 40) {
          doc.addPage();
          currentY = 25;
        }

        doc.setFont("Helvetica", "bold");
        doc.setFontSize(13);
        doc.setTextColor(94, 84, 142);
        doc.text("🎨 VISUAL THEMES & REFLECTIONS", 15, currentY);
        currentY += 8;

        images.forEach(it => {
          if (currentY > pageHeight - 30) {
            doc.addPage();
            currentY = 25;
          }

          const isEncrypted = it.isEncrypted || it.content?.startsWith('shield_v15_');
          const revealedPayload = isEncrypted ? decryptText(it.content) : it.content;

          doc.setFont("Helvetica", "bold");
          doc.setFontSize(11);
          doc.setTextColor(40, 40, 40);
          doc.text(it.title, 15, currentY);
          currentY += 4.5;

          doc.setFont("Helvetica", "normal");
          doc.setFontSize(10);
          doc.setTextColor(100, 100, 100);

          let displayConceptText = `Concept focus query: ${revealedPayload}`;
          if (it.caption) {
            displayConceptText += `\nAnnotation: ${it.caption}`;
          }

          const splitImgText = doc.splitTextToSize(displayConceptText, pageWidth - 30);
          doc.text(splitImgText, 15, currentY);
          
          currentY += (splitImgText.length * 4.5) + 6;
        });
        currentY += 5;
      }

      // 3d. Additional Elements
      if (standard.length > 0) {
        if (currentY > pageHeight - 40) {
          doc.addPage();
          currentY = 25;
        }

        doc.setFont("Helvetica", "bold");
        doc.setFontSize(13);
        doc.setTextColor(94, 84, 142);
        doc.text("🔮 ADDITIONAL HORIZONS & MOTIFS", 15, currentY);
        currentY += 8;

        standard.forEach(it => {
          if (currentY > pageHeight - 30) {
            doc.addPage();
            currentY = 25;
          }

          const isEncrypted = it.isEncrypted || it.content?.startsWith('shield_v15_');
          const revealedPayload = isEncrypted ? decryptText(it.content) : it.content;

          doc.setFont("Helvetica", "bold");
          doc.setFontSize(11);
          doc.setTextColor(40, 40, 40);
          doc.text(it.title, 15, currentY);
          currentY += 4.5;

          doc.setFont("Helvetica", "normal");
          doc.setFontSize(10);
          doc.setTextColor(80, 80, 80);

          const splitStdText = doc.splitTextToSize(revealedPayload || '', pageWidth - 30);
          doc.text(splitStdText, 15, currentY);

          currentY += (splitStdText.length * 4.5) + 6;
        });
      }
    }

    // Page numbers & consistent footer
    const pageCount = doc.internal.pages.length - 1;
    for (let i = 1; i <= pageCount; i++) {
      doc.setPage(i);
      
      doc.setDrawColor(200, 182, 255);
      doc.setLineWidth(0.25);
      doc.line(15, pageHeight - 15, pageWidth - 15, pageHeight - 15);

      doc.setFont("Helvetica", "normal");
      doc.setFontSize(8);
      doc.setTextColor(150, 150, 150);
      doc.text("aura.board • Materialise Your Intentions", 15, pageHeight - 10);
      doc.text(`Page ${i} of ${pageCount}`, pageWidth - 15, pageHeight - 10, { align: 'right' });
    }

    const cleanFileName = b.title.toLowerCase().replace(/[^a-z0-9]+/g, '_') + '_dossier.pdf';
    doc.save(cleanFileName);
    showSyncBanner('Successfully downloaded your vision board PDF dossier offline!', false);

  } catch (error) {
    console.error("PDF Compilation error:", error);
    showSyncBanner('Failed to compile offline PDF document.', true);
  }
}

// 14. SEARCH INPUT EVENT ROUTING
function setupSearchHandlers() {
  setupBoardSearchHandlers({
    getBoardsSearchQuery: () => boardsSearchQuery,
    setBoardsSearchQuery: (value) => {
      boardsSearchQuery = value;
    },
    renderUserBoardsList
  });
}

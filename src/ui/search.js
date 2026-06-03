export function setupSearchHandlers({ getBoardsSearchQuery, setBoardsSearchQuery, renderUserBoardsList }) {
  const searchInput = document.getElementById('board-search-input');
  const clearBtn = document.getElementById('board-search-clear-btn');

  if (searchInput) {
    searchInput.addEventListener('input', (e) => {
      setBoardsSearchQuery(e.target.value);
      if (clearBtn) {
        if (getBoardsSearchQuery()) {
          clearBtn.classList.remove('hidden');
        } else {
          clearBtn.classList.add('hidden');
        }
      }
      renderUserBoardsList();
    });
  }

  if (clearBtn) {
    clearBtn.addEventListener('click', () => {
      setBoardsSearchQuery("");
      if (searchInput) {
        searchInput.value = "";
      }
      clearBtn.classList.add('hidden');
      renderUserBoardsList();
    });
  }
}

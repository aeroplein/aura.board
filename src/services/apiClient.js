export function fetchWithCredentials(url, options = {}) {
  return fetch(url, {
    ...options,
    credentials: 'include'
  });
}

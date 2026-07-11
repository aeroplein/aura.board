export function fetchWithCredentials(url, options = {}) {
  return fetch(url, {
    ...options,
    credentials: 'include'
  });
}

export async function parseJsonResponse(response, fallbackMessage = 'Request failed. Please try again.') {
  const contentType = response.headers.get('content-type') || '';
  const text = await response.text();
  const isJson = contentType.includes('application/json') || contentType.includes('+json');

  if (!text.trim()) {
    if (!response.ok) {
      throw new Error(fallbackMessage);
    }
    return null;
  }

  if (!isJson) {
    if (!response.ok) {
      throw new Error(fallbackMessage);
    }
    throw new Error('Server returned an unexpected response format.');
  }

  try {
    return JSON.parse(text);
  } catch {
    throw new Error(fallbackMessage);
  }
}

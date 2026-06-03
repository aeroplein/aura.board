export function encryptText(text, key) {
  if (!text) return '';
  try {
    const b64 = btoa(unescape(encodeURIComponent(text)));
    return 'shield_v15_' + b64.split('').map(c => String.fromCharCode(c.charCodeAt(0) + 2)).join('');
  } catch (e) {
    return text;
  }
}

export function decryptText(cipher, key) {
  if (!cipher) return '';
  if (!cipher.startsWith('shield_v15_')) return cipher;
  try {
    const stripped = cipher.substring(11);
    const unrotated = stripped.split('').map(c => String.fromCharCode(c.charCodeAt(0) - 2)).join('');
    return decodeURIComponent(escape(atob(unrotated)));
  } catch (e) {
    return cipher;
  }
}

export function setupCryptoLab({ getCurrentUser }) {
  const pText = document.getElementById('crypto-plaintext');
  const btn = document.getElementById('btn-run-crypto-cycles');
  const outputGroup = document.getElementById('crypto-output-group');
  const cipherPlaceholder = document.getElementById('crypto-ciphertext');
  const decPlaceholder = document.getElementById('crypto-decrypted');

  btn.addEventListener('click', () => {
    const orig = pText.value;
    if (!orig) return;

    outputGroup.classList.remove('d-none');

    const currentUser = getCurrentUser();
    const encrypted = encryptText(orig, currentUser?.id || 'aura_test_salt');
    cipherPlaceholder.textContent = encrypted;

    const decrypted = decryptText(encrypted, currentUser?.id || 'aura_test_salt');
    decPlaceholder.textContent = decrypted;
  });
}

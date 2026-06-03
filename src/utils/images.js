import { escapeHtml } from './html.js';

const curatedImageSources = [
  {
    terms: ['reading', 'book', 'library', 'cozy', 'nook', 'sanctuary'],
    url: 'https://images.unsplash.com/photo-1495446815901-a7297e633e8d?auto=format&fit=crop&w=1200&q=80'
  },
  {
    terms: ['garden', 'plant', 'green', 'sustainable', 'urban'],
    url: 'https://images.unsplash.com/photo-1466692476868-aef1dfb1e735?auto=format&fit=crop&w=1200&q=80'
  },
  {
    terms: ['fitness', 'triathlon', 'ironman', 'endurance', 'running'],
    url: 'https://images.unsplash.com/photo-1517836357463-d25dfeac3438?auto=format&fit=crop&w=1200&q=80'
  },
  {
    terms: ['design', 'studio', 'minimalist', 'swiss', 'workspace'],
    url: 'https://images.unsplash.com/photo-1497366754035-f200968a6e72?auto=format&fit=crop&w=1200&q=80'
  },
  {
    terms: ['office', 'engineer', 'engineering', 'developer', 'computer', 'desk', 'tech', 'coding'],
    url: 'https://images.unsplash.com/photo-1497366811353-6870744d04b2?auto=format&fit=crop&w=1200&q=80'
  },
  {
    terms: ['cute', 'pastel', 'soft', 'cozy', 'aesthetic'],
    url: 'https://images.unsplash.com/photo-1518455027359-f3f8164ba6bd?auto=format&fit=crop&w=1200&q=80'
  },
  {
    terms: ['travel', 'exploration', 'global', 'beach', 'mountain'],
    url: 'https://images.unsplash.com/photo-1500530855697-b586d89ba3ee?auto=format&fit=crop&w=1200&q=80'
  }
];

export function resolveImageSource(rawValue) {
  const value = (rawValue || '').trim();
  if (!value) return curatedImageSources[0].url;
  if (value.startsWith('/data/') || value.startsWith('data:image') || value.startsWith('/api/images/')) return value;
  if (value.startsWith('http')) {
    if (!value.includes('images.unsplash.com/featured')) return value;
    const keyword = decodeURIComponent(value.split('?').pop() || '');
    return resolveImageSource(keyword);
  }

  const sig = `${Date.now()}-${Math.floor(Math.random() * 100000)}`;
  return `/api/images/search?query=${encodeURIComponent(value)}&sig=${encodeURIComponent(sig)}`;
}

export function resolveFallbackImageSource(rawValue) {
  const value = (rawValue || '').trim().toLowerCase();
  const curated = curatedImageSources.find(source => source.terms.some(term => value.includes(term)));
  return curated?.url || 'https://images.unsplash.com/photo-1497366754035-f200968a6e72?auto=format&fit=crop&w=1200&q=80';
}

export function imageDataAttributes(title, caption) {
  return `data-fallback-title="${escapeHtml(title || 'Visual Concept')}" data-fallback-caption="${escapeHtml(caption || 'Image preview unavailable')}"`;
}

export function imageFallbackMarkup(title, caption) {
  return `
    <div class="image-fallback d-flex flex-column align-items-center justify-content-center text-center w-full h-full p-3 bg-[#F8F7FF] dark:bg-[#1E1B2E] text-[#5E548E]">
      <i data-lucide="image" class="w-7 h-7 text-[#C8B6FF] mb-2"></i>
      <span class="text-[10px] font-bold">${escapeHtml(title || 'Visual Concept')}</span>
      <span class="text-[9px] text-[#9F86C0] mt-1">${escapeHtml(caption || 'Image preview unavailable')}</span>
    </div>
  `;
}

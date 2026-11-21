import type { Request } from 'playwright';

const embedHosts = new Set(['www.youtube.com', 'www.youtube-nocookie.com']);

export function isYouTubeEmbedRequest(request: Request, startOrigin: string): boolean {
  const url = new URL(request.url());
  if (url.protocol !== 'https:' || url.port || url.username || url.password) return false;

  // A navigation request can arrive before Playwright has identified its frame.
  let frame;
  try {
    frame = request.frame();
  } catch {
    return false;
  }
  const parent = frame.parentFrame();
  const parentUrl = parent ? URL.parse(parent.url()) : null;
  const method = request.method();
  const type = request.resourceType();
  if (parent && request.isNavigationRequest()) {
    return method === 'GET' && embedHosts.has(url.hostname) && /^\/embed\/[\w-]+\/?$/.test(url.pathname)
      && parentUrl?.origin === startOrigin;
  }

  if (!parent) {
    return method === 'GET' && type === 'script' && embedHosts.has(url.hostname)
      && (url.pathname === '/iframe_api' || url.pathname === '/player_api'
        || url.pathname.endsWith('/www-widgetapi.js'));
  }

  const frameUrl = URL.parse(frame.url());
  if (parentUrl?.origin !== startOrigin || !frameUrl || !embedHosts.has(frameUrl.hostname)
    || !frameUrl.pathname.startsWith('/embed/')) return false;
  const playerAsset = embedHosts.has(url.hostname) || ['i.ytimg.com', 's.ytimg.com'].includes(url.hostname);
  const videoAsset = url.hostname === 'googlevideo.com' || url.hostname.endsWith('.googlevideo.com');
  if (method === 'GET' && type !== 'document' && (playerAsset || videoAsset)) return true;
  return method === 'POST' && embedHosts.has(url.hostname) && url.pathname === '/youtubei/v1/player';
}

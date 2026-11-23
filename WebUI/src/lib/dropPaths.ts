import { bridge } from './bridge';
import type { PreloadPathValidationItem } from './types';

function getFileDropPaths(files: File[]): string[] {
  return files
    .map((f) => {
      const withPath = f as File & { path?: string };
      return withPath.path ?? '';
    })
    .filter((p) => Boolean(p && p.trim()));
}

function normalizeDropTextLine(raw: string): string {
  const lower = raw.toLowerCase();
  if (lower.startsWith('file:///')) {
    try {
      return decodeURIComponent(raw.slice(8)).replace(/\//g, '\\');
    } catch {
      return raw;
    }
  }
  if (lower.startsWith('file://')) {
    try {
      const decoded = decodeURIComponent(raw.slice(7)).replace(/\//g, '\\');
      return `\\\\${decoded.replace(/^\\+/, '')}`;
    } catch {
      return raw;
    }
  }
  return raw;
}

export async function resolveDroppedPreloadPaths(
  dataTransfer: DataTransfer | null
): Promise<PreloadPathValidationItem[]> {
  if (!dataTransfer) return [];

  const droppedFiles = Array.from(dataTransfer.files ?? []);
  const plainText = dataTransfer.getData('text/plain') ?? '';
  const uriList = dataTransfer.getData('text/uri-list') ?? '';

  const textLines = `${plainText}\n${uriList}`
    .split(/[\r\n]+/)
    .map((s) => s.trim())
    .filter((s) => Boolean(s) && !s.startsWith('#'))
    .map(normalizeDropTextLine);

  const candidates =
    textLines.length > 0 ? textLines : getFileDropPaths(droppedFiles);
  const res = await bridge.resolveDrop(candidates, droppedFiles);
  return res.items;
}

import { useCallback, useEffect, useState } from 'react';
import { bridge } from '../lib/bridge';
import type { ThemeMode } from '../lib/types';

export const THEME_STORAGE_KEY = 'wtp-theme';

function readStoredTheme(): ThemeMode | null {
  try {
    const val = localStorage.getItem(THEME_STORAGE_KEY);
    if (val === 'dark' || val === 'light') {
      return val;
    }
  } catch {
    // Ignore storage read restrictions
  }
  return null;
}

function getSystemPrefersDark(): boolean {
  try {
    return (
      typeof window !== 'undefined' &&
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-color-scheme: dark)').matches
    );
  } catch {
    return true;
  }
}

export function resolveEffectiveTheme(mode: ThemeMode): 'dark' | 'light' {
  if (mode === 'dark' || mode === 'light') {
    return mode;
  }
  return getSystemPrefersDark() ? 'dark' : 'light';
}

function applyThemeToDom(mode: ThemeMode): 'dark' | 'light' {
  const effective = resolveEffectiveTheme(mode);
  if (typeof document !== 'undefined') {
    const meta = document.querySelector('meta[name="color-scheme"]');
    if (meta) {
      meta.setAttribute('content', mode === 'system' ? 'light dark' : mode);
    }
    document.documentElement.dataset.theme = effective;
  }
  return effective;
}

export function useTheme(initialTheme?: ThemeMode) {
  const [themeMode, setThemeMode] = useState<ThemeMode>(() => {
    const stored = readStoredTheme();
    if (stored) return stored;
    return initialTheme ?? 'system';
  });

  const [systemDark, setSystemDark] = useState<boolean>(() =>
    getSystemPrefersDark()
  );

  const effectiveTheme: 'dark' | 'light' =
    themeMode === 'system' ? (systemDark ? 'dark' : 'light') : themeMode;

  useEffect(() => {
    applyThemeToDom(themeMode);
  }, [themeMode, systemDark]);

  // Listen to OS color-scheme changes without ever wiping an explicit user override
  useEffect(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return;
    }
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    const onChange = (event: MediaQueryListEvent) => {
      setSystemDark(event.matches);
    };
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, []);

  // Stay in sync across windows/tabs via storage event
  useEffect(() => {
    if (typeof window === 'undefined') return;
    const onStorage = (event: StorageEvent) => {
      if (event.key !== THEME_STORAGE_KEY) return;
      if (event.newValue === 'light' || event.newValue === 'dark') {
        setThemeMode(event.newValue);
      } else {
        setThemeMode('system');
      }
    };
    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, []);

  const syncBootstrapTheme = useCallback((serverTheme: ThemeMode) => {
    const stored = readStoredTheme();
    if (stored) {
      applyThemeToDom(stored);
      return;
    }
    setThemeMode(serverTheme);
    try {
      if (serverTheme === 'system') {
        localStorage.removeItem(THEME_STORAGE_KEY);
      } else {
        localStorage.setItem(THEME_STORAGE_KEY, serverTheme);
      }
    } catch {
      // Ignore storage write restriction
    }
    applyThemeToDom(serverTheme);
  }, []);

  const setTheme = useCallback(async (nextMode: ThemeMode) => {
    setThemeMode(nextMode);
    try {
      if (nextMode === 'system') {
        localStorage.removeItem(THEME_STORAGE_KEY);
      } else {
        localStorage.setItem(THEME_STORAGE_KEY, nextMode);
      }
    } catch {
      // Ignore storage write restriction
    }
    applyThemeToDom(nextMode);
    try {
      await bridge.setTheme(nextMode);
    } catch {
      // Keep UI responsive even if host sync fails in standalone browser
    }
  }, []);

  return {
    themeMode,
    effectiveTheme,
    systemTheme: (systemDark ? 'dark' : 'light') as 'dark' | 'light',
    setTheme,
    syncBootstrapTheme,
  };
}

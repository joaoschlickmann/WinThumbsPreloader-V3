import '@testing-library/jest-dom/vitest';
import { expect } from 'vitest';
import * as axeMatchers from 'vitest-axe/matchers';
import type { AxeMatchers } from 'vitest-axe';

expect.extend(axeMatchers);

declare module 'vitest' {
  interface Assertion<R = void, T = unknown> extends AxeMatchers {
    readonly __vitest_axe_marker__?: [R, T];
  }
  interface AsymmetricMatchersContaining extends AxeMatchers {}
}

// Polyfill HTMLDialogElement.showModal / close and UA stylesheet for jsdom
if (typeof document !== 'undefined') {
  if (!document.querySelector('meta[name="color-scheme"]')) {
    const meta = document.createElement('meta');
    meta.setAttribute('name', 'color-scheme');
    meta.setAttribute('content', 'light dark');
    document.head.appendChild(meta);
  }

  const uaStyle = document.createElement('style');
  uaStyle.textContent = 'dialog:not([open]) { display: none; }';
  document.head.appendChild(uaStyle);
}

if (typeof HTMLDialogElement !== 'undefined') {
  if (!HTMLDialogElement.prototype.showModal) {
    HTMLDialogElement.prototype.showModal = function showModal() {
      this.setAttribute('open', '');
    };
  }
  if (!HTMLDialogElement.prototype.close) {
    HTMLDialogElement.prototype.close = function close(returnValue?: string) {
      if (returnValue !== undefined) {
        this.returnValue = returnValue;
      }
      this.removeAttribute('open');
      this.dispatchEvent(new Event('close'));
    };
  }
}

if (typeof HTMLFormElement !== 'undefined') {
  HTMLFormElement.prototype.requestSubmit = function requestSubmit(
    submitter?: HTMLElement | null
  ) {
    const method = this.getAttribute('method')?.toLowerCase();
    if (method === 'dialog') {
      const dialog = this.closest('dialog');
      if (dialog) {
        const val =
          submitter && 'value' in submitter
            ? String((submitter as HTMLButtonElement).value ?? '')
            : '';
        dialog.close(val);
        return;
      }
    }
    this.dispatchEvent(
      new Event('submit', { bubbles: true, cancelable: true })
    );
  };
}

// Default matchMedia mock for jsdom
if (typeof window !== 'undefined' && !window.matchMedia) {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: query.includes('dark'),
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }),
  });
}

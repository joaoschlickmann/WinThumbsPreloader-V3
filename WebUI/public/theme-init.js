(function () {
  try {
    var saved = localStorage.getItem('wtp-theme');
    var meta = document.querySelector('meta[name="color-scheme"]');
    if (saved === 'light' || saved === 'dark') {
      if (meta) {
        meta.setAttribute('content', saved);
      }
      document.documentElement.dataset.theme = saved;
      return;
    }
    if (meta) {
      meta.setAttribute('content', 'light dark');
    }
  } catch (_err) {
    // Storage access may be restricted; fall through to OS scheme resolution.
  }

  try {
    var prefersDark =
      window.matchMedia &&
      window.matchMedia('(prefers-color-scheme: dark)').matches;
    document.documentElement.dataset.theme = prefersDark ? 'dark' : 'light';
  } catch (_err) {
    document.documentElement.dataset.theme = 'dark';
  }
})();

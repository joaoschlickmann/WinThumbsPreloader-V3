import { beforeEach, describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { axe } from 'vitest-axe';
import { App } from '../../App';
import { resetMockState } from '../../lib/mockBridge';

function setThemeAttribute(mode: 'dark' | 'light') {
  document.documentElement.dataset.theme = mode;
  document
    .querySelector('meta[name="color-scheme"]')
    ?.setAttribute('content', mode);
}

describe('System Views (CodecsView, OptimizeView, SettingsView)', () => {
  beforeEach(() => {
    resetMockState();
    localStorage.clear();
    setThemeAttribute('dark');
  });

  it('CodecsView filters families, confirms external handler replacement, uninstalls all, and passes axe in dark & light themes', async () => {
    const user = userEvent.setup();
    const { container } = render(<App />);

    await user.click(screen.getByRole('button', { name: /^Codecs$/i }));
    expect(
      await screen.findByRole('heading', { name: /Thumbnail Codecs/i })
    ).toBeInTheDocument();

    // All 10 families rendered initially
    expect(
      screen.getByLabelText(/Adobe Photoshop \(PSD, PSB\)/i)
    ).toBeInTheDocument();
    expect(
      screen.getByLabelText(/Camera RAW \(DNG, CR2, CR3/i)
    ).toBeInTheDocument();

    // Filter to PDF -> check PDF (which has External handler) -> Apply Selection -> confirms replacement
    const filterInput = screen.getByLabelText(
      /Filter Codec Families or Extensions/i
    );
    await user.type(filterInput, 'pdf');
    const pdfCheckbox = screen.getByLabelText(/PDF Documents \(PDFium\)/i);
    await user.click(pdfCheckbox);
    await user.clear(filterInput);

    await user.click(screen.getByRole('button', { name: /^Apply Selection$/i }));
    const confirmDialog = await screen.findByRole('dialog');
    expect(
      within(confirmDialog).getByRole('heading', {
        name: /Replace existing external thumbnail handlers\?/i,
      })
    ).toBeInTheDocument();
    expect(
      within(confirmDialog).getByText(/Microsoft Edge \/ PDF Handler/i)
    ).toBeInTheDocument();

    await user.click(
      within(confirmDialog).getByRole('button', {
        name: /Back Up & Replace Handlers/i,
      })
    );
    await waitFor(() => {
      expect(screen.getByText('4/10 Active')).toBeInTheDocument();
    });

    // Uninstall All opens destructive ConfirmDialog with Cancel focused
    await user.click(screen.getByRole('button', { name: /^Uninstall All$/i }));
    const uninstallDialog = await screen.findByRole('dialog');
    const cancelBtn = within(uninstallDialog).getByRole('button', {
      name: /^Cancel$/i,
    });
    expect(document.activeElement).toBe(cancelBtn);

    await user.click(
      within(uninstallDialog).getByRole('button', { name: /^Uninstall All$/i })
    );
    await waitFor(() => {
      expect(screen.getByText('0/10 Active')).toBeInTheDocument();
    });

    expect(await axe(container)).toHaveNoViolations();
    setThemeAttribute('light');
    expect(await axe(container)).toHaveNoViolations();
  });

  it('OptimizeView covers all 3 sub-tabs, disclosures, Load Recommended, Apply, and passes axe in dark & light themes', async () => {
    const user = userEvent.setup();
    const { container } = render(<App />);

    await user.click(screen.getByRole('button', { name: /^Optimize$/i }));
    expect(
      await screen.findByRole('heading', { name: /Windows Optimizer/i })
    ).toBeInTheDocument();

    // Expand disclosure on first tweak
    const detailButtons = screen.getAllByRole('button', {
      name: /Details & Trade-offs/i,
    });
    await user.click(detailButtons[0]);
    expect(await screen.findByText(/What it does:/i)).toBeInTheDocument();
    expect(screen.getByText(/Pros:/i)).toBeInTheDocument();
    expect(screen.getByText(/Cons \/ Notes:/i)).toBeInTheDocument();

    expect(await axe(container)).toHaveNoViolations();

    // Load Recommended -> switch to Explorer Performance tab -> pick 16384 KB
    await user.click(
      screen.getAllByRole('button', { name: /Load Recommended/i })[0]
    );
    await user.click(
      screen.getByRole('tab', { name: /Explorer Performance/i })
    );
    const iconSelect = await screen.findByLabelText(/Icon Cache Size/i);
    expect(iconSelect).toBeEnabled();
    await user.selectOptions(iconSelect, '16384');

    expect(await axe(container)).toHaveNoViolations();

    // Switch to Shell & Maintenance tab -> toggle Context Menu -> Restart Explorer confirm -> Restore Defaults confirm
    await user.click(
      screen.getByRole('tab', { name: /Shell & Maintenance/i })
    );
    expect(
      await screen.findByText(/Registered \(Current User\)/i)
    ).toBeInTheDocument();
    await user.click(
      screen.getByRole('button', { name: /Remove Context Menu/i })
    );
    expect(await screen.findByText('Not Registered')).toBeInTheDocument();

    expect(await axe(container)).toHaveNoViolations();
    setThemeAttribute('light');
    expect(await axe(container)).toHaveNoViolations();

    // Apply optimizations via Elevate & Apply (UAC)
    await user.click(
      screen.getByRole('button', { name: /Elevate & Apply \(UAC\)/i })
    );
    await user.click(screen.getByRole('tab', { name: /Cache & Network/i }));
    expect(await screen.findAllByText('Optimized')).not.toHaveLength(0);
  });

  it('SettingsView exercises all 4 sub-tabs, warnings, confirmations, CLI table, and passes axe in dark & light themes', async () => {
    const user = userEvent.setup();
    const { container } = render(<App />);

    await user.click(screen.getByRole('button', { name: /^Settings$/i }));
    expect(
      await screen.findByRole('heading', { name: /^Settings$/i })
    ).toBeInTheDocument();

    // 1. General & Preloader tab
    const prioritySelect = await screen.findByLabelText(
      /Preloader Process Priority/i
    );
    await user.selectOptions(prioritySelect, 'Realtime');
    expect(
      await screen.findByText(/Realtime priority may reduce desktop/i)
    ).toBeInTheDocument();
    await user.click(
      screen.getByRole('button', { name: /Save General Settings/i })
    );

    expect(await axe(container)).toHaveNoViolations();
    setThemeAttribute('light');
    expect(await axe(container)).toHaveNoViolations();
    setThemeAttribute('dark');

    // 2. Extensions tab
    await user.click(screen.getByRole('tab', { name: /^Extensions$/i }));
    const scopeSelect = await screen.findByLabelText(/Folder Icon Scope/i);
    await user.selectOptions(scopeSelect, 'all');
    expect(await screen.findByText(/Performance Warning:/i)).toBeInTheDocument();

    const formatSelect = screen.getByLabelText(/Extensions Auto-Format/i);
    await user.selectOptions(formatSelect, 'Vertically');
    await user.click(screen.getByRole('button', { name: /Format Now/i }));
    await user.click(screen.getByRole('button', { name: /Save Extensions/i }));

    expect(await axe(container)).toHaveNoViolations();
    setThemeAttribute('light');
    expect(await axe(container)).toHaveNoViolations();
    setThemeAttribute('dark');

    // 3. Logging tab
    await user.click(screen.getByRole('tab', { name: /^Logging$/i }));
    const logFreqSelect = await screen.findByLabelText(/Logging Frequency/i);
    await user.selectOptions(logFreqSelect, '4');
    expect(
      await screen.findByText(/Debug Logging Enabled:/i)
    ).toBeInTheDocument();

    // Restricted folder triggers admin confirmation dialog
    const logsInput = screen.getByLabelText(/Logs Folder/i);
    await user.clear(logsInput);
    await user.type(logsInput, 'C:\\Program Files\\WinThumbsLogs');
    await user.click(screen.getByRole('button', { name: /Save Logging/i }));

    expect(
      await screen.findByRole('heading', {
        name: /Administrator permissions may be required for log folder/i,
      })
    ).toBeInTheDocument();
    await user.click(
      screen.getByRole('button', { name: /Use Folder Anyway/i })
    );

    expect(await axe(container)).toHaveNoViolations();
    setThemeAttribute('light');
    expect(await axe(container)).toHaveNoViolations();
    setThemeAttribute('dark');

    // 4. About & Updates tab
    await user.click(screen.getByRole('tab', { name: /About & Updates/i }));
    expect(
      await screen.findByRole('table', {
        name: /WinThumbsPreloader command-line flags/i,
      })
    ).toBeInTheDocument();
    expect(screen.getByText('-apply-optimizations')).toBeInTheDocument();
    expect(screen.getByText('-reopencodecs')).toBeInTheDocument();

    expect(await axe(container)).toHaveNoViolations();
    setThemeAttribute('light');
    expect(await axe(container)).toHaveNoViolations();
  });
});

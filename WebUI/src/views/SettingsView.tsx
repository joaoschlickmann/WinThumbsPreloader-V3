import { useCallback, useEffect, useState } from 'react';
import { bridge } from '../lib/bridge';
import type {
  SettingsStateDto,
  ThemeMode,
  UpdateStatusDto,
  ViewId,
} from '../lib/types';
import { GlassPanel } from '../components/ui/GlassPanel';
import { SegmentedControl } from '../components/ui/SegmentedControl';
import { AboutTab } from './settings/AboutTab';
import { ExtensionsTab } from './settings/ExtensionsTab';
import { GeneralTab } from './settings/GeneralTab';
import { LoggingTab } from './settings/LoggingTab';

export type SettingsTabId = 'general' | 'extensions' | 'logging' | 'about';

export interface SettingsViewProps {
  themeMode: ThemeMode;
  systemTheme: 'dark' | 'light';
  onThemeChange: (next: ThemeMode) => void;
  updateStatus: UpdateStatusDto | null;
  onUpdateStatusChanged: (next: UpdateStatusDto) => void;
  onOpenUpdateModal: () => void;
  onNavigate: (view: ViewId) => void;
  onNotify: (tone: 'info' | 'warning' | 'error', message: string) => void;
}

export function SettingsView({
  themeMode,
  systemTheme,
  onThemeChange,
  updateStatus,
  onUpdateStatusChanged,
  onOpenUpdateModal,
  onNavigate,
  onNotify,
}: SettingsViewProps) {
  const [activeTab, setActiveTab] = useState<SettingsTabId>('general');
  const [state, setState] = useState<SettingsStateDto | null>(null);

  const loadSettings = useCallback(async () => {
    try {
      const next = await bridge.settings.getState();
      setState(next);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to load settings.'
      );
    }
  }, [onNotify]);

  useEffect(() => {
    void loadSettings();
  }, [loadSettings]);

  return (
    <div className="ds-stack-md">
      <div className="ds-page-header">
        <div>
          <h1 id="main-heading" tabIndex={-1} className="ds-title">
            Settings
          </h1>
          <p className="ds-subtitle">
            Configure preloader defaults, tracked file extensions, diagnostic
            logging, and application updates.
          </p>
        </div>

        <SegmentedControl
          mode="tablist"
          idPrefix="settings-tabs"
          ariaLabel="Settings categories"
          value={activeTab}
          onChange={setActiveTab}
          options={[
            { value: 'general', label: 'General & Preloader' },
            { value: 'extensions', label: 'Extensions' },
            { value: 'logging', label: 'Logging' },
            { value: 'about', label: 'About & Updates' },
          ]}
        />
      </div>

      {!state ? (
        <GlassPanel title="Loading Settings">
          <p className="ds-hint" role="status">
            Loading WinThumbsPreloader configuration...
          </p>
        </GlassPanel>
      ) : (
        <div
          role="tabpanel"
          id={`settings-tabs-panel-${activeTab}`}
          aria-labelledby={`settings-tabs-tab-${activeTab}`}
        >
          {activeTab === 'general' && (
            <GeneralTab
              general={state.general}
              themeMode={themeMode}
              systemTheme={systemTheme}
              onThemeChange={onThemeChange}
              onStateUpdated={setState}
              onNotify={onNotify}
            />
          )}
          {activeTab === 'extensions' && (
            <ExtensionsTab
              extensions={state.extensions}
              onNavigate={onNavigate}
              onStateUpdated={setState}
              onNotify={onNotify}
            />
          )}
          {activeTab === 'logging' && (
            <LoggingTab
              logging={state.logging}
              onStateUpdated={setState}
              onNotify={onNotify}
            />
          )}
          {activeTab === 'about' && (
            <AboutTab
              about={state.about}
              updateStatus={updateStatus}
              onUpdateStatusChanged={onUpdateStatusChanged}
              onOpenUpdateModal={onOpenUpdateModal}
              onNotify={onNotify}
            />
          )}
        </div>
      )}
    </div>
  );
}

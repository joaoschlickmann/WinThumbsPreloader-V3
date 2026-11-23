import type { ReactNode } from 'react';
import { AlertCircle, AlertTriangle, CheckCircle2, Info } from 'lucide-react';

export type StatusTone = 'neutral' | 'active' | 'ok' | 'warning' | 'error';

export interface StatusPillProps {
  tone?: StatusTone;
  children: ReactNode;
  icon?: ReactNode;
  showDefaultIcon?: boolean;
  className?: string;
}

export function StatusPill({
  tone = 'neutral',
  children,
  icon,
  showDefaultIcon = true,
  className = '',
}: StatusPillProps) {
  const toneClass =
    tone === 'ok'
      ? 'ds-pill-ok'
      : tone === 'active'
        ? 'ds-pill-active'
        : tone === 'warning'
          ? 'ds-pill-warning'
          : tone === 'error'
            ? 'ds-pill-error'
            : '';

  const resolvedIcon =
    icon ??
    (showDefaultIcon
      ? tone === 'active' || tone === 'ok'
        ? <CheckCircle2 size={13} strokeWidth={2} aria-hidden="true" />
        : tone === 'warning'
          ? <AlertTriangle size={13} strokeWidth={2} aria-hidden="true" />
          : tone === 'error'
            ? <AlertCircle size={13} strokeWidth={2} aria-hidden="true" />
            : <Info size={13} strokeWidth={2} aria-hidden="true" />
      : null);

  return (
    <span className={['ds-pill', toneClass, className].filter(Boolean).join(' ')}>
      {resolvedIcon}
      <span>{children}</span>
    </span>
  );
}

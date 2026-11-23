import type { ReactNode } from 'react';

export interface GlassPanelProps {
  title?: string;
  subtitle?: string;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
}

export function GlassPanel({
  title,
  subtitle,
  actions,
  children,
  className = '',
}: GlassPanelProps) {
  return (
    <section className={['ds-glass ds-panel', className].filter(Boolean).join(' ')}>
      {(title || actions) && (
        <div className="ds-row-between">
          <div className="ds-stack-sm" style={{ minWidth: 0, flex: 1 }}>
            {title && <h2 className="ds-section-title">{title}</h2>}
            {subtitle && <p className="ds-hint">{subtitle}</p>}
          </div>
          {actions && <div className="ds-row">{actions}</div>}
        </div>
      )}
      {children}
    </section>
  );
}

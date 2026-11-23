import { AlertCircle, AlertTriangle, CheckCircle2, X } from 'lucide-react';
import { Button } from './Button';

export interface ToastItem {
  id: string;
  tone: 'info' | 'warning' | 'error';
  message: string;
}

export interface ToastRegionProps {
  toasts: ToastItem[];
  onDismiss: (id: string) => void;
}

export function ToastRegion({ toasts, onDismiss }: ToastRegionProps) {
  if (toasts.length === 0) {
    return null;
  }

  return (
    <div className="ds-toast-region" aria-label="Notifications">
      {toasts.map((toast) => {
        const toneClass =
          toast.tone === 'warning'
            ? 'ds-toast-warning'
            : toast.tone === 'error'
              ? 'ds-toast-error'
              : '';

        return (
          <div
            key={toast.id}
            className={['ds-toast', toneClass].filter(Boolean).join(' ')}
          >
            {toast.tone === 'error' ? (
              <AlertCircle size={16} strokeWidth={2} aria-hidden="true" style={{ flexShrink: 0, marginTop: 2 }} />
            ) : toast.tone === 'warning' ? (
              <AlertTriangle size={16} strokeWidth={2} aria-hidden="true" style={{ flexShrink: 0, marginTop: 2 }} />
            ) : (
              <CheckCircle2 size={16} strokeWidth={2} aria-hidden="true" style={{ flexShrink: 0, marginTop: 2 }} />
            )}
            <span style={{ flex: 1, minWidth: 0 }}>{toast.message}</span>
            <Button
              variant="ghost"
              size="icon"
              aria-label="Dismiss notification"
              onClick={() => onDismiss(toast.id)}
            >
              <X size={14} strokeWidth={2} aria-hidden="true" />
            </Button>
          </div>
        );
      })}
    </div>
  );
}

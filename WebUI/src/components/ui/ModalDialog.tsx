import { useEffect, useId, useRef, type MouseEvent, type ReactNode } from 'react';
import { AlertTriangle, X } from 'lucide-react';
import { Button } from './Button';

export interface ModalDialogProps {
  open: boolean;
  title: string;
  description?: string;
  onClose: (returnValue?: string) => void;
  lightDismiss?: boolean;
  allowClose?: boolean;
  icon?: ReactNode;
  children?: ReactNode;
  actions?: ReactNode;
}

function isPointOutsideRect(
  clientX: number,
  clientY: number,
  rect: DOMRect
): boolean {
  return (
    clientX < rect.left ||
    clientX > rect.right ||
    clientY < rect.top ||
    clientY > rect.bottom
  );
}

export function ModalDialog({
  open,
  title,
  description,
  onClose,
  lightDismiss = true,
  allowClose = true,
  icon,
  children,
  actions,
}: ModalDialogProps) {
  const dialogRef = useRef<HTMLDialogElement | null>(null);
  const programmaticCloseRef = useRef(false);
  const titleId = useId();
  const descId = useId();

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    if (open) {
      programmaticCloseRef.current = false;
      if (!dialog.open) {
        dialog.showModal();
        const target =
          dialog.querySelector<HTMLElement>('[data-dialog-autofocus="true"]') ??
          dialog.querySelector<HTMLElement>('[autofocus]');
        target?.focus();
      }
    } else if (dialog.open) {
      programmaticCloseRef.current = true;
      dialog.close('');
    }
  }, [open]);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    const handleClose = () => {
      const value = dialog.returnValue;
      dialog.returnValue = '';
      if (programmaticCloseRef.current) {
        programmaticCloseRef.current = false;
        return;
      }
      onClose(value);
    };

    const handleCancel = (event: Event) => {
      if (!allowClose) {
        event.preventDefault();
      }
    };

    dialog.addEventListener('close', handleClose);
    dialog.addEventListener('cancel', handleCancel);
    return () => {
      dialog.removeEventListener('close', handleClose);
      dialog.removeEventListener('cancel', handleCancel);
    };
  }, [onClose, allowClose]);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog || !lightDismiss) return;
    if ('closedBy' in HTMLDialogElement.prototype) return;

    let pressedOutside = false;
    const onPointerDown = (event: PointerEvent) => {
      if (!dialog.open) return;
      pressedOutside = isPointOutsideRect(
        event.clientX,
        event.clientY,
        dialog.getBoundingClientRect()
      );
    };

    const onPointerUp = (event: PointerEvent) => {
      if (!dialog.open) return;
      if (
        pressedOutside &&
        isPointOutsideRect(
          event.clientX,
          event.clientY,
          dialog.getBoundingClientRect()
        )
      ) {
        dialog.close('');
      }
      pressedOutside = false;
    };

    dialog.addEventListener('pointerdown', onPointerDown);
    dialog.addEventListener('pointerup', onPointerUp);
    return () => {
      dialog.removeEventListener('pointerdown', onPointerDown);
      dialog.removeEventListener('pointerup', onPointerUp);
    };
  }, [lightDismiss]);

  const closedByProps = {
    closedby: lightDismiss ? 'any' : 'closerequest',
  } as Record<string, string>;

  return (
    <dialog
      ref={dialogRef}
      className="ds-dialog"
      aria-labelledby={titleId}
      aria-describedby={description ? descId : undefined}
      {...closedByProps}
    >
      <div className="ds-dialog-inner">
        <div className="ds-dialog-header">
          <div className="ds-dialog-title-row">
            {icon}
            <h2 id={titleId} className="ds-dialog-title">
              {title}
            </h2>
          </div>
          <Button
            variant="ghost"
            size="icon"
            aria-label="Close dialog"
            disabled={!allowClose}
            onClick={() => {
              if (allowClose) {
                dialogRef.current?.close('');
              }
            }}
          >
            <X size={18} strokeWidth={2} aria-hidden="true" />
          </Button>
        </div>

        {description && (
          <p id={descId} className="ds-dialog-body" style={{ margin: 0 }}>
            {description}
          </p>
        )}

        {children}

        {actions && <div className="ds-dialog-actions">{actions}</div>}
      </div>
    </dialog>
  );
}

export interface ConfirmDialogProps {
  open: boolean;
  title: string;
  description: string;
  confirmLabel: string;
  cancelLabel?: string;
  destructive?: boolean;
  loading?: boolean;
  children?: ReactNode;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel,
  cancelLabel = 'Cancel',
  destructive = false,
  loading = false,
  children,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const handleChoice = (
    event: MouseEvent<HTMLButtonElement>,
    choice: 'cancel' | 'confirm'
  ) => {
    const dialog = event.currentTarget.closest('dialog');
    if (dialog && typeof dialog.close === 'function') {
      dialog.close(choice);
      return;
    }
    if (choice === 'confirm') {
      onConfirm();
    } else {
      onCancel();
    }
  };

  return (
    <ModalDialog
      open={open}
      title={title}
      description={description}
      onClose={(val) => {
        if (val === 'confirm') {
          onConfirm();
        } else {
          onCancel();
        }
      }}
      icon={
        destructive ? (
          <AlertTriangle
            size={20}
            strokeWidth={2}
            aria-hidden="true"
            style={{ color: 'var(--ds-error)', flexShrink: 0 }}
          />
        ) : undefined
      }
    >
      {children}
      <div className="ds-dialog-actions">
        <Button
          type="button"
          variant="secondary"
          disabled={loading}
          autoFocus={destructive}
          data-dialog-autofocus={destructive ? 'true' : undefined}
          onClick={(e) => handleChoice(e, 'cancel')}
        >
          {cancelLabel}
        </Button>
        <Button
          type="button"
          variant={destructive ? 'danger' : 'primary'}
          loading={loading}
          autoFocus={!destructive}
          data-dialog-autofocus={!destructive ? 'true' : undefined}
          onClick={(e) => handleChoice(e, 'confirm')}
        >
          {confirmLabel}
        </Button>
      </div>
    </ModalDialog>
  );
}

import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Loader2 } from 'lucide-react';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'danger' | 'ghost';
  size?: 'default' | 'sm' | 'icon';
  loading?: boolean;
  loadingLabel?: string;
  children?: ReactNode;
}

export function Button({
  variant = 'secondary',
  size = 'default',
  loading = false,
  loadingLabel,
  disabled = false,
  type = 'button',
  className = '',
  children,
  ...rest
}: ButtonProps) {
  const variantClass =
    variant === 'primary'
      ? 'ds-button-primary'
      : variant === 'danger'
        ? 'ds-button-danger'
        : variant === 'ghost'
          ? 'ds-button-ghost'
          : '';

  const sizeClass =
    size === 'sm'
      ? 'ds-button-sm'
      : size === 'icon'
        ? 'ds-button-icon'
        : '';

  const classes = ['ds-button', variantClass, sizeClass, className]
    .filter(Boolean)
    .join(' ');

  return (
    <button
      type={type}
      className={classes}
      disabled={disabled || loading}
      {...rest}
    >
      {loading && (
        <Loader2
          size={14}
          strokeWidth={2}
          aria-hidden="true"
          className="ds-spinner"
        />
      )}
      {loading && loadingLabel ? loadingLabel : children}
    </button>
  );
}

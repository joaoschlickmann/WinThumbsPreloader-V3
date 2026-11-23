import { useId } from 'react';

export interface SwitchProps {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  hint?: string;
  disabled?: boolean;
  id?: string;
  showStateText?: boolean;
}

export function Switch({
  label,
  checked,
  onChange,
  hint,
  disabled = false,
  id,
  showStateText = false,
}: SwitchProps) {
  const autoId = useId();
  const inputId = id ?? autoId;
  const hintId = `${inputId}-hint`;

  return (
    <div className="ds-switch-row">
      <div className="ds-switch-meta">
        <label htmlFor={inputId} className="ds-switch-label">
          {label}
        </label>
        {hint && (
          <p id={hintId} className="ds-hint">
            {hint}
          </p>
        )}
      </div>
      <div className="ds-switch-control">
        {showStateText && (
          <span className="ds-switch-state" aria-hidden="true">
            {checked ? 'On' : 'Off'}
          </span>
        )}
        <input
          id={inputId}
          type="checkbox"
          role="switch"
          className="ds-switch-input"
          checked={checked}
          disabled={disabled}
          aria-describedby={hint ? hintId : undefined}
          onChange={(event) => onChange(event.target.checked)}
        />
      </div>
    </div>
  );
}

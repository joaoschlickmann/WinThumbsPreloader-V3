import { useEffect, useState, type InputHTMLAttributes } from 'react';

export interface DraftNumberInputProps
  extends Omit<
    InputHTMLAttributes<HTMLInputElement>,
    'type' | 'value' | 'onChange' | 'min' | 'max'
  > {
  value: number;
  min: number;
  max: number;
  fallback?: number;
  onCommit: (nextValue: number) => void;
}

export function DraftNumberInput({
  value,
  min,
  max,
  fallback,
  onCommit,
  onBlur,
  ...rest
}: DraftNumberInputProps) {
  const [draft, setDraft] = useState<string>(String(value));
  const [focused, setFocused] = useState(false);

  useEffect(() => {
    if (!focused) {
      setDraft(String(value));
    }
  }, [value, focused]);

  const clampValue = (raw: string): number => {
    const trimmed = raw.trim();
    if (trimmed === '') {
      return fallback ?? min;
    }
    const parsed = Math.trunc(Number(trimmed));
    if (!Number.isFinite(parsed)) {
      return fallback ?? value;
    }
    return Math.max(min, Math.min(max, parsed));
  };

  return (
    <input
      {...rest}
      type="number"
      min={min}
      max={max}
      value={draft}
      onFocus={(e) => {
        setFocused(true);
        rest.onFocus?.(e);
      }}
      onChange={(e) => {
        const raw = e.target.value;
        setDraft(raw);
        const trimmed = raw.trim();
        if (trimmed !== '') {
          const parsed = Math.trunc(Number(trimmed));
          if (Number.isFinite(parsed) && parsed >= min && parsed <= max) {
            onCommit(parsed);
          }
        }
      }}
      onBlur={(e) => {
        setFocused(false);
        const committed = clampValue(draft);
        setDraft(String(committed));
        onCommit(committed);
        onBlur?.(e);
      }}
    />
  );
}

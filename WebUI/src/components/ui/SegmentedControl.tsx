import { useId, useRef, type KeyboardEvent, type ReactNode } from 'react';

export interface SegmentedOption<T extends string> {
  value: T;
  label: string;
  icon?: ReactNode;
  disabled?: boolean;
}

export interface SegmentedControlProps<T extends string> {
  options: ReadonlyArray<SegmentedOption<T>>;
  value: T;
  onChange: (value: T) => void;
  ariaLabel: string;
  mode?: 'tablist' | 'group';
  idPrefix?: string;
  className?: string;
}

export function SegmentedControl<T extends string>({
  options,
  value,
  onChange,
  ariaLabel,
  mode = 'group',
  idPrefix,
  className = '',
}: SegmentedControlProps<T>) {
  const fallbackPrefix = useId();
  const resolvedPrefix = idPrefix ?? fallbackPrefix;
  const buttonRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const isTablist = mode === 'tablist';

  const moveFocusAndSelect = (targetIndex: number) => {
    const enabledIndices = options
      .map((opt, idx) => (!opt.disabled ? idx : -1))
      .filter((idx) => idx >= 0);
    if (enabledIndices.length === 0) return;

    const clamped = enabledIndices.includes(targetIndex)
      ? targetIndex
      : enabledIndices[0];
    const targetOpt = options[clamped];
    if (!targetOpt) return;
    onChange(targetOpt.value);
    buttonRefs.current[clamped]?.focus();
  };

  const handleKeyDown = (
    event: KeyboardEvent<HTMLButtonElement>,
    index: number
  ) => {
    if (!isTablist) return;

    const enabledIndices = options
      .map((opt, idx) => (!opt.disabled ? idx : -1))
      .filter((idx) => idx >= 0);
    if (enabledIndices.length === 0) return;

    const currentPos = enabledIndices.indexOf(index);
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
      event.preventDefault();
      const next = enabledIndices[(currentPos + 1) % enabledIndices.length];
      moveFocusAndSelect(next);
    } else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
      event.preventDefault();
      const prev =
        enabledIndices[
          (currentPos - 1 + enabledIndices.length) % enabledIndices.length
        ];
      moveFocusAndSelect(prev);
    } else if (event.key === 'Home') {
      event.preventDefault();
      moveFocusAndSelect(enabledIndices[0]);
    } else if (event.key === 'End') {
      event.preventDefault();
      moveFocusAndSelect(enabledIndices[enabledIndices.length - 1]);
    }
  };

  return (
    <div
      role={isTablist ? 'tablist' : 'group'}
      aria-label={ariaLabel}
      className={['ds-segmented', className].filter(Boolean).join(' ')}
    >
      {options.map((opt, index) => {
        const selected = opt.value === value;
        return (
          <button
            key={opt.value}
            ref={(el) => {
              buttonRefs.current[index] = el;
            }}
            type="button"
            id={isTablist ? `${resolvedPrefix}-tab-${opt.value}` : undefined}
            role={isTablist ? 'tab' : undefined}
            aria-selected={isTablist ? selected : undefined}
            aria-controls={isTablist ? `${resolvedPrefix}-panel-${opt.value}` : undefined}
            aria-pressed={!isTablist ? selected : undefined}
            tabIndex={isTablist ? (selected ? 0 : -1) : undefined}
            disabled={opt.disabled}
            className="ds-segmented-btn"
            onClick={() => onChange(opt.value)}
            onKeyDown={(e) => handleKeyDown(e, index)}
          >
            {opt.icon}
            <span>{opt.label}</span>
          </button>
        );
      })}
    </div>
  );
}

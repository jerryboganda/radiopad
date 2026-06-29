'use client';

import { useEffect, useRef, useState } from 'react';

export interface AnimatedNumberProps {
  /** Target value to animate toward. */
  value: number;
  /** Count-up duration in ms (default 600). */
  duration?: number;
  /** Fixed decimal places when no custom `format` is given. */
  decimals?: number;
  /** Custom formatter (e.g. percent, currency, thousands separators). */
  format?: (n: number) => string;
  className?: string;
}

/** ease-out cubic */
function easeOut(t: number): number {
  return 1 - Math.pow(1 - t, 3);
}

/**
 * Animates a number from its previous value to the new value with a count-up
 * effect. Respects `prefers-reduced-motion` (jumps straight to the value) and
 * cleans up its rAF loop on unmount / value change.
 */
export default function AnimatedNumber({
  value,
  duration = 600,
  decimals = 0,
  format,
  className,
}: AnimatedNumberProps) {
  const [display, setDisplay] = useState(value);
  const fromRef = useRef(value);
  const rafRef = useRef<number | null>(null);

  useEffect(() => {
    const reduce =
      typeof window !== 'undefined' &&
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    const from = fromRef.current;
    const to = value;

    if (reduce || from === to || duration <= 0 || typeof performance === 'undefined') {
      setDisplay(to);
      fromRef.current = to;
      return;
    }

    const start = performance.now();
    const tick = (now: number) => {
      const t = Math.min(1, (now - start) / duration);
      setDisplay(from + (to - from) * easeOut(t));
      if (t < 1) {
        rafRef.current = requestAnimationFrame(tick);
      } else {
        fromRef.current = to;
      }
    };
    rafRef.current = requestAnimationFrame(tick);

    return () => {
      if (rafRef.current != null) cancelAnimationFrame(rafRef.current);
    };
  }, [value, duration]);

  const text = format ? format(display) : display.toFixed(decimals);
  return <span className={className}>{text}</span>;
}

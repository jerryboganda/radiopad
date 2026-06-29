'use client';

import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';

export type RevealAnimation = 'fade-in' | 'fade-in-up' | 'scale-in' | 'pop-in';

export interface RevealProps {
  children: ReactNode;
  /** Entrance animation to play when the element scrolls into view. */
  animation?: RevealAnimation;
  /** Delay (ms) before the entrance animation starts — useful for cascades. */
  delay?: number;
  /** Animate only the first time it enters the viewport (default). */
  once?: boolean;
  className?: string;
  style?: CSSProperties;
}

const ANIM_CLASS: Record<RevealAnimation, string> = {
  'fade-in': 'rp-anim-fade-in',
  'fade-in-up': 'rp-anim-fade-in-up',
  'scale-in': 'rp-anim-scale-in',
  'pop-in': 'rp-anim-pop-in',
};

/**
 * Reveals its children with a token-driven entrance animation when scrolled
 * into view, via IntersectionObserver. Falls back to immediately visible when
 * IO is unavailable; reduced-motion users see content instantly (motion.css).
 */
export default function Reveal({
  children,
  animation = 'fade-in-up',
  delay = 0,
  once = true,
  className,
  style,
}: RevealProps) {
  const ref = useRef<HTMLDivElement | null>(null);
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    if (typeof IntersectionObserver === 'undefined') {
      setVisible(true);
      return;
    }
    const io = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            setVisible(true);
            if (once) io.disconnect();
          } else if (!once) {
            setVisible(false);
          }
        }
      },
      { threshold: 0.12, rootMargin: '0px 0px -8% 0px' },
    );
    io.observe(el);
    return () => io.disconnect();
  }, [once]);

  const cls = [
    'rp-reveal',
    visible ? 'is-visible' : '',
    visible ? ANIM_CLASS[animation] : '',
    className,
  ]
    .filter(Boolean)
    .join(' ');

  return (
    <div
      ref={ref}
      className={cls}
      style={delay && visible ? { animationDelay: `${delay}ms`, ...style } : style}
    >
      {children}
    </div>
  );
}

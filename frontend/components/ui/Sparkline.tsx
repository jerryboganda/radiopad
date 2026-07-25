export interface SparklineProps {
  /** Daily values, oldest first. Rendered as a single trend line — no axes/labels. */
  data: number[];
  width?: number;
  height?: number;
  /** Any CSS color value or var(); defaults to the current text color so it inherits stat-card icon tinting via `color`. */
  stroke?: string;
  className?: string;
}

/** Minimal hand-rolled trend line — no charting library. Flat/empty data renders a flat mid-line rather than nothing, so the sparkline slot never looks broken. */
export default function Sparkline({ data, width = 64, height = 24, stroke = 'currentColor', className }: SparklineProps) {
  const values = data.length > 0 ? data : [0, 0];
  const min = Math.min(...values);
  const max = Math.max(...values);
  const range = max - min || 1;
  const stepX = width / Math.max(1, values.length - 1);
  const points = values
    .map((v, i) => {
      const x = i * stepX;
      const y = height - ((v - min) / range) * (height - 2) - 1;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(' ');

  return (
    <svg
      className={className}
      width={width}
      height={height}
      viewBox={`0 0 ${width} ${height}`}
      role="img"
      aria-label={`Trend over the last ${values.length} days: ${values.join(', ')}`}
    >
      <polyline points={points} fill="none" stroke={stroke} strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

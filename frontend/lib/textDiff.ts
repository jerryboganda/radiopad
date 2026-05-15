/**
 * PRD §16.4 — Simple line-by-line text diff utility.
 * Used by the Prompt Studio diff viewer and the prior-compare panel.
 * No external dependencies — pure function over split lines.
 */

export type DiffLine = {
  type: 'same' | 'added' | 'removed';
  text: string;
};

/**
 * Compute a line-by-line diff between `a` (old) and `b` (new).
 *
 * Uses a classic LCS (longest common subsequence) approach to produce
 * a minimal edit script that marks each line as same / added / removed.
 */
export function computeDiff(a: string, b: string): DiffLine[] {
  const linesA = a.split(/\r?\n/);
  const linesB = b.split(/\r?\n/);

  const n = linesA.length;
  const m = linesB.length;

  // Build LCS table
  const dp: number[][] = Array.from({ length: n + 1 }, () =>
    Array.from({ length: m + 1 }, () => 0),
  );
  for (let i = 1; i <= n; i++) {
    for (let j = 1; j <= m; j++) {
      if (linesA[i - 1] === linesB[j - 1]) {
        dp[i][j] = dp[i - 1][j - 1] + 1;
      } else {
        dp[i][j] = Math.max(dp[i - 1][j], dp[i][j - 1]);
      }
    }
  }

  // Backtrack to produce diff
  const result: DiffLine[] = [];
  let i = n;
  let j = m;
  while (i > 0 || j > 0) {
    if (i > 0 && j > 0 && linesA[i - 1] === linesB[j - 1]) {
      result.push({ type: 'same', text: linesA[i - 1] });
      i--;
      j--;
    } else if (j > 0 && (i === 0 || dp[i][j - 1] >= dp[i - 1][j])) {
      result.push({ type: 'added', text: linesB[j - 1] });
      j--;
    } else {
      result.push({ type: 'removed', text: linesA[i - 1] });
      i--;
    }
  }

  result.reverse();
  return result;
}

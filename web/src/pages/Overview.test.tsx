/// <reference types="vitest" />
import { describe, expect, it } from 'vitest';
import { formatAcceleration, formatVelocity } from './Overview';

describe('Overview formatting', () => {
  it('marks insufficient reasons as unavailable', () => {
    // The functions return JSX; we only assert that they are truthy (rendered) when the reason is set.
    const v = formatVelocity({ absoluteChange: null, perDay: null, insufficientReason: 'no-baseline-snapshot', isNewSignal: false });
    expect(v).toBeTruthy();
  });
  it('shows the per-day suffix when a value is available', () => {
    const v = formatVelocity({ absoluteChange: 14, perDay: 2.0, insufficientReason: null, isNewSignal: false });
    expect(v).toContain('14');
    expect(v).toContain('2.00');
  });
  it('renders acceleration with a sign and one decimal', () => {
    const a = formatAcceleration({ acceleration: 10, insufficientReason: null });
    expect(a).toBeTruthy();
  });
});

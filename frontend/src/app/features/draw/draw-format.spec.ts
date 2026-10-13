import { categoryDrawFormats, effectiveDrawFormat, GLOBAL_DRAW_FORMATS } from './draw-format';

describe('draw-format', () => {
  it('never offers Best of Three globally', () => {
    expect(GLOBAL_DRAW_FORMATS).not.toContain('BestOfThree');
  });

  it('offers Best of Three only for exactly two athletes', () => {
    expect(categoryDrawFormats(2)).toContain('BestOfThree');
    expect(categoryDrawFormats(1)).not.toContain('BestOfThree');
    expect(categoryDrawFormats(3)).not.toContain('BestOfThree');
  });

  it('preselects Best of Three for an undrawn two-athlete category', () => {
    expect(effectiveDrawFormat({ explicit: undefined, stored: null, athleteCount: 2, globalFormat: 'SingleElimination' }))
      .toBe('BestOfThree');
    expect(effectiveDrawFormat({ explicit: undefined, stored: null, athleteCount: 3, globalFormat: 'RoundRobin' }))
      .toBe('RoundRobin');
  });

  it('lets an explicit (e.g. global) choice override the preselection', () => {
    expect(effectiveDrawFormat({ explicit: 'RoundRobin', stored: null, athleteCount: 2, globalFormat: 'RoundRobin' }))
      .toBe('RoundRobin');
  });

  it('keeps the stored format of a drawn category', () => {
    expect(effectiveDrawFormat({ explicit: undefined, stored: 'SingleElimination', athleteCount: 2, globalFormat: 'RoundRobin' }))
      .toBe('SingleElimination');
  });

  it('falls back to the global format when Best of Three no longer fits', () => {
    expect(effectiveDrawFormat({ explicit: 'BestOfThree', stored: null, athleteCount: 3, globalFormat: 'RoundRobin' }))
      .toBe('RoundRobin');
  });

  it('keeps a stored Best-of-Three draw even when the athlete count changed', () => {
    expect(effectiveDrawFormat({ explicit: undefined, stored: 'BestOfThree', athleteCount: 3, globalFormat: 'RoundRobin' }))
      .toBe('BestOfThree');
    expect(categoryDrawFormats(3, 'BestOfThree')).toContain('BestOfThree');
  });
});

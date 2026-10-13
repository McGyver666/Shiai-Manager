import { BracketFormat } from '../../core/models';

/** Formats offered in the global format selection; Best of Three depends on a single category's size. */
export const GLOBAL_DRAW_FORMATS: readonly BracketFormat[] = [
  'SingleElimination',
  'DoubleElimination',
  'SingleEliminationWithRepechage',
  'RoundRobin',
  'RoundRobinWithKnockout',
];

/** Translation key of each draw format label. */
export const DRAW_FORMAT_LABEL_KEYS: Readonly<Record<BracketFormat, string>> = {
  SingleElimination: 'draw.format.single',
  DoubleElimination: 'draw.format.doubleElimination',
  SingleEliminationWithRepechage: 'draw.format.repechage',
  RoundRobin: 'draw.format.roundRobin',
  RoundRobinWithKnockout: 'draw.format.roundRobinWithKnockout',
  BestOfThree: 'draw.format.bestOfThree',
};

/** Best of Three is only available for a category with exactly two athletes. */
export function isBestOfThreeAvailable(athleteCount: number): boolean {
  return athleteCount === 2;
}

/** Formats selectable for a single category; a stored Best-of-Three draw stays selectable as shown. */
export function categoryDrawFormats(athleteCount: number, current?: BracketFormat): BracketFormat[] {
  return isBestOfThreeAvailable(athleteCount) || current === 'BestOfThree'
    ? [...GLOBAL_DRAW_FORMATS, 'BestOfThree']
    : [...GLOBAL_DRAW_FORMATS];
}

/**
 * Format shown and used for a category: an explicit choice (per category or applied globally) wins,
 * then the stored draw format, then Best of Three for two athletes, otherwise the global format.
 * An explicit Best-of-Three choice that no longer fits the athlete count is ignored; a stored format is
 * always kept.
 */
export function effectiveDrawFormat(options: {
  explicit: BracketFormat | undefined;
  stored: BracketFormat | null;
  athleteCount: number;
  globalFormat: BracketFormat;
}): BracketFormat {
  const bestOfThreeFits = isBestOfThreeAvailable(options.athleteCount);
  if (options.explicit && (options.explicit !== 'BestOfThree' || bestOfThreeFits)) {
    return options.explicit;
  }

  return options.stored ?? (bestOfThreeFits ? 'BestOfThree' : options.globalFormat);
}

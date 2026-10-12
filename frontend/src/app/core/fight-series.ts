import { AccentSideColor, FightSeries, FightSide } from './models';
import { RunningScoreSide } from '../shared/running-score/running-score.component';

/** Maximum number of bouts in a Best-of-Three Kampfserie. */
export const SERIES_MAX_BOUTS = 3;

/** Translation key with interpolation parameters. */
export interface TranslatedText {
  key: string;
  params: Record<string, number>;
}

/**
 * Serienstand of a fight in the side order of a view (`leftSide` first);
 * each value keeps the color of its side.
 */
export function seriesScoreSides(
  series: FightSeries,
  leftSide: FightSide,
  accent: AccentSideColor,
  translate: (key: string) => string,
): RunningScoreSide[] {
  const white: RunningScoreSide = { value: series.whiteWins, tone: 'white', label: translate('match.whiteSide') };
  const other: RunningScoreSide = {
    value: series.blueWins,
    tone: accent === 'Red' ? 'red' : 'blue',
    label: translate(accent === 'Red' ? 'match.redSide' : 'match.blueSide'),
  };
  return leftSide === 'white' ? [white, other] : [other, white];
}

/**
 * Position of a fight in its series: "Kampf 2 von 3" (<c>ofTotal</c>) or "Kampf 2 der Serie"
 * (<c>inSeries</c>); the third bout is always the "Entscheidungskampf".
 */
export function seriesPositionText(series: FightSeries, style: 'ofTotal' | 'inSeries'): TranslatedText {
  if (series.isDecidingFight) {
    return { key: 'series.decidingFight', params: {} };
  }

  return style === 'ofTotal'
    ? { key: 'series.boutOfTotal', params: { n: series.boutNumber, total: SERIES_MAX_BOUTS } }
    : { key: 'series.boutInSeries', params: { n: series.boutNumber } };
}

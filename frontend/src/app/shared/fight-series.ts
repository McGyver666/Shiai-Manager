import { AccentSideColor, BracketFormat, Fight, FightSeries, FightSide } from '../core/models';
import { RunningScoreSide } from './running-score/running-score.component';

/** Maximum number of bouts in a Best-of-Three Kampfserie. */
export const SERIES_MAX_BOUTS = 3;

/** Translation key with interpolation parameters. */
export interface TranslatedText {
  key: string;
  params: Record<string, number>;
}

type Translate = (key: string, params?: Record<string, string | number>) => string;

/** True when a category drawn with <c>format</c> is a Kampfserie. */
export function isSeriesFormat(format: BracketFormat | null | undefined): boolean {
  return format === 'BestOfThree';
}

/**
 * Serienstand of a fight in the side order of a view (`leftSide` first); each value keeps the color
 * of its side. Null for fights outside a Kampfserie.
 */
export function seriesScoreSides(
  series: FightSeries | null | undefined,
  leftSide: FightSide,
  accent: AccentSideColor,
  translate: Translate,
): RunningScoreSide[] | null {
  if (!series) {
    return null;
  }

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

/** Heading of a bout in a series listing; "Kampf n" until the series data is loaded. */
export function seriesBoutHeading(fight: Pick<Fight, 'series' | 'fightNumber'>): TranslatedText {
  return fight.series
    ? seriesPositionText(fight.series, 'inSeries')
    : { key: 'draw.fight', params: { n: fight.fightNumber } };
}

/** Translated {@link seriesPositionText}; null for fights outside a Kampfserie. */
export function seriesPositionLabel(
  series: FightSeries | null | undefined,
  style: 'ofTotal' | 'inSeries',
  translate: Translate,
): string | null {
  if (!series) {
    return null;
  }

  const text = seriesPositionText(series, style);
  return translate(text.key, text.params);
}

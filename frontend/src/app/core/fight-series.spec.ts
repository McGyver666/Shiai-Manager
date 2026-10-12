import { FightSeries } from './models';
import { seriesPositionText, seriesScoreSides } from './fight-series';

describe('fight-series', () => {
  const translate = (key: string) => key;
  const whiteLeads: FightSeries = { boutNumber: 2, isDecidingFight: false, whiteWins: 1, blueWins: 0 };

  it('orders the Serienstand white-first for views with white on the left', () => {
    const sides = seriesScoreSides(whiteLeads, 'white', 'Blue', translate);

    expect(sides.map((side) => side.value)).toEqual([1, 0]);
    expect(sides.map((side) => side.tone)).toEqual(['white', 'blue']);
    expect(sides.map((side) => side.label)).toEqual(['match.whiteSide', 'match.blueSide']);
  });

  it('orders the Serienstand accent-first for views with blue on the left', () => {
    const sides = seriesScoreSides(whiteLeads, 'blue', 'Blue', translate);

    expect(sides.map((side) => side.value)).toEqual([0, 1]);
    expect(sides.map((side) => side.tone)).toEqual(['blue', 'white']);
  });

  it('uses the red tone and label when the tournament accent is red', () => {
    const accent = seriesScoreSides(whiteLeads, 'white', 'Red', translate)[1];
    expect(accent.tone).toBe('red');
    expect(accent.label).toBe('match.redSide');
  });

  it('describes a regular bout and the deciding fight', () => {
    expect(seriesPositionText(whiteLeads, 'ofTotal')).toEqual({ key: 'series.boutOfTotal', params: { n: 2, total: 3 } });
    expect(seriesPositionText(whiteLeads, 'inSeries')).toEqual({ key: 'series.boutInSeries', params: { n: 2 } });

    const decider: FightSeries = { boutNumber: 3, isDecidingFight: true, whiteWins: 1, blueWins: 1 };
    expect(seriesPositionText(decider, 'ofTotal')).toEqual({ key: 'series.decidingFight', params: {} });
  });
});

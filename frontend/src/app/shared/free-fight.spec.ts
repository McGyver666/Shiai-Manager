import { Category, Fight } from '../core/models';
import { fightClockSettings, fightSideName, hasBothSides, isWinningSide } from './free-fight';

describe('free-fight', () => {
  const athleteName = (id: string | null) => (id ? `Athlet ${id}` : '?');
  const category = { matchDurationSeconds: 180, goldenScoreDurationSeconds: 60 } as Category;
  const regular = { whiteAthleteId: 'a1', blueAthleteId: 'a2', freeFight: null } as Fight;
  const freeFight = {
    whiteAthleteId: 'a1',
    blueAthleteId: null,
    freeFight: {
      whiteName: null,
      blueName: 'Gast 2',
      matchDurationSeconds: 120,
      goldenScoreEnabled: true,
      goldenScoreDurationSeconds: 90,
      winnerSide: null,
    },
  } as Fight;

  it('names a free-text side of a Freikampf by its name and an athlete side by the athlete', () => {
    expect(fightSideName(freeFight, 'white', athleteName)).toBe('Athlet a1');
    expect(fightSideName(freeFight, 'blue', athleteName)).toBe('Gast 2');
    expect(fightSideName(regular, 'blue', athleteName)).toBe('Athlet a2');
  });

  it('takes the clock settings of a Freikampf from the fight and of a regular fight from its category', () => {
    expect(fightClockSettings(freeFight, category)).toEqual({ matchDurationSeconds: 120, goldenScoreDurationSeconds: 90 });
    expect(fightClockSettings(regular, category)).toEqual({ matchDurationSeconds: 180, goldenScoreDurationSeconds: 60 });
    expect(fightClockSettings(regular, undefined)).toEqual({ matchDurationSeconds: 300, goldenScoreDurationSeconds: 180 });
  });

  it('treats a free-text side as known', () => {
    expect(hasBothSides(freeFight)).toBe(true);
    expect(hasBothSides(regular)).toBe(true);
    expect(hasBothSides({ ...regular, blueAthleteId: null })).toBe(false);
  });

  it('never marks an undecided side as winner, even when both ids are empty', () => {
    const pendingFreeText = { ...freeFight, whiteAthleteId: null, winnerId: null } as Fight;
    expect(isWinningSide(pendingFreeText, 'white')).toBe(false);

    const wonByBlue = { ...freeFight, freeFight: { ...freeFight.freeFight!, winnerSide: 'Blue' } } as Fight;
    expect(isWinningSide(wonByBlue, 'blue')).toBe(true);
    expect(isWinningSide(wonByBlue, 'white')).toBe(false);

    expect(isWinningSide({ ...regular, winnerId: 'a2' }, 'blue')).toBe(true);
    expect(isWinningSide({ ...regular, winnerId: null, whiteAthleteId: null }, 'white')).toBe(false);
  });
});

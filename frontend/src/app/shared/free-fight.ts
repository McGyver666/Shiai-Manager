import { Category, Fight, FightSide } from '../core/models';

/** Fight time and golden score duration used by the fight clocks. */
export interface FightClockSettings {
  matchDurationSeconds: number;
  goldenScoreDurationSeconds: number;
}

/** A Freikampf carries its own clock settings; a regular fight uses its category's (ADR-0013). */
export function fightClockSettings(fight: Fight, category: Category | null | undefined): FightClockSettings {
  if (fight.freeFight) {
    return {
      matchDurationSeconds: fight.freeFight.matchDurationSeconds,
      goldenScoreDurationSeconds: fight.freeFight.goldenScoreDurationSeconds,
    };
  }

  return {
    matchDurationSeconds: category?.matchDurationSeconds ?? 300,
    goldenScoreDurationSeconds: category?.goldenScoreDurationSeconds ?? 180,
  };
}

/** Name shown for a side: the free-text name of a Freikampf side, otherwise the athlete's name. */
export function fightSideName(fight: Fight, side: FightSide, athleteName: (id: string | null) => string): string {
  const freeName = side === 'white' ? fight.freeFight?.whiteName : fight.freeFight?.blueName;
  return freeName ?? athleteName(side === 'white' ? fight.whiteAthleteId : fight.blueAthleteId);
}

/** True when both sides are known: an athlete or a Freikampf free-text name. */
export function hasBothSides(fight: Fight): boolean {
  return !!(fight.whiteAthleteId ?? fight.freeFight?.whiteName) && !!(fight.blueAthleteId ?? fight.freeFight?.blueName);
}

/** True when the side won: a Freikampf records the side, a regular fight the winning athlete. */
export function isWinningSide(fight: Fight, side: FightSide): boolean {
  if (fight.freeFight) {
    return fight.freeFight.winnerSide === (side === 'white' ? 'White' : 'Blue');
  }

  return fight.winnerId !== null && fight.winnerId === (side === 'white' ? fight.whiteAthleteId : fight.blueAthleteId);
}

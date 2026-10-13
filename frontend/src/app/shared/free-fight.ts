import { Category, Fight, FightSide, WinningSide } from '../core/models';

/** Label shown instead of a category name for a Freikampf. */
export const FREE_FIGHT_LABEL_KEY = 'freeFight.label';

// Defaults and limits of a Freikampf; they mirror FightTiming and FreeFightRequest on the server.
export const FREE_FIGHT_DEFAULT_MATCH_DURATION_SECONDS = 240;
export const FREE_FIGHT_DEFAULT_GOLDEN_SCORE_DURATION_SECONDS = 180;
export const FREE_FIGHT_MIN_DURATION_SECONDS = 30;
export const FREE_FIGHT_MAX_DURATION_SECONDS = 3600;
export const FREE_FIGHT_MAX_NAME_LENGTH = 60;

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

/** Category name of a fight, or the Freikampf label for a fight without category. */
export function fightCategoryName(
  categoryId: string | null,
  categoryName: (id: string) => string,
  translate: (key: string) => string,
): string {
  return categoryId === null ? translate(FREE_FIGHT_LABEL_KEY) : categoryName(categoryId);
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

/** API name of a side, as used for the winner of a Freikampf. */
export function winningSide(side: FightSide): WinningSide {
  return side === 'white' ? 'White' : 'Blue';
}

/** True when the side won: a Freikampf records the side, a regular fight the winning athlete. */
export function isWinningSide(fight: Fight, side: FightSide): boolean {
  if (fight.freeFight) {
    return fight.freeFight.winnerSide === winningSide(side);
  }

  return fight.winnerId !== null && fight.winnerId === (side === 'white' ? fight.whiteAthleteId : fight.blueAthleteId);
}

/** Formats seconds as "m:ss" for a fight-time input. */
export function formatClockInput(totalSeconds: number): string {
  const seconds = Math.max(0, Math.floor(totalSeconds));
  return `${Math.floor(seconds / 60)}:${(seconds % 60).toString().padStart(2, '0')}`;
}

/** Parses a fight-time input ("m:ss", "mm:ss" or whole minutes); null when it is not a valid time. */
export function parseClockInput(text: string): number | null {
  const match = /^\s*(\d{1,2})(?::([0-5]\d))?\s*$/.exec(text);
  if (!match) return null;
  return Number(match[1]) * 60 + Number(match[2] ?? 0);
}

import { Component, computed, input, ChangeDetectionStrategy } from '@angular/core';

/** Side color of a running-score value. */
export type RunningScoreTone = 'white' | 'blue' | 'red';

/** One side of a running score: its won bouts, side color and accessible name. */
export interface RunningScoreSide {
  value: number;
  tone: RunningScoreTone;
  label: string;
}

/**
 * Generic running score of two sides (e.g. Serienstand, Begegnungsstand): a heading, one number per
 * side in the order and color of the surrounding view, and an optional note. Callers pass already
 * translated texts, so the block carries no domain or format-specific logic.
 */
@Component({
  selector: 'app-running-score',
  standalone: true,
  templateUrl: './running-score.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './running-score.component.css',
})
export class RunningScoreComponent {
  readonly heading = input.required<string>();
  /** Sides in display order (left to right). */
  readonly sides = input.required<readonly RunningScoreSide[]>();
  readonly note = input<string | null>(null);
  readonly size = input<'normal' | 'compact'>('normal');

  protected readonly ariaLabel = computed(() =>
    `${this.heading()}: ${this.sides().map((side) => `${side.label} ${side.value}`).join(', ')}`);
}

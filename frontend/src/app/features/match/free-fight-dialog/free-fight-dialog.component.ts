import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../../core/api.service';
import { extractApiError } from '../../../core/http-error';
import { I18nService } from '../../../core/i18n.service';
import { Athlete, Fight, FightSide, FreeFightRequest, FreeFightSideRequest } from '../../../core/models';
import { TranslatePipe } from '../../../core/translate.pipe';
import {
  FREE_FIGHT_DEFAULT_GOLDEN_SCORE_DURATION_SECONDS,
  FREE_FIGHT_DEFAULT_MATCH_DURATION_SECONDS,
  FREE_FIGHT_MAX_DURATION_SECONDS,
  FREE_FIGHT_MAX_NAME_LENGTH,
  FREE_FIGHT_MIN_DURATION_SECONDS,
  formatClockInput,
  parseClockInput,
} from '../../../shared/free-fight';

/** One side of the dialog; an empty athleteId means free text. */
interface SideForm {
  side: FightSide;
  athleteId: string;
  name: string;
}

/**
 * Dialog at the Mattentisch to add a Freikampf to the tatami queue or to edit a waiting one (ADR-0013).
 * It saves through the API itself and reports the outcome to the host.
 */
@Component({
  selector: 'app-free-fight-dialog',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  templateUrl: './free-fight-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './free-fight-dialog.component.css',
})
export class FreeFightDialogComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly i18n = inject(I18nService);

  readonly tournamentId = input.required<string>();
  readonly tatamiId = input.required<string>();
  /** Waiting Freikampf to edit; null adds a new one. */
  readonly fight = input<Fight | null>(null);
  readonly athletes = input.required<readonly Athlete[]>();
  /** Athletes that have not reached the tournament's minimum rest yet. */
  readonly insufficientRestAthleteIds = input<ReadonlySet<string>>(new Set());
  /** Label key of the accent side ("Blau" or "Rot"). */
  readonly accentSideLabelKey = input.required<string>();

  readonly saved = output<void>();
  readonly cancelled = output<void>();

  protected readonly maxNameLength = FREE_FIGHT_MAX_NAME_LENGTH;
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly athleteOptions = computed(() =>
    [...this.athletes()].sort((a, b) => a.lastName.localeCompare(b.lastName) || a.firstName.localeCompare(b.firstName)));

  protected sides: SideForm[] = [];
  protected matchDuration = '';
  protected goldenScoreEnabled = false;
  protected goldenScoreDuration = '';
  protected placeNext = false;

  ngOnInit(): void {
    const fight = this.fight();
    const details = fight?.freeFight;
    if (fight && details) {
      this.sides = [
        { side: 'white', athleteId: fight.whiteAthleteId ?? '', name: details.whiteName ?? '' },
        { side: 'blue', athleteId: fight.blueAthleteId ?? '', name: details.blueName ?? '' },
      ];
      this.matchDuration = formatClockInput(details.matchDurationSeconds);
      this.goldenScoreEnabled = details.goldenScoreEnabled;
      this.goldenScoreDuration = formatClockInput(details.goldenScoreDurationSeconds);
      return;
    }

    this.sides = [
      { side: 'white', athleteId: '', name: this.i18n.translate('match.whiteSide') },
      { side: 'blue', athleteId: '', name: this.i18n.translate(this.accentSideLabelKey()) },
    ];
    this.matchDuration = formatClockInput(FREE_FIGHT_DEFAULT_MATCH_DURATION_SECONDS);
    this.goldenScoreDuration = formatClockInput(FREE_FIGHT_DEFAULT_GOLDEN_SCORE_DURATION_SECONDS);
  }

  protected sideLabelKey(side: FightSide): string {
    return side === 'white' ? 'match.whiteSide' : this.accentSideLabelKey();
  }

  /** Names of selected athletes that have not reached the tournament's minimum rest yet. */
  protected restWarnings(): string[] {
    const blocked = this.insufficientRestAthleteIds();
    return this.sides
      .filter((side) => side.athleteId && blocked.has(side.athleteId))
      .map((side) => this.athleteName(side.athleteId));
  }

  protected cancel(): void {
    if (!this.saving()) this.cancelled.emit();
  }

  protected save(): void {
    if (this.saving()) return;

    const matchDurationSeconds = this.parseDuration(this.matchDuration);
    // A hidden golden score duration keeps its last valid value.
    const goldenScoreDurationSeconds = this.goldenScoreEnabled
      ? this.parseDuration(this.goldenScoreDuration)
      : this.parseDuration(this.goldenScoreDuration) ?? FREE_FIGHT_DEFAULT_GOLDEN_SCORE_DURATION_SECONDS;
    if (matchDurationSeconds === null || goldenScoreDurationSeconds === null) {
      this.error.set(this.i18n.translate('freeFight.invalidTime'));
      return;
    }

    const [white, blue] = this.sides;
    const body: FreeFightRequest = {
      white: this.toRequest(white),
      blue: this.toRequest(blue),
      matchDurationSeconds,
      goldenScoreEnabled: this.goldenScoreEnabled,
      goldenScoreDurationSeconds,
    };
    const fight = this.fight();
    const request = fight
      ? this.api.updateFreeFight(this.tournamentId(), fight.id, body)
      : this.api.createFreeFight(this.tournamentId(), this.tatamiId(), { ...body, placeNext: this.placeNext });

    this.saving.set(true);
    this.error.set(null);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.saved.emit();
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(extractApiError(err, this.i18n.translate('freeFight.saveFailed'), (key) => this.i18n.translate(key)));
      },
    });
  }

  private parseDuration(text: string): number | null {
    const seconds = parseClockInput(text);
    return seconds !== null && seconds >= FREE_FIGHT_MIN_DURATION_SECONDS && seconds <= FREE_FIGHT_MAX_DURATION_SECONDS
      ? seconds
      : null;
  }

  private athleteName(id: string): string {
    const athlete = this.athletes().find((candidate) => candidate.id === id);
    return athlete ? `${athlete.lastName}, ${athlete.firstName}` : id;
  }

  private toRequest(side: SideForm): FreeFightSideRequest {
    return side.athleteId ? { athleteId: side.athleteId, name: null } : { athleteId: null, name: side.name };
  }
}

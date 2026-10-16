import {
  Component,
  HostListener,
  OnDestroy,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  untracked,
  ChangeDetectionStrategy
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { AuthStateService } from '../../core/auth-state.service';
import { extractApiError } from '../../core/http-error';
import { I18nService } from '../../core/i18n.service';
import { SideThemeService } from '../../core/side-theme.service';
import { TimeService } from '../../core/time.service';
import { TournamentContextService } from '../../core/tournament-context.service';
import { CategoryFightsUpdatedEvent, TatamiQueueChangedEvent, TournamentHubService } from '../../core/tournament-hub.service';
import { TranslatePipe } from '../../core/translate.pipe';
import { HANSOKU_MAKE_SHIDO_COUNT, ShidoIndicatorComponent } from '../../shared/shido-indicator/shido-indicator.component';
import { RunningScoreComponent, RunningScoreSide } from '../../shared/running-score/running-score.component';
import { seriesPositionLabel, seriesScoreSides } from '../../shared/fight-series';
import {
  fightCategoryName,
  fightClockSettings,
  fightSideName,
  hasBothSides,
  isWinningSide,
  winningSide,
} from '../../shared/free-fight';
import { FreeFightDialogComponent } from './free-fight-dialog/free-fight-dialog.component';
import {
  AdjustScoreRequest,
  Athlete,
  Category,
  Club,
  Fight,
  FightSide,
  FightStatus,
  ScoreType,
  Tatami,
  TatamiQueue,
  OsaeKomiRequest,
  WinningSide,
} from '../../core/models';

const OPERATOR_NAME_KEY = 'judo.operatorName';
const KEYBOARD_BLOCKED_SELECTORS = 'input, textarea, select, dialog, [contenteditable]:not([contenteditable="false" i]), [role="dialog"], [aria-modal="true"]';

function isSpaceKey(event: KeyboardEvent): boolean {
  return event.code === 'Space' || event.key === ' ';
}

interface WinnerConfirmationState {
  fight: Fight;
  winnerId: string | null;
  /** Winning side of a Freikampf; null for Hiki-wake and regular fights. */
  winnerSide: WinningSide | null;
  nextFight: Fight | null;
}

/** Open Freikampf dialog; fight is null while adding a new one. */
interface FreeFightDialogState {
  fight: Fight | null;
}

@Component({
  selector: 'app-match',
  standalone: true,
  imports: [TranslatePipe, ShidoIndicatorComponent, RunningScoreComponent, FreeFightDialogComponent],
  templateUrl: './match.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './match.component.css',
})
export class MatchComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthStateService);
  private readonly i18n = inject(I18nService);
  protected readonly sideTheme = inject(SideThemeService);
  protected readonly context = inject(TournamentContextService);
  private readonly hub = inject(TournamentHubService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly canOperateLive = this.auth.canOperateLive;
  private readonly time = inject(TimeService);

  protected readonly tatamis = signal<Tatami[]>([]);
  protected readonly categories = signal<Category[]>([]);
  protected readonly athletes = signal<Map<string, Athlete>>(new Map());
  protected readonly clubs = signal<Map<string, Club>>(new Map());
  protected readonly selectedTatamiId = signal<string | null>(null);
  protected readonly queue = signal<TatamiQueue | null>(null);
  protected readonly operatorName = signal<string>(this.restoreOperatorName());
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly winnerConfirmation = signal<WinnerConfirmationState | null>(null);
  protected readonly confirmingWinner = signal(false);
  protected readonly freeFightDialog = signal<FreeFightDialogState | null>(null);
  protected readonly athleteList = computed(() => [...this.athletes().values()]);
  protected readonly isWinningSide = isWinningSide;
  protected readonly nowEpochMs = signal<number>(Date.now());

  /** Remaining seconds in the current fight's countdown. */
  protected readonly timerSeconds = signal<number | null>(null);
  protected readonly timerRemainingExactSeconds = signal<number | null>(null);
  protected readonly timerIsGoldenScore = signal(false);
  protected readonly osaeKomiSeconds = signal<number | null>(null);
  protected readonly osaeKomiElapsedExactSeconds = signal<number | null>(null);
  protected readonly osaeKomiCapSeconds = signal<number | null>(null);
  protected readonly osaeKomiSide = signal<FightSide | null>(null);
  protected readonly osaeKomiPaused = signal(false);
  private timerHandle: ReturnType<typeof setInterval> | null = null;
  private lastClockResyncCheckAtMs = 0;
  /** Some browsers activate a focused button on Space keyup, so a handled Space keydown also cancels its keyup. */
  private suppressSpaceKeyup = false;

  /** Track the previous fight to detect osae-komi transitions and cleanup. */
  private previousFight: Fight | null = null;
  /** When osae-komi ends, store the frozen display until a new one starts, the fight resumes or another fight becomes current. */
  private frozenOsaeKomiDisplay: { fightId: string; seconds: number; cap: number; side: FightSide } | null = null;
  private categoriesRequest: Subscription | null = null;

  protected readonly scoreTypes: ScoreType[] = ['Ippon', 'WazaAri', 'Yuko', 'Shido'];

  protected readonly hubConnected = computed(() => this.hub.connected());
  protected readonly currentFight = computed(() => this.queue()?.current ?? null);
  protected readonly freeFightModeEnabled = computed(() => this.queue()?.freeFightModeEnabled ?? false);
  /** All pending fights assigned to the tatami, excluding the current fight, in queue order. */
  protected readonly upcomingFights = computed(() => {
    const q = this.queue();
    if (!q) return [] as Fight[];
    const currentId = q.current?.id;
    return q.upcoming.filter((fight) => fight.id !== currentId);
  });
  protected readonly insufficientRestAthleteIds = computed(() => {
    const nowMs = this.nowEpochMs();
    const minimumGapSeconds = this.context.tournament()?.minimumRestBetweenFightsSeconds ?? 0;
    const ids = new Set<string>();
    if (minimumGapSeconds <= 0) {
      return ids;
    }

    for (const athlete of this.athletes().values()) {
      if (!athlete.lastFightEndedAtUtc) {
        continue;
      }

      const endedAtMs = new Date(athlete.lastFightEndedAtUtc).getTime();
      if (Number.isNaN(endedAtMs)) {
        continue;
      }

      const elapsedSeconds = Math.floor((nowMs - endedAtMs) / 1000);
      if (elapsedSeconds >= 0 && elapsedSeconds < minimumGapSeconds) {
        ids.add(athlete.id);
      }
    }

    return ids;
  });

  private fightSub?: Subscription;
  private categoryFightsSub?: Subscription;
  private tatamiQueueSub?: Subscription;
  private serverTimeSub?: Subscription;
  private reconnectSub?: Subscription;
  private querySub?: Subscription;
  private timeRefreshHandle: ReturnType<typeof setInterval> | null = null;

  private readonly selectedTatamiEffect = effect(() => {
    const tournamentId = this.context.tournamentId();
    const tatamiId = this.selectedTatamiId();

    if (!tournamentId || !tatamiId) {
      this.queue.set(null);
      this.stopTimer();
      this.previousFight = null;
      this.frozenOsaeKomiDisplay = null;
      return;
    }

    untracked(() => this.refreshQueue());
  });

  /** The clock captures category durations when it restarts, so restart it once categories arrive. */
  private readonly categoriesEffect = effect(() => {
    this.categories();
    untracked(() => {
      const q = this.queue();
      if (q) this.restartTimer(q.current);
    });
  });

  ngOnInit(): void {
    void this.time.synchronize(5);
    this.startTimeRefresh();

    const tid = this.context.tournamentId();
    if (!tid) return;

    this.querySub = this.route.queryParamMap.subscribe((params) => {
      const tatamiId = params.get('tatamiId');
      this.selectedTatamiId.set(tatamiId);
    });

    this.api.getTournament(tid).subscribe((tournament) => {
      this.context.refreshIfActive(tournament);
    });

    this.api.getTatamis(tid).subscribe((tatamis) => {
      const sortedTatamis = [...tatamis].sort(
        (a, b) => a.displayOrder - b.displayOrder || a.name.localeCompare(b.name),
      );
      this.tatamis.set(sortedTatamis);

      if (sortedTatamis.length === 0) {
        this.selectedTatamiId.set(null);
        return;
      }

      const selectedTatamiId = this.selectedTatamiId();
      const selectedTatamiExists = selectedTatamiId
        ? sortedTatamis.some((tatami) => tatami.id === selectedTatamiId)
        : false;

      if (!selectedTatamiExists) {
        void this.router.navigate([], {
          relativeTo: this.route,
          queryParams: {
            tournamentId: tid,
            tatamiId: sortedTatamis[0].id,
          },
          queryParamsHandling: 'merge',
          replaceUrl: true,
        });
      }
    });
    this.loadCategories(tid);
    this.api.getAthletes(tid).subscribe(athletes => {
      const map = new Map(athletes.map(a => [a.id, a]));
      this.athletes.set(map);
    });
    this.api.getClubs(tid).subscribe(clubs => {
      const map = new Map(clubs.map(club => [club.id, club]));
      this.clubs.set(map);
    });

    this.fightSub = this.hub.fightUpdated$.subscribe(fight => {
      const selectedTatamiId = this.selectedTatamiId();
      if (!selectedTatamiId) {
        return;
      }

      const q = this.queue();
      const isRelevantQueueFight = q
        ? q.current?.id === fight.id || q.next?.id === fight.id || q.onDeck?.id === fight.id
        : false;

      // Completed fights can re-seed downstream fights where tatami assignment is not yet visible
      // on the event payload. Force a queue refresh so the operator view re-sorts immediately.
      if (fight.status === 'Completed') {
        this.refreshQueue();
        this.refreshAthletes();
        return;
      }

      // Refresh whenever an updated fight belongs to the selected tatami so queue reordering,
      // current/next/on-deck changes and score updates stay in sync across clients.
      if (fight.tatamiId === selectedTatamiId || isRelevantQueueFight) {
        this.refreshQueue();
      }
    });

    this.categoryFightsSub = this.hub.categoryFightsUpdated$.subscribe((evt) => {
      this.handleCategoryFightsUpdated(evt);
    });

    this.tatamiQueueSub = this.hub.tatamiQueueChanged$.subscribe((evt) => {
      this.handleTatamiQueueChanged(evt);
    });

    this.serverTimeSub = this.hub.serverTimeSync$.subscribe((serverNowUtc) => {
      this.time.ingestServerNowUtc(serverNowUtc);
    });

    this.reconnectSub = this.hub.reconnected$.subscribe(() => {
      void this.time.synchronize(5);
    });
  }

  ngOnDestroy(): void {
    this.querySub?.unsubscribe();
    this.fightSub?.unsubscribe();
    this.categoryFightsSub?.unsubscribe();
    this.tatamiQueueSub?.unsubscribe();
    this.serverTimeSub?.unsubscribe();
    this.reconnectSub?.unsubscribe();
    this.categoriesRequest?.unsubscribe();
    this.stopTimer();
    this.stopTimeRefresh();
  }

  private handleCategoryFightsUpdated(evt: CategoryFightsUpdatedEvent): void {
    const tournamentId = this.context.tournamentId();
    if (!tournamentId || !evt.tournamentId) {
      return;
    }

    if (evt.tournamentId.toLowerCase() !== tournamentId.toLowerCase()) {
      return;
    }

    this.refreshQueue();
    this.refreshAthletes();
  }

  private handleTatamiQueueChanged(evt: TatamiQueueChangedEvent): void {
    const tatamiId = this.selectedTatamiId();
    if (tatamiId && evt.tatamiId?.toLowerCase() === tatamiId.toLowerCase()) {
      this.refreshQueue();
    }
  }

  private loadCategories(tournamentId: string): void {
    this.categoriesRequest?.unsubscribe();
    this.categoriesRequest = this.api.getCategories(tournamentId).subscribe(cats => this.categories.set(cats));
  }

  private refreshAthletes(): void {
    const tid = this.context.tournamentId();
    if (!tid) {
      return;
    }

    this.api.getAthletes(tid).subscribe((athletes) => {
      const map = new Map(athletes.map((a) => [a.id, a]));
      this.athletes.set(map);
    });
  }

  @HostListener('document:visibilitychange')
  protected onVisibilityChange(): void {
    if (document.visibilityState === 'visible') {
      void this.time.synchronizeIfStale();
    }
  }

  @HostListener('document:keydown', ['$event'])
  protected onKeyboardShortcut(event: KeyboardEvent): void {
    const isSpace = isSpaceKey(event);
    if (isSpace && !event.repeat) {
      this.suppressSpaceKeyup = false;
    }

    if (event.defaultPrevented || event.repeat || event.ctrlKey || event.altKey || event.metaKey || event.shiftKey) {
      return;
    }

    if (this.isKeyboardShortcutBlocked(event)) {
      return;
    }

    if (!this.canOperateLive() || !this.context.tournamentId() || !this.selectedTatamiId()) {
      return;
    }

    const fight = this.currentFight();
    if (!fight) {
      return;
    }

    const key = isSpace ? 'space' : event.key.toLowerCase();
    let action: (() => void) | null = null;

    switch (key) {
      case 'space':
        if (fight.status === 'Pending') {
          action = () => this.startFight(fight);
        } else if (fight.status === 'InProgress') {
          action = () => this.stopFight(fight);
        } else if (fight.status === 'Paused') {
          action = () => this.resumeFight(fight);
        }
        break;
      case 's':
        if (this.canRecordScore(fight) && this.canStartOsaeKomi(fight, 'white')) {
          action = () => this.startOsaeKomi(fight, 'white');
        }
        break;
      case 'f':
        if (this.canRecordScore(fight) && this.canStartOsaeKomi(fight, 'blue')) {
          action = () => this.startOsaeKomi(fight, 'blue');
        }
        break;
      case 'd':
        if (this.canRecordScore(fight) && this.osaeKomiSide() !== null) {
          action = () => this.stopOsaeKomi(fight);
        }
        break;
    }

    if (!action) {
      return;
    }

    event.preventDefault();
    this.suppressSpaceKeyup = isSpace;
    action();
  }

  @HostListener('document:keyup', ['$event'])
  protected onKeyboardShortcutKeyup(event: KeyboardEvent): void {
    if (!this.suppressSpaceKeyup || !isSpaceKey(event)) {
      return;
    }

    this.suppressSpaceKeyup = false;
    event.preventDefault();
  }

  private isKeyboardShortcutBlocked(event: KeyboardEvent): boolean {
    if (this.winnerConfirmation() !== null || this.freeFightDialog() !== null) {
      return true;
    }

    const target = event.target instanceof Element ? event.target : document.activeElement;
    if (!target) {
      return false;
    }

    return target.matches(KEYBOARD_BLOCKED_SELECTORS)
      || target.closest(KEYBOARD_BLOCKED_SELECTORS) !== null;
  }

  protected refreshQueue(): void {
    const tid = this.context.tournamentId();
    const mid = this.selectedTatamiId();
    if (!tid || !mid) return;

    this.api.getTatamiQueue(tid, mid).subscribe({
      next: q => {
        this.queue.set(q);
        this.restartTimer(q.current);
        const categoryId = q.current?.categoryId;
        const categoryLoadPending = this.categoriesRequest !== null && !this.categoriesRequest.closed;
        if (categoryId && !categoryLoadPending && !this.categories().some(c => c.id === categoryId)) {
          this.loadCategories(tid);
        }
      },
      error: () => this.errorMessage.set(this.i18n.translate('match.queueLoadFailed')),
    });
  }

  private restartTimer(fight: Fight | null): void {
    this.stopTimer();

    if (this.frozenOsaeKomiDisplay && this.frozenOsaeKomiDisplay.fightId !== fight?.id) {
      this.frozenOsaeKomiDisplay = null;
    }

    // Detect osae-komi transitions of the same fight before resetting state.
    if (this.previousFight !== null && fight !== null && this.previousFight.id === fight.id) {
      const prevHadOsaeKomi = this.previousFight.osaeKomiSide !== null;
      const newHasOsaeKomi = fight.osaeKomiSide !== null;
      const wasPausedNowRunning = this.previousFight.status === 'Paused' && fight.status === 'InProgress';
      const isNowPaused = fight.status === 'Paused';

      if (prevHadOsaeKomi && !newHasOsaeKomi && !wasPausedNowRunning) {
        const serverStoppedDisplay = this.getServerStoppedOsaeKomiDisplay(this.previousFight, fight);
        if (serverStoppedDisplay) {
          this.frozenOsaeKomiDisplay = { fightId: fight.id, ...serverStoppedDisplay };
        } else if (!isNowPaused) {
          const s = this.osaeKomiSeconds();
          const c = this.osaeKomiCapSeconds();
          const side = this.osaeKomiSide();
          if (s !== null && c !== null && side !== null) {
            this.frozenOsaeKomiDisplay = { fightId: fight.id, seconds: s, cap: c, side };
          }
        }
      }

      // New osae-komi started: clear frozen display.
      if (newHasOsaeKomi) {
        this.frozenOsaeKomiDisplay = null;
      }

      // Fight was resumed: clear frozen display.
      if (wasPausedNowRunning) {
        this.frozenOsaeKomiDisplay = null;
      }
    }

    this.previousFight = fight;

    // Not started: show configured duration.
    if (!fight || !fight.startedAtUtc) {
      const duration = fight ? this.clockSettings(fight).matchDurationSeconds : 300;
      this.timerSeconds.set(duration);
      this.timerRemainingExactSeconds.set(duration);
      this.timerIsGoldenScore.set(false);
      this.osaeKomiSeconds.set(null);
      this.osaeKomiElapsedExactSeconds.set(null);
      this.osaeKomiCapSeconds.set(null);
      this.osaeKomiSide.set(null);
      this.osaeKomiPaused.set(false);
      return;
    }

    const { matchDurationSeconds: duration, goldenScoreDurationSeconds: goldenScoreDuration } = this.clockSettings(fight);

    const tick = () => {
      const nowMs = Date.now();
      if (nowMs - this.lastClockResyncCheckAtMs >= 10_000) {
        this.lastClockResyncCheckAtMs = nowMs;
        void this.time.synchronizeIfStale();
      }

      const timerReferenceMs = fight.osaeKomiPausedAtUtc
        ? new Date(fight.osaeKomiPausedAtUtc).getTime()
        : fight.status === 'Paused' && fight.pausedAtUtc
          ? new Date(fight.pausedAtUtc).getTime()
        : this.time.nowMs();
      const elapsed = (timerReferenceMs - new Date(fight.startedAtUtc!).getTime()) / 1000;
      const regularRemaining = Math.max(0, duration - elapsed);

      // Use fight.isGoldenScore from server (reload-safe, score-aware).
      if (fight.isGoldenScore) {
        this.timerIsGoldenScore.set(true);
        const goldenElapsed = Math.max(0, elapsed - duration);
        const goldenRemaining = Math.max(0, goldenScoreDuration - goldenElapsed);
        this.timerRemainingExactSeconds.set(goldenRemaining);
        this.timerSeconds.set(Math.ceil(goldenRemaining));
      } else {
        // Regular time countdown.
        this.timerIsGoldenScore.set(false);
        this.timerRemainingExactSeconds.set(regularRemaining);
        this.timerSeconds.set(Math.ceil(regularRemaining));
      }

      // Update osae-komi display or use frozen display if stopped.
      if (fight.osaeKomiSide) {
        const side = fight.osaeKomiSide === 'White' ? 'white' : 'blue';
        const cap = this.getOsaeKomiCap(fight, side);
        const activeMilliseconds = fight.osaeKomiStartedAtUtc
          ? Math.max(0, this.time.nowMs() - new Date(fight.osaeKomiStartedAtUtc).getTime())
          : 0;
        const holdExactSeconds = Math.max(0, Math.min(cap,
          ((fight.osaeKomiElapsedMilliseconds ?? 0) + activeMilliseconds) / 1000));
        const holdSeconds = Math.min(cap, Math.ceil(holdExactSeconds));
        this.osaeKomiSeconds.set(holdSeconds);
        this.osaeKomiElapsedExactSeconds.set(holdExactSeconds);
        this.osaeKomiCapSeconds.set(cap);
        this.osaeKomiSide.set(side);
        this.osaeKomiPaused.set(fight.osaeKomiPausedAtUtc !== null);
        this.frozenOsaeKomiDisplay = null; // Active hold, no frozen display.
      } else {
        // No active osae-komi: clear signals (display will use frozen fallback).
        // This allows start buttons to be re-enabled after stopping osae-komi.
        this.osaeKomiSeconds.set(null);
        this.osaeKomiElapsedExactSeconds.set(null);
        this.osaeKomiCapSeconds.set(null);
        this.osaeKomiSide.set(null);
        this.osaeKomiPaused.set(false);
      }
    };

    tick();
    this.timerHandle = setInterval(tick, 100);
  }

  private stopTimer(): void {
    if (this.timerHandle !== null) {
      clearInterval(this.timerHandle);
      this.timerHandle = null;
    }
  }

  private getServerStoppedOsaeKomiDisplay(
    previousFight: Fight,
    stoppedFight: Fight,
  ): { seconds: number; cap: number; side: FightSide } | null {
    if (!previousFight.osaeKomiSide || !previousFight.osaeKomiStartedAtUtc) {
      return null;
    }

    const side = previousFight.osaeKomiSide === 'White' ? 'white' : 'blue';
    const cap = this.getOsaeKomiCap(previousFight, side);
    const stoppedAtMs = new Date(stoppedFight.updatedAtUtc).getTime();
    if (Number.isNaN(stoppedAtMs)) {
      return null;
    }

    const accumulatedMilliseconds = previousFight.osaeKomiElapsedMilliseconds ?? 0;
    const activeMilliseconds = previousFight.osaeKomiStartedAtUtc
      ? Math.max(0, stoppedAtMs - new Date(previousFight.osaeKomiStartedAtUtc).getTime())
      : 0;

    return {
      seconds: Math.min(cap, Math.max(0, (accumulatedMilliseconds + activeMilliseconds) / 1000)),
      cap,
      side,
    };
  }

  private startTimeRefresh(): void {
    this.refreshCurrentTime();
    this.timeRefreshHandle = setInterval(() => {
      this.refreshCurrentTime();
    }, 1000);
  }

  private stopTimeRefresh(): void {
    if (this.timeRefreshHandle !== null) {
      clearInterval(this.timeRefreshHandle);
      this.timeRefreshHandle = null;
    }
  }

  private refreshCurrentTime(): void {
    const localNowMs = Date.now();
    if (localNowMs - this.lastClockResyncCheckAtMs >= 10_000) {
      this.lastClockResyncCheckAtMs = localNowMs;
      void this.time.synchronizeIfStale();
    }

    this.nowEpochMs.set(this.time.nowMs());
  }

  private formatWholeSeconds(seconds: number): string {
    const rounded = Math.max(0, Math.ceil(seconds));
    const m = Math.floor(rounded / 60);
    const s = rounded % 60;
    return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
  }

  private formatTenthsCountdown(seconds: number): string {
    const clamped = Math.max(0, seconds);
    const wholeSeconds = Math.floor(clamped);
    const tenths = Math.floor((clamped - wholeSeconds) * 10);
    const m = Math.floor(wholeSeconds / 60);
    const s = wholeSeconds % 60;
    return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}.${tenths}`;
  }

  private formatFightDuration(totalSeconds: number): string {
    const normalized = Math.max(0, Math.floor(totalSeconds));
    const minutes = Math.floor(normalized / 60);
    const seconds = normalized % 60;
    return `${minutes}:${seconds.toString().padStart(2, '0')}`;
  }

  protected athleteName(id: string | null): string {
    if (!id) return '?';
    const a = this.athletes().get(id);
    return a ? `${a.lastName}, ${a.firstName}` : id.substring(0, 8);
  }

  protected athleteClubName(id: string | null): string | null {
    if (!id) return null;
    const athlete = this.athletes().get(id);
    if (!athlete) return null;
    return this.clubs().get(athlete.clubId)?.name ?? null;
  }

  protected lastFightInfoLabel(id: string | null): string | null {
    if (!id) return null;
    const athlete = this.athletes().get(id);
    if (!athlete?.lastFightEndedAtUtc || athlete.lastFightDurationSeconds === null) {
      return null;
    }

    const endedAt = new Date(athlete.lastFightEndedAtUtc);
    if (Number.isNaN(endedAt.getTime())) {
      return null;
    }

    const timeLabel = new Intl.DateTimeFormat('de-DE', {
      hour: '2-digit',
      minute: '2-digit',
    }).format(endedAt);

    return `${timeLabel} (${this.formatFightDuration(athlete.lastFightDurationSeconds)})`;
  }

  protected hasInsufficientRest(id: string | null): boolean {
    if (!id) return false;
    return this.insufficientRestAthleteIds().has(id);
  }

  protected categoryName(id: string | null): string {
    return fightCategoryName(
      id,
      (categoryId) => this.categories().find((category) => category.id === categoryId)?.name ?? categoryId,
      (key) => this.i18n.translate(key));
  }

  protected sideName(fight: Fight, side: FightSide): string {
    return fightSideName(fight, side, (id) => this.athleteName(id));
  }

  private clockSettings(fight: Fight) {
    return fightClockSettings(fight, this.categories().find((category) => category.id === fight.categoryId));
  }

  /** Tournament id for a live match action, or null when the user may not operate the match control. */
  private liveTournamentId(): string | null {
    return this.canOperateLive() ? this.context.tournamentId() : null;
  }

  protected canOperateActiveFight(fight: Fight): boolean {
    return this.canOperateLive() && this.canRecordScore(fight);
  }

  protected canMoveUp(index: number): boolean {
    const fight = this.upcomingFights()[index];
    // The server only reorders fights whose sides are both known.
    if (!this.canOperateLive() || !fight || !hasBothSides(fight)) return false;
    // Index 0 swaps with the current fight, which is only allowed before it starts.
    return index > 0 || this.currentFight()?.status === 'Pending';
  }

  protected canMoveDown(index: number): boolean {
    return this.canOperateLive() && index < this.upcomingFights().length - 1;
  }

  protected moveFightUp(fight: Fight, index: number): void {
    if (!this.canMoveUp(index)) return;
    this.moveFight(fight, 'Up');
  }

  protected moveFightDown(fight: Fight, index: number): void {
    if (!this.canMoveDown(index)) return;
    this.moveFight(fight, 'Down');
  }

  private moveFight(fight: Fight, direction: 'Up' | 'Down'): void {
    const tid = this.liveTournamentId();
    if (!tid) return;
    this.api.moveFightInQueue(tid, fight.id, direction).subscribe({
      next: () => this.refreshQueue(),
      error: () => this.errorMessage.set('Kampfreihenfolge konnte nicht geändert werden.'),
    });
  }

  protected startFight(fight: Fight): void {
    const tid = this.liveTournamentId();
    if (!tid) return;
    this.api.startFight(tid, fight.id, this.operatorName()).subscribe({
      next: () => this.refreshQueue(),
      error: () => this.errorMessage.set('Kampf konnte nicht gestartet werden.'),
    });
  }

  protected stopFight(fight: Fight): void {
    const tid = this.liveTournamentId();
    if (!tid) return;
    this.api.stopFight(tid, fight.id, this.operatorName()).subscribe({
      next: () => this.refreshQueue(),
      error: () => this.errorMessage.set('Kampf konnte nicht gestoppt werden.'),
    });
  }

  protected resumeFight(fight: Fight): void {
    const tid = this.liveTournamentId();
    if (!tid) return;
    this.api.resumeFight(tid, fight.id, this.operatorName()).subscribe({
      next: () => this.refreshQueue(),
      error: () => this.errorMessage.set('Kampf konnte nicht fortgesetzt werden.'),
    });
  }

  protected adjustScore(fight: Fight, side: FightSide, scoreType: ScoreType, delta: number): void {
    const tid = this.liveTournamentId();
    if (!tid) return;
    const body: AdjustScoreRequest = { side, scoreType, delta };
    this.api.adjustScore(tid, fight.id, body, this.operatorName()).subscribe({
      next: () => this.refreshQueue(),
      error: () => this.errorMessage.set('Wertung konnte nicht gespeichert werden.'),
    });
  }

    protected startOsaeKomi(fight: Fight, side: FightSide): void {
      const tid = this.liveTournamentId();
      if (!tid) return;
      const body: OsaeKomiRequest = { side };
      this.api.startOsaeKomi(tid, fight.id, body, this.operatorName()).subscribe({
        next: () => this.refreshQueue(),
        error: () => this.errorMessage.set('Osae-komi konnte nicht gestartet werden.'),
      });
    }

    protected stopOsaeKomi(fight: Fight): void {
      const tid = this.liveTournamentId();
      if (!tid) return;
      this.api.stopOsaeKomi(tid, fight.id, this.operatorName()).subscribe({
        next: () => this.refreshQueue(),
        error: () => this.errorMessage.set('Osae-komi konnte nicht gestoppt werden.'),
      });
    }

    protected toggleOsaeKomi(fight: Fight): void {
      if (fight.osaeKomiSide === null) return;
      const tid = this.liveTournamentId();
      if (!tid) return;

      const paused = this.isOsaeKomiPaused(fight);
      const request = paused
        ? this.api.resumeOsaeKomi(tid, fight.id, this.operatorName())
        : this.api.pauseOsaeKomi(tid, fight.id, this.operatorName());
      request.subscribe({
        next: () => this.refreshQueue(),
        error: () => this.errorMessage.set(this.i18n.translate(
          paused ? 'match.osaeKomiResumeFailed' : 'match.osaeKomiPauseFailed')),
      });
    }

  protected confirmWinner(fight: Fight, winnerId: string): void {
    if (!this.canOperateLive() || this.confirmingWinner()) return;
    this.winnerConfirmation.set({
      fight,
      winnerId,
      winnerSide: null,
      nextFight: this.queue()?.next ?? null,
    });
  }

  /** A Freikampf records the winning side; a regular fight the winning athlete. */
  protected confirmSideWins(fight: Fight, side: FightSide): void {
    if (!fight.freeFight) {
      const winnerId = side === 'white' ? fight.whiteAthleteId : fight.blueAthleteId;
      if (winnerId) this.confirmWinner(fight, winnerId);
      return;
    }

    if (!this.canOperateLive() || this.confirmingWinner()) return;
    this.winnerConfirmation.set({
      fight,
      winnerId: null,
      winnerSide: winningSide(side),
      nextFight: this.queue()?.next ?? null,
    });
  }

  /** Hiki-wake is recorded for team-matchday fights and, at the operator's discretion, for Freikämpfe. */
  protected canConfirmDraw(fight: Fight): boolean {
    return !!fight.freeFight || this.context.tournament()?.competitionMode === 'TeamMatchday';
  }

  protected confirmDraw(fight: Fight): void {
    if (!this.canOperateLive() || this.confirmingWinner()) return;
    this.winnerConfirmation.set({
      fight,
      winnerId: null,
      winnerSide: null,
      nextFight: this.queue()?.next ?? null,
    });
  }

  protected cancelWinnerConfirmation(): void {
    if (this.confirmingWinner()) {
      return;
    }

    this.winnerConfirmation.set(null);
  }

  protected confirmWinnerAndLoadNext(): void {
    if (this.confirmingWinner()) return;
    const tid = this.liveTournamentId();
    const confirmation = this.winnerConfirmation();
    if (!tid || !confirmation) return;

    this.confirmingWinner.set(true);
    const body = confirmation.fight.freeFight
      ? { winnerId: null, winnerSide: confirmation.winnerSide }
      : { winnerId: confirmation.winnerId };
    this.api.confirmResult(tid, confirmation.fight.id, body, this.operatorName()).subscribe({
      next: () => {
        this.winnerConfirmation.set(null);
        this.queue.set(null);
        this.refreshQueue();
        this.confirmingWinner.set(false);
      },
      error: () => {
        this.errorMessage.set('Ergebnis konnte nicht bestätigt werden.');
        this.confirmingWinner.set(false);
      },
    });
  }

  protected clearError(): void {
    this.errorMessage.set(null);
  }

  // ─── Freikampf (ADR-0013) ───

  protected canEditFreeFight(fight: Fight): boolean {
    return this.canOperateLive() && !!fight.freeFight && fight.status === 'Pending';
  }

  protected openCreateFreeFight(): void {
    if (this.canOperateLive()) this.freeFightDialog.set({ fight: null });
  }

  protected openEditFreeFight(fight: Fight): void {
    if (this.canEditFreeFight(fight)) this.freeFightDialog.set({ fight });
  }

  protected closeFreeFightDialog(): void {
    this.freeFightDialog.set(null);
  }

  protected onFreeFightSaved(): void {
    this.freeFightDialog.set(null);
    this.refreshQueue();
  }

  protected toggleFreeFightMode(event: Event): void {
    const tid = this.liveTournamentId();
    const tatamiId = this.selectedTatamiId();
    const checkbox = event.target as HTMLInputElement | null;
    if (!tid || !tatamiId || !checkbox || !this.canOperateLive()) {
      return;
    }

    this.api.setFreeFightMode(tid, tatamiId, checkbox.checked).subscribe({
      next: () => this.refreshQueue(),
      error: (err) => {
        checkbox.checked = this.freeFightModeEnabled();
        this.errorMessage.set(extractApiError(
          err,
          this.i18n.translate('freeFight.modeSaveFailed'),
          (key) => this.i18n.translate(key)));
      },
    });
  }

  protected isFreeFightModePositionOne(fight: Fight): boolean {
    const queue = this.queue();
    if (!queue?.freeFightModeEnabled || !fight.freeFight) {
      return false;
    }

    const currentIsRunning = queue.current?.status === 'InProgress' || queue.current?.status === 'Paused';
    const positionIndex = currentIsRunning || queue.current ? 1 : 0;
    if (!currentIsRunning && queue.current?.freeFight) {
      return queue.upcoming[0]?.id === fight.id;
    }

    return queue.upcoming[positionIndex]?.id === fight.id;
  }

  protected deleteFreeFight(fight: Fight): void {
    const tid = this.liveTournamentId();
    const confirmationKey = this.isFreeFightModePositionOne(fight)
      ? 'freeFight.modeDeleteConfirm'
      : 'freeFight.deleteConfirm';
    if (!tid || !this.canEditFreeFight(fight) || !confirm(this.i18n.translate(confirmationKey))) return;

    this.api.deleteFreeFight(tid, fight.id).subscribe({
      next: () => this.refreshQueue(),
      error: (err) => this.errorMessage.set(extractApiError(
        err, this.i18n.translate('freeFight.deleteFailed'), (key) => this.i18n.translate(key))),
    });
  }

  protected scoreCount(fight: Fight, side: FightSide, scoreType: ScoreType): number {
    const prefix = side === 'white' ? 'white' : 'blue';
    switch (scoreType) {
      case 'Ippon': return prefix === 'white' ? fight.whiteIpponCount : fight.blueIpponCount;
      case 'WazaAri': return prefix === 'white' ? fight.whiteWazaAriCount : fight.blueWazaAriCount;
      case 'Yuko': return prefix === 'white' ? fight.whiteYukoCount : fight.blueYukoCount;
      case 'Shido': return prefix === 'white' ? fight.whitePenalties : fight.bluePenalties;
      default: return 0;
    }
  }

  protected canRemoveScore(fight: Fight, side: FightSide, scoreType: ScoreType): boolean {
    return this.scoreCount(fight, side, scoreType) > 0;
  }

  protected canAddScore(fight: Fight, side: FightSide, scoreType: ScoreType): boolean {
    switch (scoreType) {
      case 'Ippon': return this.scoreCount(fight, side, scoreType) < 1;
      case 'WazaAri': return this.scoreCount(fight, side, scoreType) < 2;
      case 'Shido': return this.scoreCount(fight, side, scoreType) < HANSOKU_MAKE_SHIDO_COUNT;
      default: return true;
    }
  }

  protected scoreLabel(scoreType: ScoreType): string {
    switch (scoreType) {
      case 'Ippon': return 'match.ippon';
      case 'WazaAri': return 'match.wazaari';
      case 'Yuko': return 'match.yuko';
      case 'Shido': return 'match.shido';
    }
  }

  protected statusLabelKey(status: FightStatus): string {
    switch (status) {
      case 'InProgress': return 'match.statusInProgress';
      case 'Paused': return 'match.statusPaused';
      case 'Completed': return 'match.statusCompleted';
      default: return 'match.statusPending';
    }
  }

  protected hasWazaAri(fight: Fight, side: FightSide): boolean {
    return side === 'white' ? fight.whiteWazaAriCount > 0 : fight.blueWazaAriCount > 0;
  }

  protected holdTimerLabel(): string {
    const frozenDisplay = this.frozenOsaeKomiDisplay;
    const seconds = this.osaeKomiSeconds() ?? frozenDisplay?.seconds ?? null;
    const exactSeconds = this.osaeKomiElapsedExactSeconds();
    const cap = this.osaeKomiCapSeconds() ?? frozenDisplay?.cap ?? 25;
    if (seconds === null) return '--s';

    const isRunning = this.osaeKomiSide() !== null && exactSeconds !== null;
    const remainingToCap = exactSeconds !== null ? Math.max(0, cap - exactSeconds) : Number.MAX_VALUE;
    const showTenths = isRunning && remainingToCap <= 10;
    const primary = showTenths && exactSeconds !== null
      ? `${exactSeconds.toFixed(1)}s`
      : frozenDisplay
        ? `${seconds.toFixed(1)}s`
        : this.osaeKomiPaused() && exactSeconds !== null
          ? `${exactSeconds.toFixed(1)}s`
          : `${seconds}s`;

    return `${primary} / ${cap}s`;
  }

  protected fightTimerLabel(): string {
    const fight = this.currentFight();
    const remainingExact = this.timerRemainingExactSeconds();
    if (!fight || remainingExact === null) {
      return '--:--';
    }

    const showTenths = fight.status === 'InProgress' && remainingExact <= 10;
    return showTenths
      ? this.formatTenthsCountdown(remainingExact)
      : this.formatWholeSeconds(remainingExact);
  }

  protected isPaused(fight: Fight): boolean {
    return fight.status === 'Paused';
  }

  protected isRunning(fight: Fight): boolean {
    return fight.status === 'InProgress';
  }

  protected canStartOsaeKomi(fight: Fight, side: FightSide): boolean {
    return fight.status === 'InProgress' && this.osaeKomiSide() === null;
  }

  protected isOsaeKomiPaused(fight: Fight): boolean {
    return fight.osaeKomiSide !== null && fight.osaeKomiPausedAtUtc !== null;
  }

  protected osaeKomiToggleLabelKey(): string {
    return this.osaeKomiPaused() ? 'match.resumeOsae' : 'match.pauseOsae';
  }

  protected sideLabelKey(side: FightSide): string {
    return this.sideTheme.sideLabelKey(side, this.context.tournament());
  }

  protected startOsaeLabelKey(): string {
    return this.sideTheme.startOsaeLabelKey(this.context.tournament());
  }

  protected confirmWinnerLabelKey(): string {
    return this.sideTheme.confirmWinnerLabelKey(this.context.tournament());
  }

  /** Serienstand in the operator layout (White left); null outside a Kampfserie. */
  protected seriesScore(fight: Fight): RunningScoreSide[] | null {
    return fight.series
      ? seriesScoreSides(fight.series, 'white', this.sideTheme.accentSideColor(this.context.tournament()), (key) => this.i18n.translate(key))
      : null;
  }

  /** "Kampf 2 von 3" (score note) or "Kampf 2 der Serie" (upcoming list); null outside a Kampfserie. */
  protected seriesLabel(fight: Fight, style: 'ofTotal' | 'inSeries'): string | null {
    return seriesPositionLabel(fight.series, style, (key, params) => this.i18n.translate(key, params));
  }

  protected canRecordScore(fight: Fight): boolean {
    return fight.status === 'InProgress' || fight.status === 'Paused';
  }

  private getOsaeKomiCap(fight: Fight, side: FightSide): number {
    const t = this.context.tournament();
    if (this.hasWazaAri(fight, side)) {
      return t?.osaeKomiWazaAriSeconds ?? 10;
    }
    return t?.osaeKomiIpponSeconds ?? 20;
  }

  private restoreOperatorName(): string {
    try { return localStorage.getItem(OPERATOR_NAME_KEY) ?? 'Operator'; } catch { return 'Operator'; }
  }
}

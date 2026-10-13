import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { AuthStateService } from '../../core/auth-state.service';
import { I18nService } from '../../core/i18n.service';
import { Category, RegistrationDetail, Tournament } from '../../core/models';
import { TournamentContextService } from '../../core/tournament-context.service';
import { DrawComponent } from './draw.component';

describe('DrawComponent (format selection)', () => {
  const tournament = { id: 't1', accentSideColor: 'Blue' } as Tournament;

  function category(id: string): Category {
    return { id, tournamentId: 't1', name: id, drawFormat: null, isLocked: false } as Category;
  }

  function registrations(categoryId: string, count: number): RegistrationDetail[] {
    return Array.from({ length: count }, (_, i) => ({ id: `${categoryId}-${i}`, categoryId }) as RegistrationDetail);
  }

  async function render(): Promise<{ root: HTMLElement; detect: () => Promise<void> }> {
    TestBed.configureTestingModule({
      imports: [DrawComponent],
      providers: [
        provideRouter([]),
        { provide: I18nService, useValue: { translate: (key: string) => key } },
        { provide: AuthStateService, useValue: { canOperate: signal(true) } },
        {
          provide: TournamentContextService,
          useValue: { tournamentId: signal('t1'), tournament: signal(tournament), refreshIfActive: () => undefined },
        },
        {
          provide: ApiService,
          useValue: {
            getTournament: () => of(tournament),
            getCategories: () => of([category('pair'), category('trio')]),
            getAthletes: () => of([]),
            getClubs: () => of([]),
            getRegistrations: () => of([...registrations('pair', 2), ...registrations('trio', 3)]),
            getFights: () => of([]),
            getCategoryStandings: () => of([]),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(DrawComponent);
    const detect = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    await detect();
    return { root: fixture.nativeElement as HTMLElement, detect };
  }

  /** `globalFormat` is the toolbar select; `format-<id>` the select in the section of category `<id>`. */
  function select(root: HTMLElement, name: string): HTMLSelectElement {
    const scope = name === 'globalFormat'
      ? root.querySelector('.toolbar-actions')
      : [...root.querySelectorAll('.category-section')]
        .find((section) => section.querySelector('h2')?.textContent?.trim() === name.replace('format-', ''));
    const match = scope?.querySelector('select');
    if (!match) {
      throw new Error(`select ${name} not rendered`);
    }
    return match;
  }

  function options(root: HTMLElement, name: string): string[] {
    return [...select(root, name).options].map((option) => option.value);
  }

  it('offers Best of Three only for the category with exactly two athletes, never globally', async () => {
    const { root } = await render();

    expect(options(root, 'globalFormat')).not.toContain('BestOfThree');
    expect(options(root, 'format-pair')).toContain('BestOfThree');
    expect(options(root, 'format-trio')).not.toContain('BestOfThree');
  });

  it('preselects Best of Three for a two-athlete category', async () => {
    const { root } = await render();

    expect(select(root, 'format-pair').value).toBe('BestOfThree');
    expect(select(root, 'format-trio').value).toBe('SingleElimination');
  });

  it('applies a changed global format to every category, including two-athlete ones', async () => {
    const { root, detect } = await render();

    const global = select(root, 'globalFormat');
    global.value = 'RoundRobin';
    global.dispatchEvent(new Event('change'));
    await detect();

    expect(select(root, 'format-pair').value).toBe('RoundRobin');
    expect(select(root, 'format-trio').value).toBe('RoundRobin');
    expect(options(root, 'format-pair')).toContain('BestOfThree');
  });
});

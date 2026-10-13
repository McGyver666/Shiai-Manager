import type { Mock } from 'vitest';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { AuthStateService } from '../../core/auth-state.service';
import { Category, Fight, Tatami } from '../../core/models';
import { I18nService } from '../../core/i18n.service';
import { TournamentContextService } from '../../core/tournament-context.service';
import { TatamiAssignmentComponent } from './tatami-assignment.component';

describe('TatamiAssignmentComponent', () => {
  let apiMock: Record<string, Mock>;

  const tatami = { id: 'tatami-1', tournamentId: 'tournament-1', name: 'Matte 1', displayOrder: 1, isActive: true } as Tatami;
  const category = { id: 'category-1', tournamentId: 'tournament-1', name: 'U18 -66' } as Category;
  const fight = {
    id: 'fight-1', tournamentId: 'tournament-1', categoryId: 'category-1', round: 1, fightNumber: 1,
    whiteAthleteId: null, blueAthleteId: null, isBye: false, status: 'Pending', tatamiId: null,
  } as Fight;

  const conflict = () => throwError(() => new HttpErrorResponse({ status: 409, error: { title: 'Konflikt' } }));

  beforeEach(() => {
    apiMock = {
      getCategories: vi.fn().mockReturnValue(of([category])),
      getTatamis: vi.fn().mockReturnValue(of([tatami])),
      getAthletes: vi.fn().mockReturnValue(of([])),
      getFights: vi.fn().mockReturnValue(of([fight])),
      assignTatami: vi.fn().mockReturnValue(conflict()),
      assignTatamiBulk: vi.fn().mockReturnValue(conflict()),
    };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ApiService, useValue: apiMock },
        { provide: AuthStateService, useValue: { canOperate: signal(true) } },
        { provide: TournamentContextService, useValue: { tournamentId: signal('tournament-1') } },
        { provide: I18nService, useValue: { translate: (key: string) => key } },
      ],
    });
  });

  function createComponent() {
    const fixture = TestBed.createComponent(TatamiAssignmentComponent);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance as any };
  }

  it('reloads the fights and explains the conflict when a single assignment is rejected', () => {
    const { component } = createComponent();
    expect(apiMock['getFights']).toHaveBeenCalledTimes(1);

    component.assignFight('fight-1', 'tatami-1');

    expect(apiMock['getFights']).toHaveBeenCalledTimes(2);
    expect(component.error()).toBe('tatamiAssignment.startedFightConflict');
    expect(component.assigning()).toBe(false);
  });

  it('reloads the fights and explains the conflict when an auto assignment is rejected', () => {
    const { component } = createComponent();

    component.autoAssign();

    expect(apiMock['getFights']).toHaveBeenCalledTimes(2);
    expect(component.error()).toBe('tatamiAssignment.startedFightConflict');
  });

  it('reloads the fights and explains the conflict when a category assignment is rejected', () => {
    const { component } = createComponent();
    component.updateCategoryTatamiSelection('category-1', 'tatami-1');

    component.assignCategory('category-1');

    expect(apiMock['getFights']).toHaveBeenCalledTimes(2);
    expect(component.error()).toBe('tatamiAssignment.startedFightConflict');
  });

  it('keeps the generic save error for other failures without reloading', () => {
    apiMock['assignTatami'].mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    const { component } = createComponent();

    component.assignFight('fight-1', 'tatami-1');

    expect(apiMock['getFights']).toHaveBeenCalledTimes(1);
    expect(component.error()).not.toBe('tatamiAssignment.startedFightConflict');
  });
});

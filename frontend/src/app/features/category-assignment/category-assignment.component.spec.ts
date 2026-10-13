import type { MockedObject } from 'vitest';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { I18nService } from '../../core/i18n.service';
import { Category, RegistrationDetail } from '../../core/models';
import { TournamentContextService } from '../../core/tournament-context.service';
import { CategoryAssignmentComponent } from './category-assignment.component';

describe('CategoryAssignmentComponent', () => {
  let apiSpy: MockedObject<Pick<ApiService, 'assignCategory' | 'getCategories' | 'getRegistrations' | 'autoAssignCategories'>>;
  let component: any;
  let fixture: ComponentFixture<CategoryAssignmentComponent>;

  const translations: Record<string, string> = {
    'errors.ageGroups.noCategoriesForNaturalAgeGroup': 'No categories for natural age group {ageGroup}.',
  };
  const translate = (key: string, params?: Record<string, string | number>) =>
    (translations[key] ?? key).replace(/\{(\w+)\}/g, (_m, token: string) => String(params?.[token] ?? `{${token}}`));

  const category = {
    id: 'u11-40',
    name: 'U11 W -40 kg',
    ageGroup: 'U11',
    gender: 'Female',
    weightClassKg: 40,
    minBirthYear: 2017,
    maxBirthYear: 2018,
    isLocked: false,
  } as Category;
  const registration = {
    id: 'registration-1',
    categoryId: null,
    athleteBirthYear: 2016,
  } as RegistrationDetail;

  beforeEach(() => {
    apiSpy = {
      assignCategory: vi.fn().mockName('ApiService.assignCategory'),
      getCategories: vi.fn().mockName('ApiService.getCategories'),
      getRegistrations: vi.fn().mockName('ApiService.getRegistrations'),
      autoAssignCategories: vi.fn().mockName('ApiService.autoAssignCategories'),
    };
    apiSpy.assignCategory.mockReturnValue(of({} as any));
    apiSpy.getCategories.mockReturnValue(of([]));
    apiSpy.getRegistrations.mockReturnValue(of([]));
    TestBed.configureTestingModule({
      imports: [CategoryAssignmentComponent],
      providers: [
        { provide: ApiService, useValue: apiSpy },
        { provide: I18nService, useValue: { translate } },
        { provide: TournamentContextService, useValue: { tournamentId: signal('tournament-1') } },
      ],
    });
    fixture = TestBed.createComponent(CategoryAssignmentComponent);
    component = fixture.componentInstance;
    component.categories.set([category]);
    component.registrations.set([registration]);
  });

  function selectWith(value: string): HTMLSelectElement {
    const select = document.createElement('select');
    select.add(new Option('', ''));
    select.add(new Option(category.name, category.id));
    select.value = value;
    return select;
  }

  it('asks for confirmation when the birth year is outside the category range and keeps the old value on cancel', () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    const select = selectWith(category.id);

    component.reassign(registration, select);

    expect(window.confirm).toHaveBeenCalled();
    expect(apiSpy.assignCategory).not.toHaveBeenCalled();
    expect(select.value).toBe('');
  });

  it('assigns after confirmation because the birth-year range is only a plausibility check', () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    component.reassign(registration, selectWith(category.id));

    expect(apiSpy.assignCategory).toHaveBeenCalledWith('tournament-1', 'registration-1', { categoryId: 'u11-40' });
  });

  it('shows the localized auto-assignment reason next to each unassigned athlete', () => {
    const unassignedRegistrations = [
      { ...registration, id: 'registration-u21', athleteId: 'athlete-u21', athleteLastName: 'Junior', athleteFirstName: 'Jan', athleteWeightKg: 70 },
      { ...registration, id: 'registration-old', athleteId: 'athlete-old', athleteLastName: 'Senior', athleteFirstName: 'Sam', athleteWeightKg: 80 },
    ] as RegistrationDetail[];
    apiSpy.getRegistrations.mockReturnValue(of(unassignedRegistrations));
    apiSpy.autoAssignCategories.mockReturnValue(of({
      assignedCount: 0,
      unassignedCount: 2,
      unassigned: [
        {
          athleteId: 'athlete-u21', firstName: 'Jan', lastName: 'Junior',
          reason: 'Für die natürliche Altersklasse U21 des Athleten sind keine Kategorien angelegt.',
          reasonKey: 'errors.ageGroups.noCategoriesForNaturalAgeGroup',
          reasonParameters: { ageGroup: 'U21' },
        },
        {
          athleteId: 'athlete-old', firstName: 'Sam', lastName: 'Senior',
          reason: 'Unbekannter Grund',
          reasonKey: 'errors.ageGroups.unknownKey',
          reasonParameters: null,
        },
      ],
    }));
    fixture.detectChanges();

    component.autoAssign();
    fixture.detectChanges();

    const rows = Array.from(fixture.nativeElement.querySelectorAll('tbody tr')) as HTMLElement[];
    const rowFor = (lastName: string) => rows.find((row) => row.textContent?.includes(lastName))!;
    expect(rowFor('Junior').textContent).toContain('No categories for natural age group U21.');
    expect(rowFor('Senior').textContent).toContain('Unbekannter Grund');
  });
});

import { TestBed } from '@angular/core/testing';
import { RunningScoreComponent, RunningScoreSide } from './running-score.component';

describe('RunningScoreComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [RunningScoreComponent] });
  });

  const white: RunningScoreSide = { value: 1, tone: 'white', label: 'Weiß' };
  const blue: RunningScoreSide = { value: 0, tone: 'blue', label: 'Blau' };

  function render(inputs: { heading: string; sides: RunningScoreSide[]; note?: string | null; size?: 'normal' | 'compact' }): HTMLElement {
    const fixture = TestBed.createComponent(RunningScoreComponent);
    fixture.componentRef.setInput('heading', inputs.heading);
    fixture.componentRef.setInput('sides', inputs.sides);
    if (inputs.note !== undefined) fixture.componentRef.setInput('note', inputs.note);
    if (inputs.size) fixture.componentRef.setInput('size', inputs.size);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function values(root: HTMLElement): string[] {
    return [...root.querySelectorAll('.running-score__value')].map((el) => el.textContent?.trim() ?? '');
  }

  it('renders the heading and the values in the given side order', () => {
    const root = render({ heading: 'Serie', sides: [white, blue] });

    expect(root.querySelector('.running-score__heading')?.textContent?.trim()).toBe('Serie');
    expect(values(root)).toEqual(['1', '0']);
  });

  it('follows a reversed side order and keeps each value in its side color', () => {
    const root = render({ heading: 'Serie', sides: [{ ...blue, tone: 'red' }, white] });

    expect(values(root)).toEqual(['0', '1']);
    const items = root.querySelectorAll('.running-score__value');
    expect(items[0].classList).toContain('running-score__value--red');
    expect(items[1].classList).toContain('running-score__value--white');
  });

  it('shows the optional note only when given', () => {
    expect(render({ heading: 'Serie', sides: [white, blue] }).querySelector('.running-score__note')).toBeNull();

    const root = render({ heading: 'Serie', sides: [white, blue], note: 'Kampf 2 von 3' });
    expect(root.querySelector('.running-score__note')?.textContent?.trim()).toBe('Kampf 2 von 3');
  });

  it('labels the score for assistive technology', () => {
    const root = render({ heading: 'Serie', sides: [white, blue] });

    expect(root.querySelector('.running-score')?.getAttribute('aria-label')).toBe('Serie: Weiß 1, Blau 0');
  });

  it('applies the compact variant', () => {
    expect(render({ heading: 'Serie', sides: [white, blue], size: 'compact' })
      .querySelector('.running-score--compact')).not.toBeNull();
    expect(render({ heading: 'Serie', sides: [white, blue] }).querySelector('.running-score--compact')).toBeNull();
  });
});

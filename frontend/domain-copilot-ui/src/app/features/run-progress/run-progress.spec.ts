import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RunProgress } from './run-progress';

describe('RunProgress', () => {
  let component: RunProgress;
  let fixture: ComponentFixture<RunProgress>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RunProgress]
    })
    .compileComponents();

    fixture = TestBed.createComponent(RunProgress);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

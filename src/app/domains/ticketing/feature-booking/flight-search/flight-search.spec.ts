// Ensure JIT compiler is available for tests that require partially compiled libraries
import '@angular/compiler';

import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { page } from 'vitest/browser';

import { createTestFlight } from '../../../../testing/create-test-flight';
//import { provideTestConfig } from '../../../../testing/provide-test-config';
import { ConfigService } from '../../../shared/util-common/config-service';
import { FlightSearch } from './flight-search';
import { FlightStore } from './flight-store';

describe('flight-search', () => {
  let component: FlightSearch;
  let fixture: ComponentFixture<FlightSearch>;
  let ctrl: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FlightSearch],
      providers: [
        provideRouter([]),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { baseUrl: '', model: '' } },
        //provideTestConfig(),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(FlightSearch);
    component = fixture.componentInstance;

    ctrl = TestBed.inject(HttpTestingController);

    // Await initial data loading
    const request = await vi.waitFor(
      () => ctrl.expectOne('/flight?from=Graz&to=Hamburg'),
      { interval: 50, timeout: 1000 },
    );
    request.flush([]);
  });

  afterEach(() => {
    ctrl.verify();
  });

  it('can be created', () => {
    expect(component).not.toBeUndefined();
  });

  it('disables search button when from and to are not given', async () => {
    await page.getByLabelText('From').fill('');
    await page.getByLabelText('To').fill('');

    const button = page.getByRole('button', { name: 'Search' });
    await expect.element(button).toBeDisabled();
  });

  it('enables search button when from and to are given', async () => {
    await page.getByLabelText('From').fill('Paris');
    await page.getByLabelText('To').fill('London');

    const button = page.getByRole('button', { name: 'Search' });

    await expect.element(button).toBeEnabled();

    // Alternative
    await expect.element(button).not.toBeDisabled();
  });

  it('searches for flights when from and to are given', async () => {
    const flightStore = TestBed.inject(FlightStore);
    // Alternative for local services
    // flightStore = fixture.debugElement.injector.get(FlightStore);

    vi.spyOn(flightStore, 'updateFilter');

    await page.getByLabelText('From').fill('Paris');
    await page.getByLabelText('To').fill('London');

    const button = page.getByRole('button', { name: 'Search' });

    await button.click();

    const request = await vi.waitFor(() =>
      ctrl.expectOne('/flight?from=Paris&to=London'),
    );

    // simulate network delay of 2 seconds before flushing response
    await new Promise<void>((resolve) =>
      setTimeout(() => {
        request.flush([
          createTestFlight(1),
          createTestFlight(2),
          createTestFlight(3),
        ]);
        resolve();
      }, 2000),
    );

    const headings = page.getByRole('heading', {
      name: 'Paris - London',
    });

    await expect.element(headings).toHaveLength(3);

    expect(flightStore.updateFilter).toHaveBeenCalled();
    expect(flightStore.updateFilter).toHaveBeenCalledTimes(1);
    expect(flightStore.updateFilter).toHaveBeenCalledWith('Paris', 'London');

    // wait 2 seconds after test completion (simulate post-response delay / observation)
    await new Promise<void>((resolve) => setTimeout(resolve, 2000));
  });
});

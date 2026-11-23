import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { environment } from '../../environments/environment';
import { ShipmentDto, TrackingUpdatedDto } from '../core/models';
import { ConnectionState, TrackingService } from '../core/tracking.service';
import { ShipmentsComponent } from './shipments';

const LIST_URL = `${environment.shipmentApiUrl}/api/shipments`;

function shipment(overrides: Partial<ShipmentDto> = {}): ShipmentDto {
  return {
    id: 'id-1',
    trackingNumber: 'KGN-1',
    description: 'Box',
    destination: 'Berlin',
    status: 'Delivered',
    createdAt: '2026-11-15T09:30:00Z',
    updatedAt: '2026-11-18T14:05:00Z',
    ...overrides
  };
}

function updated(overrides: Partial<TrackingUpdatedDto> = {}): TrackingUpdatedDto {
  return {
    trackingNumber: 'KGN-1',
    description: 'Box',
    destination: 'Berlin',
    status: 'InTransit',
    createdAt: '2026-11-15T09:30:00Z',
    updatedAt: '2026-11-20T10:00:00Z',
    ...overrides
  };
}

class FakeTrackingService {
  readonly shipmentUpdated = new BehaviorSubject<TrackingUpdatedDto | null>(null);
  readonly connectionState = new BehaviorSubject<ConnectionState>('disconnected');
  started = 0;
  disposed = 0;

  start(): void {
    this.started++;
  }

  dispose(): void {
    this.disposed++;
  }
}

describe('ShipmentsComponent', () => {
  let fixture: ComponentFixture<ShipmentsComponent>;
  let http: HttpTestingController;
  let fake: FakeTrackingService;

  beforeEach(async () => {
    fake = new FakeTrackingService();
    fixture = await TestBed.configureTestingModule({
      imports: [ShipmentsComponent],
      providers: [provideHttpClientTesting(), { provide: TrackingService, useValue: fake }]
    }).createComponent(ShipmentsComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify();
  });

  it('renders the empty state for an empty list', () => {
    http.expectOne(LIST_URL).flush([]);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('No shipments');
    expect(el.textContent).toContain(
      'Shipments created in the customer portal will appear here.'
    );
    expect(el.querySelector('table')).toBeNull();
  });

  it('renders rows with badge classes for 2 shipments', () => {
    http
      .expectOne(LIST_URL)
      .flush([
        shipment(),
        shipment({
          id: 'id-2',
          trackingNumber: 'KGN-2',
          description: undefined,
          destination: 'Istanbul',
          status: 'InTransit',
          // No offset: parsed as local time, so the rendered stamp is timezone-independent.
          updatedAt: '2026-11-19T08:20:00'
        })
      ]);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('KGN-1');
    expect(el.textContent).toContain('KGN-2');
    expect(el.textContent).toContain('—');
    const badges = Array.from(el.querySelectorAll('span.badge'));
    expect(badges[0].className).toContain('badge-delivered');
    expect(badges[1].className).toContain('badge-in-transit');
    expect(el.textContent).toContain('2026-11-19 08:20');
  });

  it('renders the error banner and Retry re-issues the list fetch', () => {
    http
      .expectOne(LIST_URL)
      .flush('', { status: 500, statusText: 'Internal Server Error' });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain(
      "We couldn't reach the KargoNet service. Check your connection and try again."
    );
    const retry = Array.from(el.querySelectorAll('button')).find(
      (b) => (b as HTMLElement).textContent?.trim() === 'Retry'
    ) as HTMLButtonElement;
    retry.click();
    expect(http.expectOne(LIST_URL)).toBeTruthy();
  });

  it('Apply with a 200 response patches the row badge', () => {
    http.expectOne(LIST_URL).flush([shipment()]);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const select = el.querySelector('select') as HTMLSelectElement;
    select.value = 'Exception';
    select.dispatchEvent(new Event('change'));
    const apply = Array.from(el.querySelectorAll('tbody tr button')).find(
      (b) => b.textContent?.trim() === 'Apply'
    ) as HTMLButtonElement;
    apply.click();
    http
      .expectOne(`${environment.shipmentApiUrl}/api/shipments/id-1/status`)
      .flush(shipment({ status: 'Exception' }));
    fixture.detectChanges();

    expect((el.querySelector('span.badge') as HTMLElement).className).toContain(
      'badge-exception'
    );
    expect((el.querySelector('select') as HTMLSelectElement).value).toBe('Exception');
  });

  it('Apply with a 500 response shows the inline row error and keeps the selection', () => {
    http.expectOne(LIST_URL).flush([shipment()]);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const select = el.querySelector('select') as HTMLSelectElement;
    select.value = 'AtHub';
    select.dispatchEvent(new Event('change'));
    const apply = Array.from(el.querySelectorAll('tbody tr button')).find(
      (b) => b.textContent?.trim() === 'Apply'
    ) as HTMLButtonElement;
    apply.click();
    http
      .expectOne(`${environment.shipmentApiUrl}/api/shipments/id-1/status`)
      .flush('', { status: 500, statusText: 'Internal Server Error' });
    fixture.detectChanges();

    expect(el.textContent).toContain('Couldn\'t update status — try again.');
    expect((el.querySelector('select') as HTMLSelectElement).value).toBe('AtHub');
  });

  it('ShipmentUpdated for an existing row patches it in place', () => {
    http.expectOne(LIST_URL).flush([shipment()]);
    fixture.detectChanges();

    fake.shipmentUpdated.next(updated({ status: 'OutForDelivery' }));
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect((el.querySelector('span.badge') as HTMLElement).className).toContain(
      'badge-out-for-delivery'
    );
  });

  it('ShipmentUpdated for a new tracking number prepends a row and resolves its id', () => {
    http.expectOne(LIST_URL).flush([shipment()]);
    fixture.detectChanges();

    fake.shipmentUpdated.next(
      updated({ trackingNumber: 'KGN-2', status: 'Created', destination: 'Ankara' })
    );
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const rows = el.querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('KGN-2');
    expect((rows[0].querySelector('select') as HTMLSelectElement).disabled).toBe(true);

    http
      .expectOne(`${environment.shipmentApiUrl}/api/shipments/KGN-2`)
      .flush(shipment({ id: 'id-2', trackingNumber: 'KGN-2', destination: 'Ankara' }));
    fixture.detectChanges();

    expect((rows[0].querySelector('select') as HTMLSelectElement).disabled).toBe(false);
  });

  it('the indicator reflects the three connection states', () => {
    http.expectOne(LIST_URL).flush([]);
    fixture.detectChanges();
    expect(fake.started).toBe(1);

    const el = fixture.nativeElement as HTMLElement;
    fake.connectionState.next('connected');
    fixture.detectChanges();
    expect(el.textContent).toContain('Live');

    fake.connectionState.next('reconnecting');
    fixture.detectChanges();
    expect(el.textContent).toContain('Reconnecting…');

    fake.connectionState.next('disconnected');
    fixture.detectChanges();
    expect(el.textContent).toContain('Offline');
  });

  it('keeps the status select preselected to the row status across a passive CD pass', () => {
    http.expectOne(LIST_URL).flush([shipment()]);
    fixture.detectChanges();

    // A hub state change (e.g. connected) triggers change detection without
    // the user touching the select; the preselect must not degrade to the
    // first option or the tracking number.
    fake.connectionState.next('connected');
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const select = el.querySelector('select') as HTMLSelectElement;
    expect(select.value).toBe('Delivered');
  });
});

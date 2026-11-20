import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { environment } from '../../environments/environment';
import { ShipmentDto } from '../core/models';
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

describe('ShipmentsComponent', () => {
  let fixture: ComponentFixture<ShipmentsComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    fixture = await TestBed.configureTestingModule({
      imports: [ShipmentsComponent],
      providers: [provideHttpClientTesting()]
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
          updatedAt: '2026-11-19T08:00:00Z'
        })
      ]);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('KGN-1');
    expect(el.textContent).toContain('KGN-2');
    expect(el.textContent).toContain('—');
    expect(el.textContent).toContain('2026-11-18 14:05');
    const badges = Array.from(el.querySelectorAll('span.badge')).map(
      (b) => b.className
    );
    expect(badges).toContain('badge badge-delivered');
    expect(badges).toContain('badge badge-in-transit');
  });

  it('renders the error banner and Retry re-issues the list fetch', () => {
    http
      .expectOne(LIST_URL)
      .flush('', { status: 500, statusText: 'Internal Server Error' });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain(
      "We couldn't reach the KargoNet service. Check that the Shipment API is running and try again."
    );

    const retry = el.querySelector('button') as HTMLButtonElement;
    expect(retry.textContent).toContain('Retry');
    retry.click();
    fixture.detectChanges();

    const req = http.expectOne(LIST_URL);
    req.flush([]);
    fixture.detectChanges();
    expect(el.textContent).toContain('No shipments');
  });
});

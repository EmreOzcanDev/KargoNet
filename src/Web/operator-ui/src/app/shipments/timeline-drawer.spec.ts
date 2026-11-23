import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { environment } from '../../environments/environment';
import { ShipmentDto, TrackingEvent, TrackingUpdatedDto } from '../core/models';
import { ConnectionState, TrackingService } from '../core/tracking.service';
import { ShipmentsComponent } from './shipments';

const LIST_URL = `${environment.shipmentApiUrl}/api/shipments`;
const timelineUrl = (n: string) =>
  `${environment.trackingApiUrl}/api/tracking/${n}/events`;

function shipment(overrides: Partial<ShipmentDto> = {}): ShipmentDto {
  return {
    id: 'id-1',
    trackingNumber: 'KGN-1',
    description: 'Box',
    destination: 'Berlin',
    status: 'InTransit',
    createdAt: '2026-11-15T09:30:00Z',
    updatedAt: '2026-11-15T09:30:00Z',
    ...overrides
  };
}

function tracking(n: string, overrides: Partial<TrackingUpdatedDto> = {}): TrackingUpdatedDto {
  return {
    trackingNumber: n,
    description: 'Box',
    destination: 'Berlin',
    status: 'InTransit',
    createdAt: '2026-11-15T09:30:00Z',
    updatedAt: '2026-11-16T10:00:00Z',
    ...overrides
  };
}

function event(n: string, overrides: Partial<TrackingEvent> = {}): TrackingEvent {
  return {
    eventId: 'e1',
    trackingNumber: n,
    eventType: 'shipment.created',
    payloadJson: '{"description":"Box","destination":"Berlin"}',
    // No offset: parsed as local time, so the rendered stamp is timezone-independent.
    occurredAt: '2026-11-15T09:30:00',
    ...overrides
  };
}

const statusEvent = (n: string, status: string): TrackingEvent =>
  event(n, {
    eventId: 'e2',
    eventType: 'shipment.status.changed',
    payloadJson: JSON.stringify({ status }),
    occurredAt: '2026-11-16T10:00:00'
  });

class FakeTrackingService {
  readonly shipmentUpdated = new BehaviorSubject<TrackingUpdatedDto | null>(null);
  readonly connectionState = new BehaviorSubject<ConnectionState>('disconnected');
  joined: string[] = [];
  left: string[] = [];

  start(): void {}
  dispose(): void {}
  joinGroup(n: string): void {
    this.joined.push(n);
  }
  leaveGroup(n: string): void {
    this.left.push(n);
  }
}

describe('TimelineDrawer', () => {
  let fixture: ComponentFixture<ShipmentsComponent>;
  let http: HttpTestingController;
  let fake: FakeTrackingService;
  let el: HTMLElement;

  beforeEach(async () => {
    fake = new FakeTrackingService();
    fixture = await TestBed.configureTestingModule({
      imports: [ShipmentsComponent],
      providers: [provideHttpClientTesting(), { provide: TrackingService, useValue: fake }]
    }).createComponent(ShipmentsComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne(LIST_URL).flush([
      shipment(),
      shipment({ id: 'id-2', trackingNumber: 'KGN-2' })
    ]);
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => {
    http.verify();
  });

  const timelineButton = (row: number): HTMLButtonElement =>
    Array.from(el.querySelectorAll(`tbody tr:nth-child(${row}) button`)).find(
      (b) => b.textContent?.trim() === 'Timeline'
    ) as HTMLButtonElement;

  it('opening Timeline joins the group, fetches, and renders rows', () => {
    timelineButton(1).click();
    fixture.detectChanges();

    expect(fake.joined).toEqual(['KGN-1']);
    const req = http.expectOne(timelineUrl('KGN-1'));
    expect(el.textContent).toContain('Loading timeline…');
    req.flush([event('KGN-1'), statusEvent('KGN-1', 'InTransit')]);
    fixture.detectChanges();

    expect(el.textContent).toContain('Shipment created');
    expect(el.textContent).toContain('Box / Berlin');
    expect(el.textContent).toContain('Status changed to InTransit');
    expect(el.querySelector('span.badge')?.className).toContain('badge-in-transit');
    expect(el.textContent).toContain('2026-11-15 09:30');
    expect(el.textContent).toContain('2026-11-16 10:00');
  });

  it('a ShipmentUpdated for the open number re-fetches the timeline', () => {
    timelineButton(1).click();
    fixture.detectChanges();
    http.expectOne(timelineUrl('KGN-1')).flush([event('KGN-1')]);
    fixture.detectChanges();
    expect(el.textContent).not.toContain('Status changed to');

    fake.shipmentUpdated.next(tracking('KGN-1'));
    fixture.detectChanges();

    const req = http.expectOne(timelineUrl('KGN-1'));
    req.flush([event('KGN-1'), statusEvent('KGN-1', 'Delivered')]);
    fixture.detectChanges();

    expect(el.textContent).toContain('Status changed to Delivered');
  });

  it('closing leaves the group and removes the drawer', () => {
    timelineButton(1).click();
    fixture.detectChanges();
    http.expectOne(timelineUrl('KGN-1')).flush([event('KGN-1')]);
    fixture.detectChanges();

    (el.querySelector('[aria-label="Close"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(fake.left).toEqual(['KGN-1']);
    expect(el.querySelector('.drawer-panel')).toBeNull();
  });

  it('renders the empty and error states with Retry', () => {
    timelineButton(1).click();
    fixture.detectChanges();
    http.expectOne(timelineUrl('KGN-1')).flush([]);
    fixture.detectChanges();
    expect(el.textContent).toContain(
      'No events yet — events will appear as the shipment progresses.'
    );

    (el.querySelector('[aria-label="Close"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    timelineButton(2).click();
    fixture.detectChanges();
    http
      .expectOne(timelineUrl('KGN-2'))
      .flush('', { status: 500, statusText: 'Internal Server Error' });
    fixture.detectChanges();

    const retry = Array.from(el.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Retry'
    ) as HTMLButtonElement;
    retry.click();
    http.expectOne(timelineUrl('KGN-2')).flush([event('KGN-2')]);
    fixture.detectChanges();
    expect(el.textContent).toContain('Shipment created');
  });

  it('renders the eventType text when payloadJson is invalid', () => {
    timelineButton(1).click();
    fixture.detectChanges();
    http
      .expectOne(timelineUrl('KGN-1'))
      .flush([event('KGN-1', { eventType: 'custom.event', payloadJson: 'not-json' })]);
    fixture.detectChanges();

    expect(el.textContent).toContain('custom.event');
  });

  it('opening a second drawer closes the first group', () => {
    timelineButton(1).click();
    fixture.detectChanges();
    http.expectOne(timelineUrl('KGN-1')).flush([event('KGN-1')]);
    fixture.detectChanges();

    timelineButton(2).click();
    fixture.detectChanges();

    expect(fake.left).toContain('KGN-1');
    expect(fake.joined).toContain('KGN-2');
    expect(el.querySelector('.drawer-panel')?.textContent).toContain('KGN-2');
    http.expectOne(timelineUrl('KGN-2')).flush([event('KGN-2')]);
    fixture.detectChanges();
  });
});

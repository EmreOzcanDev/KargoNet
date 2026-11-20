import {
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnDestroy,
  Output
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { TrackingApiService } from '../core/tracking-api.service';
import { TrackingService } from '../core/tracking.service';
import { TrackingEvent } from '../core/models';
import { STATUS_BADGE_CLASS } from '../core/statuses';

interface TimelineRow {
  label: string;
  subLine?: string;
  status?: string;
  time: string;
}

type DrawerState = 'loading' | 'empty' | 'error' | 'populated';

function formatTime(value: string): string {
  const d = new Date(value);
  if (isNaN(d.getTime())) {
    return value;
  }
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** Shared timeline rules 1–6: occurredAt ascending, known types mapped, raw eventType fallback, payload parse guarded. */
function mapEvents(events: TrackingEvent[]): TimelineRow[] {
  return [...events]
    .sort((a, b) => a.occurredAt.localeCompare(b.occurredAt))
    .map((evt) => {
      const row: TimelineRow = { label: evt.eventType, time: formatTime(evt.occurredAt) };
      try {
        const payload = JSON.parse(evt.payloadJson) as Record<string, unknown>;
        switch (evt.eventType) {
          case 'shipment.created': {
            row.label = 'Shipment created';
            const parts: string[] = [];
            const description = payload['description'];
            const destination = payload['destination'];
            if (typeof description === 'string' && description.trim() !== '') {
              parts.push(description);
            }
            if (typeof destination === 'string' && destination.trim() !== '') {
              parts.push(destination);
            }
            if (parts.length > 0) {
              row.subLine = parts.join(' / ');
            }
            break;
          }
          case 'shipment.status.changed': {
            const status = payload['status'];
            if (typeof status === 'string') {
              row.label = `Status changed to ${status}`;
              row.status = status;
            }
            break;
          }
          default:
            // Unknown event type: raw text as label.
            break;
        }
      } catch {
        // Unparseable payload: eventType label, no sub-line.
      }
      return row;
    });
}

@Component({
  selector: 'app-timeline-drawer',
  imports: [CommonModule],
  templateUrl: './timeline-drawer.html',
  styleUrl: './timeline-drawer.scss'
})
export class TimelineDrawerComponent implements OnDestroy {
  @Output() readonly close = new EventEmitter<void>();

  state: DrawerState = 'loading';
  rows: TimelineRow[] = [];
  readonly badgeClass = STATUS_BADGE_CLASS;

  private _trackingNumber = '';
  private sub: Subscription | null = null;

  constructor(
    private readonly api: TrackingApiService,
    private readonly tracking: TrackingService,
    private readonly cd: ChangeDetectorRef
  ) {}

  @Input()
  set trackingNumber(n: string) {
    if (n === this._trackingNumber) {
      return;
    }
    if (this._trackingNumber !== '') {
      this.tracking.leaveGroup(this._trackingNumber);
    }
    this._trackingNumber = n;
    this.tracking.joinGroup(n);
    this.sub?.unsubscribe();
    this.sub = this.tracking.shipmentUpdated.subscribe((dto) => {
      if (dto !== null && dto.trackingNumber === n) {
        this.load();
      }
    });
    this.load();
    this.cd.markForCheck();
  }

  get trackingNumber(): string {
    return this._trackingNumber;
  }

  ngOnDestroy(): void {
    this.closeDrawer();
  }

  closeDrawer(): void {
    this.sub?.unsubscribe();
    this.sub = null;
    if (this._trackingNumber !== '') {
      this.tracking.leaveGroup(this._trackingNumber);
      this._trackingNumber = '';
    }
    this.close.emit();
  }

  load(): void {
    this.state = 'loading';
    this.rows = [];
    const n = this._trackingNumber;
    this.api.getTimeline(n).subscribe({
      next: (events) => {
        this.rows = mapEvents(events);
        this.state = this.rows.length > 0 ? 'populated' : 'empty';
        this.cd.markForCheck();
      },
      error: () => {
        this.state = 'error';
        this.cd.markForCheck();
      }
    });
  }
}

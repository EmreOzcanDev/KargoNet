import {
  ChangeDetectorRef,
  Component,
  OnDestroy,
  OnInit
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { ShipmentApiService } from '../core/shipment-api.service';
import { TrackingService } from '../core/tracking.service';
import { ShipmentDto, TrackingUpdatedDto } from '../core/models';
import { TimelineDrawerComponent } from './timeline-drawer';
import { STATUS_BADGE_CLASS, STATUS_OPTIONS } from '../core/statuses';

type ListState = 'loading' | 'empty' | 'error' | 'populated';

interface RowMeta {
  statusSelect: string | null;
  applyError: boolean;
  selectDisabled: boolean;
}

@Component({
  selector: 'app-shipments',
  imports: [CommonModule, TimelineDrawerComponent],
  templateUrl: './shipments.html',
  styleUrl: './shipments.scss'
})
export class ShipmentsComponent implements OnInit, OnDestroy {
  state: ListState = 'loading';
  shipments: ShipmentDto[] = [];
  readonly statusOptions = STATUS_OPTIONS;
  readonly badgeClass = STATUS_BADGE_CLASS;
  indicator = { label: 'Offline', cls: 'indicator-disconnected' };
  openNumber: string | null = null;

  private rowMeta = new Map<string, RowMeta>();
  private sub: Subscription | null = null;

  constructor(
    private readonly api: ShipmentApiService,
    private readonly tracking: TrackingService,
    private readonly cd: ChangeDetectorRef
  ) {}

  openTimeline(trackingNumber: string): void {
    this.openNumber = trackingNumber;
    this.cd.markForCheck();
  }

  ngOnInit(): void {
    this.load();
    this.tracking.start();
    const updatedSub = this.tracking.shipmentUpdated.subscribe((dto) => {
      if (dto === null) {
        return;
      }
      this.onShipmentUpdated(dto);
    });
    const stateSub = this.tracking.connectionState.subscribe((state) => {
      this.indicator =
        state === 'connected'
          ? { label: 'Live', cls: 'indicator-connected' }
          : state === 'reconnecting'
            ? { label: 'Reconnecting…', cls: 'indicator-reconnecting' }
            : { label: 'Offline', cls: 'indicator-disconnected' };
      this.cd.markForCheck();
    });
    this.sub = new Subscription();
    this.sub.add(updatedSub);
    this.sub.add(stateSub);
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    this.tracking.dispose();
  }

  load(): void {
    this.state = 'loading';
    this.shipments = [];
    this.rowMeta.clear();
    this.api.listShipments().subscribe({
      next: (rows) => {
        this.shipments = rows;
        this.state = rows.length > 0 ? 'populated' : 'empty';
        this.cd.markForCheck();
      },
      error: () => {
        this.state = 'error';
        this.cd.markForCheck();
      }
    });
  }

  meta(trackingNumber: string): RowMeta {
    let m = this.rowMeta.get(trackingNumber);
    if (m === undefined) {
      m = { statusSelect: null, applyError: false, selectDisabled: false };
      this.rowMeta.set(trackingNumber, m);
    }
    return m;
  }

  selectFor(row: ShipmentDto): string {
    const m = this.rowMeta.get(row.trackingNumber);
    return m?.statusSelect ?? row.status;
  }

  onStatusSelect(row: ShipmentDto, event: Event): void {
    this.meta(row.trackingNumber).statusSelect = (
      event.target as HTMLSelectElement
    ).value;
    this.cd.markForCheck();
  }

  applyStatus(row: ShipmentDto): void {
    const m = this.meta(row.trackingNumber);
    this.api.updateStatus(row.id, m.statusSelect).subscribe({
      next: (updated) => {
        row.status = updated.status;
        row.updatedAt = updated.updatedAt;
        row.description = updated.description;
        row.destination = updated.destination;
        m.statusSelect = updated.status;
        m.applyError = false;
        this.cd.markForCheck();
      },
      error: () => {
        m.applyError = true;
        this.cd.markForCheck();
      }
    });
  }

  private onShipmentUpdated(dto: TrackingUpdatedDto): void {
    const row = this.shipments.find(
      (r) => r.trackingNumber === dto.trackingNumber
    );
    if (row !== undefined) {
      row.status = dto.status;
      row.updatedAt = dto.updatedAt;
      row.description = dto.description;
      row.destination = dto.destination;
      this.meta(row.trackingNumber).statusSelect = dto.status;
    } else {
      this.prependRow(dto);
    }
    this.cd.markForCheck();
  }

  private prependRow(dto: TrackingUpdatedDto): void {
    const row: ShipmentDto = {
      id: '',
      trackingNumber: dto.trackingNumber,
      description: dto.description,
      destination: dto.destination,
      status: dto.status,
      createdAt: dto.createdAt,
      updatedAt: dto.updatedAt
    };
    this.shipments.unshift(row);
    const m = this.meta(dto.trackingNumber);
    m.statusSelect = dto.status;
    m.selectDisabled = true;
    if (this.state !== 'populated') {
      this.state = 'populated';
    }
    this.api.getShipment(dto.trackingNumber).subscribe({
      next: (full) => {
        row.id = full.id;
        row.createdAt = full.createdAt;
        m.selectDisabled = false;
        this.cd.markForCheck();
      },
      error: () => {
        // Row stays with its status select disabled (O1).
        this.cd.markForCheck();
      }
    });
  }

  formatUpdated(value: string): string {
    const d = new Date(value);
    if (isNaN(d.getTime())) {
      return value;
    }
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }
}

import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ShipmentApiService } from '../core/shipment-api.service';
import { ShipmentDto } from '../core/models';
import { STATUS_BADGE_CLASS } from '../core/statuses';

type ListState = 'loading' | 'empty' | 'error' | 'populated';

@Component({
  selector: 'app-shipments',
  imports: [CommonModule],
  templateUrl: './shipments.html',
  styleUrl: './shipments.scss'
})
export class ShipmentsComponent implements OnInit {
  state: ListState = 'loading';
  shipments: ShipmentDto[] = [];
  readonly badgeClass = STATUS_BADGE_CLASS;

  constructor(
    private readonly api: ShipmentApiService,
    private readonly cd: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.state = 'loading';
    this.shipments = [];
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

  formatUpdated(value: string): string {
    const d = new Date(value);
    if (isNaN(d.getTime())) {
      return value;
    }
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }
}

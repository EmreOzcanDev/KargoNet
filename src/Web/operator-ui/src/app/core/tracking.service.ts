import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState
} from '@microsoft/signalr';
import { environment } from '../../environments/environment';
import { TrackingUpdatedDto } from './models';

export type ConnectionState = 'connected' | 'reconnecting' | 'disconnected';

function mapState(state: HubConnectionState): ConnectionState {
  switch (state) {
    case HubConnectionState.Connected:
      return 'connected';
    case HubConnectionState.Reconnecting:
      return 'reconnecting';
    default:
      return 'disconnected';
  }
}

/**
 * Owns the app's single SignalR connection to the tracking hub (D4).
 * `ShipmentUpdated` broadcasts land on `shipmentUpdated`; hub state on
 * `connectionState`. `joinGroup`/`leaveGroup` manage per-number groups.
 * `dispose()` is idempotent and safe from `ngOnDestroy`.
 */
@Injectable({ providedIn: 'root' })
export class TrackingService {
  readonly shipmentUpdated = new BehaviorSubject<TrackingUpdatedDto | null>(null);
  readonly connectionState = new BehaviorSubject<ConnectionState>('disconnected');

  private connection: HubConnection | null = null;

  protected createConnection(): HubConnection {
    return new HubConnectionBuilder()
      .withUrl(`${environment.trackingApiUrl}/hubs/tracking`)
      .build();
  }

  start(): void {
    if (this.connection !== null) {
      return;
    }
    const connection = this.createConnection();
    connection.on('ShipmentUpdated', (dto: TrackingUpdatedDto) => {
      if (
        dto !== null &&
        typeof dto.trackingNumber === 'string' &&
        dto.trackingNumber.trim() !== ''
      ) {
        this.shipmentUpdated.next(dto);
      }
    });
    connection.on(
      'stateChanged',
      (_previous: HubConnectionState, current: HubConnectionState) => {
        this.connectionState.next(mapState(current));
      }
    );
    this.connection = connection;
    void connection.start().catch(() => {});
  }

  joinGroup(trackingNumber: string): void {
    this.start();
    void this.connection?.invoke('JoinTrackingGroup', trackingNumber).catch(() => {});
  }

  leaveGroup(trackingNumber: string): void {
    void this.connection?.invoke('LeaveTrackingGroup', trackingNumber).catch(() => {});
  }

  dispose(): void {
    const connection = this.connection;
    this.connection = null;
    if (connection !== null) {
      void connection.stop();
    }
  }
}

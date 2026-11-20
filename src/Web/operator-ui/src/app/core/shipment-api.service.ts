import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ShipmentDto } from './models';

@Injectable({ providedIn: 'root' })
export class ShipmentApiService {
  constructor(private readonly http: HttpClient) {}

  listShipments(): Observable<ShipmentDto[]> {
    return this.http.get<ShipmentDto[]>(`${environment.shipmentApiUrl}/api/shipments`);
  }

  getShipment(trackingNumber: string): Observable<ShipmentDto> {
    return this.http.get<ShipmentDto>(
      `${environment.shipmentApiUrl}/api/shipments/${trackingNumber}`
    );
  }

  updateStatus(id: string, status: string): Observable<ShipmentDto> {
    return this.http.put<ShipmentDto>(
      `${environment.shipmentApiUrl}/api/shipments/${id}/status`,
      { status }
    );
  }
}

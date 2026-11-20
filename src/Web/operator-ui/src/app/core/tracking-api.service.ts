import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { TrackingEvent } from './models';

@Injectable({ providedIn: 'root' })
export class TrackingApiService {
  constructor(private readonly http: HttpClient) {}

  getTimeline(trackingNumber: string): Observable<TrackingEvent[]> {
    return this.http.get<TrackingEvent[]>(
      `${environment.trackingApiUrl}/api/tracking/${trackingNumber}/events`
    );
  }
}

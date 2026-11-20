/** Shipment row shape — camelCase JSON from GET /api/shipments. */
export interface ShipmentDto {
  id: string;
  trackingNumber: string;
  description?: string;
  destination: string;
  status: string;
  createdAt: string;
  updatedAt: string;
}

/** Tracking event shape — camelCase JSON from GET /api/tracking/{n}/events. */
export interface TrackingEvent {
  eventId: string;
  trackingNumber: string;
  eventType: string;
  payloadJson: string;
  occurredAt: string;
}

/** Hub broadcast shape — no `id` field (frozen Tracking.Api DTO). */
export interface TrackingUpdatedDto {
  trackingNumber: string;
  description: string;
  destination: string;
  status: string;
  createdAt: string;
  updatedAt: string;
}

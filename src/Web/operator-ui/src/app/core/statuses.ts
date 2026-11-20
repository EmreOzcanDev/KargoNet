/**
 * Single source of truth for the 7 shipment status values (D6).
 * Templates render badges only via STATUS_BADGE_CLASS — never hardcoded.
 */
export const STATUS_OPTIONS: readonly string[] = [
  'Created',
  'PickedUp',
  'InTransit',
  'AtHub',
  'OutForDelivery',
  'Delivered',
  'Exception'
];

/** Maps each status to its badge CSS class (colors per UI spec badge map). */
export const STATUS_BADGE_CLASS: Record<string, string> = {
  Created: 'badge-created',
  PickedUp: 'badge-picked-up',
  InTransit: 'badge-in-transit',
  AtHub: 'badge-at-hub',
  OutForDelivery: 'badge-out-for-delivery',
  Delivered: 'badge-delivered',
  Exception: 'badge-exception'
};

import { STATUS_OPTIONS, STATUS_BADGE_CLASS } from './statuses';

describe('statuses', () => {
  it('exposes exactly the 7 enum values', () => {
    expect([...STATUS_OPTIONS]).toEqual([
      'Created',
      'PickedUp',
      'InTransit',
      'AtHub',
      'OutForDelivery',
      'Delivered',
      'Exception'
    ]);
  });

  it('maps every status to a badge class', () => {
    for (const status of STATUS_OPTIONS) {
      expect(STATUS_BADGE_CLASS[status], `missing badge class for ${status}`).toBeTruthy();
    }
  });
});

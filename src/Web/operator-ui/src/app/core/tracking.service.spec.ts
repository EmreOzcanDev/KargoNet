import { HubConnection } from '@microsoft/signalr';
import { TrackingService, ConnectionState } from './tracking.service';
import { TrackingUpdatedDto } from './models';

class FakeHubConnection {
  handlers: Record<string, (...args: unknown[]) => void> = {};
  invoked: Array<[string, ...unknown[]]> = [];
  started = 0;
  stopped = 0;
  startResult: Promise<void> = Promise.resolve();
  reconnecting: () => void = () => {};
  reconnected: () => void = () => {};
  closed: () => void = () => {};

  on(event: string, handler: (...args: unknown[]) => void): this {
    this.handlers[event] = handler;
    return this;
  }

  onreconnecting(callback: () => void): void {
    this.reconnecting = callback;
  }

  onreconnected(callback: () => void): void {
    this.reconnected = callback;
  }

  onclose(callback: () => void): void {
    this.closed = callback;
  }

  start(): Promise<void> {
    this.started++;
    return this.startResult;
  }

  stop(): Promise<void> {
    this.stopped++;
    return Promise.resolve();
  }

  invoke(method: string, ...args: unknown[]): Promise<void> {
    this.invoked.push([method, ...args]);
    return Promise.resolve();
  }
}

class TestTrackingService extends TrackingService {
  constructor(private readonly fake: FakeHubConnection) {
    super();
  }

  protected override createConnection(): HubConnection {
    return this.fake as unknown as HubConnection;
  }
}

describe('TrackingService', () => {
  let fake: FakeHubConnection;
  let service: TestTrackingService;

  beforeEach(() => {
    fake = new FakeHubConnection();
    service = new TestTrackingService(fake);
  });

  const dto = (trackingNumber = 'KGN-1'): TrackingUpdatedDto => ({
    trackingNumber,
    description: 'Widget',
    destination: 'Istanbul',
    status: 'InTransit',
    createdAt: '2026-11-20T08:00:00Z',
    updatedAt: '2026-11-20T10:00:00Z'
  });

  it('delivers ShipmentUpdated payloads to shipmentUpdated', () => {
    service.start();
    expect(fake.started).toBe(1);
    fake.handlers['ShipmentUpdated'](dto());
    expect(service.shipmentUpdated.value).toEqual(dto());
  });

  it('ignores ShipmentUpdated payloads without a tracking number', () => {
    service.start();
    fake.handlers['ShipmentUpdated']({ ...dto(), trackingNumber: '' });
    expect(service.shipmentUpdated.value).toBeNull();
  });

  it('reports connected once start succeeds, then follows the lifecycle callbacks', async () => {
    service.start();
    await fake.startResult;
    expect(service.connectionState.value).toBe('connected');
    fake.reconnecting();
    expect(service.connectionState.value).toBe('reconnecting');
    fake.reconnected();
    expect(service.connectionState.value).toBe('connected');
    fake.closed();
    expect(service.connectionState.value).toBe('disconnected');
  });

  it('stays disconnected when start fails', async () => {
    fake.startResult = Promise.reject(new Error('negotiate failed'));
    service.start();
    await fake.startResult.catch(() => {});
    expect(service.connectionState.value).toBe('disconnected');
  });

  it('joinGroup and leaveGroup invoke the hub group methods', () => {
    service.joinGroup('KGN-1');
    service.leaveGroup('KGN-1');
    expect(fake.invoked).toEqual([
      ['JoinTrackingGroup', 'KGN-1'],
      ['LeaveTrackingGroup', 'KGN-1']
    ]);
  });

  it('dispose stops the connection and is idempotent', () => {
    service.start();
    service.dispose();
    service.dispose();
    expect(fake.stopped).toBe(1);
  });

  it('start is idempotent — one connection per service', () => {
    service.start();
    service.start();
    expect(fake.started).toBe(1);
  });

  it('connectionState starts disconnected', () => {
    expect(service.connectionState.value).toBe('disconnected');
  });
});

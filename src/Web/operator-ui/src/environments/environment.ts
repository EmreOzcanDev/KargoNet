export interface Environment {
  shipmentApiUrl: string;
  trackingApiUrl: string;
}

export const environment: Environment = {
  shipmentApiUrl: 'http://localhost:5001',
  trackingApiUrl: 'http://localhost:5002'
};

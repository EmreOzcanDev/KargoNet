import { Routes } from '@angular/router';
import { ShipmentsComponent } from './shipments/shipments';

export const routes: Routes = [
  { path: '', pathMatch: 'full', component: ShipmentsComponent },
  { path: '**', redirectTo: '' }
];

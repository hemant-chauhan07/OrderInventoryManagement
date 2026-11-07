import { apiRequest } from './apiClient';
import type { AuthResponse, CustomerReport, Order, PagedResponse, Product, ProductFilters } from './models';
export const authApi = {
  login: (email: string, password: string) => apiRequest<AuthResponse>('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }),
  register: (fullName: string, email: string, password: string) => apiRequest('/api/auth/register', { method: 'POST', body: JSON.stringify({ fullName, email, password }) })
};
export const productApi = {
  get: (id: number) => apiRequest<Product>(`/api/products/${id}`),
  list: (filters: ProductFilters) => {
    const params = new URLSearchParams();
    Object.entries(filters).forEach(([key, value]) => { if (value !== '' && value !== undefined && value !== null) params.set(key, String(value)); });
    return apiRequest<PagedResponse<Product>>(`/api/products?${params.toString()}`);
  },
  create: (data: Omit<Product, 'id' | 'inStock' | 'rowVersion'>) => apiRequest<Product>('/api/products', { method: 'POST', body: JSON.stringify(data) }),
  update: (id: number, data: Omit<Product, 'id' | 'inStock'>) => apiRequest<Product>(`/api/products/${id}`, { method: 'PUT', body: JSON.stringify(data) })
};
export const orderApi = {
  mine: (pageNumber = 1) => apiRequest<PagedResponse<Order>>(`/api/orders/my?pageNumber=${pageNumber}&pageSize=10`),
  all: (pageNumber = 1) => apiRequest<PagedResponse<Order>>(`/api/orders?pageNumber=${pageNumber}&pageSize=10`),
  place: (items: { productId: number; quantity: number }[], idempotencyKey: string) => apiRequest<Order>('/api/orders', { method: 'POST', headers: { 'Idempotency-Key': idempotencyKey }, body: JSON.stringify({ items }) }),
  changeStatus: (id: number, status: string) => apiRequest<Order>(`/api/orders/${id}/status`, { method: 'PATCH', body: JSON.stringify({ status }) })
};
export const reportApi = { topCustomers: (days = 30) => apiRequest<CustomerReport[]>(`/api/reports/top-customers?days=${days}`) };

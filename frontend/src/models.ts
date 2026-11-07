export type UserRole = 'Admin' | 'Customer';
export interface AuthResponse { accessToken: string; refreshToken: string; accessTokenExpiresAt: string; refreshTokenExpiresAt: string; userId: number; fullName: string; email: string; role: UserRole; }
export interface Product { id: number; sku: string; name: string; price: number; stockQuantity: number; inStock: boolean; isActive: boolean; rowVersion: string; }
export interface OrderItem { productId: number; productName: string; sku: string; quantity: number; unitPrice: number; lineTotal: number; }
export interface Order { id: number; customerId: number; customerName: string; status: 'Pending' | 'Confirmed' | 'Shipped' | 'Cancelled' | string; subTotal: number; discount: number; total: number; createdAt: string; items: OrderItem[]; }
export interface CustomerReport { customerId: number; customerName: string; email: string; totalSpend: number; totalOrders: number; }
export interface PagedResponse<T> { items: T[]; pageNumber: number; pageSize: number; totalCount: number; totalPages: number; hasPreviousPage: boolean; hasNextPage: boolean; }
export interface ProductFilters { search: string; minPrice: string; maxPrice: string; inStock: string; pageNumber: number; pageSize: number; sortBy: string; sortDirection: string; }

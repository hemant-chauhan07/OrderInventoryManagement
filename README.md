# Order & Inventory Management

A layered order and inventory system using ASP.NET Core 8 Web API, EF Core 8, SQL Server, JWT authentication and React (Vite). The backend is split into API, Application, Domain and Infrastructure projects.

## Solution layout

```text
OrderInventory.sln
src/
  OrderInventory.API/             Controllers, middleware, hosted service, startup
  OrderInventory.Application/     DTOs, service contracts, application exceptions
  OrderInventory.Domain/          Entities and enums
  OrderInventory.Infrastructure/  EF Core, SQL Server, migrations, services
frontend/                         React + TypeScript + Vite client
QUERIES.sql                       Required SQL Server queries
REVIEW.md                         Technical review and known validation notes
```

## Requirements

- .NET 8 SDK
- SQL Server 2019+ (LocalDB, Developer, Express or managed SQL Server)
- Node.js 20+ and npm
- EF Core CLI: `dotnet tool install --global dotnet-ef --version 8.*`

## Configure and run the API

The checked-in `appsettings.json` uses Windows LocalDB as a convenient local default. Change `ConnectionStrings:DefaultConnection` for your SQL Server instance. Do not commit production credentials or signing keys.

Set a Base64-encoded JWT signing key containing at least 64 bytes. Example PowerShell:

```powershell
$key = [byte[]]::new(64)
[Security.Cryptography.RandomNumberGenerator]::Fill($key)
dotnet user-secrets init --project src/OrderInventory.API
dotnet user-secrets set "JwtSettings:SecretKey" ([Convert]::ToBase64String($key)) --project src/OrderInventory.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=OrderInventoryDB;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False" --project src/OrderInventory.API
```

For SQL authentication, use a connection string with `User Id` and `Password` in user secrets or environment variables. The API reads `ConnectionStrings__DefaultConnection` and `JwtSettings__SecretKey` from environment variables as well.

Restore, migrate and start:

```bash
dotnet restore OrderInventory.sln
dotnet ef database update --project src/OrderInventory.Infrastructure --startup-project src/OrderInventory.API
dotnet run --project src/OrderInventory.API
```

The API applies migrations and runs idempotent seed logic on startup. Swagger is available at `/swagger` in Development. EF Core SQL command logging is enabled at Information level.

## Seed data

A fresh database receives:
- 1 administrator: `admin@example.com`
- 3 customers: `customer1@example.com`, `customer2@example.com`, `customer3@example.com`
- 15 products

The seed password for all four development users is `ChangeMe123!`. Change/remove these development credentials before deploying. Seed users and products are inserted only when their respective tables are empty.

## Run the React frontend

```bash
cd frontend
npm install
npm run dev
```

Open `http://localhost:5173`. Vite proxies `/api` requests to the API's default HTTP development URL `http://localhost:5148`. If you change the API URL, update `frontend/vite.config.ts`.

The UI provides:
- Login and customer registration.
- Product catalog with a 300 ms debounced search, minimum/maximum price and stock filters, server-side pagination/sorting, and shareable URL query parameters.
- A cart persisted in local storage across refreshes and checkout with a stable `Idempotency-Key` per cart submission.
- Customer order history with cancellation shown only for Pending orders.
- Admin product create/edit (including the SQL Server `RowVersion` token), paged order management with valid status transitions, and a top-customers table.
- A shared typed API layer and fetch interceptor that attaches JWTs, coordinates refresh requests after 401, and retries each original request once. 409 conflicts and 429 login throttling are displayed as user-readable messages.
- Role-gated routes, loading/empty/error states, and responsive laptop/mobile layouts.

Frontend source is organized into `src/models.ts` (TypeScript contracts), `src/apiClient.ts` (JWT/refresh interceptor), `src/api.ts` (API service functions), `src/App.tsx` (screens and route guards), and `src/style.css` (responsive styling).

## API endpoints

| Method | Endpoint | Access |
|---|---|---|
| POST | `/api/auth/register` | Anonymous |
| POST | `/api/auth/login` | Anonymous, 5 requests/minute/IP |
| POST | `/api/auth/refresh` | Anonymous, refresh token required |
| GET | `/api/products` | Anonymous, paged/filterable/sortable |
| GET | `/api/products/{id}` | Anonymous |
| POST | `/api/products` | Admin |
| PUT | `/api/products/{id}` | Admin, requires RowVersion |
| POST | `/api/orders` | Customer, requires `Idempotency-Key` header |
| GET | `/api/orders/my` | Customer |
| GET | `/api/orders` | Admin, paged list of all orders |
| GET | `/api/orders/{id}` | Owner or Admin |
| PATCH | `/api/orders/{id}/status` | Admin; customer may cancel own Pending order |
| GET | `/api/reports/top-customers?days=30` | Admin |

Product listing query parameters: `search`, `minPrice`, `maxPrice`, `inStock`, `sortBy` (`name`, `price`, `stock`), `sortDirection` (`asc`, `desc`), `pageNumber`, `pageSize`.

## Business and consistency rules

- An order reserves all requested inventory within one SQL transaction. A failed line rolls back every reservation and the order.
- SQL Server `rowversion` is used for optimistic concurrency. Reservation updates compare the loaded token and available quantity in a conditional SQL `UPDATE`; a lost race returns 409.
- Order creation is idempotent per customer/key. The database unique index is the final duplicate guard; a request hash rejects reuse of a key with a different cart.
- The order creation transaction uses Serializable isolation for per-customer order-count/5th-order discount correctness under concurrent checkouts.
- Status transitions: Pending → Confirmed → Shipped; Pending/Confirmed → Cancelled. Shipped and Cancelled are terminal. Customers may only cancel their own Pending order.
- Cancellation changes status conditionally and restores inventory in the same transaction, preventing double restoration when API requests race the expiration worker.
- A hosted `BackgroundService` checks every minute and cancels Pending orders older than 30 minutes, restoring inventory transactionally.
- Discounts are calculated server-side: 5% if subtotal is above 10,000, plus 10% on every fifth customer order. Decimal values are rounded to two places using AwayFromZero.
- Product lists are cached in `IMemoryCache`; writes invalidate the versioned cache namespace.
- Product filtering, sorting, count and pagination execute in SQL Server (`IQueryable`).

## SQL reporting and generated SQL

All five requested SQL Server exercises are in [`QUERIES.sql`](QUERIES.sql). Query 1 uses Confirmed/Shipped orders created in the last `days` days.

`ReportService.GetTopCustomersAsync` implements query 1 as one EF Core aggregate query (`Where` → `GroupBy` → `Sum`/`Count` → `OrderBy` → `Take(5)` → `ToListAsync`). It does not load orders into memory and does not perform N+1 queries. The service logs `query.ToQueryString()` under the `Top-customers SQL` message on each report request. EF Core command logging is also enabled in `appsettings.json`.

Representative SQL generated for this LINQ expression (the runtime log is authoritative for the installed EF Core patch version):

```sql
DECLARE @__since_0 datetime2 = '...';

SELECT TOP(5)
    [o].[CustomerId],
    [u].[FullName] AS [CustomerName],
    [u].[Email],
    COALESCE(SUM([o].[Total]), 0.0) AS [TotalSpend],
    COUNT(*) AS [TotalOrders]
FROM [Orders] AS [o]
INNER JOIN [Users] AS [u] ON [o].[CustomerId] = [u].[Id]
WHERE [o].[CreatedAt] >= @__since_0
  AND [o].[Status] IN (N'Confirmed', N'Shipped')
GROUP BY [o].[CustomerId], [u].[FullName], [u].[Email]
ORDER BY COALESCE(SUM([o].[Total]), 0.0) DESC, [o].[CustomerId];
```

To capture the exact output for your SQL Server/EF Core version, run the API, call the report as an Admin and copy the `Top-customers SQL` log entry into this section.

## Error handling and security

- API exceptions return RFC 7807 ProblemDetails.
- Authentication and role authorization use JWT bearer tokens.
- Refresh tokens are stored as SHA-256 hashes and rotated on refresh.
- Login uses the built-in ASP.NET Core fixed-window rate limiter partitioned by client IP.
- Product create/update is Admin-only. Email and SKU have unique database indexes.
- Never enable sensitive-data logging in production.

## Build and validation

```bash
dotnet build OrderInventory.sln
dotnet test OrderInventory.sln
cd frontend && npm install && npm run build
```

SQL Server is required to validate migrations, rowversion behavior, locking, and integration scenarios. The repository's development environment should run the above commands before deployment. This workspace did not have the .NET SDK or SQL Server runtime available, so a successful .NET build or live SQL execution is not claimed here. npm package installation was also blocked by an unavailable npm registry connection in this environment; the edited TypeScript/TSX files passed syntax transpilation and a static check using temporary React type stubs, but a full Vite production build still needs to be run in an environment with npm dependencies installed.

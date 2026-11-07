# Technical Review — Order & Inventory Management

## Repository audit

The supplied project already had a layered .NET 8 solution, EF Core SQL Server mappings and migration, authentication, product CRUD/listing, order creation, idempotency persistence, and the five initial SQL exercises. The original README overstated some completion: the frontend folder, expiration worker and report endpoint were absent.

## Work completed in this revision

- Added a React + TypeScript + Vite client in `frontend/`.
- Added `GET /api/reports/top-customers?days=30` with one EF Core aggregate SQL query and runtime SQL logging via `ToQueryString()`.
- Added a hosted worker that cancels Pending orders older than 30 minutes and restores stock in a transaction.
- Changed status updates to conditional SQL updates, so only one concurrent cancellation can claim the transition and restore inventory.
- Restricted customer cancellation to their own Pending orders; Admin transitions retain the specified state machine.
- Serialized each customer's order-count/discount calculation with a per-customer SQL Server update lock, avoiding broad serializable locks on shared product rows.
- Kept conditional RowVersion/quantity checks for stock reservations and per-customer idempotency records.
- Reworked `QUERIES.sql` to make the running-revenue result include zero-revenue months and to average product quantities at the product/order grain.
- Updated setup, seed credentials, endpoints, frontend and validation documentation.

## Important implementation notes

- Product `RowVersion` is SQL Server `rowversion`. EF Core `ExecuteUpdate` reservation predicates compare the originally loaded token and stock quantity; a zero-row update is reported as HTTP 409.
- A transaction encloses all reservations, order rows and the idempotency record. A failed product reservation rolls back all stock changes.
- Customer order creation uses a customer-row `UPDLOCK` during order-count allocation. Different customers can still contend on the same product through the optimistic RowVersion update.
- API and expiration-worker cancellation both conditionally change Pending/Confirmed to Cancelled and restore each order's item quantities within the same transaction.
- Product cache invalidation is triggered by product mutations, order reservations, manual cancellation and automatic cancellation.

## Remaining environment validation

The current execution environment did not contain the .NET SDK or SQL Server, and npm dependency installation could not complete within the available execution window. Therefore no successful `dotnet build`, migration run, browser test, SQL execution or concurrent integration test is claimed. Before production use, run the commands in README on a machine with .NET 8, SQL Server and Node.js, then add/run SQL Server integration tests for last-unit contention, same-key replay, cancellation races, and fifth-order discount races.

The README includes the SQL shape emitted by the report LINQ query and the service logs the exact `ToQueryString()` output at runtime. Copy the exact runtime log into README after executing the endpoint against your EF Core patch version.

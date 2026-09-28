# Supply — Full-Stack E-Commerce Application

> A production-oriented e-commerce application built with **Angular 22**, **ASP.NET Core Web API (.NET 10)**, **Entity Framework Core**, **SQL Server**, and **Stripe**.

\

Supply started as an **Order Management API** and has grown into a complete shopping application with a customer storefront, admin dashboard, authentication, payments, inventory management, concurrency protection, and automated backend testing.

The Angular frontend communicates with the real ASP.NET Core backend, while integration tests replace Stripe with a fake payment service so tests never call the real Stripe API.

**Current focus:** Docker, Docker Compose, CI/CD, deployment configuration, monitoring, and payment-lifecycle hardening.

---

## ✨ Project Highlights

- Full-stack **Angular + ASP.NET Core** architecture
- RESTful API with clear DTO contracts
- ASP.NET Core Identity + JWT authentication
- Customer and Admin role authorization
- Product catalog with filtering, sorting, search, and pagination
- Customer basket and shipping-address management
- Transactional checkout and inventory reduction
- Stripe Payment Element integration
- Signed Stripe webhook processing
- Order and payment state separation
- Optimistic concurrency using SQL Server `RowVersion`
- Historical product and shipping-address snapshots
- Admin dashboard for products, orders, customers, stock, and metrics
- Centralized RFC-style `ProblemDetails`
- Unit and integration tests
- Fake Stripe implementation for automated testing
- Concurrent-checkout test coverage
- Current DevOps phase: Docker → CI/CD → deployment

---

# 🧰 Tech Stack

| Area                    | Technologies                                                      |
| ----------------------- | ----------------------------------------------------------------- |
| **Backend**             | C#, .NET 10, ASP.NET Core Web API                                 |
| **Frontend**            | Angular 22, TypeScript, SCSS, Signals, Reactive Forms             |
| **Database**            | SQL Server, Entity Framework Core 10, Migrations                  |
| **Authentication**      | ASP.NET Core Identity, JWT Bearer Authentication                  |
| **Authorization**       | Customer/Admin roles, resource ownership checks                   |
| **Payments**            | Stripe.net, Stripe.js, Payment Intents, Payment Element, Webhooks |
| **API Design**          | REST, DTOs, validation, pagination, OpenAPI                       |
| **Error Handling**      | Centralized `ProblemDetails` responses                            |
| **Testing**             | xUnit, `WebApplicationFactory`, SQL Server integration tests      |
| **Frontend Testing**    | Vitest                                                            |
| **Concurrency**         | SQL Server `RowVersion`, optimistic concurrency                   |
| **Development**         | Git, GitHub, .NET User Secrets, Angular proxy                     |
| **Current DevOps Work** | Docker, Docker Compose, CI/CD, deployment                         |

---

# 🛍️ Customer Storefront

The customer-facing Angular application provides a complete shopping flow.

### Catalog

- Responsive branded storefront
- Product listing
- Product detail pages
- Product images
- Search
- Category filtering
- Minimum and maximum price filters
- In-stock filtering
- Sorting
- Pagination

### Authentication

- Customer registration
- Login
- JWT session handling
- Protected customer routes
- Customer identity resolved from the authenticated user

### Basket

Customers can:

- Add products
- Increase or decrease quantities
- Remove products
- Clear the basket
- View current prices
- View available stock
- View calculated line totals
- View the basket total

Adding a product to the basket **does not reserve inventory**.

Stock is validated again during checkout.

### Shipping Addresses

Customers can:

- Create addresses
- Edit addresses
- Delete addresses
- Select a default address

Business rules include:

- The first address automatically becomes the default
- Only one address can be default
- Deleting the default address promotes another address when possible
- Customers cannot access addresses owned by another customer

### Orders

Customers can:

- View paginated order history
- View individual order details
- Cancel eligible pending orders

Orders preserve historical snapshots of:

- Product name
- SKU
- Quantity
- Unit price
- Shipping recipient
- Address
- City
- Province
- Postal code
- Country

Later product or address changes therefore **do not rewrite order history**.

---

# 💳 Checkout & Stripe Payments

Checkout is handled by the backend through:

```http
POST /api/me/checkout
Authorization: Bearer <access-token>
Content-Type: application/json
```

Example:

```json
{
  "addressId": 6
}
```

The browser only supplies the selected address.

The backend determines:

- Authenticated customer
- Basket contents
- Product prices
- Product availability
- Inventory
- Order total
- Checkout eligibility

---

## Checkout Lifecycle

```text
Customer
   │
   ▼
Angular Checkout
   │
   ▼
POST /api/me/checkout
   │
   ▼
ASP.NET Core Checkout Service
   │
   ├── Validate customer
   ├── Validate address ownership
   ├── Validate basket
   ├── Validate stock
   ├── Create order snapshots
   ├── Reduce inventory
   │
   ▼
SQL Transaction Commit
   │
   ▼
Stripe Payment Intent
   │
   ▼
Client Secret
   │
   ▼
Stripe Payment Element
   │
   ▼
Payment Confirmation
   │
   ▼
Stripe Webhook
   │
   ├── payment_intent.succeeded
   ├── payment_intent.payment_failed
   └── payment_intent.canceled
```

---

## Checkout Behavior

For a new checkout, the backend:

1. Resolves the authenticated customer.
2. Verifies that the customer is active.
3. Checks whether an unfinished checkout can be resumed.
4. Validates address ownership.
5. Validates basket contents.
6. Re-checks current product availability and stock.
7. Creates the order.
8. Creates product and shipping-address snapshots.
9. Calculates the total on the server.
10. Reduces inventory inside a SQL transaction.
11. Commits the SQL transaction.
12. Creates or retrieves the Stripe Payment Intent.
13. Returns the order and Stripe `clientSecret`.
14. Angular renders Stripe's Payment Element.
15. Stripe.js confirms the payment.
16. Stripe sends a signed webhook to the backend.
17. The backend updates the payment state.

---

## Important Basket Behavior

Creating an order **does not clear the basket immediately**.

The basket is cleared only after the backend receives:

```text
payment_intent.succeeded
```

This prevents checkout creation alone from being treated as a completed purchase.

---

## Stripe Integration

Payment Intents:

- Use **CAD**
- Convert order totals to cents
- Use automatic payment methods
- Store the associated order ID in Stripe metadata

Stripe requests use an order-based idempotency key:

```text
supply-order-{order.Id}
```

Existing Payment Intents can also be retrieved when resuming a checkout.

---

## Stripe Webhooks

The webhook endpoint validates the:

```text
Stripe-Signature
```

Supported payment events currently include:

### `payment_intent.succeeded`

- Marks payment as successful
- Records `PaidAt`
- Clears the customer's basket

### `payment_intent.payment_failed`

- Updates the payment status
- Keeps the basket

### `payment_intent.canceled`

- Updates the payment status
- Keeps the basket

Repeated success notifications for an already-successful order are ignored by the success handler.

---

# 📦 Order Lifecycle

Business order status and Stripe payment status are intentionally separate concepts.

The normal order lifecycle is:

```text
Pending
   │
   ▼
Processing
   │
   ▼
Shipped
   │
   ▼
Completed
```

A pending order can also transition to:

```text
Cancelled
```

Invalid transitions are rejected.

A successful Stripe payment **does not automatically change the business order to `Processing`**.

This keeps payment processing separate from fulfillment workflow.

---

# 🛡️ Authentication & Authorization

Supply uses **ASP.NET Core Identity** for users and roles.

Two primary roles exist:

```text
Customer
Admin
```

Login returns a JWT access token.

Customer routes use:

```text
/api/me/...
```

Instead of trusting a customer ID supplied by the browser, the backend derives the customer from the authenticated Identity user.

This prevents users from changing identifiers in requests to access another customer's data.

Ownership checks protect:

- Orders
- Addresses
- Baskets

Administrative operations require:

```csharp
Admin
```

role authorization.

Angular route guards improve navigation UX, while the backend remains responsible for actual authorization enforcement.

---

# 🧑‍💼 Admin Dashboard

The Angular application contains a protected admin area:

```text
/admin
```

The dashboard is protected by both:

- Angular admin route guards
- Backend role authorization

---

## Dashboard Overview

The admin dashboard currently displays:

- Total orders
- Order-value metrics
- Average order value
- Active products
- Inactive products
- Low-stock products
- Monthly revenue-style metrics
- Orders grouped by status
- Recent orders

> Current revenue calculations represent **non-cancelled order value**, including unpaid orders. They are not yet equivalent to settled Stripe revenue.

---

## Product Administration

Administrators can:

- View products
- Filter active/inactive products
- Create products
- Edit products
- Change SKU
- Change price
- Change category
- Change stock
- Activate/deactivate products

Product and stock changes use `RowVersion` values to detect stale writes.

---

## Order Administration

Administrators can:

- View paginated orders
- View order details
- Change valid order statuses
- Cancel pending orders

Order cancellation restores inventory.

---

# 🔐 Concurrency & Data Integrity

The project uses several mechanisms to protect data integrity.

### Optimistic Concurrency

Products use SQL Server:

```csharp
RowVersion
```

When two clients attempt to update stale versions of the same product, the backend detects the conflict instead of silently overwriting newer data.

---

## Database Constraints

The schema includes:

- Unique product SKUs
- Unique customer emails
- Unique category names
- Unique category slugs
- Unique filtered Stripe Payment Intent IDs
- One basket per customer
- One basket-item row per product
- Explicit decimal precision for money
- Foreign-key constraints
- Explicit delete behavior

---

## Transactions

Order creation and inventory reduction run inside a SQL transaction.

This prevents partially-created orders where inventory and order data become inconsistent.

---

# 🧱 Architecture

The backend follows a service-based architecture:

```text
Angular Client
      │
      ▼
Controllers
      │
      ▼
Services
      │
      ▼
Entity Framework Core
      │
      ▼
SQL Server
```

Controllers handle HTTP concerns.

Services contain business rules.

Entity Framework Core handles persistence.

DTOs define the public API contracts.

---

## Stripe Abstraction

Payment operations are abstracted behind:

```csharp
IStripePaymentService
```

Production uses:

```csharp
StripePaymentService
```

Integration tests use:

```csharp
FakeStripePaymentService
```

This allows checkout integration tests to exercise the application's payment workflow **without making real Stripe API calls**.

---

# 📁 Project Structure

```text
SupplyECommerce/
│
├── Controllers/
│   ├── Public endpoints
│   ├── Customer endpoints
│   ├── Admin endpoints
│   └── Stripe webhook endpoint
│
├── Services/
│   ├── Authentication
│   ├── Products
│   ├── Categories
│   ├── Basket
│   ├── Addresses
│   ├── Orders
│   ├── Checkout
│   └── Stripe
│
├── Dtos/
│   └── Request, response and pagination contracts
│
├── Models/
│   └── Domain entities and status enums
│
├── Data/
│   ├── EF Core DbContext
│   ├── Model configuration
│   └── Identity seeding
│
├── Migrations/
│   └── EF Core database migrations
│
├── Exceptions/
│   └── Application exceptions and global error handling
│
├── Options/
│   └── JWT configuration
│
├── scripts/
│   └── seed-demo-products.sql
│
├── OrderManagementApi.UnitTests/
│   └── Unit tests
│
├── OrderManagementApi.IntegrationTests/
│   └── API integration tests
│
└── ECommerceWeb/
    └── Angular storefront and admin dashboard
```

The backend project and test assemblies retain the original `OrderManagementApi` naming.

The solution file is:

```text
ECommerceApi.slnx
```

---

# 🌐 API Overview

| Area            | Main Routes                                       | Access           |
| --------------- | ------------------------------------------------- | ---------------- |
| Authentication  | `POST /api/auth/register`, `POST /api/auth/login` | Public           |
| Products        | `GET /api/products`, `GET /api/products/{id}`     | Public           |
| Categories      | `GET /api/categories`                             | Public           |
| Addresses       | `/api/me/addresses`                               | Customer         |
| Basket          | `/api/me/basket`                                  | Customer         |
| Checkout        | `POST /api/me/checkout`                           | Customer         |
| Customer Orders | `/api/me/orders`                                  | Customer         |
| Admin Products  | `/api/products/admin`, product write endpoints    | Admin            |
| Admin Orders    | `/api/orders`                                     | Admin            |
| Admin Customers | `/api/customers`                                  | Admin            |
| Dashboard       | `/api/admin/dashboard/...`                        | Admin            |
| Stripe Webhook  | `POST /api/webhooks/stripe`                       | Stripe Signature |

---

## Product Querying

The public product endpoint supports:

```text
search
category
minPrice
maxPrice
inStock
sortBy
sortDirection
page
pageSize
```

Example:

```http
GET /api/products?category=keyboards&inStock=true&sortBy=price&sortDirection=asc&page=1&pageSize=10
```

---

## Pagination

Paged responses follow this structure:

```json
{
  "items": [],
  "currentPage": 1,
  "pageSize": 10,
  "totalCount": 0,
  "totalPages": 0
}
```

---

# ⚠️ Error Handling

Application errors are converted into centralized `ProblemDetails` responses.

Handled scenarios include:

- Missing products
- Missing customers
- Missing orders
- Missing addresses
- Missing basket items
- Invalid requests
- Insufficient stock
- Duplicate SKUs
- Concurrency conflicts
- Unauthorized access
- Forbidden admin operations

Responses also contain a trace identifier to assist debugging.

---

# 🧪 Testing

## Backend Unit Tests

From the repository root:

```bash
dotnet test OrderManagementApi.UnitTests/OrderManagementApi.UnitTests.csproj
```

Current unit tests cover business rules including:

- Basket quantity validation
- Order-status transitions
- Order cancellation eligibility

---

## Backend Integration Tests

```bash
dotnet test OrderManagementApi.IntegrationTests/OrderManagementApi.IntegrationTests.csproj
```

Integration tests use:

- `WebApplicationFactory<Program>`
- `Testing` environment
- Real SQL Server test database
- EF Core migrations
- Fake Stripe payment service

The current test database configuration is:

```text
Server=localhost;
Database=OrderManagementEfCoreDb_Test;
Trusted_Connection=True;
TrustServerCertificate=True;
```

Keep the test database separate from application data.

---

## Integration Test Coverage

Current integration tests cover:

- Public product visibility
- Missing-product `ProblemDetails`
- Unauthorized admin access
- Forbidden admin access
- Successful admin authorization
- Invalid login credentials
- Successful checkout
- Inventory reduction
- Basket retention before successful payment
- Empty basket checkout
- Address ownership validation
- Stock changes before checkout
- Customer order ownership
- Cancellation and stock restoration
- Valid order transitions
- Invalid order transitions
- Stale `RowVersion` conflicts
- Concurrent checkouts competing for the final unit of stock

Signed-webhook lifecycle tests and additional payment edge cases are planned.

---

## Frontend Tests

From:

```text
ECommerceWeb/
```

Run:

```bash
npm run build
npm test -- --watch=false
```

Vitest is configured with an initial application test.

Broader storefront, admin, and payment-flow test coverage is planned.

---

# 🚀 Running Locally

## Prerequisites

Install:

- .NET 10 SDK
- SQL Server
- EF Core CLI compatible with EF Core 10
- Node.js
- npm
- Stripe test account
- Stripe CLI

The Angular lockfile currently specifies Node:

```text
^22.22.3 || ^24.15.0 || >=26.0.0
```

---

## 1. Clone the Repository

```bash
git clone https://github.com/parham78/SupplyECommerce.git
cd SupplyECommerce
dotnet restore ECommerceApi.slnx
```

---

## 2. Configure Backend Secrets

Use .NET User Secrets.

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your SQL Server connection string>"

dotnet user-secrets set "Jwt:Key" "<your random signing key of at least 32 bytes>"

dotnet user-secrets set "Jwt:Issuer" "OrderManagementApi"

dotnet user-secrets set "Jwt:Audience" "OrderManagementApi"

dotnet user-secrets set "Jwt:ExpirationMinutes" "30"

dotnet user-secrets set "Stripe:SecretKey" "<your Stripe test secret key>"
```

---

## Optional Admin Bootstrap

```bash
dotnet user-secrets set "Admin:Email" "<your admin email>"

dotnet user-secrets set "Admin:Password" "<your strong admin password>"
```

Never commit:

- JWT signing keys
- Stripe secret keys
- Stripe webhook secrets
- Admin passwords
- Production database credentials

---

## 3. Prepare SQL Server

Apply migrations:

```bash
dotnet ef database update --project OrderManagementApi.csproj
```

You can optionally run:

```text
scripts/seed-demo-products.sql
```

to create demo categories and products.

---

## 4. Configure Stripe Webhooks

Authenticate:

```bash
stripe login
```

Start local forwarding:

```bash
stripe listen --forward-to http://localhost:5289/api/webhooks/stripe
```

Stripe will display a signing secret beginning with:

```text
whsec_
```

Save it:

```bash
dotnet user-secrets set "Stripe:WebhookSecret" "<your whsec_ value>"
```

Restart the API whenever the webhook secret changes.

---

## 5. Start the API

```bash
dotnet run --project OrderManagementApi.csproj --launch-profile http
```

Backend:

```text
http://localhost:5289
```

OpenAPI:

```text
http://localhost:5289/openapi/v1.json
```

The project currently exposes the OpenAPI document but does not configure Swagger UI.

---

## 6. Start Angular

```bash
cd ECommerceWeb
npm ci
npm start
```

Open:

```text
http://localhost:4200
```

Angular proxies:

```text
/api
```

to:

```text
http://localhost:5289
```

through:

```text
src/proxy.conf.json
```

---

# 💰 Current Payment Boundaries

The Stripe integration is functional, but several production-hardening improvements remain.

### Inventory Reservation

Inventory is reduced when the order is created, **before payment succeeds**.

Failed or abandoned payments do not yet automatically release reserved inventory.

### Cancellation & Refunds

Cancelling an order currently restores inventory.

It does **not yet automatically cancel or refund an associated Stripe payment**.

### Checkout Resumption

Checkout resumption currently finds the customer's unfinished order.

It does not yet compare the order against a basket-version snapshot.

### Basket Clearing

Successful-payment handling clears the customer's current basket.

It does not yet compare basket contents against a checkout snapshot.

### Additional Hardening

Future work includes:

- Parallel duplicate checkout protection
- Webhook ordering safeguards
- Payment reconciliation
- Abandoned checkout cleanup
- Inventory release
- Payment-aware cancellation
- Refund handling
- Expanded webhook tests

---

# 🗺️ Roadmap

## Current Phase — DevOps & Deployment

The next phase focuses on:

- [ ] Dockerize the ASP.NET Core API
- [ ] Dockerize the Angular frontend
- [ ] Add Docker Compose
- [ ] Containerize SQL Server for local development
- [ ] Add automated CI builds
- [ ] Run backend tests in CI
- [ ] Run frontend builds/tests in CI
- [ ] Add deployment configuration
- [ ] Configure environment-specific secrets
- [ ] Publicly deploy the application
- [ ] Add health checks
- [ ] Add structured logging
- [ ] Add application monitoring

---

## Payment Hardening

- [ ] Automatic release of inventory from abandoned payments
- [ ] Stripe cancellation/refund integration
- [ ] Webhook reconciliation
- [ ] Stronger duplicate-checkout protection
- [ ] Payment-aware cancellation
- [ ] Payment-confirmation UX
- [ ] Signed webhook lifecycle integration tests

---

## Possible Later Features

- [ ] Refresh tokens
- [ ] Email confirmation
- [ ] Password reset
- [ ] Product image upload/management
- [ ] Product reviews
- [ ] Redis caching
- [ ] Background processing
- [ ] Additional observability
- [ ] Expanded frontend automated tests

---

# 🎯 Project Goal

The current goal is to extend that application-development knowledge into **Docker, CI/CD, cloud deployment, monitoring, and production operations**.

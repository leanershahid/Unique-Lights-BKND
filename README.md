# Unique Lights API

ASP.NET Core 9 Web API for the Unique Lights lighting store. PostgreSQL (Supabase) via EF Core 9 + Npgsql,
JWT access tokens + rotating httpOnly refresh cookie, Supabase Storage for product images.

The HTTP contract is defined in [`../docs/API.md`](../docs/API.md) and is the single source of truth shared with the React frontend.

## Project layout

```
backend/
├── dotnet-tools.json            # local dotnet-ef 9.x
└── UniqueLights.Api/
    ├── Controllers/ (+ Admin/)  # thin controllers, DTOs only, ApiResponse envelope
    ├── Services/ (+ Admin/, Common/)  # business logic (interfaces + implementations)
    ├── Repositories/            # ProductRepository (catalogue queries/projections), OrderRepository, CartRepository
    ├── DTOs/                    # request/response contracts grouped by feature
    ├── Entities/                # EF Core entities
    ├── Data/                    # AppDbContext, AuditInterceptor, DesignTimeDbContextFactory, DbSeeder, Migrations/
    ├── Configurations/          # IEntityTypeConfiguration<T> per entity + options classes
    ├── Middleware/              # global exception -> envelope middleware
    ├── Validators/              # FluentValidation validators + MVC action filter
    ├── Extensions/              # DI registration, ClaimsPrincipal helpers, .env loader, rate limiting
    ├── Authentication/          # JWT, refresh token, password hashing, role constants
    └── Exceptions/              # NotFound/Conflict/BusinessRule/Forbidden/Validation/ServiceUnavailable
```

Services use `AppDbContext` as the unit of work; repositories hold the reusable/complex queries
(catalogue filtering + projections, order loading, cart projections).

## Configuration

`appsettings.json` only contains non-secret defaults. Secrets come from environment variables
(`__` separates sections), .NET user-secrets, or a local `.env` file (copied from `.env.example`, git-ignored,
loaded at startup; real environment variables win).

| Key | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string (**required**) |
| `Jwt__Secret` | HMAC signing key, ≥ 32 chars (**required**) |
| `Jwt__Issuer`, `Jwt__Audience`, `Jwt__AccessTokenMinutes` (15), `Jwt__RefreshTokenDays` (7) | Token settings |
| `Supabase__Url`, `Supabase__ServiceKey`, `Supabase__Bucket` (`product-images`) | Image storage. Without them, image upload returns 503 "Image storage is not configured" (adding images by URL still works). |
| `Commerce__*` | Currency, tax rate (0.18), free shipping threshold (2000), shipping fees, ETA days |
| `Cors__AllowedOrigins__0..n` | Frontend origins (credentials allowed). Default `http://localhost:5173` |
| `RefreshCookie__SameSite` / `RefreshCookie__Secure` | Default `Lax` / `false` for local dev. **Production with the SPA on a different site than the API needs `None` + `true` (HTTPS).** |
| `Seed__Enabled` (true), `Seed__AdminEmail`, `Seed__AdminPassword` | Seeding; admin user is created only when both admin values are set |
| `Database__MigrateOnStartup` | Apply pending migrations at startup. Default: `true` in Development, `false` otherwise |
| `ForwardedHeaders__Enabled` | Set `true` behind a reverse proxy so client IPs / scheme are correct |

The app fails fast at startup with a clear message if the connection string is empty or `Jwt:Secret` is missing / shorter than 32 characters.

Using user-secrets instead of `.env`:

```bash
cd backend/UniqueLights.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=...;Port=5432;..."
dotnet user-secrets set "Jwt:Secret" "<long random value>"
dotnet user-secrets set "Seed:AdminEmail" "admin@yourdomain.com"
dotnet user-secrets set "Seed:AdminPassword" "<strong password>"
```

## Supabase setup

1. **Database connection string** – Supabase Dashboard → *Project Settings → Database → Connection string*.
   Use the **Session pooler** (port **5432**) or the direct connection for migrations and for the API
   (EF migrations need a session-level connection; the transaction pooler on port 6543 is not suitable for migrations).
   Convert the URI into Npgsql key/value form, e.g.
   `Host=aws-0-ap-south-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<db-password>;SSL Mode=Require;Trust Server Certificate=true`
2. **Storage** – run [`../database/supabase-storage.sql`](../database/supabase-storage.sql) in the SQL editor. It creates the
   public `product-images` bucket (5 MB, jpg/png/webp) with public read access; writes are only possible with the service role key.
3. **Service role key** – *Project Settings → API → service_role*. Put it in `Supabase__ServiceKey` (server-side only; never ship it to the browser).
   `Supabase__Url` is the project URL (`https://<project-ref>.supabase.co`).

## Database / migrations

The tool is pinned locally (`backend/dotnet-tools.json`):

```bash
cd backend
dotnet tool restore

cd UniqueLights.Api
# apply migrations to the configured database (needs ConnectionStrings__DefaultConnection)
dotnet tool run dotnet-ef database update

# add a migration after model changes
dotnet tool run dotnet-ef migrations add <Name> -o Data/Migrations

# regenerate the idempotent SQL script (offline, no database needed)
dotnet tool run dotnet-ef migrations script --idempotent -o ../../database/schema.sql
```

`DesignTimeDbContextFactory` uses a placeholder connection string when none is configured, so `migrations add` /
`migrations script` work without a database. Alternatively apply `database/schema.sql` directly in the Supabase SQL editor.

Schema notes: snake_case names; Guid (v7) ids generated in the app; `created_at`/`updated_at` timestamptz set by an
interceptor; soft delete (`is_deleted`/`deleted_at`) with global query filters for users, categories, products,
variants, addresses, coupons and reviews; unique indexes on slugs/SKUs/codes are filtered on `is_deleted = false`;
order numbers come from the `order_number_seq` sequence (`UL-10001`, `UL-10002`, …); inventory uses PostgreSQL `xmin`
as an optimistic concurrency token so concurrent checkouts cannot oversell.

## Running

```bash
cd backend/UniqueLights.Api
cp .env.example .env      # then fill in real values
dotnet run                # http://localhost:5273 (launchSettings "http" profile)
```

- Swagger UI (Development only): `http://localhost:5273/swagger` – click *Authorize* and paste an access token.
- Health check: `GET /health` (includes a database check).
- Logs: console + rolling files in `UniqueLights.Api/logs/` (14 days).

On startup the app applies pending migrations (if `Database:MigrateOnStartup`) and runs the seeder (if `Seed:Enabled`).

## Seeding

The seeder is idempotent:

- Roles `Customer` and `Admin` (fixed ids, also in the migration via `HasData`).
- Admin user from `Seed:AdminEmail` / `Seed:AdminPassword` (only when both are set; gets Admin + Customer roles; an existing user's password is never overwritten).
- Categories (Pendant Lights, Chandeliers, Wall Lights, Floor Lamps, Table Lamps, Ceiling Lights, Outdoor Lights, Smart Bulbs) – upserted by slug.
- 14 demo products with variants, inventory and images – **only when the products table is empty**.
  Image URLs are relative (`/images/...`) and are served by the frontend.
- Coupons `WELCOME10` (10 % up to ₹1,000, min ₹1,500) and `FESTIVE500` (₹500 off, min ₹5,000).

No customers, orders or reviews are seeded. To create an admin later, set the two `Seed__Admin*` values and restart.

## API overview

All responses use the envelope `{ success, data, message, errors, traceId? }` (see API.md). Validation errors are
`400` with camelCase `errors` keys; `401/403/404/409/422/429/503` all use the same envelope.

| Area | Endpoints |
|---|---|
| Auth | `POST /api/auth/register`, `login`, `refresh`, `logout`, `GET /api/auth/me` (login/register rate limited 10/min/IP, refresh 30/min/IP) |
| Customer | `/api/customers/me` profile, password, addresses (CRUD + default) |
| Catalogue | `/api/categories`, `/api/products` (search, filters, sort, paging), `/filters`, `/suggest`, `/{slug}`, `/{slug}/related` |
| Reviews | `/api/reviews/product/{id}` (+`/summary`), `POST /api/reviews`, `DELETE /api/reviews/{id}` |
| Cart / wishlist | `/api/cart` (items, merge, clear), `/api/wishlist` |
| Checkout | `POST /api/coupons/validate`, `POST /api/orders/quote`, `POST /api/orders`, `GET /api/orders`, `GET /api/orders/{orderNumber}`, `POST /api/orders/{orderNumber}/cancel` |
| Admin | `/api/admin/dashboard`, `products` (+ variants, images), `categories`, `orders` (+ status), `customers` (+ status), `inventory`, `coupons` – role `Admin` required |

### Payments

`IPaymentGateway` is implemented by `DevelopmentPaymentGateway` – a **stub that moves no money**:
Cash on Delivery → payment `Pending` (marked `Paid` when the order is delivered); Card / UPI → `Paid` immediately with a
`DEV-xxxxxxxxxxxx` reference. Replace the registration in `Extensions/ServiceCollectionExtensions.cs` with a real
gateway (Razorpay, Stripe, …) before taking real orders.

## Deploying to Render

The repository includes a `Dockerfile` and a `render.yaml` Blueprint.

1. Render dashboard → **New → Blueprint** → connect GitHub and pick this repository. Render reads `render.yaml` and creates the `unique-lights-api` web service (Docker, Singapore region, health check `/health`).
2. Fill in the three secret values Render asks for:
   - `ConnectionStrings__DefaultConnection`: Supabase session-pooler connection string, e.g.
     `Host=aws-0-ap-south-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<db-password>;SSL Mode=Require`
   - `Supabase__ServiceKey`: a Supabase secret key (`sb_secret_...`) or the legacy `service_role` key.
   - `Cors__AllowedOrigins__0`: the website's URL, for example `https://your-site.vercel.app` (no trailing slash). Add more origins as `Cors__AllowedOrigins__1`, `__2`, …
3. **Apply**. The first build takes a few minutes. When it is live, `https://<service>.onrender.com/health` returns `Healthy`.

`Jwt__Secret` is generated by Render. Changing it signs everyone out. Production uses `RefreshCookie__SameSite=None` and `RefreshCookie__Secure=true` so sign-in works when the website is on another domain, and `ForwardedHeaders__Enabled=true` so client IPs and HTTPS are read correctly behind Render's proxy.

On the free plan the service sleeps after 15 minutes without traffic; the first request after that takes about a minute.

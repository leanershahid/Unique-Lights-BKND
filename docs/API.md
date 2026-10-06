# Unique Lights API contract

This document is the single source of truth shared by the backend (`backend/UniqueLights.Api`) and the frontend (`frontend/unique-lights-web`). JSON is camelCase. Enums are serialized as strings. Dates are ISO-8601 UTC strings. Money is a JSON number with two decimals (backed by `numeric(12,2)`). IDs are UUID strings.

## Envelope

Every response (success and error) uses the same envelope:

```ts
interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  message: string | null;          // human-readable summary
  errors: Record<string, string[]> | null; // validation errors keyed by camelCase field
  traceId?: string | null;         // present on errors
}

interface PagedResult<T> {
  items: T[];
  page: number;        // 1-based
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
```

Status codes: `200` OK, `201` Created (with `data`), `204` is NOT used (return `200` with `data: null` instead), `400` validation (with `errors`), `401` unauthenticated, `403` forbidden, `404` not found, `409` conflict (duplicate email/slug/sku, insufficient stock), `422` business rule failure (e.g. coupon invalid), `500` unexpected.

Paging query params everywhere: `page` (default 1), `pageSize` (default 12, max 100).

## Auth

Access token: JWT (Bearer), 15 minutes. Claims: `sub` (user id), `email`, `name`, `role` (one per role). Refresh token: random 64 bytes, stored **hashed** in `refresh_tokens`, delivered as an **httpOnly cookie** named `ul_refresh` (path `/api/auth`, 7 days, `SameSite` and `Secure` configurable). Refresh rotates the token.

Roles: `Customer`, `Admin`.

```ts
interface UserDto { id: string; email: string; firstName: string; lastName: string; phone: string | null; roles: string[]; createdAt: string; }
interface AuthResponse { accessToken: string; expiresAt: string; user: UserDto; }
interface RegisterRequest { email: string; password: string; firstName: string; lastName: string; phone?: string | null; }
interface LoginRequest { email: string; password: string; }
```

| Method | Path | Auth | Body | Data |
|---|---|---|---|---|
| POST | /api/auth/register | – | RegisterRequest | AuthResponse (201) |
| POST | /api/auth/login | – | LoginRequest | AuthResponse |
| POST | /api/auth/refresh | cookie | – | AuthResponse |
| POST | /api/auth/logout | cookie | – | null (revokes + clears cookie) |
| GET | /api/auth/me | Bearer | – | UserDto |

Password rules: min 8 chars, at least one letter and one digit.

## Customers (self-service)

```ts
interface UpdateProfileRequest { firstName: string; lastName: string; phone?: string | null; }
interface ChangePasswordRequest { currentPassword: string; newPassword: string; }
interface AddressDto { id: string; label: string | null; fullName: string; phone: string; line1: string; line2: string | null; city: string; state: string; postalCode: string; country: string; isDefault: boolean; }
interface AddressRequest { label?: string | null; fullName: string; phone: string; line1: string; line2?: string | null; city: string; state: string; postalCode: string; country: string; isDefault: boolean; }
```

| Method | Path | Body | Data |
|---|---|---|---|
| GET | /api/customers/me | – | UserDto |
| PUT | /api/customers/me | UpdateProfileRequest | UserDto |
| PUT | /api/customers/me/password | ChangePasswordRequest | null |
| GET | /api/customers/me/addresses | – | AddressDto[] |
| POST | /api/customers/me/addresses | AddressRequest | AddressDto (201) |
| PUT | /api/customers/me/addresses/{id} | AddressRequest | AddressDto |
| PATCH | /api/customers/me/addresses/{id}/default | – | AddressDto |
| DELETE | /api/customers/me/addresses/{id} | – | null (soft delete) |

All require Bearer.

## Categories (public)

```ts
interface CategoryDto { id: string; name: string; slug: string; description: string | null; imageUrl: string | null; parentId: string | null; sortOrder: number; productCount: number; children: CategoryDto[]; }
```

| Method | Path | Data |
|---|---|---|
| GET | /api/categories | CategoryDto[] (tree: top-level with children, active only) |
| GET | /api/categories/{slug} | CategoryDto |

## Products (public)

```ts
type ProductSort = "featured" | "newest" | "price_asc" | "price_desc" | "rating" | "name";

interface ProductImageDto { id: string; url: string; altText: string | null; sortOrder: number; isPrimary: boolean; }

interface ProductListItemDto {
  id: string; name: string; slug: string; brand: string | null; shortDescription: string | null;
  price: number;                 // default variant price
  compareAtPrice: number | null; // original price when discounted
  discountPercent: number | null;// integer 1-99 when compareAtPrice > price
  primaryImage: ProductImageDto | null;
  secondaryImage: ProductImageDto | null;
  categoryName: string; categorySlug: string;
  averageRating: number; reviewCount: number;
  inStock: boolean; isNew: boolean; isFeatured: boolean;
  defaultVariantId: string | null;
}

interface ProductVariantDto {
  id: string; sku: string; name: string;
  color: string | null; finish: string | null; colorTemperature: string | null; wattage: number | null;
  price: number; compareAtPrice: number | null;
  stock: number;        // available = onHand - reserved, never negative
  inStock: boolean; isDefault: boolean;
}

interface ProductDetailDto extends ProductListItemDto {
  sku: string; description: string | null;
  specifications: Record<string, string>;   // e.g. { "Wattage": "12 W", "Lumens": "1100 lm" }
  images: ProductImageDto[];
  variants: ProductVariantDto[];
  category: { id: string; name: string; slug: string };
}

interface ProductFiltersDto { brands: string[]; finishes: string[]; colorTemperatures: string[]; minPrice: number; maxPrice: number; }

interface SearchSuggestionsDto { products: ProductListItemDto[]; categories: { id: string; name: string; slug: string }[]; }
```

| Method | Path | Query | Data |
|---|---|---|---|
| GET | /api/products | search, category (slug, includes children), minPrice, maxPrice, brands (comma list), finishes (comma list), colorTemperatures (comma list), inStock (bool), featured (bool), isNew (bool), sort (ProductSort, default featured), page, pageSize | PagedResult<ProductListItemDto> |
| GET | /api/products/filters | category | ProductFiltersDto |
| GET | /api/products/suggest | q (min 2 chars) | SearchSuggestionsDto (max 6 products, 4 categories) |
| GET | /api/products/{slug} | – | ProductDetailDto |
| GET | /api/products/{slug}/related | – | ProductListItemDto[] (max 8, same category) |

Only active, non-deleted products in active categories are public.

## Reviews

```ts
interface ReviewDto { id: string; productId: string; rating: number; title: string | null; comment: string | null; authorName: string; isVerifiedPurchase: boolean; createdAt: string; isMine: boolean; }
interface ReviewSummaryDto { averageRating: number; reviewCount: number; distribution: Record<"1"|"2"|"3"|"4"|"5", number>; }
interface CreateReviewRequest { productId: string; rating: number; title?: string | null; comment?: string | null; }
```

| Method | Path | Auth | Data |
|---|---|---|---|
| GET | /api/reviews/product/{productId}?page&pageSize | optional | PagedResult<ReviewDto> |
| GET | /api/reviews/product/{productId}/summary | – | ReviewSummaryDto |
| POST | /api/reviews | Bearer | ReviewDto (201). 409 if user already reviewed product. rating 1–5, title ≤ 120, comment ≤ 2000 |
| DELETE | /api/reviews/{id} | Bearer (owner or Admin) | null |

Creating/deleting a review recalculates `products.average_rating` and `products.review_count`. `authorName` = first name + last initial ("Priya S.").

## Cart (Bearer)

```ts
interface CartItemDto { id: string; variantId: string; productId: string; productName: string; productSlug: string; variantName: string; sku: string; imageUrl: string | null; unitPrice: number; compareAtPrice: number | null; quantity: number; lineTotal: number; availableStock: number; }
interface CartDto { id: string; items: CartItemDto[]; itemCount: number; subtotal: number; }
interface AddCartItemRequest { variantId: string; quantity: number; }      // 1..99, merged into existing line
interface UpdateCartItemRequest { quantity: number; }                       // 1..99
interface MergeCartRequest { items: AddCartItemRequest[]; }
```

| Method | Path | Body | Data |
|---|---|---|---|
| GET | /api/cart | – | CartDto (creates empty cart if none) |
| POST | /api/cart/items | AddCartItemRequest | CartDto (409 if exceeds stock) |
| PATCH | /api/cart/items/{itemId} | UpdateCartItemRequest | CartDto |
| DELETE | /api/cart/items/{itemId} | – | CartDto |
| DELETE | /api/cart | – | CartDto (empty) |
| POST | /api/cart/merge | MergeCartRequest | CartDto (guest cart merged after login; quantities clamped to stock) |

## Wishlist (Bearer)

```ts
interface WishlistItemDto { id: string; productId: string; addedAt: string; product: ProductListItemDto; }
interface WishlistDto { items: WishlistItemDto[]; }
```

| Method | Path | Body | Data |
|---|---|---|---|
| GET | /api/wishlist | – | WishlistDto |
| POST | /api/wishlist | { productId: string } | WishlistDto (idempotent) |
| DELETE | /api/wishlist/{productId} | – | WishlistDto |

## Coupons

```ts
interface ValidateCouponRequest { code: string; subtotal: number; }
interface CouponValidationDto { code: string; description: string | null; discountAmount: number; }
```

| Method | Path | Auth | Data |
|---|---|---|---|
| POST | /api/coupons/validate | Bearer | CouponValidationDto (422 with message when invalid/expired/below minimum) |

## Orders / checkout (Bearer)

```ts
type OrderStatus = "Pending" | "Confirmed" | "Processing" | "Shipped" | "OutForDelivery" | "Delivered" | "Cancelled";
type DeliveryMethod = "Standard" | "Express";
type PaymentMethod = "CashOnDelivery" | "Card" | "Upi";
type PaymentStatus = "Pending" | "Paid" | "Failed" | "Refunded";

interface CheckoutQuoteRequest { deliveryMethod: DeliveryMethod; couponCode?: string | null; }
interface CheckoutQuoteDto { subtotal: number; discount: number; shipping: number; tax: number; total: number; couponCode: string | null; couponMessage: string | null; estimatedDelivery: string; }
interface CreateOrderRequest { addressId: string; deliveryMethod: DeliveryMethod; paymentMethod: PaymentMethod; couponCode?: string | null; notes?: string | null; }

interface OrderItemDto { id: string; productId: string; variantId: string; productName: string; productSlug: string; variantName: string; sku: string; imageUrl: string | null; unitPrice: number; quantity: number; lineTotal: number; }
interface OrderAddressDto { fullName: string; phone: string; line1: string; line2: string | null; city: string; state: string; postalCode: string; country: string; }
interface PaymentDto { method: PaymentMethod; status: PaymentStatus; amount: number; transactionReference: string | null; paidAt: string | null; }
interface OrderTimelineEntryDto { status: OrderStatus; at: string | null; completed: boolean; }
interface OrderSummaryDto { id: string; orderNumber: string; status: OrderStatus; total: number; itemCount: number; placedAt: string; previewImages: string[]; customerName?: string | null; customerEmail?: string | null; }
interface OrderDto {
  id: string; orderNumber: string; status: OrderStatus;
  subtotal: number; discount: number; shipping: number; tax: number; total: number;
  couponCode: string | null; deliveryMethod: DeliveryMethod; notes: string | null; trackingNumber: string | null;
  placedAt: string; estimatedDelivery: string | null;
  shippingAddress: OrderAddressDto; payment: PaymentDto; items: OrderItemDto[];
  timeline: OrderTimelineEntryDto[];   // Placed → Confirmed → Shipped → OutForDelivery → Delivered (or Cancelled)
  customerName?: string | null; customerEmail?: string | null;
}
```

| Method | Path | Body | Data |
|---|---|---|---|
| POST | /api/orders/quote | CheckoutQuoteRequest | CheckoutQuoteDto (uses server cart) |
| POST | /api/orders | CreateOrderRequest | OrderDto (201). Transaction: validates stock, decrements inventory, records coupon usage, creates payment, clears cart |
| GET | /api/orders?page&pageSize | – | PagedResult<OrderSummaryDto> |
| GET | /api/orders/{orderNumber} | – | OrderDto (own orders only, else 404) |
| POST | /api/orders/{orderNumber}/cancel | – | OrderDto (only Pending/Confirmed; restocks) |

Pricing rules (configurable under `Commerce` in appsettings): currency INR; tax 18% GST on (subtotal − discount); Standard shipping free when subtotal ≥ 2000 else 149; Express 349 always; Standard ETA 5 days, Express 2 days. CashOnDelivery → payment Pending; Card/Upi → handled by `IPaymentGateway` (default implementation is a development stub that marks Paid with a generated reference; replace with a real gateway).

## Admin (Bearer + role Admin)

```ts
interface DashboardDto {
  totalRevenue: number; ordersCount: number; customersCount: number; productsCount: number; averageOrderValue: number;
  revenueLast30Days: number; revenuePrevious30Days: number;
  salesByDay: { date: string; revenue: number; orders: number }[];        // last 30 days, every day present
  ordersByStatus: { status: OrderStatus; count: number }[];
  topProducts: { productId: string; name: string; imageUrl: string | null; unitsSold: number; revenue: number }[];
  recentOrders: OrderSummaryDto[];
  lowStock: InventoryItemDto[];
}

interface AdminProductListItemDto { id: string; name: string; slug: string; sku: string; categoryName: string; price: number; compareAtPrice: number | null; totalStock: number; variantCount: number; isActive: boolean; isFeatured: boolean; isNew: boolean; primaryImageUrl: string | null; updatedAt: string; }
interface AdminVariantDto extends ProductVariantDto { isActive: boolean; quantityOnHand: number; reservedQuantity: number; lowStockThreshold: number; }
interface AdminProductDetailDto { id: string; name: string; slug: string; sku: string; shortDescription: string | null; description: string | null; brand: string | null; basePrice: number; compareAtPrice: number | null; categoryId: string; isActive: boolean; isFeatured: boolean; isNew: boolean; specifications: Record<string, string>; images: ProductImageDto[]; variants: AdminVariantDto[]; createdAt: string; updatedAt: string; }
interface UpsertProductRequest { name: string; slug?: string | null; sku: string; shortDescription?: string | null; description?: string | null; brand?: string | null; basePrice: number; compareAtPrice?: number | null; categoryId: string; isActive: boolean; isFeatured: boolean; isNew: boolean; specifications: Record<string, string>; }
interface UpsertVariantRequest { sku: string; name: string; color?: string | null; finish?: string | null; colorTemperature?: string | null; wattage?: number | null; price: number; compareAtPrice?: number | null; isDefault: boolean; isActive: boolean; initialStock?: number | null; lowStockThreshold?: number | null; }
interface AddImageUrlRequest { url: string; altText?: string | null; isPrimary: boolean; }
interface UpdateImageRequest { altText?: string | null; sortOrder: number; isPrimary: boolean; }

interface UpsertCategoryRequest { name: string; slug?: string | null; description?: string | null; imageUrl?: string | null; parentId?: string | null; sortOrder: number; isActive: boolean; }
interface AdminCategoryDto extends CategoryDto { isActive: boolean; }

interface UpdateOrderStatusRequest { status: OrderStatus; trackingNumber?: string | null; }

interface AdminCustomerDto { id: string; email: string; firstName: string; lastName: string; phone: string | null; isActive: boolean; roles: string[]; ordersCount: number; totalSpent: number; createdAt: string; lastOrderAt: string | null; }
interface AdminCustomerDetailDto extends AdminCustomerDto { addresses: AddressDto[]; recentOrders: OrderSummaryDto[]; }

interface InventoryItemDto { variantId: string; productId: string; productName: string; variantName: string; sku: string; imageUrl: string | null; quantityOnHand: number; reservedQuantity: number; available: number; lowStockThreshold: number; isLowStock: boolean; }
interface UpdateInventoryRequest { quantityOnHand?: number | null; adjustment?: number | null; lowStockThreshold?: number | null; }   // exactly one of quantityOnHand / adjustment, or only threshold

type DiscountType = "Percentage" | "FixedAmount";
interface CouponDto { id: string; code: string; description: string | null; discountType: DiscountType; value: number; minOrderAmount: number | null; maxDiscountAmount: number | null; startsAt: string | null; endsAt: string | null; usageLimit: number | null; usedCount: number; isActive: boolean; createdAt: string; }
interface UpsertCouponRequest { code: string; description?: string | null; discountType: DiscountType; value: number; minOrderAmount?: number | null; maxDiscountAmount?: number | null; startsAt?: string | null; endsAt?: string | null; usageLimit?: number | null; isActive: boolean; }
```

| Method | Path | Body / Query | Data |
|---|---|---|---|
| GET | /api/admin/dashboard | – | DashboardDto |
| GET | /api/admin/products | search, categoryId, status (active/inactive/all), page, pageSize | PagedResult<AdminProductListItemDto> |
| GET | /api/admin/products/{id} | – | AdminProductDetailDto |
| POST | /api/admin/products | UpsertProductRequest | AdminProductDetailDto (201). If no variants are posted, a default variant `Standard` is created with sku = product sku |
| PUT | /api/admin/products/{id} | UpsertProductRequest | AdminProductDetailDto |
| DELETE | /api/admin/products/{id} | – | null (soft delete) |
| POST | /api/admin/products/{id}/variants | UpsertVariantRequest | AdminProductDetailDto |
| PUT | /api/admin/products/{id}/variants/{variantId} | UpsertVariantRequest | AdminProductDetailDto |
| DELETE | /api/admin/products/{id}/variants/{variantId} | – | AdminProductDetailDto (cannot delete last variant → 422) |
| POST | /api/admin/products/{id}/images | multipart: file (jpg/png/webp ≤ 5 MB), altText, isPrimary | AdminProductDetailDto (uploads to Supabase Storage bucket) |
| POST | /api/admin/products/{id}/images/url | AddImageUrlRequest | AdminProductDetailDto |
| PATCH | /api/admin/products/{id}/images/{imageId} | UpdateImageRequest | AdminProductDetailDto |
| DELETE | /api/admin/products/{id}/images/{imageId} | – | AdminProductDetailDto (also deletes the storage object) |
| GET | /api/admin/categories | – | AdminCategoryDto[] (flat, includes inactive) |
| POST | /api/admin/categories | UpsertCategoryRequest | AdminCategoryDto (201) |
| PUT | /api/admin/categories/{id} | UpsertCategoryRequest | AdminCategoryDto |
| DELETE | /api/admin/categories/{id} | – | null (422 if it has products or children) |
| GET | /api/admin/orders | status, search (order number / email / name), page, pageSize | PagedResult<OrderSummaryDto> |
| GET | /api/admin/orders/{orderNumber} | – | OrderDto |
| PATCH | /api/admin/orders/{orderNumber}/status | UpdateOrderStatusRequest | OrderDto (valid forward transitions only; Cancelled restocks; Delivered marks COD payment Paid) |
| GET | /api/admin/customers | search, page, pageSize | PagedResult<AdminCustomerDto> |
| GET | /api/admin/customers/{id} | – | AdminCustomerDetailDto |
| PATCH | /api/admin/customers/{id}/status | { isActive: boolean } | AdminCustomerDto |
| GET | /api/admin/inventory | search, lowStockOnly, page, pageSize | PagedResult<InventoryItemDto> |
| PATCH | /api/admin/inventory/{variantId} | UpdateInventoryRequest | InventoryItemDto |
| GET | /api/admin/coupons | – | CouponDto[] |
| POST | /api/admin/coupons | UpsertCouponRequest | CouponDto (201) |
| PUT | /api/admin/coupons/{id} | UpsertCouponRequest | CouponDto |
| DELETE | /api/admin/coupons/{id} | – | null (soft delete) |

## Order status transitions

```
Pending → Confirmed → Processing → Shipped → OutForDelivery → Delivered
Pending | Confirmed | Processing → Cancelled
```

## Backend notes

Clarifications from the backend implementation (no contract lines above were changed):

- **Envelope**: `traceId` is only present on error responses. Errors such as body-less 401/403/404, 429 (rate limit) and 503 also use the envelope. Malformed JSON bodies return `400` with errors keyed by the JSON path (e.g. `email`) or `request`.
- **Auth**: login/register are rate limited to 10 requests/minute per IP, refresh to 30/minute (`429`). Wrong credentials → `401 "Invalid email or password"`; deactivated account → `403 "Your account has been deactivated"`. A failed refresh returns `401` and clears the `ul_refresh` cookie. Re-presenting an already-rotated refresh token within 20 seconds (two tabs refreshing at once) is tolerated; any other reuse revokes all of the user's sessions.
- **Products**: `compareAtPrice` is `null` unless it is greater than `price`. An unknown `category` slug yields an empty page (not 404). `/api/products/suggest` with `q` shorter than 2 characters returns empty lists. `CategoryDto.productCount` on the public tree includes products of child categories; `GET /api/admin/categories` is flat (`children: []`) and counts direct products only (active and inactive).
- **Cart**: lines whose product/variant became inactive stay in the cart with `availableStock: 0` and are rejected at checkout (`422`). Quantity above 99 → `422`; above stock → `409`.
- **Wishlist**: products that become inactive or deleted are hidden from the wishlist response.
- **Checkout**: `POST /api/orders/quote` never fails because of a coupon — an invalid code returns `couponCode: null` and the reason in `couponMessage`; `POST /api/orders` with an invalid coupon returns `422`. Free standard shipping compares the subtotal **before** discount with the threshold. Card/UPI orders (paid via the development gateway stub) are created with status `Confirmed`; Cash on Delivery orders start as `Pending`.
- **Timeline**: the `Pending` entry represents "Placed" (`at = placedAt`). `Processing` is an internal admin step and is not part of the timeline. Cancelled orders list only the steps that happened, followed by `Cancelled`.
- **Order status (admin)**: any forward move along `Pending → Confirmed → Processing → Shipped → OutForDelivery → Delivered` is allowed (skipped steps get the same timestamp); `trackingNumber` can be sent with any status change (or alone with the current status). Cancelling (customer or admin) restocks inventory, releases the coupon use and marks a `Paid` payment as `Refunded` (stub).
- **Admin products**: `POST` without `slug` generates one from the name (suffixing `-2`, `-3` … when taken); `PUT` without `slug` keeps the existing slug. `initialStock` on `UpsertVariantRequest` is only used when creating a variant — change stock through `PATCH /api/admin/inventory/{variantId}`. The multipart image upload form fields are `file`, `altText`, `isPrimary`; without Supabase configuration it returns `503 "Image storage is not configured"`.
- **Admin customers**: the list includes every user (customers and admins); `roles` tells them apart. An admin cannot deactivate their own account (`422`). Deactivating a user revokes their refresh tokens.

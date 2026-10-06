CREATE TABLE IF NOT EXISTS __ef_migrations_history (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE SEQUENCE order_number_seq START WITH 10001 INCREMENT BY 1 NO CYCLE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE categories (
        id uuid NOT NULL,
        name character varying(120) NOT NULL,
        slug character varying(140) NOT NULL,
        description character varying(1000),
        image_url character varying(1024),
        parent_id uuid,
        sort_order integer NOT NULL,
        is_active boolean NOT NULL DEFAULT TRUE,
        is_deleted boolean NOT NULL,
        deleted_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_categories PRIMARY KEY (id),
        CONSTRAINT fk_categories_categories_parent_id FOREIGN KEY (parent_id) REFERENCES categories (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE coupons (
        id uuid NOT NULL,
        code character varying(40) NOT NULL,
        description character varying(300),
        discount_type character varying(20) NOT NULL,
        value numeric(12,2) NOT NULL,
        min_order_amount numeric(12,2),
        max_discount_amount numeric(12,2),
        starts_at timestamp with time zone,
        ends_at timestamp with time zone,
        usage_limit integer,
        used_count integer NOT NULL,
        is_active boolean NOT NULL DEFAULT TRUE,
        is_deleted boolean NOT NULL,
        deleted_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_coupons PRIMARY KEY (id),
        CONSTRAINT ck_coupons_used_count_non_negative CHECK (used_count >= 0),
        CONSTRAINT ck_coupons_value_positive CHECK (value > 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE roles (
        id uuid NOT NULL,
        name character varying(50) NOT NULL,
        CONSTRAINT pk_roles PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE users (
        id uuid NOT NULL,
        email character varying(256) NOT NULL,
        normalized_email character varying(256) NOT NULL,
        password_hash character varying(512) NOT NULL,
        first_name character varying(100) NOT NULL,
        last_name character varying(100) NOT NULL,
        phone character varying(32),
        is_active boolean NOT NULL DEFAULT TRUE,
        last_login_at timestamp with time zone,
        is_deleted boolean NOT NULL,
        deleted_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_users PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE products (
        id uuid NOT NULL,
        name character varying(200) NOT NULL,
        slug character varying(220) NOT NULL,
        sku character varying(64) NOT NULL,
        short_description character varying(500),
        description character varying(10000),
        brand character varying(100),
        base_price numeric(12,2) NOT NULL,
        compare_at_price numeric(12,2),
        category_id uuid NOT NULL,
        is_active boolean NOT NULL DEFAULT TRUE,
        is_featured boolean NOT NULL,
        is_new boolean NOT NULL,
        specifications jsonb NOT NULL DEFAULT ('{}'::jsonb),
        average_rating numeric(3,2) NOT NULL DEFAULT 0.0,
        review_count integer NOT NULL DEFAULT 0,
        is_deleted boolean NOT NULL,
        deleted_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_products PRIMARY KEY (id),
        CONSTRAINT fk_products_categories_category_id FOREIGN KEY (category_id) REFERENCES categories (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE addresses (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        label character varying(50),
        full_name character varying(150) NOT NULL,
        phone character varying(32) NOT NULL,
        line1 character varying(200) NOT NULL,
        line2 character varying(200),
        city character varying(100) NOT NULL,
        state character varying(100) NOT NULL,
        postal_code character varying(20) NOT NULL,
        country character varying(100) NOT NULL DEFAULT 'India',
        is_default boolean NOT NULL,
        is_deleted boolean NOT NULL,
        deleted_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_addresses PRIMARY KEY (id),
        CONSTRAINT fk_addresses_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE carts (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_carts PRIMARY KEY (id),
        CONSTRAINT fk_carts_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE orders (
        id uuid NOT NULL,
        order_number character varying(32) NOT NULL DEFAULT ('UL-' || nextval('order_number_seq')),
        user_id uuid NOT NULL,
        status character varying(20) NOT NULL,
        subtotal numeric(12,2) NOT NULL,
        discount numeric(12,2) NOT NULL,
        shipping numeric(12,2) NOT NULL,
        tax numeric(12,2) NOT NULL,
        total numeric(12,2) NOT NULL,
        coupon_id uuid,
        coupon_code character varying(40),
        delivery_method character varying(20) NOT NULL,
        ship_full_name character varying(150) NOT NULL,
        ship_phone character varying(32) NOT NULL,
        ship_line1 character varying(200) NOT NULL,
        ship_line2 character varying(200),
        ship_city character varying(100) NOT NULL,
        ship_state character varying(100) NOT NULL,
        ship_postal_code character varying(20) NOT NULL,
        ship_country character varying(100) NOT NULL,
        notes character varying(1000),
        tracking_number character varying(100),
        placed_at timestamp with time zone NOT NULL,
        confirmed_at timestamp with time zone,
        processing_at timestamp with time zone,
        shipped_at timestamp with time zone,
        out_for_delivery_at timestamp with time zone,
        delivered_at timestamp with time zone,
        cancelled_at timestamp with time zone,
        estimated_delivery timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_orders PRIMARY KEY (id),
        CONSTRAINT ck_orders_amounts_non_negative CHECK (subtotal >= 0 AND discount >= 0 AND shipping >= 0 AND tax >= 0 AND total >= 0),
        CONSTRAINT fk_orders_coupons_coupon_id FOREIGN KEY (coupon_id) REFERENCES coupons (id) ON DELETE SET NULL,
        CONSTRAINT fk_orders_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE refresh_tokens (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        token_hash character varying(128) NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        created_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        replaced_by_token_hash character varying(128),
        created_by_ip character varying(64),
        CONSTRAINT pk_refresh_tokens PRIMARY KEY (id),
        CONSTRAINT fk_refresh_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE user_roles (
        user_id uuid NOT NULL,
        role_id uuid NOT NULL,
        CONSTRAINT pk_user_roles PRIMARY KEY (user_id, role_id),
        CONSTRAINT fk_user_roles_roles_role_id FOREIGN KEY (role_id) REFERENCES roles (id) ON DELETE CASCADE,
        CONSTRAINT fk_user_roles_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE wishlists (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_wishlists PRIMARY KEY (id),
        CONSTRAINT fk_wishlists_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE product_images (
        id uuid NOT NULL,
        product_id uuid NOT NULL,
        url character varying(1024) NOT NULL,
        storage_path character varying(512),
        alt_text character varying(250),
        sort_order integer NOT NULL,
        is_primary boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_product_images PRIMARY KEY (id),
        CONSTRAINT fk_product_images_products_product_id FOREIGN KEY (product_id) REFERENCES products (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE product_variants (
        id uuid NOT NULL,
        product_id uuid NOT NULL,
        sku character varying(64) NOT NULL,
        name character varying(150) NOT NULL,
        color character varying(50),
        finish character varying(50),
        color_temperature character varying(20),
        wattage integer,
        price numeric(12,2) NOT NULL,
        compare_at_price numeric(12,2),
        is_default boolean NOT NULL,
        is_active boolean NOT NULL DEFAULT TRUE,
        is_deleted boolean NOT NULL,
        deleted_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_product_variants PRIMARY KEY (id),
        CONSTRAINT ck_product_variants_price_non_negative CHECK (price >= 0),
        CONSTRAINT ck_product_variants_wattage_positive CHECK (wattage IS NULL OR wattage > 0),
        CONSTRAINT fk_product_variants_products_product_id FOREIGN KEY (product_id) REFERENCES products (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE reviews (
        id uuid NOT NULL,
        product_id uuid NOT NULL,
        user_id uuid NOT NULL,
        rating smallint NOT NULL,
        title character varying(120),
        comment character varying(2000),
        is_approved boolean NOT NULL DEFAULT TRUE,
        is_verified_purchase boolean NOT NULL,
        is_deleted boolean NOT NULL,
        deleted_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_reviews PRIMARY KEY (id),
        CONSTRAINT ck_reviews_rating_range CHECK (rating BETWEEN 1 AND 5),
        CONSTRAINT fk_reviews_products_product_id FOREIGN KEY (product_id) REFERENCES products (id) ON DELETE CASCADE,
        CONSTRAINT fk_reviews_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE payments (
        id uuid NOT NULL,
        order_id uuid NOT NULL,
        method character varying(20) NOT NULL,
        status character varying(20) NOT NULL,
        amount numeric(12,2) NOT NULL,
        transaction_reference character varying(100),
        paid_at timestamp with time zone,
        failure_reason character varying(500),
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_payments PRIMARY KEY (id),
        CONSTRAINT fk_payments_orders_order_id FOREIGN KEY (order_id) REFERENCES orders (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE wishlist_items (
        id uuid NOT NULL,
        wishlist_id uuid NOT NULL,
        product_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_wishlist_items PRIMARY KEY (id),
        CONSTRAINT fk_wishlist_items_products_product_id FOREIGN KEY (product_id) REFERENCES products (id) ON DELETE CASCADE,
        CONSTRAINT fk_wishlist_items_wishlists_wishlist_id FOREIGN KEY (wishlist_id) REFERENCES wishlists (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE cart_items (
        id uuid NOT NULL,
        cart_id uuid NOT NULL,
        variant_id uuid NOT NULL,
        quantity integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_cart_items PRIMARY KEY (id),
        CONSTRAINT ck_cart_items_quantity_range CHECK (quantity BETWEEN 1 AND 99),
        CONSTRAINT fk_cart_items_carts_cart_id FOREIGN KEY (cart_id) REFERENCES carts (id) ON DELETE CASCADE,
        CONSTRAINT fk_cart_items_product_variants_variant_id FOREIGN KEY (variant_id) REFERENCES product_variants (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE inventory (
        id uuid NOT NULL,
        variant_id uuid NOT NULL,
        quantity_on_hand integer NOT NULL DEFAULT 0,
        reserved_quantity integer NOT NULL DEFAULT 0,
        low_stock_threshold integer NOT NULL DEFAULT 5,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_inventory PRIMARY KEY (id),
        CONSTRAINT ck_inventory_low_stock_threshold_non_negative CHECK (low_stock_threshold >= 0),
        CONSTRAINT ck_inventory_quantity_on_hand_non_negative CHECK (quantity_on_hand >= 0),
        CONSTRAINT ck_inventory_reserved_quantity_non_negative CHECK (reserved_quantity >= 0),
        CONSTRAINT fk_inventory_product_variants_variant_id FOREIGN KEY (variant_id) REFERENCES product_variants (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE TABLE order_items (
        id uuid NOT NULL,
        order_id uuid NOT NULL,
        product_id uuid NOT NULL,
        variant_id uuid NOT NULL,
        product_name character varying(200) NOT NULL,
        product_slug character varying(220) NOT NULL,
        variant_name character varying(150) NOT NULL,
        sku character varying(64) NOT NULL,
        image_url character varying(1024),
        unit_price numeric(12,2) NOT NULL,
        quantity integer NOT NULL,
        line_total numeric(12,2) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_order_items PRIMARY KEY (id),
        CONSTRAINT ck_order_items_quantity_positive CHECK (quantity > 0),
        CONSTRAINT fk_order_items_orders_order_id FOREIGN KEY (order_id) REFERENCES orders (id) ON DELETE CASCADE,
        CONSTRAINT fk_order_items_product_variants_variant_id FOREIGN KEY (variant_id) REFERENCES product_variants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_order_items_products_product_id FOREIGN KEY (product_id) REFERENCES products (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    INSERT INTO roles (id, name)
    VALUES ('6a1f0c2e-0d1b-4f3a-9c41-0000000000a1', 'Admin');
    INSERT INTO roles (id, name)
    VALUES ('6a1f0c2e-0d1b-4f3a-9c41-0000000000c1', 'Customer');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_addresses_user_id ON addresses (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_cart_items_cart_id_variant_id ON cart_items (cart_id, variant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_cart_items_variant_id ON cart_items (variant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_carts_user_id ON carts (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_categories_parent_id ON categories (parent_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_categories_slug ON categories (slug) WHERE is_deleted = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_coupons_code ON coupons (code) WHERE is_deleted = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_inventory_variant_id ON inventory (variant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_order_items_order_id ON order_items (order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_order_items_product_id ON order_items (product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_order_items_variant_id ON order_items (variant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_orders_coupon_id ON orders (coupon_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_orders_order_number ON orders (order_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_orders_placed_at ON orders (placed_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_orders_status ON orders (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_orders_user_id_placed_at ON orders (user_id, placed_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_payments_order_id ON payments (order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_product_images_product_id_sort_order ON product_images (product_id, sort_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_product_variants_product_id ON product_variants (product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_product_variants_sku ON product_variants (sku) WHERE is_deleted = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_products_brand ON products (brand);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_products_category_id_is_active ON products (category_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_products_name ON products (name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_products_sku ON products (sku) WHERE is_deleted = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_products_slug ON products (slug) WHERE is_deleted = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_refresh_tokens_token_hash ON refresh_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_refresh_tokens_user_id ON refresh_tokens (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_reviews_product_id_user_id ON reviews (product_id, user_id) WHERE is_deleted = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_reviews_user_id ON reviews (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_roles_name ON roles (name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_user_roles_role_id ON user_roles (role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_users_normalized_email ON users (normalized_email) WHERE is_deleted = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE INDEX ix_wishlist_items_product_id ON wishlist_items (product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_wishlist_items_wishlist_id_product_id ON wishlist_items (wishlist_id, product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_wishlists_user_id ON wishlists (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261006112043_InitialCreate') THEN
    INSERT INTO __ef_migrations_history (migration_id, product_version)
    VALUES ('20261006112043_InitialCreate', '9.0.20');
    END IF;
END $EF$;
COMMIT;


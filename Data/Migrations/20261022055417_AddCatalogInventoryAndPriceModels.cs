using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace E_commerce_backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogInventoryAndPriceModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attribute_keys",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attribute_keys", x => x.id);
                    table.CheckConstraint("CK_attribute_keys_code", "length(code) > 0 AND code = lower(btrim(code))");
                    table.CheckConstraint("CK_attribute_keys_display_name", "length(btrim(display_name)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.id);
                    table.CheckConstraint("CK_categories_display_name", "length(btrim(display_name)) BETWEEN 1 AND 120 AND display_name = btrim(display_name)");
                    table.CheckConstraint("CK_categories_not_own_parent", "parent_id IS NULL OR parent_id <> id");
                    table.CheckConstraint("CK_categories_slug", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                    table.ForeignKey(
                        name: "FK_categories_categories_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "app",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Draft"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                    table.CheckConstraint("CK_products_name", "length(btrim(name)) > 0");
                    table.CheckConstraint("CK_products_slug", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                    table.CheckConstraint("CK_products_status", "status IN ('Draft', 'Published', 'Hidden')");
                });

            migrationBuilder.CreateTable(
                name: "attribute_pairs",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attribute_pairs", x => x.id);
                    table.CheckConstraint("CK_attribute_pairs_value", "length(btrim(value)) > 0");
                    table.ForeignKey(
                        name: "FK_attribute_pairs_attribute_keys_attribute_key_id",
                        column: x => x.attribute_key_id,
                        principalSchema: "app",
                        principalTable: "attribute_keys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_categories",
                schema: "app",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_categories", x => new { x.product_id, x.category_id });
                    table.ForeignKey(
                        name: "FK_product_categories_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "app",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_categories_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "app",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_variants",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Draft"),
                    description = table.Column<string>(type: "text", nullable: true),
                    short_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    image_urls = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_variants", x => x.id);
                    table.CheckConstraint("CK_product_variants_sku", "length(btrim(sku)) > 0");
                    table.CheckConstraint("CK_product_variants_status", "status IN ('Draft', 'Published', 'Hidden')");
                    table.ForeignKey(
                        name: "FK_product_variants_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "app",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "variant_attribute_pairs",
                schema: "app",
                columns: table => new
                {
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_pair_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_variant_attribute_pairs", x => new { x.variant_id, x.attribute_pair_id });
                    table.ForeignKey(
                        name: "FK_variant_attribute_pairs_attribute_pairs_attribute_pair_id",
                        column: x => x.attribute_pair_id,
                        principalSchema: "app",
                        principalTable: "attribute_pairs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_variant_attribute_pairs_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "app",
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "variant_price_changes",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    selling_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", nullable: false, defaultValue: "PLN"),
                    effective_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    reason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_variant_price_changes", x => x.id);
                    table.CheckConstraint("CK_variant_price_changes_amounts", "base_amount >= 0 AND selling_amount >= 0 AND selling_amount <= base_amount");
                    table.CheckConstraint("CK_variant_price_changes_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_variant_price_changes_reason", "length(btrim(reason)) > 0");
                    table.ForeignKey(
                        name: "FK_variant_price_changes_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "app",
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "variant_stock",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_on_hand = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_variant_stock", x => x.id);
                    table.CheckConstraint("CK_variant_stock_nonnegative", "quantity_on_hand >= 0");
                    table.ForeignKey(
                        name: "FK_variant_stock_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "app",
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attribute_keys_code",
                schema: "app",
                table: "attribute_keys",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attribute_pairs_attribute_key_id_value",
                schema: "app",
                table: "attribute_pairs",
                columns: new[] { "attribute_key_id", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_categories_root_slug",
                schema: "app",
                table: "categories",
                column: "slug",
                unique: true,
                filter: "parent_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_categories_sibling_slug",
                schema: "app",
                table: "categories",
                columns: new[] { "parent_id", "slug" },
                unique: true,
                filter: "parent_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_product_categories_category_id",
                schema: "app",
                table: "product_categories",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_variants_product_id",
                schema: "app",
                table: "product_variants",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_variants_sku",
                schema: "app",
                table: "product_variants",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_slug",
                schema: "app",
                table: "products",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_variant_attribute_pairs_attribute_pair_id",
                schema: "app",
                table: "variant_attribute_pairs",
                column: "attribute_pair_id");

            migrationBuilder.CreateIndex(
                name: "IX_variant_price_changes_variant_id_currency_effective_at_crea~",
                schema: "app",
                table: "variant_price_changes",
                columns: new[] { "variant_id", "currency", "effective_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_variant_stock_variant_id",
                schema: "app",
                table: "variant_stock",
                column: "variant_id",
                unique: true);

            // Dane katalogu są dostępne przez backend, nie przez Supabase Data API.
            migrationBuilder.Sql("""
                DO $$
                DECLARE table_name text;
                BEGIN
                    FOREACH table_name IN ARRAY ARRAY[
                        'products', 'product_variants', 'attribute_keys', 'attribute_pairs',
                        'variant_attribute_pairs', 'categories', 'product_categories',
                        'variant_stock', 'variant_price_changes'
                    ] LOOP
                        EXECUTE format('ALTER TABLE app.%I ENABLE ROW LEVEL SECURITY', table_name);
                        EXECUTE format('REVOKE ALL ON TABLE app.%I FROM PUBLIC, anon, authenticated, service_role', table_name);
                    END LOOP;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION app.set_catalog_updated_at()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    NEW.updated_at := CURRENT_TIMESTAMP;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER set_products_updated_at BEFORE UPDATE ON app.products
                    FOR EACH ROW EXECUTE FUNCTION app.set_catalog_updated_at();
                CREATE TRIGGER set_product_variants_updated_at BEFORE UPDATE ON app.product_variants
                    FOR EACH ROW EXECUTE FUNCTION app.set_catalog_updated_at();
                CREATE TRIGGER set_attribute_pairs_updated_at BEFORE UPDATE ON app.attribute_pairs
                    FOR EACH ROW EXECUTE FUNCTION app.set_catalog_updated_at();
                CREATE TRIGGER set_categories_updated_at BEFORE UPDATE ON app.categories
                    FOR EACH ROW EXECUTE FUNCTION app.set_catalog_updated_at();
                CREATE TRIGGER set_variant_stock_updated_at BEFORE UPDATE ON app.variant_stock
                    FOR EACH ROW EXECUTE FUNCTION app.set_catalog_updated_at();
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION app.reject_variant_price_change_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Price history is append-only';
                END;
                $$;

                CREATE TRIGGER reject_variant_price_change_mutation
                    BEFORE UPDATE OR DELETE ON app.variant_price_changes
                    FOR EACH ROW EXECUTE FUNCTION app.reject_variant_price_change_mutation();
                """);

            // Wspólna blokada transakcyjna serializuje wszystkie zmiany drzewa.
            migrationBuilder.Sql("""
                CREATE FUNCTION app.check_category_parent()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock(20261022, 1);
                    IF NEW.parent_id IS NOT NULL THEN
                        IF NEW.parent_id = NEW.id THEN
                            RAISE EXCEPTION 'Category cannot be its own parent';
                        END IF;
                        IF EXISTS (
                            WITH RECURSIVE ancestors AS (
                                SELECT id, parent_id FROM app.categories WHERE id = NEW.parent_id
                                UNION ALL
                                SELECT c.id, c.parent_id FROM app.categories c
                                JOIN ancestors a ON c.id = a.parent_id
                            )
                            SELECT 1 FROM ancestors WHERE id = NEW.id
                        ) THEN
                            RAISE EXCEPTION 'Category move would create a cycle';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER check_category_parent
                    BEFORE INSERT OR UPDATE OF parent_id ON app.categories
                    FOR EACH ROW EXECUTE FUNCTION app.check_category_parent();
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION app.check_product_has_variant()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE product_id_to_check uuid;
                BEGIN
                    IF TG_TABLE_NAME = 'products' THEN
                        product_id_to_check := NEW.id;
                    ELSE
                        product_id_to_check := OLD.product_id;
                    END IF;

                    PERFORM 1 FROM app.products WHERE id = product_id_to_check FOR NO KEY UPDATE;
                    IF FOUND AND NOT EXISTS (
                        SELECT 1 FROM app.product_variants WHERE product_id = product_id_to_check
                    ) THEN
                        RAISE EXCEPTION 'Product must have at least one variant';
                    END IF;
                    RETURN NULL;
                END;
                $$;

                CREATE CONSTRAINT TRIGGER check_product_has_variant_after_insert
                    AFTER INSERT ON app.products DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION app.check_product_has_variant();
                CREATE CONSTRAINT TRIGGER check_product_has_variant_after_variant_delete
                    AFTER DELETE ON app.product_variants DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION app.check_product_has_variant();
                CREATE CONSTRAINT TRIGGER check_product_has_variant_after_variant_move
                    AFTER UPDATE OF product_id ON app.product_variants DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION app.check_product_has_variant();
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION app.check_unique_variant_attributes()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE product_id_to_check uuid;
                DECLARE variant_id_to_check uuid;
                BEGIN
                    IF TG_TABLE_NAME = 'product_variants' THEN
                        variant_id_to_check := NEW.id;
                    ELSIF TG_OP = 'DELETE' THEN
                        variant_id_to_check := OLD.variant_id;
                    ELSE
                        variant_id_to_check := NEW.variant_id;
                    END IF;

                    SELECT product_id INTO product_id_to_check
                    FROM app.product_variants WHERE id = variant_id_to_check;
                    IF product_id_to_check IS NULL THEN
                        RETURN NULL;
                    END IF;

                    PERFORM 1 FROM app.products WHERE id = product_id_to_check FOR NO KEY UPDATE;
                    IF EXISTS (
                        WITH variant_sets AS (
                            SELECT v.id,
                                   COALESCE(array_agg(link.attribute_pair_id ORDER BY link.attribute_pair_id)
                                       FILTER (WHERE link.attribute_pair_id IS NOT NULL), ARRAY[]::uuid[]) AS pair_ids
                            FROM app.product_variants v
                            LEFT JOIN app.variant_attribute_pairs link ON link.variant_id = v.id
                            WHERE v.product_id = product_id_to_check
                            GROUP BY v.id
                        )
                        SELECT 1 FROM variant_sets first_variant
                        JOIN variant_sets second_variant
                          ON first_variant.id < second_variant.id
                         AND first_variant.pair_ids = second_variant.pair_ids
                    ) THEN
                        RAISE EXCEPTION 'Variants of one product must have distinct attribute sets';
                    END IF;
                    RETURN NULL;
                END;
                $$;

                CREATE CONSTRAINT TRIGGER check_unique_variant_attributes_after_variant_insert
                    AFTER INSERT ON app.product_variants DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION app.check_unique_variant_attributes();
                CREATE CONSTRAINT TRIGGER check_unique_variant_attributes_after_variant_move
                    AFTER UPDATE OF product_id ON app.product_variants DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION app.check_unique_variant_attributes();
                CREATE CONSTRAINT TRIGGER check_unique_variant_attributes_after_link_insert
                    AFTER INSERT ON app.variant_attribute_pairs DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION app.check_unique_variant_attributes();
                CREATE CONSTRAINT TRIGGER check_unique_variant_attributes_after_link_delete
                    AFTER DELETE ON app.variant_attribute_pairs DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION app.check_unique_variant_attributes();

                CREATE FUNCTION app.reject_variant_attribute_link_update()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Replace a variant attribute link by deleting and inserting it';
                END;
                $$;
                CREATE TRIGGER reject_variant_attribute_link_update
                    BEFORE UPDATE ON app.variant_attribute_pairs
                    FOR EACH ROW EXECUTE FUNCTION app.reject_variant_attribute_link_update();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER reject_variant_attribute_link_update ON app.variant_attribute_pairs;");
            migrationBuilder.Sql("DROP FUNCTION app.reject_variant_attribute_link_update();");
            migrationBuilder.Sql("DROP TRIGGER check_unique_variant_attributes_after_link_delete ON app.variant_attribute_pairs;");
            migrationBuilder.Sql("DROP TRIGGER check_unique_variant_attributes_after_link_insert ON app.variant_attribute_pairs;");
            migrationBuilder.Sql("DROP TRIGGER check_unique_variant_attributes_after_variant_move ON app.product_variants;");
            migrationBuilder.Sql("DROP TRIGGER check_unique_variant_attributes_after_variant_insert ON app.product_variants;");
            migrationBuilder.Sql("DROP FUNCTION app.check_unique_variant_attributes();");
            migrationBuilder.Sql("DROP TRIGGER check_product_has_variant_after_variant_move ON app.product_variants;");
            migrationBuilder.Sql("DROP TRIGGER check_product_has_variant_after_variant_delete ON app.product_variants;");
            migrationBuilder.Sql("DROP TRIGGER check_product_has_variant_after_insert ON app.products;");
            migrationBuilder.Sql("DROP FUNCTION app.check_product_has_variant();");
            migrationBuilder.Sql("DROP TRIGGER check_category_parent ON app.categories;");
            migrationBuilder.Sql("DROP FUNCTION app.check_category_parent();");
            migrationBuilder.Sql("DROP TRIGGER reject_variant_price_change_mutation ON app.variant_price_changes;");
            migrationBuilder.Sql("DROP FUNCTION app.reject_variant_price_change_mutation();");
            migrationBuilder.Sql("DROP TRIGGER set_products_updated_at ON app.products;");
            migrationBuilder.Sql("DROP TRIGGER set_product_variants_updated_at ON app.product_variants;");
            migrationBuilder.Sql("DROP TRIGGER set_attribute_pairs_updated_at ON app.attribute_pairs;");
            migrationBuilder.Sql("DROP TRIGGER set_categories_updated_at ON app.categories;");
            migrationBuilder.Sql("DROP TRIGGER set_variant_stock_updated_at ON app.variant_stock;");
            migrationBuilder.Sql("DROP FUNCTION app.set_catalog_updated_at();");

            migrationBuilder.DropTable(
                name: "product_categories",
                schema: "app");

            migrationBuilder.DropTable(
                name: "variant_attribute_pairs",
                schema: "app");

            migrationBuilder.DropTable(
                name: "variant_price_changes",
                schema: "app");

            migrationBuilder.DropTable(
                name: "variant_stock",
                schema: "app");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "app");

            migrationBuilder.DropTable(
                name: "attribute_pairs",
                schema: "app");

            migrationBuilder.DropTable(
                name: "product_variants",
                schema: "app");

            migrationBuilder.DropTable(
                name: "attribute_keys",
                schema: "app");

            migrationBuilder.DropTable(
                name: "products",
                schema: "app");
        }
    }
}

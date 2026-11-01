using Microsoft.EntityFrameworkCore.Migrations;

namespace E_commerce_backend.Data.Migrations;

public partial class AddCatalogPublicationRules
{
    private static void InstallPublicationRules(MigrationBuilder migration) => migration.Sql("""
        CREATE FUNCTION app.catalog_canonical(value text) RETURNS text LANGUAGE sql IMMUTABLE STRICT AS $$
          SELECT normalize(btrim(value, U&'\0009\000A\000B\000C\000D\0020\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000'), NFC)
        $$;
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM app.attribute_pairs WHERE value <> app.catalog_canonical(value)) THEN
            RAISE EXCEPTION 'Noncanonical attribute values exist. Audit and explicitly resolve them before migration';
          END IF;
          IF EXISTS (SELECT 1 FROM app.product_variants v WHERE v.status='Published' AND NOT EXISTS
            (SELECT 1 FROM app.variant_price_changes c WHERE c.variant_id=v.id AND c.currency='PLN' AND c.effective_at<=clock_timestamp()))
            OR EXISTS (SELECT 1 FROM app.products p WHERE p.status='Published' AND
              (NOT EXISTS (SELECT 1 FROM app.product_categories c WHERE c.product_id=p.id)
              OR NOT EXISTS (SELECT 1 FROM app.product_variants v WHERE v.product_id=p.id AND v.status='Published'))) THEN
            RAISE EXCEPTION 'Incomplete published catalog exists. Audit and explicitly resolve it before migration';
          END IF;
        END $$;
        ALTER TABLE app.attribute_pairs ADD CONSTRAINT "CK_attribute_pairs_canonical" CHECK(value=app.catalog_canonical(value));

        CREATE FUNCTION app.preserve_first_catalog_publication() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='UPDATE' AND OLD.published_at IS NOT NULL THEN NEW.published_at := OLD.published_at;
          ELSIF NEW.status='Published' AND NEW.published_at IS NULL
            AND (TG_OP='INSERT' OR NEW.status IS DISTINCT FROM OLD.status) THEN NEW.published_at := clock_timestamp(); END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER preserve_product_publication BEFORE INSERT OR UPDATE ON app.products
          FOR EACH ROW EXECUTE FUNCTION app.preserve_first_catalog_publication();
        CREATE TRIGGER preserve_variant_publication BEFORE INSERT OR UPDATE ON app.product_variants
          FOR EACH ROW EXECUTE FUNCTION app.preserve_first_catalog_publication();

        CREATE FUNCTION app.check_catalog_publication() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE target uuid; BEGIN
          IF TG_TABLE_NAME='products' THEN target := NEW.id;
          ELSE target := CASE WHEN TG_OP='DELETE' THEN OLD.product_id ELSE NEW.product_id END; END IF;
          IF EXISTS (SELECT 1 FROM app.product_variants v WHERE v.product_id=target AND v.status='Published' AND NOT EXISTS
            (SELECT 1 FROM app.variant_price_changes c WHERE c.variant_id=v.id AND c.currency='PLN' AND c.effective_at<=clock_timestamp())) THEN
            RAISE EXCEPTION 'Published variant requires a current PLN price' USING ERRCODE='23514', CONSTRAINT='catalog_variant_publication';
          END IF;
          IF EXISTS (SELECT 1 FROM app.products WHERE id=target AND status='Published') AND
            (NOT EXISTS (SELECT 1 FROM app.product_categories WHERE product_id=target) OR
             NOT EXISTS (SELECT 1 FROM app.product_variants WHERE product_id=target AND status='Published')) THEN
            RAISE EXCEPTION 'Published product requires category and published variant' USING ERRCODE='23514', CONSTRAINT='catalog_product_publication';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER check_catalog_product_publication AFTER INSERT OR UPDATE ON app.products
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_catalog_publication();
        CREATE CONSTRAINT TRIGGER check_catalog_variant_publication AFTER INSERT OR UPDATE OR DELETE ON app.product_variants
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_catalog_publication();
        CREATE CONSTRAINT TRIGGER check_catalog_category_publication AFTER INSERT OR UPDATE OR DELETE ON app.product_categories
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_catalog_publication();
        REVOKE ALL ON FUNCTION app.catalog_canonical(text), app.preserve_first_catalog_publication(),
          app.check_catalog_publication() FROM PUBLIC, anon, authenticated, service_role;
        """);

    private static void RemovePublicationRules(MigrationBuilder migration) => migration.Sql("""
        DROP TRIGGER check_catalog_product_publication ON app.products;
        DROP TRIGGER check_catalog_variant_publication ON app.product_variants;
        DROP TRIGGER check_catalog_category_publication ON app.product_categories;
        DROP TRIGGER preserve_product_publication ON app.products;
        DROP TRIGGER preserve_variant_publication ON app.product_variants;
        DROP FUNCTION app.check_catalog_publication();
        DROP FUNCTION app.preserve_first_catalog_publication();
        ALTER TABLE app.attribute_pairs DROP CONSTRAINT "CK_attribute_pairs_canonical";
        DROP FUNCTION app.catalog_canonical(text);
        """);
}

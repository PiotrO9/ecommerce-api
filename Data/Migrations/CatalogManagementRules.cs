using Microsoft.EntityFrameworkCore.Migrations;

namespace E_commerce_backend.Data.Migrations;

public partial class AddCatalogManagement
{
    private static void InstallManagementRules(MigrationBuilder migration) => migration.Sql("""
        ALTER TABLE app.catalog_operations ENABLE ROW LEVEL SECURITY;
        REVOKE ALL ON TABLE app.catalog_operations FROM PUBLIC, anon, authenticated, service_role;
        CREATE TRIGGER catalog_operation_immutable BEFORE UPDATE OR DELETE ON app.catalog_operations
          FOR EACH ROW EXECUTE FUNCTION app.reject_promotion_history_mutation();

        CREATE FUNCTION app.version_catalog_entity() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP = 'INSERT' THEN NEW.version := 1;
          ELSE
            NEW.version := OLD.version + 1;
            IF TG_TABLE_NAME <> 'attribute_keys' THEN NEW.created_at := OLD.created_at; END IF;
          END IF;
          IF TG_TABLE_NAME <> 'attribute_keys' THEN NEW.updated_at := clock_timestamp(); END IF;
          RETURN NEW;
        END $$;
        DO $$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['products','product_variants','categories','attribute_keys','variant_stock'] LOOP
            EXECUTE format('CREATE TRIGGER zz_version_catalog_entity BEFORE INSERT OR UPDATE ON app.%I FOR EACH ROW EXECUTE FUNCTION app.version_catalog_entity()', t);
            EXECUTE format('ALTER TABLE app.%I ADD CONSTRAINT %I CHECK (version > 0)', t, 'CK_' || t || '_version');
          END LOOP;
          FOREACH t IN ARRAY ARRAY['categories','attribute_keys','attribute_pairs','variant_attribute_pairs','variant_stock'] LOOP
            EXECUTE format('CREATE TRIGGER catalog_pricing_write BEFORE INSERT OR UPDATE OR DELETE ON app.%I FOR EACH STATEMENT EXECUTE FUNCTION app.lock_catalog_pricing_write()', t);
          END LOOP;
        END $$;

        CREATE FUNCTION app.version_catalog_product_graph() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE target uuid; BEGIN
          target := CASE WHEN TG_OP = 'DELETE' THEN OLD.product_id ELSE NEW.product_id END;
          UPDATE app.products SET updated_at = clock_timestamp() WHERE id = target;
          IF TG_OP = 'UPDATE' AND NEW.product_id IS DISTINCT FROM OLD.product_id THEN
            UPDATE app.products SET updated_at = clock_timestamp() WHERE id = OLD.product_id;
          END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER version_product_variants AFTER INSERT OR UPDATE OR DELETE ON app.product_variants
          FOR EACH ROW EXECUTE FUNCTION app.version_catalog_product_graph();
        CREATE TRIGGER version_product_categories AFTER INSERT OR UPDATE OR DELETE ON app.product_categories
          FOR EACH ROW EXECUTE FUNCTION app.version_catalog_product_graph();

        CREATE FUNCTION app.version_catalog_variant_graph() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE target uuid; BEGIN
          target := CASE WHEN TG_OP = 'DELETE' THEN OLD.variant_id ELSE NEW.variant_id END;
          UPDATE app.product_variants SET updated_at = clock_timestamp() WHERE id = target;
          RETURN NULL;
        END $$;
        CREATE TRIGGER version_variant_attributes AFTER INSERT OR DELETE ON app.variant_attribute_pairs
          FOR EACH ROW EXECUTE FUNCTION app.version_catalog_variant_graph();

        CREATE FUNCTION app.guard_catalog_identity() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_TABLE_NAME = 'attribute_keys' THEN
            IF NEW.code IS DISTINCT FROM OLD.code THEN
              RAISE EXCEPTION 'Attribute code is immutable' USING ERRCODE='23514', CONSTRAINT='catalog_key_identity';
            END IF;
          ELSIF TG_TABLE_NAME = 'attribute_pairs' THEN
            IF NEW.attribute_key_id IS DISTINCT FROM OLD.attribute_key_id OR NEW.value IS DISTINCT FROM OLD.value THEN
              RAISE EXCEPTION 'Attribute pair is immutable' USING ERRCODE='23514', CONSTRAINT='catalog_pair_identity';
            END IF;
          ELSIF TG_TABLE_NAME = 'product_variants' THEN
            IF NEW.product_id IS DISTINCT FROM OLD.product_id THEN
              RAISE EXCEPTION 'Variant product is immutable' USING ERRCODE='23514', CONSTRAINT='catalog_variant_identity';
            END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER guard_key_identity BEFORE UPDATE ON app.attribute_keys FOR EACH ROW EXECUTE FUNCTION app.guard_catalog_identity();
        CREATE TRIGGER guard_pair_identity BEFORE UPDATE ON app.attribute_pairs FOR EACH ROW EXECUTE FUNCTION app.guard_catalog_identity();
        CREATE TRIGGER guard_variant_identity BEFORE UPDATE ON app.product_variants FOR EACH ROW EXECUTE FUNCTION app.guard_catalog_identity();

        REVOKE ALL ON FUNCTION app.version_catalog_entity(), app.version_catalog_product_graph(),
          app.version_catalog_variant_graph(), app.guard_catalog_identity() FROM PUBLIC, anon, authenticated, service_role;
        """);

    private static void RemoveManagementRules(MigrationBuilder migration) => migration.Sql("""
        DO $$ DECLARE t text; BEGIN
          IF EXISTS (SELECT 1 FROM app.catalog_operations) THEN
            RAISE EXCEPTION 'Catalog operation history exists; use a forward migration';
          END IF;
          FOREACH t IN ARRAY ARRAY['products','product_variants','categories','attribute_keys','variant_stock'] LOOP
            EXECUTE format('DROP TRIGGER zz_version_catalog_entity ON app.%I', t);
            EXECUTE format('ALTER TABLE app.%I DROP CONSTRAINT %I', t, 'CK_' || t || '_version');
          END LOOP;
          FOREACH t IN ARRAY ARRAY['categories','attribute_keys','attribute_pairs','variant_attribute_pairs','variant_stock'] LOOP
            EXECUTE format('DROP TRIGGER catalog_pricing_write ON app.%I', t);
          END LOOP;
        END $$;
        DROP TRIGGER catalog_operation_immutable ON app.catalog_operations;
        DROP TRIGGER version_product_variants ON app.product_variants;
        DROP TRIGGER version_product_categories ON app.product_categories;
        DROP TRIGGER version_variant_attributes ON app.variant_attribute_pairs;
        DROP TRIGGER guard_key_identity ON app.attribute_keys;
        DROP TRIGGER guard_pair_identity ON app.attribute_pairs;
        DROP TRIGGER guard_variant_identity ON app.product_variants;
        DROP FUNCTION app.version_catalog_entity();
        DROP FUNCTION app.version_catalog_product_graph();
        DROP FUNCTION app.version_catalog_variant_graph();
        DROP FUNCTION app.guard_catalog_identity();
        """);
}

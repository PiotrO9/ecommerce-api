using Microsoft.EntityFrameworkCore.Migrations;

namespace E_commerce_backend.Data.Migrations;

public partial class AddCollections
{
    private static void InstallRules(MigrationBuilder migration) => migration.Sql("""
        DO $$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['collections','collection_products','collection_operations'] LOOP
            EXECUTE format('ALTER TABLE app.%I ENABLE ROW LEVEL SECURITY', t);
            EXECUTE format('REVOKE ALL ON TABLE app.%I FROM PUBLIC, anon, authenticated, service_role', t);
          END LOOP;
          FOREACH t IN ARRAY ARRAY['collections','collection_products'] LOOP
            EXECUTE format('CREATE TRIGGER catalog_pricing_write BEFORE INSERT OR UPDATE OR DELETE ON app.%I FOR EACH STATEMENT EXECUTE FUNCTION app.lock_catalog_pricing_write()', t);
          END LOOP;
        END $$;
        CREATE TRIGGER collection_operation_immutable BEFORE UPDATE OR DELETE ON app.collection_operations
          FOR EACH ROW EXECUTE FUNCTION app.reject_promotion_history_mutation();

        CREATE FUNCTION app.version_collection() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP = 'INSERT' THEN NEW.version := 1;
          ELSE NEW.version := OLD.version + 1; NEW.created_at := OLD.created_at; END IF;
          NEW.updated_at := clock_timestamp(); RETURN NEW;
        END $$;
        CREATE TRIGGER version_collection BEFORE INSERT OR UPDATE ON app.collections
          FOR EACH ROW EXECUTE FUNCTION app.version_collection();

        CREATE FUNCTION app.version_collection_members() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP <> 'INSERT' THEN
            UPDATE app.collections SET updated_at = clock_timestamp() WHERE id = OLD.collection_id;
          END IF;
          IF TG_OP = 'INSERT' OR (TG_OP = 'UPDATE' AND NEW.collection_id IS DISTINCT FROM OLD.collection_id) THEN
            UPDATE app.collections SET updated_at = clock_timestamp() WHERE id = NEW.collection_id;
          END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER version_collection_members AFTER INSERT OR UPDATE OR DELETE ON app.collection_products
          FOR EACH ROW EXECUTE FUNCTION app.version_collection_members();

        CREATE FUNCTION app.check_collection_publication() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF EXISTS (SELECT 1 FROM app.collections WHERE id = NEW.id AND status = 'Published')
            AND NOT EXISTS (SELECT 1 FROM app.collection_products l JOIN app.products p ON p.id = l.product_id
              WHERE l.collection_id = NEW.id AND p.status = 'Published'
              AND EXISTS (SELECT 1 FROM app.product_categories c WHERE c.product_id = p.id)
              AND EXISTS (SELECT 1 FROM app.product_variants v WHERE v.product_id = p.id AND v.status = 'Published'
                AND EXISTS (SELECT 1 FROM app.variant_price_changes c WHERE c.variant_id = v.id AND c.effective_at <= clock_timestamp()))) THEN
            RAISE EXCEPTION 'Published collection requires a public product';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER check_collection_publication AFTER INSERT OR UPDATE ON app.collections
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_collection_publication();
        """);

    private static void RemoveRules(MigrationBuilder migration) => migration.Sql("""
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM app.promotions WHERE scope = 'collection')
            OR EXISTS (SELECT 1 FROM app.collection_operations) THEN
            RAISE EXCEPTION 'Cannot roll back collections with promotion or operation history; use a forward migration';
          END IF;
        END $$;
        DROP TRIGGER check_collection_publication ON app.collections;
        DROP TRIGGER version_collection ON app.collections;
        DROP TRIGGER version_collection_members ON app.collection_products;
        DROP TRIGGER catalog_pricing_write ON app.collections;
        DROP TRIGGER catalog_pricing_write ON app.collection_products;
        DROP TRIGGER collection_operation_immutable ON app.collection_operations;
        DROP FUNCTION app.check_collection_publication();
        DROP FUNCTION app.version_collection();
        DROP FUNCTION app.version_collection_members();
        """);
}

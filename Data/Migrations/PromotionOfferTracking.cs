using Microsoft.EntityFrameworkCore.Migrations;

namespace E_commerce_backend.Data.Migrations;

public partial class AddProductPromotions
{
    private static void InstallOfferTracking(MigrationBuilder migration) => migration.Sql("""
        DO $$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['promotions','promotion_variants','price_reductions','promotion_operations','variant_offer_events'] LOOP
            EXECUTE format('ALTER TABLE app.%I ENABLE ROW LEVEL SECURITY', t);
            EXECUTE format('REVOKE ALL ON TABLE app.%I FROM PUBLIC, anon, authenticated, service_role', t);
          END LOOP;
        END $$;
        REVOKE ALL ON SEQUENCE app.variant_offer_events_id_seq FROM PUBLIC, anon, authenticated, service_role;

        INSERT INTO app.variant_offer_events (variant_id, occurred_at, visible, complete_from_birth, source)
        SELECT v.id, clock_timestamp(), v.status = 'Published' AND p.status = 'Published'
          AND EXISTS (SELECT 1 FROM app.product_categories c WHERE c.product_id = p.id), false, 'import'
        FROM app.product_variants v JOIN app.products p ON p.id = v.product_id;

        CREATE FUNCTION app.lock_catalog_pricing_write() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN PERFORM pg_advisory_xact_lock(20261031, 3); RETURN NULL; END $$;
        DO $$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['products','product_variants','product_categories','variant_price_changes','promotions','promotion_variants'] LOOP
            EXECUTE format('CREATE TRIGGER catalog_pricing_write BEFORE INSERT OR UPDATE OR DELETE ON app.%I FOR EACH STATEMENT EXECUTE FUNCTION app.lock_catalog_pricing_write()', t);
          END LOOP;
        END $$;

        CREATE FUNCTION app.record_variant_offer(v_id uuid) RETURNS void LANGUAGE plpgsql AS $$
        DECLARE current_visible boolean; last_visible boolean; complete boolean;
        BEGIN
          SELECT v.status = 'Published' AND p.status = 'Published'
             AND EXISTS (SELECT 1 FROM app.product_categories c WHERE c.product_id = p.id)
          INTO current_visible FROM app.product_variants v JOIN app.products p ON p.id = v.product_id WHERE v.id = v_id;
          IF NOT FOUND THEN RETURN; END IF;
          SELECT visible, complete_from_birth INTO last_visible, complete FROM app.variant_offer_events
            WHERE variant_id = v_id ORDER BY occurred_at DESC, id DESC LIMIT 1;
          IF NOT FOUND OR last_visible IS DISTINCT FROM current_visible THEN
            INSERT INTO app.variant_offer_events (variant_id, occurred_at, visible, complete_from_birth, source)
              VALUES (v_id, clock_timestamp(), current_visible, COALESCE(complete, true), 'database');
          END IF;
        END $$;
        CREATE FUNCTION app.track_variant_offer() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN PERFORM app.record_variant_offer(NEW.id); RETURN NULL; END $$;
        CREATE TRIGGER track_variant_offer AFTER INSERT OR UPDATE ON app.product_variants
          FOR EACH ROW EXECUTE FUNCTION app.track_variant_offer();
        CREATE FUNCTION app.record_product_offers(p_id uuid) RETURNS void LANGUAGE plpgsql AS $$
        DECLARE v_id uuid; BEGIN
          FOR v_id IN SELECT id FROM app.product_variants WHERE product_id = p_id ORDER BY id LOOP
            PERFORM app.record_variant_offer(v_id);
          END LOOP;
        END $$;
        CREATE FUNCTION app.track_product_offers() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN PERFORM app.record_product_offers(NEW.id); RETURN NULL; END $$;
        CREATE TRIGGER track_product_offers AFTER UPDATE ON app.products
          FOR EACH ROW WHEN (OLD.status IS DISTINCT FROM NEW.status) EXECUTE FUNCTION app.track_product_offers();
        CREATE FUNCTION app.track_category_offers() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP <> 'INSERT' THEN PERFORM app.record_product_offers(OLD.product_id); END IF;
          IF TG_OP <> 'DELETE' THEN PERFORM app.record_product_offers(NEW.product_id); END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER track_category_offers AFTER INSERT OR UPDATE OR DELETE ON app.product_categories
          FOR EACH ROW EXECUTE FUNCTION app.track_category_offers();
        CREATE FUNCTION app.reject_promotion_history_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Promotion and offer history is append-only'; END $$;
        DO $$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['variant_offer_events','price_reductions','promotion_operations'] LOOP
            EXECUTE format('CREATE TRIGGER promotion_history_immutable BEFORE UPDATE OR DELETE ON app.%I FOR EACH ROW EXECUTE FUNCTION app.reject_promotion_history_mutation()', t);
          END LOOP;
        END $$;
        """);

    private static void RemoveOfferTracking(MigrationBuilder migration) => migration.Sql("""
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM app.variant_price_changes WHERE promotion_operation_id IS NOT NULL) THEN
            RAISE EXCEPTION 'Cannot roll back promotions after price operations exist; preserve history and use a forward migration';
          END IF;
        END $$;
        DROP TRIGGER track_category_offers ON app.product_categories;
        DROP TRIGGER track_product_offers ON app.products;
        DROP TRIGGER track_variant_offer ON app.product_variants;
        DROP FUNCTION app.track_category_offers();
        DROP FUNCTION app.track_product_offers();
        DROP FUNCTION app.track_variant_offer();
        DROP FUNCTION app.record_product_offers(uuid);
        DROP FUNCTION app.record_variant_offer(uuid);
        DO $$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['products','product_variants','product_categories','variant_price_changes','promotions','promotion_variants'] LOOP
            EXECUTE format('DROP TRIGGER catalog_pricing_write ON app.%I', t);
          END LOOP;
          FOREACH t IN ARRAY ARRAY['variant_offer_events','price_reductions','promotion_operations'] LOOP
            EXECUTE format('DROP TRIGGER promotion_history_immutable ON app.%I', t);
          END LOOP;
        END $$;
        DROP FUNCTION app.reject_promotion_history_mutation();
        DROP FUNCTION app.lock_catalog_pricing_write();
        """);
}

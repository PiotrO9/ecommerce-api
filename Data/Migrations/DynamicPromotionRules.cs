using Microsoft.EntityFrameworkCore.Migrations;

namespace E_commerce_backend.Data.Migrations;

public partial class AddDynamicPromotions
{
    private static void InstallDynamicRules(MigrationBuilder migration) => migration.Sql("""
        DO $$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['promotion_exclusions','promotion_conflicts','promotion_reconciliation'] LOOP
            EXECUTE format('ALTER TABLE app.%I ENABLE ROW LEVEL SECURITY', t);
            EXECUTE format('REVOKE ALL ON TABLE app.%I FROM PUBLIC, anon, authenticated, service_role', t);
            EXECUTE format('CREATE TRIGGER catalog_pricing_write BEFORE INSERT OR UPDATE OR DELETE ON app.%I FOR EACH STATEMENT EXECUTE FUNCTION app.lock_catalog_pricing_write()', t);
          END LOOP;
        END $$;
        INSERT INTO app.promotion_reconciliation(id,dirty,next_attempt_at) VALUES(1,false,clock_timestamp());

        CREATE VIEW app.dynamic_promotion_targets WITH (security_invoker=true) AS
          WITH RECURSIVE descendants(ancestor_id,id) AS (
            SELECT id,id FROM app.categories
            UNION ALL
            SELECT d.ancestor_id,c.id FROM descendants d JOIN app.categories c ON c.parent_id=d.id
          )
          SELECT p.id AS promotion_id,v.id AS variant_id
          FROM app.promotions p CROSS JOIN app.product_variants v JOIN app.products product ON product.id=v.product_id
          WHERE p.scope IN ('catalog','category') AND v.status='Published' AND product.status='Published'
            AND EXISTS (SELECT 1 FROM app.product_categories pc WHERE pc.product_id=product.id)
            AND (p.scope='catalog' OR EXISTS (
              SELECT 1 FROM app.product_categories pc JOIN descendants d ON d.id=pc.category_id
              WHERE pc.product_id=product.id AND d.ancestor_id=p.category_id))
            AND NOT EXISTS (SELECT 1 FROM app.promotion_exclusions e WHERE e.promotion_id=p.id
              AND (e.product_id=product.id OR e.variant_id=v.id));
        CREATE VIEW app.active_promotion_claims WITH (security_invoker=true) AS
          SELECT t.promotion_id,t.variant_id FROM app.dynamic_promotion_targets t JOIN app.promotions p ON p.id=t.promotion_id WHERE p.status='Active'
          UNION ALL
          SELECT m.promotion_id,m.variant_id FROM app.promotion_variants m JOIN app.promotions p ON p.id=m.promotion_id
          WHERE m.ended_at IS NULL AND p.status='Active' AND p.scope IN ('product','collection');
        REVOKE ALL ON app.dynamic_promotion_targets,app.active_promotion_claims FROM PUBLIC, anon, authenticated, service_role;

        CREATE FUNCTION app.mark_dynamic_promotions_dirty() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF EXISTS (SELECT 1 FROM app.promotions WHERE status='Active' AND scope IN ('catalog','category')) THEN
            INSERT INTO app.promotion_reconciliation(id,dirty,next_attempt_at) VALUES(1,true,clock_timestamp())
              ON CONFLICT (id) DO UPDATE SET dirty=true;
          END IF;
          RETURN NULL;
        END $$;
        DO $$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['products','product_variants','product_categories','categories','variant_price_changes','promotions','promotion_exclusions'] LOOP
            EXECUTE format('CREATE TRIGGER mark_dynamic_promotions_dirty AFTER INSERT OR UPDATE OR DELETE ON app.%I FOR EACH STATEMENT EXECUTE FUNCTION app.mark_dynamic_promotions_dirty()', t);
          END LOOP;
        END $$;

        CREATE FUNCTION app.guard_dynamic_promotion_overlap() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          -- Deferred row triggers also run during large imports. Legacy scopes have
          -- their own unique-active-membership index; avoid scanning their claims.
          IF NOT EXISTS (SELECT 1 FROM app.promotions WHERE scope IN ('catalog','category') AND status<>'Draft') THEN
            RETURN NULL;
          END IF;
          IF EXISTS (SELECT variant_id FROM app.active_promotion_claims GROUP BY variant_id HAVING count(DISTINCT promotion_id)>1) THEN
            RAISE EXCEPTION 'Variant qualifies for overlapping promotions' USING ERRCODE='23514',CONSTRAINT='dynamic_promotion_overlap';
          END IF;
          IF EXISTS (SELECT 1 FROM app.promotion_variants m JOIN app.promotions p ON p.id=m.promotion_id
            WHERE p.scope IN ('catalog','category') AND p.status<>'Active' AND m.ended_at IS NULL) THEN
            RAISE EXCEPTION 'Inactive dynamic promotion has active memberships' USING ERRCODE='23514',CONSTRAINT='dynamic_promotion_state';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER guard_dynamic_promotion_overlap AFTER INSERT OR UPDATE ON app.promotions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.guard_dynamic_promotion_overlap();
        CREATE CONSTRAINT TRIGGER guard_dynamic_product_overlap AFTER INSERT OR UPDATE ON app.products
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.status='Published') EXECUTE FUNCTION app.guard_dynamic_promotion_overlap();
        CREATE CONSTRAINT TRIGGER guard_dynamic_variant_overlap AFTER INSERT OR UPDATE ON app.product_variants
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.status='Published') EXECUTE FUNCTION app.guard_dynamic_promotion_overlap();
        CREATE CONSTRAINT TRIGGER guard_dynamic_category_overlap AFTER INSERT OR UPDATE OR DELETE ON app.product_categories
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.guard_dynamic_promotion_overlap();
        CREATE CONSTRAINT TRIGGER guard_dynamic_tree_overlap AFTER UPDATE ON app.categories
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (OLD.parent_id IS DISTINCT FROM NEW.parent_id) EXECUTE FUNCTION app.guard_dynamic_promotion_overlap();
        CREATE CONSTRAINT TRIGGER guard_dynamic_exclusion_overlap AFTER INSERT OR UPDATE OR DELETE ON app.promotion_exclusions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.guard_dynamic_promotion_overlap();

        CREATE FUNCTION app.guard_dynamic_promotion_rule() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF OLD.scope IN ('catalog','category') AND OLD.status<>'Draft' AND
            (NEW.scope,NEW.category_id,NEW.discount_type,NEW.discount_value) IS DISTINCT FROM
            (OLD.scope,OLD.category_id,OLD.discount_type,OLD.discount_value) THEN
            RAISE EXCEPTION 'Running promotion rule is immutable' USING ERRCODE='23514',CONSTRAINT='dynamic_promotion_rule';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER guard_dynamic_promotion_rule BEFORE UPDATE ON app.promotions FOR EACH ROW EXECUTE FUNCTION app.guard_dynamic_promotion_rule();
        CREATE FUNCTION app.guard_promotion_period() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (NEW.id,NEW.promotion_id,NEW.variant_id,NEW.started_at) IS DISTINCT FROM (OLD.id,OLD.promotion_id,OLD.variant_id,OLD.started_at)
            OR OLD.ended_at IS NOT NULL AND NEW.ended_at IS DISTINCT FROM OLD.ended_at THEN
            RAISE EXCEPTION 'Promotion period identity and closed end are immutable' USING ERRCODE='23514',CONSTRAINT='promotion_period_immutable';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER guard_promotion_period BEFORE UPDATE ON app.promotion_variants FOR EACH ROW EXECUTE FUNCTION app.guard_promotion_period();
        REVOKE ALL ON FUNCTION app.mark_dynamic_promotions_dirty(),app.guard_dynamic_promotion_overlap(),app.guard_dynamic_promotion_rule()
          FROM PUBLIC, anon, authenticated, service_role;
        REVOKE ALL ON FUNCTION app.guard_promotion_period() FROM PUBLIC, anon, authenticated, service_role;
        """);

    private static void RemoveDynamicRules(MigrationBuilder migration) => migration.Sql("""
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM app.promotions WHERE scope IN ('catalog','category'))
            OR EXISTS (SELECT 1 FROM app.promotion_operations WHERE kind='dynamic_reconcile') THEN
            RAISE EXCEPTION 'Dynamic promotion history exists; use a forward migration';
          END IF;
        END $$;
        DROP TRIGGER guard_dynamic_promotion_rule ON app.promotions;
        DROP TRIGGER guard_promotion_period ON app.promotion_variants;
        DROP TRIGGER guard_dynamic_promotion_overlap ON app.promotions;
        DROP TRIGGER guard_dynamic_product_overlap ON app.products;
        DROP TRIGGER guard_dynamic_variant_overlap ON app.product_variants;
        DROP TRIGGER guard_dynamic_category_overlap ON app.product_categories;
        DROP TRIGGER guard_dynamic_tree_overlap ON app.categories;
        DROP TRIGGER guard_dynamic_exclusion_overlap ON app.promotion_exclusions;
        DO $$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['products','product_variants','product_categories','categories','variant_price_changes','promotions','promotion_exclusions'] LOOP
            EXECUTE format('DROP TRIGGER mark_dynamic_promotions_dirty ON app.%I', t);
          END LOOP;
        END $$;
        DROP VIEW app.active_promotion_claims;
        DROP VIEW app.dynamic_promotion_targets;
        DROP FUNCTION app.mark_dynamic_promotions_dirty();
        DROP FUNCTION app.guard_dynamic_promotion_overlap();
        DROP FUNCTION app.guard_dynamic_promotion_rule();
        DROP FUNCTION app.guard_promotion_period();
        """);
}

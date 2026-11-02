using Microsoft.EntityFrameworkCore.Migrations;

namespace E_commerce_backend.Data.Migrations;

public partial class AddInventoryReservations
{
    private static void InstallInventoryRules(MigrationBuilder migration) => migration.Sql("""
        DO $$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['orders','order_items','stock_reservations','stock_reservation_items','stock_movements','inventory_payment_receipts'] LOOP
            EXECUTE format('ALTER TABLE app.%I ENABLE ROW LEVEL SECURITY', t);
            EXECUTE format('REVOKE ALL ON TABLE app.%I FROM PUBLIC, anon, authenticated, service_role', t);
            EXECUTE format('CREATE TRIGGER catalog_pricing_write BEFORE INSERT OR UPDATE OR DELETE ON app.%I FOR EACH STATEMENT EXECUTE FUNCTION app.lock_catalog_pricing_write()', t);
          END LOOP;
          FOREACH t IN ARRAY ARRAY['order_items','stock_reservation_items','stock_movements','inventory_payment_receipts'] LOOP
            EXECUTE format('CREATE TRIGGER inventory_history_immutable BEFORE UPDATE OR DELETE ON app.%I FOR EACH ROW EXECUTE FUNCTION app.reject_promotion_history_mutation()', t);
          END LOOP;
        END $$;

        CREATE FUNCTION app.guard_inventory_identity() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Inventory history is immutable' USING ERRCODE='23514'; END IF;
          IF TG_TABLE_NAME = 'orders' THEN
            IF (to_jsonb(NEW) - 'status' - 'updated_at') IS DISTINCT FROM (to_jsonb(OLD) - 'status' - 'updated_at')
              OR NOT (NEW.status = OLD.status
                 OR (OLD.status = 'PendingPayment' AND NEW.status IN ('Paid','Cancelled','Expired','PaymentReview'))
                 OR (OLD.status IN ('Cancelled','Expired') AND NEW.status = 'PaymentReview')) THEN
              RAISE EXCEPTION 'Invalid order transition' USING ERRCODE='23514';
            END IF;
          ELSE
            IF (to_jsonb(NEW) - 'status' - 'updated_at') IS DISTINCT FROM (to_jsonb(OLD) - 'status' - 'updated_at')
              OR NOT (NEW.status = OLD.status OR (OLD.status = 'Active' AND NEW.status IN ('Consumed','Released','Expired'))) THEN
              RAISE EXCEPTION 'Invalid reservation transition' USING ERRCODE='23514';
            END IF;
            IF NEW.status='Expired' AND OLD.status='Active' AND NEW.expires_at > clock_timestamp() THEN
              RAISE EXCEPTION 'Reservation has not expired' USING ERRCODE='23514';
            END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER inventory_identity BEFORE UPDATE OR DELETE ON app.orders FOR EACH ROW EXECUTE FUNCTION app.guard_inventory_identity();
        CREATE TRIGGER inventory_identity BEFORE UPDATE OR DELETE ON app.stock_reservations FOR EACH ROW EXECUTE FUNCTION app.guard_inventory_identity();

        CREATE FUNCTION app.assert_inventory_capacity(v_id uuid) RETURNS void LANGUAGE plpgsql AS $$
        DECLARE held bigint; on_hand integer; BEGIN
          SELECT COALESCE(sum(i.quantity),0) INTO held FROM app.stock_reservation_items i
            JOIN app.stock_reservations r ON r.id=i.reservation_id
            WHERE i.variant_id=v_id AND r.status='Active' AND r.expires_at > clock_timestamp();
          SELECT quantity_on_hand INTO on_hand FROM app.variant_stock WHERE variant_id=v_id;
          IF held > COALESCE(on_hand,0) THEN
            RAISE EXCEPTION 'Stock is below active reservations' USING ERRCODE='23514', CONSTRAINT='stock_below_reservations';
          END IF;
        END $$;

        CREATE FUNCTION app.assert_inventory_order(o_id uuid) RETURNS void LANGUAGE plpgsql AS $$
        DECLARE o app.orders; r app.stock_reservations; v_id uuid; BEGIN
          SELECT * INTO o FROM app.orders WHERE id=o_id;
          IF NOT FOUND THEN RETURN; END IF;
          SELECT * INTO r FROM app.stock_reservations WHERE order_id=o_id;
          IF NOT FOUND OR NOT (
            (o.status='PendingPayment' AND r.status='Active') OR (o.status='Paid' AND r.status='Consumed')
            OR (o.status='Cancelled' AND r.status='Released') OR (o.status='Expired' AND r.status='Expired')
            OR (o.status='PaymentReview' AND r.status IN ('Expired','Released'))) THEN
            RAISE EXCEPTION 'Order and reservation disagree' USING ERRCODE='23514';
          END IF;
          IF NOT EXISTS (SELECT 1 FROM app.order_items WHERE order_id=o_id)
            OR o.goods_total IS DISTINCT FROM (SELECT sum(quantity*unit_price) FROM app.order_items WHERE order_id=o_id)
            OR EXISTS (
              SELECT 1 FROM (SELECT variant_id,quantity FROM app.order_items WHERE order_id=o_id) a
              FULL JOIN (SELECT variant_id,quantity FROM app.stock_reservation_items WHERE reservation_id=r.id) b USING(variant_id)
              WHERE a.quantity IS DISTINCT FROM b.quantity)
            OR EXISTS (SELECT 1 FROM app.order_items i JOIN app.variant_price_changes p ON p.id=i.price_change_id
              WHERE i.order_id=o_id AND (p.variant_id<>i.variant_id OR p.currency<>o.currency OR p.selling_amount<>i.unit_price)) THEN
            RAISE EXCEPTION 'Incomplete purchase snapshot' USING ERRCODE='23514';
          END IF;
          IF r.status='Consumed' THEN
            IF EXISTS (SELECT 1 FROM app.stock_reservation_items i LEFT JOIN app.stock_movements m
              ON m.reservation_id=i.reservation_id AND m.variant_id=i.variant_id
              WHERE i.reservation_id=r.id AND (m.id IS NULL OR m.quantity_before-m.quantity_after<>i.quantity))
              OR NOT EXISTS (SELECT 1 FROM app.inventory_payment_receipts WHERE order_id=o_id AND outcome='Paid') THEN
              RAISE EXCEPTION 'Consumed reservation lacks settlement' USING ERRCODE='23514';
            END IF;
          ELSIF EXISTS (SELECT 1 FROM app.stock_movements WHERE reservation_id=r.id) THEN
            RAISE EXCEPTION 'Unconsumed reservation has movements' USING ERRCODE='23514';
          END IF;
          IF EXISTS (SELECT 1 FROM app.inventory_payment_receipts p WHERE p.order_id=o_id
            AND (p.amount<>o.goods_total OR p.currency<>o.currency OR p.outcome<>o.status)) THEN
            RAISE EXCEPTION 'Invalid payment receipt' USING ERRCODE='23514';
          END IF;
          IF o.status='PaymentReview' AND NOT EXISTS (SELECT 1 FROM app.inventory_payment_receipts WHERE order_id=o_id AND outcome='PaymentReview') THEN
            RAISE EXCEPTION 'Payment review lacks a receipt' USING ERRCODE='23514';
          END IF;
          FOR v_id IN SELECT variant_id FROM app.stock_reservation_items WHERE reservation_id=r.id LOOP
            PERFORM app.assert_inventory_capacity(v_id);
          END LOOP;
        END $$;

        CREATE FUNCTION app.check_inventory_graph() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE data jsonb; o_id uuid; BEGIN
          data := CASE WHEN TG_OP='DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END;
          IF TG_TABLE_NAME='variant_stock' THEN
            PERFORM app.assert_inventory_capacity((data->>'variant_id')::uuid);
          ELSE
            IF TG_TABLE_NAME='orders' THEN o_id:=(data->>'id')::uuid;
            ELSIF TG_TABLE_NAME IN ('order_items','stock_reservations','inventory_payment_receipts') THEN o_id:=(data->>'order_id')::uuid;
            ELSE SELECT order_id INTO o_id FROM app.stock_reservations WHERE id=(data->>'reservation_id')::uuid;
            END IF;
            PERFORM app.assert_inventory_order(o_id);
          END IF;
          RETURN NULL;
        END $$;
        DO $$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['orders','order_items','stock_reservations','stock_reservation_items','stock_movements','inventory_payment_receipts','variant_stock'] LOOP
            EXECUTE format('CREATE CONSTRAINT TRIGGER inventory_graph AFTER INSERT OR UPDATE OR DELETE ON app.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_inventory_graph()',t);
          END LOOP;
        END $$;
        REVOKE ALL ON FUNCTION app.guard_inventory_identity(), app.assert_inventory_capacity(uuid),
          app.assert_inventory_order(uuid), app.check_inventory_graph() FROM PUBLIC, anon, authenticated, service_role;
        """);

    private static void RemoveInventoryRules(MigrationBuilder migration) => migration.Sql("""
        DO $$ DECLARE t text; BEGIN
          IF EXISTS (SELECT 1 FROM app.orders) THEN RAISE EXCEPTION 'Order history exists; use a forward migration'; END IF;
          FOREACH t IN ARRAY ARRAY['orders','order_items','stock_reservations','stock_reservation_items','stock_movements','inventory_payment_receipts','variant_stock'] LOOP
            EXECUTE format('DROP TRIGGER inventory_graph ON app.%I',t);
          END LOOP;
        END $$;
        DROP FUNCTION app.check_inventory_graph();
        DROP FUNCTION app.assert_inventory_order(uuid);
        DROP FUNCTION app.assert_inventory_capacity(uuid);
        DROP TRIGGER inventory_identity ON app.orders;
        DROP TRIGGER inventory_identity ON app.stock_reservations;
        DROP FUNCTION app.guard_inventory_identity();
        """);
}

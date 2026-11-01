using Microsoft.EntityFrameworkCore.Migrations;

namespace E_commerce_backend.Data.Migrations;

public partial class AddShoppingCartsAndFavorites
{
    private static void InstallShoppingRules(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE app.shopping_states ENABLE ROW LEVEL SECURITY;
        ALTER TABLE app.cart_items ENABLE ROW LEVEL SECURITY;
        ALTER TABLE app.favorite_lists ENABLE ROW LEVEL SECURITY;
        ALTER TABLE app.favorite_items ENABLE ROW LEVEL SECURITY;
        REVOKE ALL ON app.shopping_states, app.cart_items, app.favorite_lists, app.favorite_items
            FROM PUBLIC, anon, authenticated, service_role;

        CREATE FUNCTION app.lock_shopping_items() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            PERFORM pg_advisory_xact_lock_shared(20261101, 4);
            RETURN NULL;
        END;
        $$;
        REVOKE ALL ON FUNCTION app.lock_shopping_items() FROM PUBLIC, anon, authenticated, service_role;
        CREATE TRIGGER cart_items_lock BEFORE INSERT OR UPDATE OR DELETE ON app.cart_items
            FOR EACH STATEMENT EXECUTE FUNCTION app.lock_shopping_items();
        CREATE TRIGGER favorite_items_lock BEFORE INSERT OR UPDATE OR DELETE ON app.favorite_items
            FOR EACH STATEMENT EXECUTE FUNCTION app.lock_shopping_items();

        CREATE FUNCTION app.bump_cart_version() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_OP = 'UPDATE' AND NEW.shopping_state_id <> OLD.shopping_state_id THEN
                RAISE EXCEPTION 'Cart ownership is immutable' USING ERRCODE = '23514';
            END IF;
            UPDATE app.shopping_states SET cart_version=cart_version+1, updated_at=clock_timestamp()
                WHERE id=CASE WHEN TG_OP='DELETE' THEN OLD.shopping_state_id ELSE NEW.shopping_state_id END;
            RETURN NULL;
        END;
        $$;
        REVOKE ALL ON FUNCTION app.bump_cart_version() FROM PUBLIC, anon, authenticated, service_role;
        CREATE TRIGGER cart_items_version AFTER INSERT OR UPDATE OR DELETE ON app.cart_items
            FOR EACH ROW EXECUTE FUNCTION app.bump_cart_version();

        CREATE FUNCTION app.bump_favorites_version() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_OP = 'UPDATE' AND NEW.user_id <> OLD.user_id THEN
                RAISE EXCEPTION 'Favorites ownership is immutable' USING ERRCODE = '23514';
            END IF;
            UPDATE app.favorite_lists SET version=version+1, updated_at=clock_timestamp()
                WHERE user_id=CASE WHEN TG_OP='DELETE' THEN OLD.user_id ELSE NEW.user_id END;
            RETURN NULL;
        END;
        $$;
        REVOKE ALL ON FUNCTION app.bump_favorites_version() FROM PUBLIC, anon, authenticated, service_role;
        CREATE TRIGGER favorite_items_version AFTER INSERT OR UPDATE OR DELETE ON app.favorite_items
            FOR EACH ROW EXECUTE FUNCTION app.bump_favorites_version();
        """);

    private static void RemoveShoppingRules(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TRIGGER favorite_items_version ON app.favorite_items;
        DROP TRIGGER cart_items_version ON app.cart_items;
        DROP TRIGGER favorite_items_lock ON app.favorite_items;
        DROP TRIGGER cart_items_lock ON app.cart_items;
        DROP FUNCTION app.bump_favorites_version();
        DROP FUNCTION app.bump_cart_version();
        DROP FUNCTION app.lock_shopping_items();
        """);
}

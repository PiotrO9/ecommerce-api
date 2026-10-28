\set ON_ERROR_STOP on
DO $$
BEGIN
    IF current_database() !~ '^ecomm_perf' THEN
        RAISE EXCEPTION 'Run this fixture only against an isolated ecomm_perf* database';
    END IF;
    IF EXISTS (SELECT 1 FROM app.products WHERE slug LIKE 'perf-produkt-%') THEN
        RAISE EXCEPTION 'Performance fixture already exists';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM app.categories WHERE slug = 'demo-odziez') THEN
        RAISE EXCEPTION 'Run the high/high/high demo seed before this fixture';
    END IF;
    IF (SELECT count(*) FROM app.products) >= 5000 THEN
        RAISE EXCEPTION 'The catalog already has 5000 products';
    END IF;
END $$;
SELECT (5000 - count(*))::int AS extra FROM app.products \gset
BEGIN;
ALTER TABLE app.products DISABLE TRIGGER USER;
ALTER TABLE app.product_variants DISABLE TRIGGER USER;
ALTER TABLE app.variant_attribute_pairs DISABLE TRIGGER USER;
INSERT INTO app.attribute_keys (id,code,display_name) VALUES
  (md5('perf-key-perf-kolor')::uuid,'perf-kolor','Kolor'),
  (md5('perf-key-perf-rozmiar')::uuid,'perf-rozmiar','Rozmiar'),
  (md5('perf-key-perf-marka')::uuid,'perf-marka','Marka'),
  (md5('perf-key-perf-material')::uuid,'perf-material','Materiał'),
  (md5('perf-key-perf-styl')::uuid,'perf-styl','Styl');
INSERT INTO app.attribute_pairs (id,attribute_key_id,value)
SELECT md5('perf-pair-'||p.code||'-'||p.val)::uuid,k.id,p.val
FROM (VALUES
  ('perf-kolor','czerwony'),('perf-kolor','niebieski'),('perf-kolor','czarny'),
  ('perf-rozmiar','S'),('perf-rozmiar','M'),('perf-rozmiar','L'),
  ('perf-marka','MarkaA'),('perf-marka','MarkaB'),('perf-marka','MarkaC'),
  ('perf-material','bawelna'),('perf-material','len'),('perf-material','poliester'),
  ('perf-styl','casual'),('perf-styl','sport'),('perf-styl','elegancki')
) AS p(code,val) JOIN app.attribute_keys k ON k.code=p.code;
INSERT INTO app.products (id,name,slug,status,published_at)
SELECT md5('perf-product-'||n)::uuid,
  CASE n%5 WHEN 0 THEN 'Koszulka' WHEN 1 THEN 'Bluza' WHEN 2 THEN 'Spodnie' WHEN 3 THEN 'Buty' ELSE 'Kurtka' END || ' Performance ' || lpad(n::text,5,'0'),
  'perf-produkt-'||lpad(n::text,5,'0'),'Published',now()-make_interval(days=>(n%180))
FROM generate_series(1,:extra) AS g(n);
WITH cats AS (SELECT array_agg(id ORDER BY slug) AS ids FROM app.categories WHERE slug LIKE 'demo-kategoria-%'),
root AS (SELECT id FROM app.categories WHERE slug='demo-odziez')
INSERT INTO app.product_categories (product_id,category_id)
SELECT md5('perf-product-'||n)::uuid,
  CASE WHEN n%4=0 THEN root.id ELSE cats.ids[(n%array_length(cats.ids,1))+1] END
FROM generate_series(1,:extra) AS g(n) CROSS JOIN cats CROSS JOIN root;
INSERT INTO app.product_variants (id,product_id,sku,status,image_urls,published_at)
SELECT md5('perf-variant-'||n||'-'||v)::uuid,md5('perf-product-'||n)::uuid,
  'PERF-'||lpad(n::text,5,'0')||'-'||v,'Published',ARRAY[]::text[],now()-make_interval(days=>(n%180))
FROM generate_series(1,:extra) AS g(n) CROSS JOIN generate_series(1,3) AS a(v);
INSERT INTO app.variant_attribute_pairs (variant_id,attribute_pair_id)
SELECT md5('perf-variant-'||n||'-'||v)::uuid,md5('perf-pair-'||kv.code||'-'||kv.val)::uuid
FROM generate_series(1,:extra) AS g(n)
CROSS JOIN generate_series(1,3) AS a(v)
CROSS JOIN LATERAL (VALUES
  ('perf-kolor',CASE v WHEN 1 THEN 'czerwony' WHEN 2 THEN 'niebieski' ELSE 'czarny' END),
  ('perf-rozmiar',CASE v WHEN 1 THEN 'S' WHEN 2 THEN 'M' ELSE 'L' END),
  ('perf-marka',CASE n%3 WHEN 0 THEN 'MarkaA' WHEN 1 THEN 'MarkaB' ELSE 'MarkaC' END),
  ('perf-material',CASE n%3 WHEN 0 THEN 'bawelna' WHEN 1 THEN 'len' ELSE 'poliester' END),
  ('perf-styl',CASE n%3 WHEN 0 THEN 'casual' WHEN 1 THEN 'sport' ELSE 'elegancki' END)
) AS kv(code,val);
INSERT INTO app.variant_stock (id,variant_id,quantity_on_hand)
SELECT md5('perf-stock-'||n||'-'||v)::uuid,md5('perf-variant-'||n||'-'||v)::uuid,(n%20)+v
FROM generate_series(1,:extra) AS g(n) CROSS JOIN generate_series(1,3) AS a(v);
INSERT INTO app.variant_price_changes (id,variant_id,base_amount,selling_amount,currency,effective_at,reason)
SELECT md5('perf-price-'||n||'-'||v||'-'||h)::uuid,
  md5('perf-variant-'||n||'-'||v)::uuid,
  30+(n%500)+v*7+h*3,
  20+(n%500)+v*7+h*3,
  'PLN',
  now()-make_interval(days=>(CASE h WHEN 1 THEN 90 WHEN 2 THEN 30 ELSE 1 END)),
  'performance test'
FROM generate_series(1,:extra) AS g(n)
CROSS JOIN generate_series(1,3) AS a(v)
CROSS JOIN generate_series(1,3) AS b(h);
ALTER TABLE app.variant_attribute_pairs ENABLE TRIGGER USER;
ALTER TABLE app.product_variants ENABLE TRIGGER USER;
ALTER TABLE app.products ENABLE TRIGGER USER;
COMMIT;
ANALYZE app.products;
ANALYZE app.product_variants;
ANALYZE app.product_categories;
ANALYZE app.variant_attribute_pairs;
ANALYZE app.attribute_keys;
ANALYZE app.attribute_pairs;
ANALYZE app.variant_price_changes;

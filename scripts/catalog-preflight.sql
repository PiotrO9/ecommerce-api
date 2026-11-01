-- Read-only audit before AddCatalogPublicationRules. It does not hide products,
-- merge attribute IDs, change prices or invent historical publication dates.
WITH canonical_pairs AS (
    SELECT id, attribute_key_id, value,
      normalize(btrim(value, U&'\0009\000A\000B\000C\000D\0020\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000'), NFC) AS canonical
    FROM app.attribute_pairs
), issues AS (
    SELECT 'blocker'::text severity, 'published_variant_without_current_pln'::text issue, v.id
    FROM app.product_variants v
    WHERE v.status='Published' AND NOT EXISTS (
      SELECT 1 FROM app.variant_price_changes c WHERE c.variant_id=v.id AND c.currency='PLN' AND c.effective_at<=CURRENT_TIMESTAMP)
    UNION ALL
    SELECT 'blocker','published_product_without_category',p.id FROM app.products p
    WHERE p.status='Published' AND NOT EXISTS (SELECT 1 FROM app.product_categories c WHERE c.product_id=p.id)
    UNION ALL
    SELECT 'blocker','published_product_without_published_variant',p.id FROM app.products p
    WHERE p.status='Published' AND NOT EXISTS (SELECT 1 FROM app.product_variants v WHERE v.product_id=p.id AND v.status='Published')
    UNION ALL
    SELECT 'blocker','noncanonical_attribute_value',id FROM canonical_pairs WHERE value<>canonical
    UNION ALL
    SELECT 'blocker','canonical_attribute_collision',p.id FROM canonical_pairs p
    WHERE EXISTS (SELECT 1 FROM canonical_pairs other WHERE other.attribute_key_id=p.attribute_key_id AND other.canonical=p.canonical AND other.id<>p.id)
    UNION ALL
    SELECT 'warning','sku_collision_after_trim',v.id FROM app.product_variants v
    WHERE EXISTS (SELECT 1 FROM app.product_variants other WHERE btrim(other.sku)=btrim(v.sku) AND other.id<>v.id)
    UNION ALL
    SELECT 'warning','unknown_first_product_publication',id FROM app.products WHERE status='Published' AND published_at IS NULL
    UNION ALL
    SELECT 'warning','unknown_first_variant_publication',id FROM app.product_variants WHERE status='Published' AND published_at IS NULL
)
SELECT severity,issue,id FROM issues ORDER BY severity,issue,id;

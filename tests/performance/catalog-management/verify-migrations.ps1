param([string]$PgBin = 'C:/Program Files/PostgreSQL/17/bin')
$ErrorActionPreference = 'Stop'
# An optional privileged local migration check, separate from the shared test DB.
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$connection.set_ConnectionString($env:ECOMMERCE_TEST_DATABASE)
if ([string]$connection['Host'] -notin @('127.0.0.1','localhost') -or [string]$connection['Database'] -notlike 'ecomm_test_*') {
    throw 'ECOMMERCE_TEST_DATABASE must point to an isolated local ecomm_test_* database.'
}
$databaseName = 'ecomm_test_offer_migrations_' + [guid]::NewGuid().ToString('N')
$connection['Database'] = $databaseName
$pgArgs = @('-h',[string]$connection['Host'],'-p',[string]$connection['Port'],'-U',[string]$connection['Username'])
if ([string]::IsNullOrWhiteSpace($pgArgs[3])) { $pgArgs[3] = '5432' }
if ($connection.ContainsKey('Password')) { $taskPgPassword = $env:PGPASSWORD; $env:PGPASSWORD = [string]$connection['Password'] }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$migrationLog = Join-Path $env:TEMP ($databaseName + '.log')
function Sql([string]$statement) {
    $lines = & (Join-Path $PgBin 'psql.exe') @pgArgs -d $databaseName -X -q -A -t -v ON_ERROR_STOP=1 -c $statement
    if ($LASTEXITCODE -ne 0) { throw 'Local fixture SQL failed.' }
    return ($lines -join "`n").Trim()
}
function Migrate([string]$target, [string]$expectedFailure = '') {
    & dotnet ef database update $target --connection ($connection.get_ConnectionString()) --no-build *> $migrationLog
    if ($expectedFailure) {
        if ($LASTEXITCODE -eq 0 -or -not (Select-String -LiteralPath $migrationLog -SimpleMatch $expectedFailure -Quiet)) {
            throw "Expected migration preflight rejection: $expectedFailure"
        }
    } elseif ($LASTEXITCODE -ne 0) { throw "Migration failed; inspect $migrationLog" }
}
Push-Location $repo
try {
    & (Join-Path $PgBin 'createdb.exe') @pgArgs --template=template0 --locale-provider=icu --icu-locale=en-US $databaseName
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the isolated migration database.' }
    & (Join-Path $PgBin 'psql.exe') @pgArgs -d $databaseName -X -q -v ON_ERROR_STOP=1 -f tests/performance/bootstrap-local-postgres.sql | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Bootstrap failed.' }
    Migrate '20261101132248_AddShoppingCartsAndFavorites'
    Sql @'
BEGIN;
INSERT INTO app.categories(id,display_name,slug) VALUES ('aa000000-0000-0000-0000-000000000001','Audit','audit');
INSERT INTO app.products(id,name,slug,status) VALUES
 ('aa000000-0000-0000-0000-000000000002','Legacy','legacy','Published'),
 ('aa000000-0000-0000-0000-000000000003','Invalid draft','invalid-draft','Draft');
INSERT INTO app.product_variants(id,product_id,sku,status,image_urls) VALUES
 ('aa000000-0000-0000-0000-000000000004','aa000000-0000-0000-0000-000000000002','AUDIT-OLD','Published',ARRAY[]::text[]),
 ('aa000000-0000-0000-0000-000000000005','aa000000-0000-0000-0000-000000000003','AUDIT-INVALID','Published',ARRAY[]::text[]);
INSERT INTO app.product_categories(product_id,category_id) VALUES ('aa000000-0000-0000-0000-000000000002','aa000000-0000-0000-0000-000000000001');
INSERT INTO app.variant_price_changes(id,variant_id,base_amount,selling_amount,effective_at,reason) VALUES
 ('aa000000-0000-0000-0000-000000000006','aa000000-0000-0000-0000-000000000004',99.99,99.99,now()-interval '20 days','Synthetic historical price');
INSERT INTO app.attribute_keys(id,code,display_name) VALUES ('aa000000-0000-0000-0000-000000000007','audit','Audit');
INSERT INTO app.attribute_pairs(id,attribute_key_id,value) VALUES ('aa000000-0000-0000-0000-000000000008','aa000000-0000-0000-0000-000000000007','red ');
COMMIT;
'@ | Out-Null
    $priceDigestSql = 'SELECT md5(string_agg(row_to_json(p)::text, '''' ORDER BY p.id)) FROM app.variant_price_changes p'
    $before = Sql $priceDigestSql
    Migrate '20261101133124_AddCatalogManagement'
    Migrate '20261101133155_AddCatalogPublicationRules' 'Noncanonical attribute values exist'
    if ((Sql 'SELECT count(*) FROM public."__EcommerceMigrationsHistory" WHERE "MigrationId"=''20261101133155_AddCatalogPublicationRules''') -ne '0') {
        throw 'A rejected migration must not appear in history.'
    }
    # Explicit fixture correction: the noncanonical pair is unused. No price or
    # referenced variant identity is rewritten to make migration succeed.
    Sql "DELETE FROM app.attribute_pairs WHERE id='aa000000-0000-0000-0000-000000000008'" | Out-Null
    Migrate '20261101133155_AddCatalogPublicationRules' 'Incomplete published catalog exists'
    Sql "UPDATE app.product_variants SET status='Draft' WHERE id='aa000000-0000-0000-0000-000000000005'" | Out-Null
    Migrate '20261101133155_AddCatalogPublicationRules'
    if ((Sql $priceDigestSql) -ne $before) { throw 'Historical price fields changed.' }
    if ((Sql "SELECT (published_at IS NULL)::text FROM app.products WHERE id='aa000000-0000-0000-0000-000000000002'") -ne 'true') {
        throw 'Migration invented a historical publication date.'
    }
    Sql "UPDATE app.products SET name='Legacy metadata edit' WHERE id='aa000000-0000-0000-0000-000000000002'" | Out-Null
    if ((Sql "SELECT (published_at IS NULL)::text FROM app.products WHERE id='aa000000-0000-0000-0000-000000000002'") -ne 'true') {
        throw 'A metadata edit invented a first publication date for a legacy product.'
    }
    Migrate '20261101133124_AddCatalogManagement'
    Migrate '20261101133155_AddCatalogPublicationRules'
    if ((Sql $priceDigestSql) -ne $before) { throw 'Publication Down/Up changed price history.' }
    [pscustomobject]@{database=$databaseName;upgrade='passed';canonicalPreflight='rejected';publicationPreflight='rejected';priceHistory='unchanged';unknownPublicationDate='preserved';publicationDownUp='passed'} | ConvertTo-Json
} finally {
    Pop-Location
    if ($connection.ContainsKey('Password')) { $env:PGPASSWORD = $taskPgPassword }
}

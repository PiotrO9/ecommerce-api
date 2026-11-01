param(
    [string]$Api = 'https://localhost:15476/api/v1/web',
    [string]$Database = 'ecomm_test_promotion_http',
    [int]$PgPort = 15474,
    [string]$Psql = 'C:/Program Files/PostgreSQL/17/bin/psql.exe'
)
$ErrorActionPreference = 'Stop'
$apiUri = [uri]$Api
if ($apiUri.Scheme -ne 'https' -or $apiUri.Host -notin @('localhost', '127.0.0.1') -or $Database -notmatch '^ecomm_test_') {
    throw 'Use HTTPS localhost and an isolated ecomm_test_* database.'
}
$configurationFile = Join-Path $env:APPDATA 'Microsoft/UserSecrets/a2ea706d-b353-4f89-a6c4-64bdbd0be4bb/secrets.json'
$configuration = Get-Content -LiteralPath $configurationFile -Raw | ConvertFrom-Json -AsHashtable
$authUrl = $configuration['Supabase:Url'].TrimEnd('/') + '/auth/v1'
$authHeaders = @{ apikey = $configuration['Supabase:SecretKey'] }
if ([uri]::new($authUrl).Scheme -ne 'https' -or [string]::IsNullOrWhiteSpace($authHeaders.apikey)) { throw 'Missing Supabase configuration.' }
if ([string]::IsNullOrWhiteSpace($env:ECOMMERCE_SMOKE_SEED_KEY)) { throw 'Set ECOMMERCE_SMOKE_SEED_KEY to the temporary API seed key.' }
$checks = 0
function Check([bool]$condition, [string]$label) {
    if (!$condition) { throw "FAIL: $label" }
    $script:checks++
    Write-Output "PASS: $label"
}
function Sql([string]$query) {
    $result = $query | & $Psql -h 127.0.0.1 -p $PgPort -U postgres -d $Database -At -v ON_ERROR_STOP=1 -f - 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Local test SQL failed.' }
    return $result
}
function Request([string]$method, [string]$path, $body, $session, [int]$expected = 200, [hashtable]$headers = @{}) {
    $options = @{ Uri = $Api.TrimEnd('/') + $path; Method = $method; Headers = $headers; SkipHttpErrorCheck = $true; SkipCertificateCheck = $true }
    if ($session) { $options.WebSession = $session }
    if ($null -ne $body) { $options.Body = ConvertTo-Json -InputObject $body -Depth 15 -Compress; $options.ContentType = 'application/json' }
    $response = Invoke-WebRequest @options
    if ([int]$response.StatusCode -ne $expected) {
        $code = try { ($response.Content | ConvertFrom-Json).code } catch { 'unparsed' }
        throw "HTTP $method $path expected $expected, got $($response.StatusCode), code=$code"
    }
    if ($response.Content) { return $response.Content | ConvertFrom-Json }
}
function WriteHeaders([string]$key = ([guid]::NewGuid().ToString())) {
    return @{ 'X-Requested-With' = 'XMLHttpRequest'; 'Idempotency-Key' = $key }
}

$users = [System.Collections.Generic.List[object]]::new()
$run = [guid]::NewGuid().ToString('N')
try {
    # Verify API identity against a random local database row before destructive seeding.
    $probe = [guid]::NewGuid().ToString()
    Sql "INSERT INTO app.categories(id,display_name,slug,created_at,updated_at) VALUES ('$probe','Smoke database probe','smoke-$run',now(),now());" | Out-Null
    $found = Request GET "/categories/$probe" $null $null
    Check ($found.id -eq $probe) 'API reads the isolated local database before seed reset'
    $seed = Request POST '/seed' @{ categories = 'low'; products = 'low'; variants = 'medium' } $null 200 @{ 'X-Demo-Seed-Key' = $env:ECOMMERCE_SMOKE_SEED_KEY }
    Check ($seed.created.promotions -eq 3 -and $seed.added -gt 0) 'HTTP seeder creates promotion scenarios and counters'
    foreach ($role in @('administrator', 'customer')) {
        $email = "codex-promotion-$role-$run@example.invalid"
        $password = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) + 'Aa1!'
        $body = @{ email = $email; password = $password; email_confirm = $true; user_metadata = @{ role = 'admin'; qa_run = $run } }
        # This is a server-side QA client. PowerShell's default browser-like UA is rejected for sb_secret keys.
        $response = Invoke-WebRequest -Uri "$authUrl/admin/users" -Method Post -UserAgent 'ecomm-smoke-server/1.0' -Headers $authHeaders -ContentType application/json -Body ($body | ConvertTo-Json -Depth 5) -SkipHttpErrorCheck
        if ([int]$response.StatusCode -notin @(200, 201)) { throw "Temporary Auth user creation failed: HTTP $($response.StatusCode)" }
        $created = $response.Content | ConvertFrom-Json
        $id = [guid]$created.id
        $user = @{ Id = $id; Role = $role; Session = [Microsoft.PowerShell.Commands.WebRequestSession]::new(); LoggedIn = $false }
        $users.Add($user)
        Sql "INSERT INTO auth.users(id) VALUES ('$id');" | Out-Null
        if ($role -eq 'administrator') { Sql "INSERT INTO app.administrators(user_id,created_at) VALUES ('$id',now());" | Out-Null }
        Request POST '/auth/login' @{ email = $email; password = $password } $user.Session | Out-Null
        $user.LoggedIn = $true
        $me = Request GET '/auth/me' $null $user.Session
        Check ($me.id -eq $id) "Real Supabase login and protected cookie: $role"
        $password = $null
    }
    $admin = $users[0].Session; $customer = $users[1].Session
    $product = Request GET '/products?slug=demo-czapka' $null $null
    $variant = $product.variants[0]
    $baseAmount = $variant.currentPrices[0].amount
    $priceBefore = Request GET "/admin/variants/$($variant.id)/price" $null $admin
    $historyBefore = Request GET "/admin/variants/$($variant.id)/price-changes" $null $admin
    $rule = @{ productId = $product.id; name = 'HTTPS smoke -20%'; discountType = 'percentage'; discountValue = 20 }
    Request GET '/admin/promotions' $null $null 401 | Out-Null
    Request POST '/admin/promotions' $rule $customer 403 (WriteHeaders) | Out-Null
    Check $true 'Anonymous rejected (401), real customer rejected (403) despite admin user_metadata'
    Request POST '/admin/promotions' $rule $admin 403 @{ 'Idempotency-Key' = [guid]::NewGuid().ToString() } | Out-Null
    Request POST '/admin/promotions' $rule $admin 400 @{ 'X-Requested-With' = 'XMLHttpRequest'; 'Idempotency-Key' = 'invalid' } | Out-Null
    Request POST '/admin/promotions' ($rule + @{ actorUserId = $users[0].Id }) $admin 400 (WriteHeaders) | Out-Null
    Check $true 'MVC enforces CSRF, operation UUID and rejects server-owned fields'
    $createKey = [guid]::NewGuid().ToString()
    $draft = Request POST '/admin/promotions' $rule $admin 201 (WriteHeaders $createKey)
    $id = $draft.promotion.id
    foreach ($operation in @(
        @{ Method = 'GET'; Path = '/admin/promotions'; Body = $null },
        @{ Method = 'GET'; Path = "/admin/promotions/$id"; Body = $null },
        @{ Method = 'POST'; Path = '/admin/promotions'; Body = $rule },
        @{ Method = 'PUT'; Path = "/admin/promotions/$id"; Body = $rule + @{ expectedVersion = 1 } },
        @{ Method = 'POST'; Path = "/admin/promotions/$id/preview"; Body = $null },
        @{ Method = 'POST'; Path = "/admin/promotions/$id/activate"; Body = @{ expectedVersion = 1; variants = @() } },
        @{ Method = 'POST'; Path = "/admin/promotions/$id/end"; Body = @{ expectedVersion = 1 } }
    )) {
        Request $operation.Method $operation.Path $operation.Body $customer 403 (WriteHeaders) | Out-Null
    }
    Check $true 'All seven administrative routes reject the real customer account'
    $replayedCreate = Request POST '/admin/promotions' $rule $admin 200 (WriteHeaders $createKey)
    Check ($replayedCreate.replayed -and $replayedCreate.promotion.id -eq $id) 'Draft creation can be safely replayed'
    $preview = Request POST "/admin/promotions/$id/preview?page=1&pageSize=1" $null $admin
    Check ($preview.canActivate -and $preview.items.Count -eq 1 -and $preview.expectedPrices.Count -eq $product.variants.Count) 'Preview is paged but checks and versions cover all variants'
    $activation = @{ expectedVersion = $preview.version; variants = @($preview.expectedPrices) }
    $key = [guid]::NewGuid().ToString()
    $active = Request POST "/admin/promotions/$id/activate" $activation $admin 200 (WriteHeaders $key)
    Check ($active.promotion.status -eq 'Active') 'Promotion activation through authenticated HTTPS'
    $during = Request GET '/products?slug=demo-czapka' $null $null
    $sale = $during.variants | Where-Object id -EQ $variant.id | ForEach-Object { $_.currentPrices[0] }
    Check ($sale.amount -eq [decimal]::Round($baseAmount * [decimal]0.8, 2, [MidpointRounding]::AwayFromZero) -and $sale.reduction.priorAmount -eq $baseAmount -and $sale.reduction.savingPercent -eq 20) 'Product details contain discounted price and prior-price snapshot'
    $catalog = Request GET '/products/catalog?q=Czapka&page=1&pageSize=20' $null $null
    $branch = Request GET "/products/by-category?categoryId=$($product.categories[0].id)&page=1&pageSize=100" $null $null
    $search = Request POST '/products/search' @{ q = 'Czapka'; minPrice = $sale.amount; maxPrice = $sale.amount; sort = 'price-asc' } $null
    foreach ($items in @($catalog, $branch, $search)) {
        $card = $items.items | Where-Object id -EQ $product.id
        Check ($card.pricesFrom[0].amount -eq $sale.amount -and $null -ne $card.pricesFrom[0].reduction -and $null -ne $card.pricesFrom[0].variantId) 'Catalog/category/search price uses one matching variant and reduction'
    }
    $historyActive = Request GET "/admin/variants/$($variant.id)/price-changes" $null $admin
    $replay = Request POST "/admin/promotions/$id/activate" $activation $admin 200 (WriteHeaders $key)
    $historyReplay = Request GET "/admin/variants/$($variant.id)/price-changes" $null $admin
    Check ($replay.replayed -and $historyActive.totalItems -eq $historyBefore.totalItems + 1 -and $historyReplay.totalItems -eq $historyActive.totalItems) 'Activation replay does not duplicate history'
    Request POST "/admin/promotions/$id/activate" @{ expectedVersion = 999; variants = @($preview.expectedPrices) } $admin 409 (WriteHeaders $key) | Out-Null
    $current = Request GET "/admin/variants/$($variant.id)/price" $null $admin
    Request POST "/admin/variants/$($variant.id)/price-changes" @{ baseAmount = $baseAmount + 1; reason = 'Smoke conflict'; expectedPriceChangeId = $current.price.id } $admin 409 (WriteHeaders) | Out-Null
    Check $true 'Changed body under same key and ordinary price update during promotion are blocked'
    $endBody = @{ expectedVersion = $active.promotion.version }; $endKey = [guid]::NewGuid().ToString()
    $end = Request POST "/admin/promotions/$id/end" $endBody $admin 200 (WriteHeaders $endKey)
    $restored = Request GET '/products?slug=demo-czapka' $null $null
    $restoredPrice = $restored.variants | Where-Object id -EQ $variant.id | ForEach-Object { $_.currentPrices[0] }
    Check ($end.promotion.status -eq 'Ended' -and $restoredPrice.amount -eq $baseAmount -and $null -eq $restoredPrice.reduction) 'Ending restores the base price and clears public promotion data'
    $endReplay = Request POST "/admin/promotions/$id/end" $endBody $admin 200 (WriteHeaders $endKey)
    $finalHistory = Request GET "/admin/variants/$($variant.id)/price-changes" $null $admin
    Check ($endReplay.replayed -and $finalHistory.totalItems -eq $historyBefore.totalItems + 2) 'Ending replay leaves exactly two new price records per variant'
    Request POST "/admin/promotions/$id/activate" @{ expectedVersion = $end.promotion.version; variants = @($preview.expectedPrices) } $admin 409 (WriteHeaders) | Out-Null
    Check $true 'Ended promotion cannot be reactivated'
    $fixedRule = @{ productId = $product.id; name = 'HTTPS smoke fixed amount'; discountType = 'amount'; discountValue = [decimal]::Round($baseAmount * [decimal]0.3, 2) }
    $fixed = Request POST '/admin/promotions' $fixedRule $admin 201 (WriteHeaders)
    $fixedId = $fixed.promotion.id
    $fixedPreview = Request POST "/admin/promotions/$fixedId/preview" $null $admin
    $fixedVariant = $fixedPreview.items | Where-Object variantId -EQ $variant.id
    Check ($fixedPreview.canActivate -and $fixedVariant.priorAmount -eq $sale.amount) 'Next promotion uses the previous discounted price as its reference'
    $fixedActive = Request POST "/admin/promotions/$fixedId/activate" @{ expectedVersion = $fixedPreview.version; variants = @($fixedPreview.expectedPrices) } $admin 200 (WriteHeaders)
    $fixedDetails = Request GET '/products?slug=demo-czapka' $null $null
    $fixedPrice = $fixedDetails.variants | Where-Object id -EQ $variant.id | ForEach-Object { $_.currentPrices[0] }
    Check ($fixedPrice.amount -eq $baseAmount - $fixedRule.discountValue -and $fixedPrice.reduction.priorAmount -eq $sale.amount) 'Fixed amount discount and previous-promotion reference are correct over HTTP'
    Request POST "/admin/promotions/$fixedId/end" @{ expectedVersion = $fixedActive.promotion.version } $admin 200 (WriteHeaders) | Out-Null
    $fixedHistory = Request GET "/admin/variants/$($variant.id)/price-changes" $null $admin
    Check ($fixedHistory.totalItems -eq $historyBefore.totalItems + 4) 'Both promotion cycles leave exactly four new price records per variant'
    Sql "DELETE FROM app.administrators WHERE user_id = '$($users[0].Id)';" | Out-Null
    Request GET '/admin/promotions' $null $admin 403 | Out-Null
    Check $true 'Administrator revocation takes effect with the same session'
    Write-Output "SMOKE PASSED: $checks checks"
}
finally {
    $cleanupFailures = 0
    foreach ($user in $users) {
        if ($user.LoggedIn) {
            try { Request POST '/auth/logout' $null $user.Session 204 @{ 'X-Requested-With' = 'XMLHttpRequest' } | Out-Null }
            catch { Write-Warning "Logout failed for temporary $($user.Role); deleting its Auth user next." }
        }
        try {
            $response = Invoke-WebRequest -Uri "$authUrl/admin/users/$($user.Id)" -Method Delete -UserAgent 'ecomm-smoke-server/1.0' -Headers $authHeaders -ContentType application/json -Body '{"should_soft_delete":false}' -SkipHttpErrorCheck
            if ([int]$response.StatusCode -notin @(200, 204)) { throw 'Delete failed' }
            $verify = Invoke-WebRequest -Uri "$authUrl/admin/users/$($user.Id)" -Method Get -UserAgent 'ecomm-smoke-server/1.0' -Headers $authHeaders -SkipHttpErrorCheck
            if ([int]$verify.StatusCode -ne 404) { throw 'Deletion verification failed' }
            Sql "DELETE FROM auth.users WHERE id = '$($user.Id)';" | Out-Null
            Write-Output "CLEANUP: temporary $($user.Role) deleted and verified absent"
        }
        catch { $cleanupFailures++; Write-Warning "Cleanup failed for temporary Auth user $($user.Id)." }
    }
    $configuration = $null; $authHeaders = $null; $password = $null
    if ($cleanupFailures) { throw 'Temporary Auth accounts need cleanup; inspect the warning UUIDs.' }
}

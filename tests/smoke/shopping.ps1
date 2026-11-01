param(
    [string]$Api = 'https://localhost:15476/api/v1/web',
    [string]$Database = 'ecomm_test_shopping_http',
    [int]$PgPort = 15474,
    [string]$Psql = 'C:/Program Files/PostgreSQL/17/bin/psql.exe',
    [string]$ReportPath
)
$ErrorActionPreference = 'Stop'
$apiUri = [uri]$Api
if ($apiUri.Scheme -ne 'https' -or $apiUri.Host -notin @('localhost', '127.0.0.1') -or
    $apiUri.UserInfo -or $apiUri.Query -or $apiUri.Fragment -or $Database -notmatch '^ecomm_test_[a-z0-9_]+$') {
    throw 'Use HTTPS localhost and an isolated ecomm_test_* database.'
}
if ([string]::IsNullOrWhiteSpace($env:ECOMMERCE_SMOKE_SEED_KEY)) { throw 'Set the temporary ECOMMERCE_SMOKE_SEED_KEY.' }
$configFile = Join-Path $env:APPDATA 'Microsoft/UserSecrets/a2ea706d-b353-4f89-a6c4-64bdbd0be4bb/secrets.json'
$config = Get-Content -LiteralPath $configFile -Raw | ConvertFrom-Json -AsHashtable
$authUrl = $config['Supabase:Url'].TrimEnd('/') + '/auth/v1'
$authHeaders = @{ apikey = $config['Supabase:SecretKey'] }
if ([uri]::new($authUrl).Scheme -ne 'https' -or [string]::IsNullOrWhiteSpace($authHeaders.apikey)) { throw 'Missing Supabase Auth configuration.' }
$run = [guid]::NewGuid().ToString('N')
$checks = [Collections.Generic.List[object]]::new()
$users = [Collections.Generic.List[object]]::new()
$cleanup = [Collections.Generic.List[object]]::new()
$passed = $false
$failure = $null
$lastHttp = $null
function Check([bool]$condition, [string]$label) {
    $script:checks.Add(@{ name = $label; passed = $condition })
    if (!$condition) { throw "FAIL: $label" }
    Write-Output "PASS: $label"
}
function Sql([string]$query) {
    $value = $query | & $Psql -h 127.0.0.1 -p $PgPort -U postgres -d $Database -At -v ON_ERROR_STOP=1 -f - 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Local fixture SQL failed.' }
    return $value
}
function Request([string]$method, [string]$path, $body, $session, [int]$expected = 200, [hashtable]$headers = @{}) {
    $options = @{ Uri = $Api.TrimEnd('/') + $path; Method = $method; Headers = $headers;
        SkipHttpErrorCheck = $true; SkipCertificateCheck = $true; TimeoutSec = 40 }
    if ($session) {
        # Invoke-WebRequest persists supplied headers in WebSession. Keep cookies,
        # but let each request explicitly choose its headers (including missing CSRF).
        $session.Headers.Clear()
        $options.WebSession = $session
    }
    if ($null -ne $body) { $options.Body = ConvertTo-Json -InputObject $body -Depth 10 -Compress; $options.ContentType = 'application/json' }
    $script:lastHttp = Invoke-WebRequest @options
    if ([int]$script:lastHttp.StatusCode -ne $expected) {
        $code = try { ($script:lastHttp.Content | ConvertFrom-Json).code } catch { 'unparsed' }
        throw "HTTP $method $path expected $expected, got $($script:lastHttp.StatusCode), code=$code"
    }
    if ($script:lastHttp.Content) { return $script:lastHttp.Content | ConvertFrom-Json }
}
function Headers { return @{ 'X-Requested-With' = 'XMLHttpRequest' } }
function Stamp($view) { return @{ expectedScopeId = $view.scopeId; expectedVersion = $view.version } }
function SetQuantity($session, $view, [string]$id, [int]$quantity, [int]$expected = 200) {
    return Request PUT "/cart/items/$id" ((Stamp $view) + @{ quantity = $quantity }) $session $expected (Headers)
}
function Login($user, $session) {
    Request POST '/auth/login' @{ email = $user.Email; password = $user.Password } $session | Out-Null
    $user.Sessions.Add($session)
    $me = Request GET '/auth/me' $null $session
    Check ($me.id -eq $user.Id -and $null -eq $me.firstName) "Real Supabase session resolves account $($user.Label) without profile"
}
try {
    # A random local row must be visible through this API before any destructive seed.
    $probe = [guid]::NewGuid().ToString()
    Sql "INSERT INTO app.categories(id,display_name,slug,created_at,updated_at) VALUES ('$probe','Shopping smoke probe','shopping-$run',now(),now());" | Out-Null
    $probeRead = Request GET "/categories/$probe" $null $null
    Check ($probeRead.id -eq $probe) 'API identity matches the isolated local database'
    $seed = Request POST '/seed' @{ categories = 'low'; products = 'low'; variants = 'medium' } $null 200 @{ 'X-Demo-Seed-Key' = $env:ECOMMERCE_SMOKE_SEED_KEY }
    Check ($seed.added -gt 0 -and $seed.created.variants -ge 3) 'HTTPS seed prepares the isolated catalog'
    $cap = Request GET '/products?slug=demo-czapka' $null $null
    $shirt = Request GET '/products?slug=demo-koszulka' $null $null
    $capId = ([guid]$cap.variants[0].id).ToString()
    $red = $shirt.variants | Where-Object sku -EQ 'DEMO-TSHIRT-RED-S'
    $blue = $shirt.variants | Where-Object sku -EQ 'DEMO-TSHIRT-BLUE-M'
    $redId = ([guid]$red.id).ToString(); $blueId = ([guid]$blue.id).ToString()
    $capPrice = [decimal]($cap.variants[0].currentPrices | Where-Object currency -EQ 'PLN').amount
    $redPrice = [decimal]($red.currentPrices | Where-Object currency -EQ 'PLN').amount
    $browserA = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    $empty = Request GET '/cart' $null $browserA
    Check ($null -eq $empty.scopeId -and @($empty.items).Count -eq 0) 'Anonymous GET is empty and creates no guest state'
    Request GET '/favorites' $null $browserA 401 | Out-Null
    Check $true 'Guest cannot read favorites'
    $guestInit = Request POST '/shopping/guest-session' $null $browserA 201 (Headers)
    $guestCookie = $browserA.Cookies.GetCookies($apiUri) | Where-Object Name -EQ '__Host-ecomm-shopping'
    Check ($guestCookie.Secure -and $guestCookie.HttpOnly -and (($lastHttp.Headers['Set-Cookie'] -join ';') -match 'samesite=lax')) 'Guest cookie is Secure, HttpOnly and SameSite=Lax over HTTPS'
    $guestToken = $guestCookie.Value
    $guest = Request GET '/cart' $null $browserA
    SetQuantity $browserA $guest $capId 1 | Out-Null
    $staleGuest = $guest
    $guest = Request GET '/cart' $null $browserA
    Check (@($guest.items).Count -eq 1 -and $guest.goodsSubtotal -eq $capPrice -and $guest.canCheckout) 'Guest cart returns its exact variant, live price and availability'
    SetQuantity $browserA $staleGuest $capId 2 409 | Out-Null
    Check $true 'Stale guest version cannot duplicate or overwrite a quantity'
    Request PUT "/cart/items/$capId" ((Stamp $guest) + @{ quantity = 2 }) $browserA 403 | Out-Null
    Check $true 'Cart write without CSRF header is rejected'
    SetQuantity $browserA $guest $capId 0 400 | Out-Null
    SetQuantity $browserA $guest $capId 100 400 | Out-Null
    Check $true 'Quantity zero and quantity above 99 are rejected'
    SetQuantity $browserA $guest $capId 4 409 | Out-Null
    Check $true 'Quantity above known stock is rejected'
    Request PUT "/cart/items/$capId" ((Stamp $guest) + @{ quantity = 1; userId = [guid]::NewGuid().ToString() }) $browserA 400 (Headers) | Out-Null
    Check $true 'MVC rejects a client-supplied owner in JSON'
    SetQuantity $browserA $guest $capId 2 | Out-Null
    $guest = Request GET '/cart' $null $browserA
    Check ($guest.items[0].quantity -eq 2 -and $guest.goodsSubtotal -eq 2*$capPrice) 'PUT sets the absolute guest quantity'
    Check (([int](Sql "SELECT quantity_on_hand FROM app.variant_stock WHERE variant_id='$capId';")) -eq 3) 'Adding to cart does not reserve or reduce stock'
    $again = Request POST '/shopping/guest-session' $null $browserA 200 (Headers)
    Check ($again.scopeId -eq $guestInit.scopeId -and $again.expiresAt -eq $guestInit.expiresAt) 'Guest initialization is reusable without sliding expiration'

    foreach ($label in @('A', 'B')) {
        $email = "codex-shopping-$label-$run@example.invalid"
        $password = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) + 'Aa1!'
        $response = Invoke-WebRequest -Uri "$authUrl/admin/users" -Method Post -UserAgent 'ecomm-shopping-smoke-server/1.0' -Headers $authHeaders -ContentType application/json -Body (@{ email = $email; password = $password; email_confirm = $true } | ConvertTo-Json -Compress) -SkipHttpErrorCheck -TimeoutSec 40
        if ([int]$response.StatusCode -notin @(200,201)) { throw "Temporary Auth account creation failed: HTTP $($response.StatusCode)" }
        $id = ([guid](($response.Content | ConvertFrom-Json).id)).ToString()
        $users.Add(@{ Id = $id; Label = $label; Email = $email; Password = $password; Sessions = [Collections.Generic.List[object]]::new() })
        Sql "INSERT INTO auth.users(id) VALUES ('$id');" | Out-Null
    }
    Check ($users.Count -eq 2) 'Two temporary real Auth accounts created with confirmed email and no email sending'
    Login $users[0] $browserA
    $authCookie = $browserA.Cookies.GetCookies($apiUri) | Where-Object Name -EQ '__Host-ecomm-auth'
    Check ($authCookie.Secure -and $authCookie.HttpOnly) 'Real login issues a protected Secure HttpOnly application cookie'
    $account = Request GET '/cart' $null $browserA
    Check ($account.scopeId -ne $guest.scopeId -and @($account.items).Count -eq 0) 'Login switches to an empty account cart without importing guest items'
    SetQuantity $browserA $guest $redId 1 409 | Out-Null
    Check $true 'A delayed guest write cannot target the authenticated account'
    SetQuantity $browserA $account $redId 2 | Out-Null
    $account = Request GET '/cart' $null $browserA
    Check ($account.items[0].variantId -eq $redId -and $account.items[0].quantity -eq 2) 'Account A saves its own variant and quantity'
    $favorites = Request GET '/favorites' $null $browserA
    Check (@($favorites.items).Count -eq 0) 'New account favorites start empty'
    Request PUT "/favorites/items/$redId" (Stamp $favorites) $browserA 200 (Headers) | Out-Null
    $favorites = Request GET '/favorites' $null $browserA
    Check (@($favorites.items).Count -eq 1 -and $favorites.items[0].variantId -eq $redId) 'Favorites save the exact selected variant'
    $favVersion = $favorites.version
    Request PUT "/favorites/items/$redId" (Stamp $favorites) $browserA 200 (Headers) | Out-Null
    $favorites = Request GET '/favorites' $null $browserA
    Check (@($favorites.items).Count -eq 1 -and $favorites.version -eq $favVersion) 'Repeated favorite with current version is a no-op without a duplicate'
    Request PUT "/favorites/items/$blueId" (Stamp $favorites) $browserA 200 (Headers) | Out-Null
    $favorites = Request GET '/favorites' $null $browserA
    Check (@($favorites.items).Count -eq 2 -and $favorites.items[0].variant.productId -eq $favorites.items[1].variant.productId) 'Different variants of the same product remain separate favorites'
    $page1 = Request GET '/favorites?page=1&pageSize=1' $null $browserA
    $page2 = Request GET '/favorites?page=2&pageSize=1' $null $browserA
    Check ($page1.totalItems -eq 2 -and $page1.items[0].variantId -ne $page2.items[0].variantId) 'Favorite pages have stable counts and no overlapping entries'
    Request GET '/favorites?pageSize=51' $null $browserA 400 | Out-Null
    Check $true 'Invalid favorite page size is rejected'

    $deviceA2 = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    Login $users[0] $deviceA2
    $second = Request GET '/cart' $null $deviceA2
    Check ($second.scopeId -eq $account.scopeId -and $second.items[0].quantity -eq 2) 'Second real login sees the same account cart on another device'
    $secondFavorites = Request GET '/favorites' $null $deviceA2
    Check ($secondFavorites.scopeId -eq $favorites.scopeId -and @($secondFavorites.items).Count -eq 2) 'Favorites are shared across the two authenticated sessions'
    SetQuantity $deviceA2 $second $redId 3 | Out-Null
    SetQuantity $browserA $account $redId 4 409 | Out-Null
    $account = Request GET '/cart' $null $browserA
    Check ($account.items[0].quantity -eq 3) 'Stale device cannot overwrite another device update'
    $browserB = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    Login $users[1] $browserB
    $other = Request GET '/cart' $null $browserB
    $otherFavorites = Request GET '/favorites' $null $browserB
    Check (@($other.items).Count -eq 0 -and $other.scopeId -ne $account.scopeId -and @($otherFavorites.items).Count -eq 0) 'Account B cannot see account A cart or favorites'
    SetQuantity $browserB $account $redId 1 409 | Out-Null
    Request PUT "/favorites/items/$redId" (Stamp $favorites) $browserB 409 (Headers) | Out-Null
    Check $true 'Account B cannot mutate account A scopes'
    Request POST '/auth/logout' $null $browserA 204 (Headers) | Out-Null
    $returned = Request GET '/cart' $null $browserA
    Check ($returned.scopeId -eq $guest.scopeId -and $returned.items[0].variantId -eq $capId -and $returned.items[0].quantity -eq 2) 'Logout restores the untouched guest cart'
    $cookieAfterLogout = $browserA.Cookies.GetCookies($apiUri) | Where-Object Name -EQ '__Host-ecomm-shopping'
    Check ($cookieAfterLogout.Value -eq $guestToken) 'Logout preserves the original guest cookie'
    Request GET '/favorites' $null $browserA 401 | Out-Null
    Check $true 'Favorites become inaccessible after real logout'
    $stillLogged = Request GET '/cart' $null $deviceA2
    Check ($stillLogged.scopeId -eq $account.scopeId -and $stillLogged.items[0].quantity -eq 3) 'Logging out one real session preserves the other device session'

    Login $users[0] $browserA
    $account = Request GET '/cart' $null $browserA
    Check (@($account.items).Count -eq 1 -and $account.items[0].variantId -eq $redId) 'Relogin restores account data without merging the guest cart'
    SetQuantity $browserA $account $capId 1 | Out-Null
    $account = Request GET '/cart' $null $browserA
    $beforePriceVersion = $account.version
    Sql "INSERT INTO app.variant_price_changes(id,variant_id,base_amount,selling_amount,currency,effective_at,created_at,reason) VALUES (gen_random_uuid(),'$capId',60,60,'PLN',clock_timestamp(),clock_timestamp(),'HTTPS shopping smoke');" | Out-Null
    $account = Request GET '/cart' $null $browserA
    $capItem = $account.items | Where-Object variantId -EQ $capId
    Check ($capItem.variant.price.amount -eq 60 -and $account.version -eq $beforePriceVersion) 'Cart reads a new live price without rewriting the saved selection'
    Sql "UPDATE app.variant_stock SET quantity_on_hand=0 WHERE variant_id='$capId';" | Out-Null
    $account = Request GET '/cart' $null $browserA
    $capItem = $account.items | Where-Object variantId -EQ $capId
    Check ($capItem.availability -eq 'out_of_stock' -and $account.blockedItems -eq 1 -and !$account.canCheckout -and $account.goodsSubtotal -eq 3*$redPrice) 'Unavailable item stays visible, is excluded from subtotal and blocks checkout'
    SetQuantity $browserA $account $capId 2 409 | Out-Null
    Check $true 'Increasing an unavailable existing item is rejected'
    $favorites = Request GET '/favorites' $null $browserA
    Request PUT "/favorites/items/$capId" (Stamp $favorites) $browserA 200 (Headers) | Out-Null
    $favorites = Request GET '/favorites' $null $browserA
    Check (($favorites.items | Where-Object variantId -EQ $capId).availability -eq 'out_of_stock') 'A public out-of-stock variant may still be saved as favorite'
    Sql "UPDATE app.variant_stock SET quantity_on_hand=3 WHERE variant_id='$capId';" | Out-Null
    $shirtId = ([guid]$shirt.id).ToString()
    Sql "UPDATE app.products SET status='Hidden' WHERE id='$shirtId';" | Out-Null
    $account = Request GET '/cart' $null $browserA
    $hidden = $account.items | Where-Object variantId -EQ $redId
    Check ($hidden.availability -eq 'unavailable' -and $null -eq $hidden.variant -and !$account.canCheckout) 'Hidden product remains a redacted cart placeholder'
    $favorites = Request GET '/favorites' $null $browserA
    Check (@($favorites.items | Where-Object { $_.variantId -in @($redId,$blueId) -and $null -eq $_.variant }).Count -eq 2) 'Hidden favorites expose no unpublished variant details'
    SetQuantity $browserA $account $redId 4 404 | Out-Null
    SetQuantity $browserA $account $redId 1 | Out-Null
    Check $true 'Hidden item cannot be increased but its quantity can be reduced'
    $account = Request GET '/cart' $null $browserA
    Request DELETE "/cart/items/${redId}?expectedScopeId=$($account.scopeId)&expectedVersion=$($account.version)" $null $browserA 200 (Headers) | Out-Null
    $account = Request GET '/cart' $null $browserA
    Check (@($account.items).Count -eq 1 -and $account.items[0].variantId -eq $capId) 'Unavailable item can be removed with current scope and version'
    $favorites = Request GET '/favorites' $null $browserA
    Request DELETE "/favorites/items/${redId}?expectedScopeId=$($favorites.scopeId)&expectedVersion=$($favorites.version)" $null $browserA 200 (Headers) | Out-Null
    $favorites = Request GET '/favorites' $null $browserA
    Check (@($favorites.items | Where-Object variantId -EQ $redId).Count -eq 0) 'Hidden favorite can be removed'
    Request DELETE "/cart?expectedScopeId=$($account.scopeId)&expectedVersion=$($account.version)" $null $browserA 200 (Headers) | Out-Null
    $account = Request GET '/cart' $null $browserA
    Check (@($account.items).Count -eq 0 -and !$account.canCheckout) 'Clear command removes only the active account cart'
    $guestScope = ([guid]$guest.scopeId).ToString()
    Sql "UPDATE app.shopping_states SET expires_at=clock_timestamp()-interval '1 second' WHERE id='$guestScope';" | Out-Null
    Request POST '/auth/logout' $null $browserA 204 (Headers) | Out-Null
    $expired = Request GET '/cart' $null $browserA
    Check ($null -eq $expired.scopeId -and @($expired.items).Count -eq 0) 'Server rejects an expired guest session independently of cookie expiration'
    SetQuantity $browserA $guest $capId 1 409 | Out-Null
    $newGuest = Request POST '/shopping/guest-session' $null $browserA 201 (Headers)
    Check ($newGuest.scopeId -ne $guest.scopeId) 'Expired guest must initialize a different empty cart'
    Request DELETE '/shopping/guest-session' $null $browserA 204 (Headers) | Out-Null
    $revoked = Request GET '/cart' $null $browserA
    Check ($null -eq $revoked.scopeId) 'Guest session deletion revokes the anonymous cart'
    Request POST '/shopping/merge' $null $browserA 404 (Headers) | Out-Null
    Check $true 'No cart import or merge endpoint is exposed'
    Check (($lastHttp.Headers['Cache-Control'] -join ';') -match 'no-store') 'Personal routes retain no-store even for routing errors'
    $passed = $true
}
catch {
    $failure = $_.Exception.Message
    throw
}
finally {
    $cleanupFailures = 0
    foreach ($user in $users) {
        foreach ($session in $user.Sessions) {
            try { Request POST '/auth/logout' $null $session 204 (Headers) | Out-Null }
            catch { Write-Warning "Logout failed for temporary account $($user.Label); deleting its Auth account next." }
        }
        try {
            $deleted = Invoke-WebRequest -Uri "$authUrl/admin/users/$($user.Id)" -Method Delete -UserAgent 'ecomm-shopping-smoke-server/1.0' -Headers $authHeaders -ContentType application/json -Body '{"should_soft_delete":false}' -SkipHttpErrorCheck -TimeoutSec 40
            if ([int]$deleted.StatusCode -notin @(200,204)) { throw 'Temporary account deletion failed' }
            $verify = Invoke-WebRequest -Uri "$authUrl/admin/users/$($user.Id)" -Method Get -UserAgent 'ecomm-shopping-smoke-server/1.0' -Headers $authHeaders -SkipHttpErrorCheck -TimeoutSec 40
            if ([int]$verify.StatusCode -ne 404) { throw 'Deletion verification failed' }
            Sql "DELETE FROM auth.users WHERE id='$($user.Id)';" | Out-Null
            $cleanup.Add(@{ account = $user.Label; deletedAndVerifiedAbsent = $true })
            Write-Output "CLEANUP: temporary account $($user.Label) logged out, deleted and verified absent"
        }
        catch {
            $cleanupFailures++
            $cleanup.Add(@{ account = $user.Label; deletedAndVerifiedAbsent = $false; temporaryUserId = $user.Id })
            Write-Warning "Cleanup failed for temporary Auth account $($user.Id)."
        }
        $user.Password = $null
    }
    if ($ReportPath) {
        @{ runId = $run; passed = ($passed -and !$cleanupFailures); failure = $failure; checks = @($checks.ToArray()); cleanup = @($cleanup.ToArray());
            realSupabaseAuth = $true; catalogDatabase = 'isolated-local-postgresql'; frontendTested = $false } |
            ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    }
    $config = $null; $authHeaders = $null; $password = $null; $guestToken = $null; $lastHttp = $null
    if ($cleanupFailures) { throw 'Temporary Auth accounts need cleanup; inspect the warning UUIDs.' }
}
if ($passed) { Write-Output "SHOPPING HTTPS SMOKE PASSED: $($checks.Count) checks; real Auth; temporary accounts removed." }

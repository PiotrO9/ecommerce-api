param([string]$Phase,[string]$Base,[string]$Output,[int]$Samples=40)
$ErrorActionPreference='Stop'
$client=[System.Net.Http.HttpClient]::new()
$client.Timeout=[TimeSpan]::FromSeconds(60)
$cases=@(
  @{Name='catalog-page';Method='GET';Path='/catalog?page=1&pageSize=20'},
  @{Name='text-demo';Method='GET';Path='/catalog?q=demo&page=1&pageSize=20'},
  @{Name='price-sort';Method='GET';Path='/catalog?minPrice=100&maxPrice=300&sort=price-asc&page=1&pageSize=20'},
  @{Name='category-page';Method='GET';Path='/by-category?slug=demo-odziez&page=1&pageSize=20'},
  @{Name='search-empty';Method='POST';Path='/search';Body='{}'},
  @{Name='search-demo-2';Method='POST';Path='/search';Body='{"attributes":[{"code":"demo-kolor","values":["czerwony"]},{"code":"demo-rozmiar","values":["S"]}],"page":1,"pageSize":20}'}
)
if($Phase -eq 'large'){
  $cases+=@(
    @{Name='text-perf';Method='GET';Path='/catalog?q=perf&page=1&pageSize=20'},
    @{Name='search-perf-2';Method='POST';Path='/search';Body='{"attributes":[{"code":"perf-kolor","values":["czerwony"]},{"code":"perf-rozmiar","values":["S"]}],"minPrice":100,"maxPrice":300,"page":1,"pageSize":20}'},
    @{Name='search-perf-5';Method='POST';Path='/search';Body='{"attributes":[{"code":"perf-kolor","values":["czerwony"]},{"code":"perf-rozmiar","values":["S"]},{"code":"perf-marka","values":["MarkaA"]},{"code":"perf-material","values":["bawelna"]},{"code":"perf-styl","values":["casual"]}],"page":1,"pageSize":20}'}
  )
}
if($Phase -in @('controlled','facets')){
  $cases=@(
    @{Name='search-perf-1';Method='POST';Path='/search';Body='{"attributes":[{"code":"perf-kolor","values":["czerwony"]}],"page":1,"pageSize":20}'},
    @{Name='search-perf-2-no-price';Method='POST';Path='/search';Body='{"attributes":[{"code":"perf-kolor","values":["czerwony"]},{"code":"perf-rozmiar","values":["S"]}],"page":1,"pageSize":20}'},
    @{Name='search-perf-5-no-price';Method='POST';Path='/search';Body='{"attributes":[{"code":"perf-kolor","values":["czerwony"]},{"code":"perf-rozmiar","values":["S"]},{"code":"perf-marka","values":["MarkaA"]},{"code":"perf-material","values":["bawelna"]},{"code":"perf-styl","values":["casual"]}],"page":1,"pageSize":20}'},
    @{Name='search-price-only';Method='POST';Path='/search';Body='{"minPrice":100,"maxPrice":300,"page":1,"pageSize":20}'},
    @{Name='search-perf-2-price';Method='POST';Path='/search';Body='{"attributes":[{"code":"perf-kolor","values":["czerwony"]},{"code":"perf-rozmiar","values":["S"]}],"minPrice":100,"maxPrice":300,"page":1,"pageSize":20}'},
    @{Name='search-perf-2-price-sort';Method='POST';Path='/search';Body='{"attributes":[{"code":"perf-kolor","values":["czerwony"]},{"code":"perf-rozmiar","values":["S"]}],"minPrice":100,"maxPrice":300,"sort":"price-asc","page":1,"pageSize":20}'}
  )
}
if($Phase -eq 'facets'){$cases=@($cases[0..2])}
if($Phase -eq 'price'){
  $cases=@(@{Name='price-sort';Method='GET';Path='/catalog?minPrice=100&maxPrice=300&sort=price-asc&page=1&pageSize=20'})
}
$results=@()
foreach($case in $cases){
  $ms=[System.Collections.Generic.List[double]]::new()
  $status=0
  $lastBody=''
  for($i=0;$i -lt ($Samples+5);$i++){
    $request=[System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::$($case.Method),$Base+$case.Path)
    if($case.Method -eq 'POST'){$request.Content=[System.Net.Http.StringContent]::new($case.Body,[System.Text.Encoding]::UTF8,'application/json')}
    $sw=[System.Diagnostics.Stopwatch]::StartNew()
    try{
      $response=$client.SendAsync($request).GetAwaiter().GetResult()
      $lastBody=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
      $sw.Stop()
      $status=[int]$response.StatusCode
      if($status -ne 200){throw "HTTP $status in $($case.Name): $lastBody"}
      if($i -ge 5){$ms.Add($sw.Elapsed.TotalMilliseconds)}
    } finally {if($response){$response.Dispose();$response=$null};$request.Dispose()}
  }
  $sorted=@($ms | Sort-Object)
  $body=$lastBody | ConvertFrom-Json
  $results+=[pscustomobject]@{phase=$Phase;case=$case.Name;samples=$Samples;status=$status;totalItems=$body.pagination.totalItems;p50Ms=[math]::Round(($sorted[[int][math]::Floor(($Samples-1)*0.5)]+$sorted[[int][math]::Ceiling(($Samples-1)*0.5)])/2,1);p95Ms=[math]::Round($sorted[[int][math]::Ceiling($Samples*0.95)-1],1);minMs=[math]::Round($sorted[0],1);maxMs=[math]::Round($sorted[-1],1)}
  Write-Output ($results[-1] | ConvertTo-Json -Compress)
}
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Output -Encoding utf8
$client.Dispose()

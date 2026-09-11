# Implementation Plan: SemtSkoru MVP

## Overview

SPEC.md'de tanımlanan MVP'yi (3 mahalle — Kadıköy, Üsküdar, Beşiktaş — için ulaşım/hava kalitesi/yeşil alan/trafik skorları ve iki mahalle karşılaştırma) dikey dilimler halinde inşa ediyoruz: önce iskelet, sonra veri kaynağı doğrulaması (en riskli kısım, en erken), sonra domain+DB, sonra ingestion, sonra scoring+API, sonra frontend, sonra yayına hazırlık.

## Architecture Decisions

- **Backend:** ASP.NET Core (.NET 8), Clean Architecture (Domain → Application → Infrastructure → Api bağımlılık yönü). PostgreSQL+PostGIS, EF Core, Hangfire (ingestion job'ları), Redis (skor cache), NetTopologySuite (coğrafi hesaplar).
- **Frontend:** Next.js (App Router) + TypeScript, MapLibre GL JS, TanStack Query, Tailwind.
- **Dış API'ler asla frontend'den çağrılmaz** — her zaman `ingestion job → Postgres → Scoring API → Next.js`.
- **Veri kaynağı doğrulaması ingestion kodundan ÖNCE yapılır** (Phase 2) — SPEC.md Open Question #1-2'yi kod yazmadan önce kapatmak için. Bu en yüksek riskli varsayım; erken başarısız olup erken öğrenmek daha ucuz.
- **Mahalle sınırları için fallback:** İBB'nin mahalle sınır GeoJSON'u kullanılamaz/bulunamazsa OpenStreetMap (Nominatim/Overpass) sınırlarına düşülür — 3 sabit mahalle için bu düşük risklidir.
- **Ingestion kaynakları birbirinden bağımsız alt sistemlerdir** (hava kalitesi / yeşil alan / trafik) — Phase 4'teki üç görev paralel çalıştırılabilir, plan sıralı yazılmıştır ama bağımlılık zorunluluğu yoktur.

## Task List

### Phase 1: Foundation (İskelet)
- [x] Task 1: .NET Clean Architecture solution iskeleti (Domain/Application/Infrastructure/Api + test projeleri)
- [x] Task 2: Next.js + TypeScript + Tailwind app iskeleti
- [x] Task 3: Docker Compose (Postgres+PostGIS+Redis) + EF Core DbContext + ilk boş migration

### Checkpoint: Foundation
- [x] Backend `dotnet build` ve `dotnet test` temiz geçiyor
- [x] Frontend `npm run build` temiz geçiyor
- [x] `docker compose up` ile DB ayağa kalkıyor, migration uygulanıyor

### Phase 2: Veri Kaynağı Doğrulama (Spike — kod değil, araştırma)
- [x] Task 4: Hava kalitesi / yeşil alan / trafik / ilçe sınırı için gerçek API endpoint, auth, format, lisans, güncellik doğrulaması — `docs/data-sources.md`

### Checkpoint: Data Sources
- [x] Her veri boyutu için ya doğrulanmış bir kaynak ya da açıkça işaretlenmiş bir fallback var
- [x] SPEC.md Open Question #1 ve #2 kapatıldı (insan onayı ile)

### Phase 3: Domain + Veritabanı (Dikey dilim: 3 ilçe DB'de sorgulanabilir)
- [x] Task 5: Domain entity/value object'leri (Neighborhood, DataSourceMetadata, Score) + unit testler
- [x] Task 6: EF Core migration (PostGIS geometry dahil) + 3 ilçe için seed data

### Checkpoint: Domain
- [x] 3 ilçe repository üzerinden sorgulanabiliyor (Testcontainers integration test)

### Phase 4: Ingestion (Dikey dilim: ham veri DB'de, kaynak metadata'sıyla)
- [x] Task 7: Hava kalitesi ingestion job'ı (Hangfire) + normalize + metadata
- [x] Task 8: Yeşil alan/park erişimi ingestion + mesafe hesabı (Haversine)
- [x] Task 9: Trafik yoğunluğu ingestion (statik fallback — Ocak 2025, StaticSnapshot/Historical)

### Checkpoint: Ingestion
- [x] 3 ilçe için 3 boyutta ham veri + SourceName/SourceUrl/SourceLicense/FetchedAt/PublishedAt/LastSuccessfulSyncAt/Cadence/DataFreshnessStatus DB'de
- [x] Testler gerçek dış API'ye değil, sahte/mock kaynağa karşı çalışıyor

### Phase 5: Scoring + API (Dikey dilim: bir mahallenin tam skoru API'den alınabiliyor)
- [x] Task 10: Scoring servisi (0-100 formülleri, eksik/bayat veri davranışı) + unit testler
- [x] Task 11: `GET /api/neighborhoods`, `GET /api/neighborhoods/{id}/score` + Swagger/Scalar
- [x] Task 12: `GET /api/neighborhoods/compare?a=&b=`

### Checkpoint: Backend API
- [x] Swagger üzerinden 3 mahallenin gerçek skorları görüntülenebiliyor
- [x] Integration testler (WebApplicationFactory) geçiyor

### Phase 6: Frontend (Dikey dilim: kullanıcı arayabiliyor ve karşılaştırabiliyor)
- [x] Task 13: API client + TanStack Query hook'ları (mocked API ile component testleri)
- [x] Task 14: Mahalle arama/seçme + tekil skor kartı sayfası
- [x] Task 15: İki mahalle karşılaştırma görünümü + MapLibre harita (mahalle sınırları)
- [x] Task 16: Playwright e2e — arama → iki mahalle seç → karşılaştırmayı gör

### Checkpoint: End-to-End
- [x] Backend+frontend birlikte çalışırken tam kullanıcı akışı manuel doğrulanıyor

### Phase 7: Yayına Hazırlık
- [x] Task 17: "Bayat veri" uyarı rozeti (7 günden eski kaynak için)
- [x] Task 18: README (kurulum, docker-compose ile 5 dakikada çalıştırma), LICENSE (MIT)
- [x] Task 19: CI (GitHub Actions): dotnet build/test, npm build/test/lint

### Checkpoint: Complete
- [x] SPEC.md'deki tüm Success Criteria karşılanıyor
- [x] Proje GitHub'da açık kaynak olarak yayına hazır

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Hava kalitesi/yeşil alan/trafik için varsayılan API endpoint'leri gerçekte farklı/erişilemez çıkabilir | High | Task 4 (spike) ingestion kodundan önce; her kaynak Infrastructure'da arayüz arkasında, kaynak değişimi Application/Domain'i etkilemez |
| Trafik veri setinin güncelliği/gerçek-zamanlılığı garanti değil (SPEC Open Question #2) | Medium | Task 4'te karar: canlı entegrasyon veya açıkça etiketlenmiş statik fallback |
| Mahalle sınır geometrileri temiz formatta bulunamayabilir | Medium | OSM (Nominatim/Overpass) sınırlarına fallback — 3 sabit mahalle için düşük risk |
| PostGIS/NetTopologySuite coğrafi hesapları için ekip deneyimi sınırlı olabilir | Medium | Geo mantığı Infrastructure'da izole, bilinen koordinatlarla unit test |
| Kapsam kayması (profil ağırlıklandırma, amenities skoru MVP'ye sızması) | Medium | SPEC.md "Ask first" sınırı + her checkpoint'te insan onayı |

## Open Questions

- Barındırma/altyapı seçimi (Docker Compose self-host vs. bulut) — Task 19'dan (CI/deploy) önce netleşmeli.
- Repo lisansı MIT varsayıldı — Task 18'den önce onay gerekiyor.
- Arayüz dili yalnızca Türkçe varsayıldı — Task 14'ten önce onay gerekiyor.
- ~~"SemtSkoru" adı taslak — GitHub'da yayınlamadan önce isim/alan adı kontrolü gerekiyor.~~ **Çözüldü (Task 18).** İki çakışma bulundu, isim **SemtSkoru** oldu (bkz. SPEC.md).

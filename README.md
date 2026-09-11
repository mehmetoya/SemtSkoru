# SemtSkoru

İstanbul'da bir ilçenin ulaşım, hava kalitesi ve yeşil alan koşullarını açık belediye verisiyle 0-100 arası skorlara çevirip iki ilçeyi yan yana karşılaştırmanı sağlayan açık kaynak bir web uygulaması.

"Nereye taşınmalıyım, hangi ilçe daha uygun?" sorusuna, uydurma değil gerçek İBB (İstanbul Büyükşehir Belediyesi) açık verisiyle cevap verir. MVP kapsamı üç ilçeyle sınırlı: **Kadıköy, Üsküdar, Beşiktaş**.

## Özellikler

- Bir ilçenin Hava Kalitesi / Yeşil Alan / Ulaşım skorlarını ve genel skorunu görüntüleme
- İki ilçeyi yan yana karşılaştırma, sınırlarını haritada görme
- Her skor kartında verinin hangi kaynaktan geldiği ve ne zamana ait olduğu açıkça görünür
- Bir veri kaynağı beklenenden eski kaldığında ("bayat veri") veya doğası gereği canlı olmadığında ("tarihsel veri") UI'da açıkça işaretlenir — hiçbir veri "canlı" diye sunulmaz, öyle olmadığı sürece

## Mimari

```
Belediye API/CSV/GeoJSON → Hangfire ingestion job → normalize → PostgreSQL+PostGIS → Scoring API → Next.js
```

Dış veri kaynaklarına yalnızca backend'in ingestion katmanı erişir; frontend hiçbir zaman doğrudan İBB'ye istek atmaz. Her ingest edilen kayıt kaynak adı/URL/lisans/yayın tarihi/son senkronizasyon zamanı metadata'sı taşır.

**Backend** — ASP.NET Core (.NET 10), Clean Architecture (Domain → Application → Infrastructure → Api):
PostgreSQL+PostGIS, EF Core, Hangfire (zamanlanmış ingestion), NetTopologySuite (coğrafi hesaplar), Scalar (API dokümantasyonu).

**Frontend** — Next.js (App Router) + TypeScript, MapLibre GL JS (OpenStreetMap raster tile'ları), TanStack Query, Tailwind CSS.

```
/backend
  global.json                     → .NET SDK sürüm pin'i (reproducible build)
  src/
    SemtSkoru.Domain/          → Entity ve value object'ler (Neighborhood, Score, DataSourceMetadata)
    SemtSkoru.Application/     → Scoring servisi
    SemtSkoru.Infrastructure/  → EF Core, dış API client'ları, Hangfire ingestion job'ları
    SemtSkoru.Api/             → ASP.NET Core Web API, endpoint'ler
  tests/                          → xUnit (Domain/Application unit, Api Testcontainers integration)
/web
  app/                            → Next.js sayfaları (arama, ilçe detay, karşılaştırma)
  components/                     → Skor kartı, karşılaştırma tablosu, harita, bayat-veri rozeti
  lib/                            → API client, TanStack Query hook'ları
  e2e/                            → Playwright kritik yol testi
docs/
  data-sources.md                 → Her veri kaynağının canlı doğrulanmış endpoint/lisans/güncellik bilgisi
.env.example                      → Yerel Postgres şifresi şablonu (bkz. Kurulum)
.editorconfig                     → Backend/frontend genelinde tutarlı stil kuralları
SPEC.md, tasks/plan.md, tasks/todo.md → Ürün spesifikasyonu ve uygulama planı
```

> Proje, yayına hazırlanırken bulunan isim çakışmaları nedeniyle başlangıçtaki adından **SemtSkoru**'ya değiştirildi; kod tabanındaki namespace'ler de buna göre güncellendi (bkz. SPEC.md "İsim notu").

## Kurulum (yaklaşık 5 dakika)

Önkoşullar: .NET 10 SDK, Node.js 20+, Docker (Docker Desktop veya Colima).

```bash
# 1. Yerel geliştirme şifresini ayarla (.env gitignore'da; gerçek şifreler asla commit edilmez)
cp .env.example .env
# .env içindeki POSTGRES_PASSWORD değerini kendi şifrenizle değiştirin

# 2. Postgres+PostGIS ve Redis'i ayağa kaldır
docker compose up -d

# 3. Backend: connection string'i user-secrets'a kaydet (.env'deki şifreyle aynı olmalı),
#    EF Core aracını geri yükle, migration'ları uygula, API'yi çalıştır
cd backend
dotnet user-secrets set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=semtskoru;Username=semtskoru;Password=<.env'deki şifre>" \
  --project src/SemtSkoru.Api
dotnet tool restore
dotnet tool run dotnet-ef database update \
  --project src/SemtSkoru.Infrastructure --startup-project src/SemtSkoru.Api
dotnet run --project src/SemtSkoru.Api
# API: http://localhost:5169  ·  API dokümantasyonu: http://localhost:5169/scalar/v1
```

```bash
# 3. Frontend (yeni bir terminalde)
cd web
npm install
npm run dev
# Uygulama: http://localhost:3000
```

Migration'lar Kadıköy/Üsküdar/Beşiktaş'ı gerçek sınır verisiyle (OpenStreetMap) otomatik olarak seed eder. Skorlar, arka planda çalışan Hangfire ingestion job'ları (hava kalitesi günlük, yeşil alan haftalık, trafik aylık) gerçek veriyi çektikçe dolar; job'ları hemen tetiklemek isterseniz API'nin Hangfire panosundan (`/hangfire`, sadece Development ortamında) manuel çalıştırabilirsiniz.

## Testler

```bash
# Backend: 52 test (unit + Testcontainers ile gerçek Postgres'e karşı integration)
cd backend && dotnet test

# Frontend: unit/component testleri (Vitest + React Testing Library)
cd web && npm test

# Frontend: uçtan uca kritik yol testi (gerçek backend + frontend + Postgres'e karşı, Playwright)
cd web && npm run test:e2e
```

## Veri Kaynakları ve Lisansları

Her kaynak, kod yazılmadan önce gerçek bir HTTP isteğiyle doğrulandı — bkz. [`docs/data-sources.md`](docs/data-sources.md) tam detay için.

| Boyut | Kaynak | Güncellik | Lisans |
|---|---|---|---|
| Hava Kalitesi | İBB Açık Veri Portalı (canlı API) | Saatlik | İBB Açık Veri Lisansı |
| Yeşil Alan | İBB Açık Veri Portalı (GeoJSON) | Yıllık | İBB Açık Veri Lisansı |
| Trafik/Ulaşım | İBB Açık Veri Portalı (Ocak 2025 CSV) | Tarihsel — canlı değil, UI'da açıkça etiketli | İBB Açık Veri Lisansı |
| İlçe sınırları | OpenStreetMap / Nominatim | Statik (seed-time'da bir kez çekildi) | [ODbL](https://opendatacommons.org/licenses/odbl/) |

Harita verileri © [OpenStreetMap katkıda bulunanları](https://www.openstreetmap.org/copyright), ODbL lisansı altında.

## Lisans

MIT — bkz. [LICENSE](LICENSE). (Yukarıdaki açık veri kaynaklarının kendi lisansları ayrıca geçerlidir; ODbL atıf zorunluluğu için bkz. yukarısı.)

## Claude Code ile geliştirildi

Bu proje AI coding agent'larıyla (Claude Code), [addyosmani/agent-skills](https://github.com/addyosmani/agent-skills) yaşam döngüsü (spec → plan → build → test → review → ship) kullanılarak geliştirildi.

[addyosmani/agent-skills](https://github.com/addyosmani/agent-skills) içeriği (v0.6.9, MIT lisanslı, bkz. [.claude/agent-skills-LICENSE](.claude/agent-skills-LICENSE)) bu ortamın plugin/marketplace sistemini desteklememesi nedeniyle doğrudan `.claude/` dizinine vendor edildi:

- `.claude/skills/` — 25 yaşam döngüsü skill'i (spec, plan, build, test, review, ship ve daha fazlası)
- `.claude/agents/` — 4 reviewer persona (`code-reviewer`, `security-auditor`, `test-engineer`, `web-performance-auditor`)
- `.claude/commands/` — 9 slash komut (`/spec`, `/plan`, `/build`, `/test`, `/constraints`, `/review`, `/code-simplify`, `/ship`, `/webperf`)
- `.claude/references/` — skill'lerin kullandığı ortak kontrol listeleri (güvenlik, performans, erişilebilirlik, test, gözlemlenebilirlik, definition of done, orkestrasyon kalıpları)
- `.claude/hooks/session-start.sh` — her oturum başında skill-discovery meta-skill'ini yükler

Bu repoyu Claude Code'da açan herkes bunu otomatik olarak alır — kurulum adımı veya plugin onayı gerekmez.

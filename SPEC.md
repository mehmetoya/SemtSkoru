# Spec: SemtSkoru — İstanbul Mahalle Yaşam Skoru

## Objective

**Ne yapıyoruz:** İstanbul'daki bir mahallenin ulaşım, hava kalitesi, yeşil alan erişimi ve trafik yoğunluğu açısından yaşanabilirliğini açık belediye/kamu verisinden hesaplayıp 0-100 arası skorlarla gösteren, iki mahalleyi yan yana karşılaştırabilen açık kaynak bir web uygulaması.

**Neden:** Mevcut açık veri projeleri (örn. otoparkadresi.com) veriyi haritada gösterir ama bir karara dönüştürmez. SemtSkoru "Nereye taşınmalıyım / hangi mahalle daha uygun?" sorusuna doğrudan cevap verir.

**Hedef kullanıcılar:** İstanbul'da ev arayanlar, şehre yeni taşınanlar, uzaktan çalışanlar, öğrenciler/aileler, emlak profesyonelleri.

**Tek cümlelik vaat:** "İstanbul'da bir mahallenin ulaşım, hava kalitesi ve yeşil alan koşullarını tek ekranda karşılaştır."

**Kapsam dışı (v1):** Türkiye geneli (yalnızca İstanbul'da başlanacak), profil bazlı ağırlıklandırma (uzaktan çalışan/aile/öğrenci vb.), günlük yaşam/amenities skoru, kullanıcı hesapları, 3 mahalle dışına çıkmak.

## Tech Stack

**Backend** — ASP.NET Core Web API (.NET 10 — geliştirme makinesinde kurulu olan LTS sürümü; SPEC ilk taslağında .NET 8 yazılmıştı, Task 1'de düzeltildi), Clean Architecture / modüler monolit:
- PostgreSQL + PostGIS (coğrafi veri ve mesafe sorguları)
- Entity Framework Core
- Hangfire (zamanlanmış veri çekme/senkronizasyon işleri)
- Redis (skor ve mahalle sorgu önbelleği)
- NetTopologySuite (mesafe/coğrafi hesaplamalar)
- OpenTelemetry (izlenebilirlik/observability)
- Swagger / Scalar (API dokümantasyonu)

**Frontend** — Next.js (App Router) + TypeScript:
- MapLibre GL JS (OpenStreetMap tabanlı, ücretsiz harita tile'ları)
- TanStack Query (veri getirme ve önbellekleme)
- Tailwind CSS

## Commands

Backend (`/backend`):
```
Build:     dotnet build
Test:      dotnet test
Run (dev): dotnet run --project src/SemtSkoru.Api
Migration: dotnet ef migrations add <Name> --project src/SemtSkoru.Infrastructure --startup-project src/SemtSkoru.Api
```

Frontend (`/web`):
```
Install: npm install
Dev:     npm run dev
Build:   npm run build
Test:    npm test
Lint:    npm run lint --fix
```

## Project Structure

```
/backend
  src/
    SemtSkoru.Domain/          → Entity ve value object'ler (Neighborhood, Score, DataSource)
    SemtSkoru.Application/     → Use case'ler (GetNeighborhoodScore, CompareNeighborhoods, Ingest*)
    SemtSkoru.Infrastructure/  → EF Core, Postgres/PostGIS, dış API client'ları, Hangfire job'ları
    SemtSkoru.Api/             → ASP.NET Core Web API, endpoint'ler, DI wiring
  tests/
    SemtSkoru.Domain.Tests/
    SemtSkoru.Application.Tests/
    SemtSkoru.Api.IntegrationTests/
/web
  app/            → Next.js App Router sayfaları (mahalle arama, karşılaştırma, mahalle detay)
  components/
  lib/            → API client, TanStack Query hook'ları
  tests/
docs/
  SPEC.md, tasks/plan.md, tasks/todo.md
```

**Mimari kural (kritik):** Dış veri kaynakları (İBB Açık Veri Portalı vb.) hiçbir zaman doğrudan frontend'den çağrılmaz. Akış her zaman: `Belediye API/CSV/GeoJSON → Hangfire ingestion job → normalize → PostgreSQL+PostGIS → Scoring API → Next.js`.

Her ingest edilen kayıt şu metadata'yı taşır: `SourceName, SourceUrl, SourceLicense, FetchedAt, PublishedAt, LastSuccessfulSyncAt, DataFreshnessStatus`. Açık veri projelerinde asıl risk kod değil, kaynağın kapanması veya verinin bayatlamasıdır.

## Code Style

**Backend** — standart .NET konvansiyonları, nullable reference types açık, Clean Architecture bağımlılık kuralı (Domain hiçbir şeye bağımlı değil; Application yalnız Domain'e bağımlı; Infrastructure, Application arayüzlerini implemente eder; Api hepsini birleştirir):

```csharp
public sealed record NeighborhoodScore(
    string NeighborhoodId,
    int Transportation,
    int GreenSpace,
    int AirQuality,
    int Overall);

public interface INeighborhoodScoringService
{
    Task<NeighborhoodScore> GetScoreAsync(string neighborhoodId, CancellationToken ct);
}
```

**Frontend** — fonksiyonel React bileşenleri, TypeScript strict mode, sunucu verisi için her zaman TanStack Query (manuel `useEffect` fetch yok), stil için Tailwind utility class'ları:

```tsx
export function NeighborhoodScoreCard({ neighborhoodId }: { neighborhoodId: string }) {
  const { data, isLoading } = useNeighborhoodScore(neighborhoodId);
  if (isLoading) return <ScoreCardSkeleton />;
  return <ScoreCard score={data} />;
}
```

## Testing Strategy

- **Backend:** xUnit. Domain/Application mantığı (skor formülleri, mesafe hesapları) → unit test. Infrastructure/Api → Testcontainers ile gerçek Postgres'e karşı integration test.
- **Frontend:** Vitest + React Testing Library (bileşen/hook), Playwright (kritik yol: iki mahalleyi karşılaştırma akışı, e2e).
- Test pyramid ve red-green-refactor döngüsü için proje genelinde `.claude/skills/test-driven-development` esas alınır.
- Coverage eşiği henüz belirlenmedi — `/constraints` komutuyla ayrıca netleştirilecek.

## Boundaries

- **Always:** Her veri kaydında kaynak adı/lisans/son güncelleme tarihi metadata'sını tut ve bunu kullanıcıya UI'da göster; dış API'leri yalnızca backend ingestion katmanından çağır; bir veri kaynağı 7 günden uzun süredir güncellenmemişse UI'da "bayat veri" uyarısı göster.
- **Ask first:** Ücretli/limitli üçüncü taraf servis eklemeden önce (harita tile sağlayıcı, hosting, vb.); veritabanı şema değişiklikleri; 3 mahalle sınırını genişletmeden önce; v2 kapsamına (profil ağırlıklandırma, amenities skoru, Türkiye geneli) başlamadan önce.
- **Never:** Kullanıcı hesabı/PII toplama (MVP'de yok); lisans/kullanım şartı doğrulanmamış bir veri kaynağını entegre etme; kaynağın kendisi garanti etmiyorsa veriyi "canlı/gerçek zamanlı" diye sunma.

## Success Criteria

- Kullanıcı Kadıköy, Üsküdar, Beşiktaş arasından bir mahalle arayıp Ulaşım / Yeşil Alan / Hava Kalitesi skorlarını (0-100) görebilir.
- Kullanıcı iki mahalleyi yan yana karşılaştırabilir.
- Her skor kartında veri kaynağı adı ve son güncelleme tarihi görünür.
- Ingestion job'ları günde en az bir kez çalışır; veri tazeliği izlenir ve bayat veri UI'da işaretlenir.
- Proje MIT lisansıyla, README ile GitHub'da açık kaynak olarak yayına hazırdır.

## Open Questions

1. İBB Açık Veri Portalı'ndaki hava kalitesi / yeşil alan / trafik veri setlerinin tam API endpoint'leri, kimlik doğrulama, rate limit ve lisans şartları henüz doğrulanmadı. DataIngestion modülüne başlamadan önce ayrı bir araştırma (spike) görevi gerekiyor — uydurma endpoint kullanılmayacak.
2. Trafik veri setinin gerçek zamanlılığı belirsiz (kaynağın kendi açıklamasında ileride güncelleneceği belirtiliyor). "Trafik sakinliği" skorunu statik/günlük ortalama mı yapacağız, yoksa bu netleşene kadar v1 kapsamı dışında mı bırakacağız — spike sonrasında karar verilecek.
3. Barındırma/altyapı seçimi (Docker Compose ile self-host vs. bulut) Plan aşamasında netleştirilecek.
4. Repo lisansı MIT varsayıldı — onay bekliyor.
5. Arayüz dili MVP'de yalnızca Türkçe varsayıldı — onay bekliyor.
6. "SemtSkoru" adı taslak — GitHub'da yayınlamadan önce isim/alan adı çakışması kontrol edilmeli.

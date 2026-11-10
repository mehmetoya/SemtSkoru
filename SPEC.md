# Spec: SemtSkoru — İstanbul İlçe Yaşam Skoru

## Objective

**Ne yapıyoruz:** İstanbul'daki bir ilçenin ulaşım, hava kalitesi, yeşil alan erişimi, trafik yoğunluğu, otopark erişimi, sağlık hizmetlerine erişimi ve toplu taşıma erişimi açısından yaşanabilirliğini açık belediye/kamu verisinden hesaplayıp 0-100 arası skorlarla gösteren, iki ilçeyi yan yana karşılaştırabilen açık kaynak bir web uygulaması.

**Neden:** Mevcut açık veri projeleri (örn. otoparkadresi.com) veriyi haritada gösterir ama bir karara dönüştürmez. SemtSkoru "Nereye taşınmalıyım / hangi ilçe daha uygun?" sorusuna doğrudan cevap verir.

**Hedef kullanıcılar:** İstanbul'da ev arayanlar, şehre yeni taşınanlar, uzaktan çalışanlar, öğrenciler/aileler, emlak profesyonelleri.

**Tek cümlelik vaat:** "İstanbul'da bir ilçenin ulaşım, hava kalitesi, yeşil alan, otopark, sağlık erişimi ve toplu taşıma erişimi koşullarını tek ekranda karşılaştır."

**Kapsam dışı (v1):** Türkiye geneli (yalnızca İstanbul'da başlanacak), profil bazlı ağırlıklandırma (uzaktan çalışan/aile/öğrenci vb.), günlük yaşam/amenities skoru, kullanıcı hesapları.

~~3 ilçe dışına çıkmak~~ **Faz 2'de kaldırıldı.** Kullanıcı onayıyla İstanbul'un tüm 39 ilçesine genişlendi — bkz. "39 ilçe notu" aşağıda.

**39 ilçe notu (Faz 2'de eklendi):** Kadıköy/Üsküdar/Beşiktaş dışındaki 36 ilçenin sınır poligonu, aynı yöntemle (OSM/Nominatim, bir kereye mahsus çekilip migration'a gömülü) eklendi. Yeşil alan ve trafik verisi gerçekten 39 ilçenin tamamını kapsıyor (canlı doğrulandı — bkz. `docs/data-sources.md`). Hava kalitesi **kapsamıyor**: İBB'nin toplam 28 istasyonu yalnızca 18 ilçeyi kapsıyor; kalan 21 ilçe için hava kalitesi boyutu dürüstçe "Veri yok" gösteriliyor (tahmin/enterpolasyon yapılmadı — kullanıcı onayıyla).

**Otopark notu (4. boyut):** İBB'nin İSPARK (canlı) otopark API'si dördüncü bir boyut olarak eklendi: bir ilçedeki İSPARK otoparklarının ortalama boş kapasite oranı, yaşanabilirlik skoruna çevriliyor. Canlı doğrulandı (bkz. `docs/data-sources.md`): şehir genelinde ~247 otopark var, her biri kendi ilçe adıyla etiketli, ama her ilçede otopark yok — otoparkı olmayan ilçeler için bu boyut da dürüstçe "Veri yok" gösteriliyor (tahmin/enterpolasyon yapılmıyor). Bu, İSPARK'ın işlettiği otoparkların küçük bir örneklemi; cadde üstü park durumunu yansıtmıyor.

**Sağlık Erişimi notu (5. boyut):** İBB'nin "34 Dakika İstanbul Sağlık İndeksi" beşinci bir boyut olarak eklendi. Bu kaynak, önceki dört boyuttan farklı olarak **mahalle** düzeyinde yayınlanıyor (ilçe düzeyinde değil), bu yüzden ilk kez bir aggregation adımı gerekti: her ilçenin mahallelerinin sağlık erişim endeksi, mahalle nüfusuyla ağırlıklandırılarak ilçe ortalamasına indirgeniyor (nüfusu 0 olan mahalleler hesaba katılmıyor). Canlı doğrulandı (bkz. `docs/data-sources.md`): 901 mahalle, tüm 39 ilçeyi kapsıyor, hiçbir ilçe boyutsuz kalmadı. İBB, bu endeksin kendisinin 0-100 arası olduğunu belirtmiyor (yalnızca daha geniş "Yaşam Kalitesi İndeksi" 0-100 olarak tanımlanıyor); bu yüzden skor, gerçek 39 ilçe üzerinden gözlemlenen en düşük/en yüksek ilçe ortalamasının 0-100'e doğrusal ölçeklenmesiyle hesaplanıyor — tahmin edilmiş bir standart değil, dürüstçe belgelenmiş bir MVP varsayımı.

**Toplu Taşıma Erişimi notu (6. boyut):** İETT'nin şehir genelindeki otobüs durağı verisi (15.486 nokta) altıncı bir boyut olarak eklendi. Bu kaynağın ilçe alanı (`ILCEID`) sayısal bir kod ve isme güvenilir bir eşlemesi yok, bu yüzden önceki beş boyuttan farklı olarak **string eşleştirme değil, point-in-polygon** testi kullanıldı (hava kalitesi/trafiğin kullandığı aynı teknik, ~100 kat daha küçük veri hacminde). Ham durak sayısı yerine, durak sayısının ilçenin gerçek yüzölçümüne (equirectangular yaklaşıklıkla hesaplanan km²) bölünmesiyle bulunan **yoğunluk** (durak/km²) skorlanıyor — büyük bir ilçe yalnızca geniş olduğu için haksız avantaj/dezavantaj kazanmasın diye. Canlı doğrulandı (bkz. `docs/data-sources.md`): point-in-polygon eşleştirmesiyle 39/39 ilçenin tamamında en az bir durak bulundu, hiçbir ilçe boyutsuz kalmadı. Gözlemlenen yoğunluk aralığı (0.28 durak/km² Çatalca — 20.13 durak/km² Şişli) 0-100'e doğrusal olarak ölçekleniyor — sağlık erişimiyle aynı üslupta, dürüstçe belgelenmiş bir MVP varsayımı, resmi bir standart değil.

**Terminoloji notu (Task 4'te düzeltildi):** İlk taslakta Kadıköy/Üsküdar/Beşiktaş "mahalle" olarak anılıyordu; bunlar aslında birer **ilçe** (her biri onlarca resmi mahalle içerir). Task 4 araştırmasında İBB'nin hava kalitesi istasyonlarının da tam bu ilçe adlarıyla eşleştiği görüldü, bu yüzden kullanıcı onayıyla karşılaştırma birimi ilçe olarak netleştirildi. Kod tabanındaki `Neighborhood` tipi adı değiştirilmedi (İngilizce'de "neighborhood" bu ölçekte de doğal kullanım) — sadece Türkçe ürün metni düzeltildi.

**İsim notu (Task 18'de düzeltildi):** Proje başlangıçta farklı bir adla planlanmıştı, ancak yayına hazırlanırken yapılan araştırmada iki gerçek isim çakışması bulundu: `semtpusulasi.com` (İzmir bölgesi için bir gezi rehberi) ve `kentpusulasi.com` (Türkiye genelinde bir yerel işletme rehberi) — ikisi de aynı "___Pusulası" kalıbını kullanan, temaca yakın ürünler. Kullanıcı onayıyla ürün adı **SemtSkoru** olarak seçildi (çakışma yok, ürünün asıl mekaniğini — 0-100 skor — doğrudan anlatıyor). İsim değişikliğinin ardından kullanıcı, kod tabanındaki namespace'lerin, klasör adlarının ve .NET solution adının da **SemtSkoru.*** olarak güncellenmesini istedi; bu değişiklik ve geçmiş git commit'lerinin içeriği buna göre yeniden yazıldı.

## Tech Stack

**Backend** — ASP.NET Core Web API (.NET 10 — geliştirme makinesinde kurulu olan LTS sürümü; SPEC ilk taslağında .NET 8 yazılmıştı, Task 1'de düzeltildi), Clean Architecture / modüler monolit:
- PostgreSQL + PostGIS (coğrafi veri ve mesafe sorguları)
- Entity Framework Core
- Hangfire (zamanlanmış veri çekme/senkronizasyon işleri)
- NetTopologySuite (mesafe/coğrafi hesaplamalar)
- Scalar (API dokümantasyonu, .NET'in native `AddOpenApi()`'si üzerine)

(İlk taslakta ayrıca Redis (skor cache) ve OpenTelemetry planlanmıştı — hiçbiri hiç implemente edilmedi; bkz. Open Question #3'ün Redis notu.)

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
    SemtSkoru.Domain/          → Entity ve value object'ler (Neighborhood, Score, DataSourceMetadata)
    SemtSkoru.Application/     → Scoring servisi
    SemtSkoru.Infrastructure/  → EF Core, Postgres/PostGIS, dış API client'ları, Hangfire job'ları
    SemtSkoru.Api/             → ASP.NET Core Web API, endpoint'ler, DI wiring
  tests/
    SemtSkoru.Domain.Tests/
    SemtSkoru.Application.Tests/
    SemtSkoru.Api.IntegrationTests/
/web
  app/            → Next.js App Router sayfaları (ilçe arama, karşılaştırma, ilçe detay) — testler kaynak dosyalarının yanında __tests__/ altında
  components/     → Skor kartı, karşılaştırma tablosu, harita, bayat-veri rozeti
  lib/            → API client, TanStack Query hook'ları
  e2e/            → Playwright kritik yol testi
docs/
  data-sources.md → Doğrulanmış veri kaynağı endpoint/lisans/güncellik bilgisi
tasks/
  plan.md, todo.md → Uygulama planı ve görev listesi
.github/workflows/
  ci.yml          → CI (dotnet build/test, npm build/test/lint)
SPEC.md           → Bu doküman
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
- **Frontend:** Vitest + React Testing Library (bileşen/hook), Playwright (kritik yol: iki ilçeyi karşılaştırma akışı, e2e).
- Test pyramid ve red-green-refactor döngüsü için proje genelinde `.claude/skills/test-driven-development` esas alınır.
- Coverage eşiği henüz belirlenmedi — `/constraints` komutuyla ayrıca netleştirilecek.

## Boundaries

- **Always:** Her veri kaydında kaynak adı/lisans/son güncelleme tarihi metadata'sını tut ve bunu kullanıcıya UI'da göster; dış API'leri yalnızca backend ingestion katmanından çağır; bir veri kaynağı 7 günden uzun süredir güncellenmemişse UI'da "bayat veri" uyarısı göster.
- **Ask first:** Ücretli/limitli üçüncü taraf servis eklemeden önce (harita tile sağlayıcı, hosting, vb.); veritabanı şema değişiklikleri; ~~3 ilçe sınırını genişletmeden önce~~ (Faz 2'de onaylandı, bkz. yukarısı); v2 kapsamına (profil ağırlıklandırma, amenities skoru, Türkiye geneli) başlamadan önce.
- **Never:** Kullanıcı hesabı/PII toplama (MVP'de yok); lisans/kullanım şartı doğrulanmamış bir veri kaynağını entegre etme; kaynağın kendisi garanti etmiyorsa veriyi "canlı/gerçek zamanlı" diye sunma.

## Success Criteria

- Kullanıcı İstanbul'un 39 ilçesinden birini arayıp Ulaşım / Yeşil Alan / Hava Kalitesi / Otopark / Sağlık Erişimi / Toplu Taşıma Erişimi skorlarını (0-100) görebilir (hava kalitesi yalnızca istasyonu olan 18 ilçede, otopark yalnızca İSPARK tesisi olan ilçelerde; kalanında dürüstçe "Veri yok").
- Kullanıcı iki ilçeyi yan yana karşılaştırabilir.
- Her skor kartında veri kaynağı adı ve son güncelleme tarihi görünür.
- Ingestion job'ları günde en az bir kez çalışır; veri tazeliği izlenir ve bayat veri UI'da işaretlenir.
- Proje MIT lisansıyla, README ile GitHub'da açık kaynak olarak yayına hazırdır.

## Open Questions

1. ~~İBB Açık Veri Portalı'ndaki hava kalitesi / yeşil alan / trafik veri setlerinin tam API endpoint'leri...~~ **Çözüldü (Task 4).** Bkz. `docs/data-sources.md`. Hava kalitesi: canlı, kimlik doğrulama gerektirmiyor. Yeşil alan: GeoJSON, yıllık güncelleniyor. Trafik: sadece geçmiş (Ocak 2025) veri ilçe ayrımı yapabiliyor, canlı API tek bir İstanbul-geneli sayı veriyor.
2. ~~Trafik veri setinin gerçek zamanlılığı belirsiz...~~ **Çözüldü (Task 4).** Kullanıcı onayıyla: Ocak 2025 tarihsel geohash verisi kullanılacak, UI'da "canlı değil, Ocak 2025 tarihsel ortalaması" olarak açıkça etiketlenecek.
3. ~~Barındırma/altyapı seçimi (Docker Compose ile self-host vs. bulut) — henüz netleşmedi.~~ **Çözüldü (Task 21).** Bütçe olmadığından tamamen ücretsiz katmanlar: Vercel (frontend) + Render free web service (API, Docker) + Supabase free Postgres+PostGIS. Redis prod'a dahil edilmedi çünkü kod hiç kullanmıyor (bkz. Task 21 notu). Render'ın free planı boşta container'ı durdurduğundan, hem soğuk başlangıcı önlemek hem de Hangfire'ın ingestion'ının atlanmaması için 10 dakikada bir GitHub Actions "uyandırma" ping'i eklendi (`.github/workflows/keep-warm.yml`, repo public olduğundan Actions dakika maliyeti yok) — ayrı bir tetikleme endpoint'i/secret token gerekmedi, Hangfire zaten process her başladığında süresi geçmiş job'ları kendiliğinden kuyruğa alıyor. Detaylar: `docs/deployment.md`.
4. ~~Repo lisansı MIT varsayıldı — onay bekliyor.~~ **Çözüldü (Task 18).** Kullanıcı onayıyla MIT.
5. ~~Arayüz dili MVP'de yalnızca Türkçe varsayıldı — onay bekliyor.~~ **Çözüldü (fiilen Task 1-17 boyunca).** Tüm UI metni, hata mesajları ve dokümantasyon Türkçe; onaylandı (Task 18).
6. ~~Proje adı taslak — GitHub'da yayınlamadan önce isim/alan adı çakışması kontrol edilmeli.~~ **Çözüldü (Task 18).** İki gerçek çakışma bulundu (`semtpusulasi.com`, `kentpusulasi.com`); kullanıcı onayıyla ürün adı **SemtSkoru** oldu. Bkz. yukarıdaki "İsim notu".
7. Mahalle sınırları için resmi bir İBB veri seti bulunamadı (Task 4) — OpenStreetMap (Nominatim) ilçe sınırları kullanılıyor; ODbL lisansı gereği README'de OSM atıfı yapıldı (Task 18).
8. ~~3 ilçe dışına çıkılırsa hava kalitesi/trafik verisi tüm ilçeleri kapsar mı, bilinmiyordu.~~ **Çözüldü (Faz 2).** Canlı doğrulandı: yeşil alan ve trafik 39/39 ilçeyi kapsıyor; hava kalitesi yalnızca 18/39'u kapsıyor (İBB'nin 28 istasyonu var, hepsi ilçeye eşit dağılmıyor) — kalan 21 ilçe kullanıcı onayıyla dürüstçe "Veri yok" gösteriliyor, tahmin/enterpolasyon yapılmıyor.

# Yayına Alma (ücretsiz katmanlarla)

Bu proje bütçesiz — tamamen kalıcı ücretsiz katmanlar üzerinde çalışacak şekilde
tasarlandı. Üç ayrı sağlayıcı, üç ayrı sorumluluk:

| Katman | Sağlayıcı | Neden |
|---|---|---|
| Frontend (Next.js) | [Vercel](https://vercel.com) — Hobby (ücretsiz) | Next.js'in kendi platformu, sıfır konfigürasyonla App Router desteği |
| API (.NET) | [Render](https://render.com) — Free Web Service | Docker deploy'u destekliyor, kalıcı ücretsiz |
| Veritabanı (Postgres+PostGIS) | [Supabase](https://supabase.com) — Free | Yönetilen Postgres, PostGIS uzantısı dahil |

Redis burada **yok** — SPEC.md'nin ilk taslağındaki "skor cache" planı hiç implemente
edilmedi (`grep -r Redis backend/src` boş dönüyor) ve `docker-compose.yml`'deki kullanılmayan
Redis servisi de bu yüzden kaldırıldı. Bu yüzden prod için Upstash gibi ek bir ücretsiz
Redis katmanı eklemedik — olmayan bir şeyi barındırmanın anlamı yok. Redis'i gerçekten
kullanan bir cache eklenirse hem `docker-compose.yml`'e hem bu dokümana geri eklenmeli.

## 1. Supabase (veritabanı)

1. [supabase.com](https://supabase.com) üzerinde ücretsiz bir proje oluştur.
2. Dashboard'daki **Connect** butonundan **Session pooler** bağlantı dizesini al
   (`aws-N-<region>.pooler.supabase.com:5432`, kullanıcı adı `postgres.<project-ref>`).
   **Direct connection**'ı (`db.<ref>.supabase.co`) DEĞİL — canlıda gerçekten denendi:
   o host artık yalnızca IPv6 çözümleniyor, Render'ın free planı ise IPv6 çıkışını
   desteklemiyor ("Network is unreachable"). Session pooler hem IPv4 uyumlu hem de
   Supabase'in kendi dokümantasyonuna göre migration'ların/Hangfire'ın ihtiyaç duyduğu
   advisory lock, `LOCK TABLE` ve prepared statement'ları tam destekliyor — **Transaction**
   pooler (port 6543) desteklemiyor, o yüzden onu değil Session'ı seçmek önemli.
3. Supabase'in verdiği `postgresql://user:pass@host:port/db` (URI) formatını olduğu gibi
   kullanma — Npgsql/EF Core bu formatı kabul etmiyor (`Format of the initialization
   string does not conform to specification`, canlıda denendi). Anahtar=değer formatına
   çevir:

   ```
   Host=aws-N-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<password>;SSL Mode=Require
   ```

4. Migration'ı bu bağlantı dizesiyle uygula (PostGIS uzantısını migration zaten
   `CREATE EXTENSION IF NOT EXISTS postgis` ile açıyor — Supabase bunu allowlist'te
   tutuyor, ekstra yetki gerekmiyor):

   ```bash
   cd backend
   dotnet tool restore
   dotnet tool run dotnet-ef database update --connection "<yukarıdaki anahtar=değer formatı>"
   ```

## 2. Render (API)

1. Render'da yeni bir **Web Service** oluştur, bu GitHub reposunu bağla.
2. Repo kökünde `render.yaml` bir Blueprint olarak algılanır (Docker runtime, Dockerfile
   yolu `backend/Dockerfile`, context `backend/`, health check `/health`) — "New +" →
   "Blueprint" ile de kurulabilir.
3. Ortam değişkenlerini Render dashboard'undan gir (bunlar `render.yaml`'da `sync: false`
   olarak işaretli, yani repo'ya yazılmıyor — secret oldukları için elle girilmeli):
   - `ConnectionStrings__Default` → adım 1'deki Supabase bağlantı dizesi
   - `Cors__FrontendOrigin` → adım 3'teki Vercel URL'i (örn. `https://semtskoru.vercel.app`)
   - `Gemini__ApiKey` → aşağıdaki adımlarla alınan ücretsiz Gemini API anahtarı
4. Deploy sonrası servis URL'ini not al (örn. `https://semtskoru-api.onrender.com`) —
   hem Vercel'de hem GitHub Actions secret'ında kullanılacak.

**AI Semt Asistanı (Gemini) API anahtarı almak için:**

- [aistudio.google.com/apikey](https://aistudio.google.com/apikey) adresine bir Google
  hesabıyla giriş yap, **Create API key** ile ücretsiz bir anahtar oluştur (kredi kartı
  istenmez — Gemini'nin free tier'ı için ayrı bir "trial" değil, kalıcı bir ücretsiz kota).
- Render dashboard → servis → **Environment** → yeni environment variable:
  `Gemini__ApiKey` = bu anahtar (adım 3'teki gibi).
- Bu adım atlanırsa özellik kırılmaz: `POST /api/asistan` 503 döner
  (`AsistanResponseDto.status: "NotConfigured"`), uygulamanın geri kalanı etkilenmez.

Bu özellik Google Gemini'nin ücretsiz katmanını kullanır (`gemini-3.5-flash-lite`,
girdi/çıktı free tier'da "Free of charge" — bkz.
`backend/src/SemtSkoru.Infrastructure/ExternalApis/GeminiClient.cs`'deki yorumlar). Ücretsiz
katmanın güncel RPM/RPD sınırları artık ai.google.dev'de statik olarak yayınlanmıyor (yalnızca
oturum açılmış AI Studio panosunda, [aistudio.google.com/rate-limit](https://aistudio.google.com/rate-limit)
üzerinde görünüyor); `backend/src/SemtSkoru.Api/RateLimiting/RateLimitingExtensions.cs` bu
belirsizliğe karşı muhafazakâr, paylaşılan bir günlük/dakikalık bütçe (IP başına değil, tüm
uygulama için tek bir bütçe) uygular. Anahtarı ekledikten sonra o panodaki gerçek sınırları
görüp gerekirse `AiAssistantGlobalPermitLimitPerMinute`/`PerDay` sabitlerini ayarlaman gerekebilir.

**Not:** Free plan, ~15 dakika istek almayınca container'ı durduruyor; sıradaki istek
container'ı yeniden başlatıyor (ilk istekte ~30-60sn gecikme olur). Bu, günlük ingestion
job'ını (Hangfire) etkiler — bkz. aşağıdaki GitHub Actions adımı.

## 3. Vercel (frontend)

1. Vercel'de bu GitHub reposunu import et, **Root Directory**'yi `web` olarak ayarla.
2. Environment variable ekle: `NEXT_PUBLIC_API_BASE_URL` = adım 2'deki Render API URL'i.
3. Deploy sonrası Vercel URL'ini Render'daki `Cors__FrontendOrigin`'e geri yaz (adım 2.3).

**Önemli:** Vercel'in kendi GitHub App entegrasyonu bu projede **hiç yetkilendirilmedi**
(canlıda doğrulandı: `gh api repos/<owner>/<repo>/hooks` boş dönüyor — repo üzerinde
sıfır webhook var, `vercel git connect` de genel bir 400 ile başarısız oluyor). Yani
Vercel dashboard'unda "connected to GitHub" görmesen de normal — `main`'e her push,
gerçekte aşağıdaki `.github/workflows/deploy-frontend.yml` üzerinden, Vercel CLI ile
deploy oluyor. Bunu tekrar "düzeltmeye" çalışıp Vercel'in native entegrasyonuna
bağlanmayı denemek gerekmiyor; adım 4'teki secret'ı eklemek yeterli ve kalıcı.

## 4. GitHub Actions secret (frontend deploy + günlük "uyandırma")

`.github/workflows/deploy-frontend.yml`, `web/` altında bir değişiklik `main`'e her
push'landığında Vercel CLI ile (`vercel pull` → `vercel build --prod` → `vercel deploy
--prebuilt --prod`) otomatik deploy yapıyor. Gerekli tek secret bir Vercel API token'ı
(proje/org ID'leri workflow dosyasında zaten sabit — `vercel link` ile bir kere alınıp
gömüldü, secret değiller):

- [vercel.com/account/tokens](https://vercel.com/account/tokens) üzerinden yeni bir
  token oluştur (Scope: bu projenin ait olduğu takım/hesap).
- Repo Settings → Secrets and variables → Actions → yeni secret: `VERCEL_TOKEN` = bu token.

Ayrıca, günlük "uyandırma" için `.github/workflows/daily-wake.yml` her gün Render API'sinin `/health` endpoint'ine bir
istek atıyor. Bu, uykudaki container'ı uyandırıyor; Hangfire'ın recurring-job scheduler'ı
(process her başladığında) hava kalitesi/yeşil alan/trafik job'larının süresi geçmiş
olanlarını otomatik kuyruğa alıyor — yani ayrı bir "ingestion tetikle" endpoint'i veya
token yönetimi gerekmiyor, sadece container'ın günde bir kez ayağa kalkması yeterli.

Repo Settings → Secrets and variables → Actions → yeni secret:

- `API_BASE_URL` = Render API URL'i (örn. `https://semtskoru-api.onrender.com`, sonunda `/` olmadan)

## Sıra

Supabase → Render → Vercel → GitHub secret. Render'ın CORS origin'i Vercel URL'ine,
Vercel'in API URL'i Render URL'ine bağlı olduğu için ilk deploy'da her ikisini de
geçici/yanlış girip URL'ler netleşince güncellemek normal.

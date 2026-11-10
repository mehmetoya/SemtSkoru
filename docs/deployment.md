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

   Secret olmayan bir ayar daha var: `Ingestion__IbbJobsEnabled` = `false`. Bu, `render.yaml`'da
   değeriyle birlikte duruyor; ama Blueprint dashboard'la senkron değilse (servis Blueprint'ten
   değil elle oluşturulduysa) Render bunu kendiliğinden almaz, aynı **Environment** ekranından elle
   eklenmesi gerekir. Neden gerektiği için bkz. aşağıdaki "Yerel İBB ingestion" bölümü.
4. Deploy sonrası servis URL'ini not al (örn. `https://semtskoru-api-s758.onrender.com`) —
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
container'ı yeniden başlatıyor (ilk istekte ~30-60sn gecikme olur). Bu, Render'da çalışan
Hangfire job'larını (artık yalnızca Gemini job'ları; İBB ingestion'ı Render'da çalışmıyor, bkz.
"Yerel İBB ingestion") etkiler — bkz. aşağıdaki GitHub Actions adımı.

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

Ayrıca, `.github/workflows/keep-warm.yml` her 10 dakikada bir Render API'sinin `/health`
endpoint'ine bir istek atıyor — repo public olduğundan bu sık ping'in GitHub Actions dakika
maliyeti yok. Amacı iki katlı: (1) container'ı Render'ın ~15 dakikalık boşta-durdurma
penceresinin içinde sürekli sıcak tutup gerçek ziyaretçilerin ~30-60sn'lik soğuk başlangıç
gecikmesi yaşamasını önlemek, (2) Hangfire'ın recurring-job scheduler'ının (process her
başladığında) Render'da kalan job'ların — AI özet (`district-summary-generation`) ve skor
geçmişi/trend (`score-snapshot`) — süresi geçmiş olanlarını otomatik kuyruğa almasını sağlamak.
İBB veri ingestion'ını **artık bu ping'ler ayakta tutmuyor**: İBB yalnızca Türkiye IP'lerini kabul
ettiği için o altı job Render'da kapalı ve Türkiye'deki bir Mac'ten çalışıyor (bkz. "Yerel İBB
ingestion").

Repo Settings → Secrets and variables → Actions → yeni secret:

- `API_BASE_URL` = Render API URL'i (örn. `https://semtskoru-api-s758.onrender.com`, sonunda `/` olmadan)

**Önemli — asıl uyandırıcı harici bir monitör:** GitHub, zamanlanmış (cron) workflow'ları
kısıtlıyor; `keep-warm.yml` "her 10 dakika" diye ayarlı olsa da pratikte birkaç saatte bir
çalışıyor (canlıda doğrulandı, 2026-11). Bu yüzden container'ı gerçekten sıcak tutan şey
ücretsiz bir uptime monitörü; `keep-warm.yml` yalnızca yedek:

1. [uptimerobot.com](https://uptimerobot.com) üzerinde ücretsiz hesap aç.
2. **New Monitor** → tür **HTTP(s)**, URL: `<API_BASE_URL>/health`, aralık **5 dakika**.
3. Kaydet. Monitör her 5 dakikada bir istek atar (Render'ın ~15 dakikalık penceresinin
   içinde), servis düşerse de e-posta ile haber verir.

**İkinci monitör — veri bayatladığında e-posta:** Ingestion job'ları (hava kalitesi,
otopark, yeşil alan, sağlık erişimi, toplu taşıma, trafik) bir çalıştırmada kaynağa
ulaşamazsa (örn. İBB sunucuları Render'ın bölgesinden zaman aşımına düşüyorsa) uyarı loglayıp
sessizce bir sonraki çalıştırmayı bekliyor; yukarıdaki `/health` monitörü bunu görmez. Bunun
için ayrı bir endpoint var: `/health/data`, her kaynağın **son başarılı senkronizasyon**
zamanına bakıyor (veri setinin kendi yayın tarihine değil — trafik Ocak 2025'te donmuş bir
kesit, sağlık indeksi 2024 tarihli, bunlar normal). Bir kaynak kendi takvimine göre gecikmişse
(günlük job'lar 2,5 gün, haftalıklar 14,5 gün, aylık trafik 62,5 gün — yani bir kaçırılmış
çalıştırma tolere ediliyor) `503` döner, her şey yolundaysa `200`.

1. UptimeRobot'ta **New Monitor** → tür **HTTP(s)**, URL: `<API_BASE_URL>/health/data`,
   aralık **5 dakika** (daha sık gerekmez; endpoint sonucu zaten 30 sn önbellekliyor).
2. Uyarı kişisi olarak e-postanı seç ve kaydet. Kaynaklardan biri gecikince monitör "down"
   olur ve e-posta gelir; tüm kaynaklar yeniden senkronize olunca kendiliğinden "up"a döner.
3. Hangi kaynağın geciktiğini görmek için aynı adresi tarayıcıda (GET) aç: JSON'da her kaynak
   için `lastSuccessfulSyncAt`, `thresholdHours` ve `status` (`Ok` / `Overdue` /
   `NeverSynced`) var. Render loglarında da her başarısız çalıştırma tek tip bir satırla
   görünüyor: `IngestionRunFailed` diye aratman yeterli.

Bu monitörü keep-warm monitörünün **yerine değil, yanına** ekle: `/health/data` veritabanına
dokunuyor ve veri bayatken bilerek `503` veriyor; container'ı uyanık tutma işi `/health`'te
kalmalı.

## 5. Yerel İBB ingestion (Türkiye'deki bir Mac'ten)

### Neden

İBB'nin sunucuları (`api.ibb.gov.tr`, `data.ibb.gov.tr`) TCP bağlantısını **yalnızca Türkiye
IP'lerinden** kabul ediyor. 2026-11-10'da birçok ülkeden yapılan TCP denemesinde ABD, Kanada,
Almanya, Hollanda, Fransa, Birleşik Krallık, Polonya, Bulgaristan, Romanya ve Singapur'dan
gelen bağlantıların hepsi zaman aşımına düştü; bağlantı yalnızca Türkiye'den kuruldu. API'nin
çalıştığı Render (Oregon, ABD) bu yüzden İBB'ye hiç ulaşamıyor: altı İBB job'ı (hava kalitesi ve
otopark günlük, yeşil alan / sağlık erişimi / toplu taşıma erişimi haftalık, trafik aylık) her
çalıştırmada zaman aşımına düşüp "skipping this run" diyerek geçiyordu ve hava kalitesi ile
otopark verisi 19 gün bayat kaldı. Altı job'ın kaynak URL'lerinin hepsi İBB host'larında; Render'da
kalması gereken İBB dışı bir ingestion job'ı yok.

Çözüm: Render API'yi servis etmeye ve Gemini job'larını (`district-summary-generation`,
`score-snapshot`) çalıştırmaya devam ediyor; İBB job'ları ise Türkiye'deki bir Mac'ten,
**aynı Supabase veritabanına doğrudan** yazacak şekilde, launchd ile otomatik çalışıyor.

- Render'da `Ingestion__IbbJobsEnabled=false` (bkz. adım 2.3 ve `render.yaml`). Bu ayar kapalıyken
  API, İBB job'larını Hangfire'a kaydetmiyor **ve** Hangfire'ın veritabanında sakladığı eski
  kayıtları da her açılışta siliyor (`RemoveIfExists`). Varsayılan `true`, yani yerel geliştirme
  ve testler eskisi gibi Hangfire ile çalışıyor.
- Mac tarafında `backend/src/SemtSkoru.Ingest` adlı küçük bir konsol uygulaması aynı job
  sınıflarını bir kez çalıştırıp çıkıyor. Hangfire sunucusu başlatmıyor (aynı veritabanındaki
  Render sunucusuyla iş kapışırdı), migration uygulamıyor (API de uygulamıyor; migration hâlâ
  adım 1.4'teki gibi elle) ve en fazla 2 veritabanı bağlantısı kullanıyor (Supabase pooler'ın
  proje genelindeki 15 bağlantı sınırının 8'i Render'da).
- Zamanlama: her gün 09:00, 13:00 ve 19:00'da (yerel saat) `--due` ile çalışıyor. `--due`, bir
  job'ı yalnızca o kaynağın veritabanındaki **son başarılı senkronizasyonu** yeterince eskiyse
  çalıştırıyor (günlük ~20 saat, haftalık ~6,5 gün, aylık ~28 gün) — `/health/data`'nın baktığı
  sinyalin aynısı, ayrı bir durum dosyası yok. Yani günün üç denemesinden ilk başarılı olan işi
  yapıyor, sonrakiler saniyeler içinde "Nothing due" deyip çıkıyor; başarısız bir çalıştırma
  ise bir sonraki saatte yeniden deneniyor. Mac uykudayken kaçan saat, uyanınca bir kez çalışıyor
  (kapalıyken kaçanlar çalışmıyor, onları bir sonraki saat telafi ediyor).
- Bir job yalnızca gerçekten en az bir ilçenin verisini yazdıysa başarılı sayılıyor; kaynağa
  ulaşılamadıysa ya da hiçbir ilçe eşleşmediyse log'a `FAILED` satırı düşüyor ve çıkış kodu 1.
- Bağlantı dizesi **yalnızca macOS Keychain'de** duruyor; repoda ya da diskte düz metin olarak
  hiçbir yerde yok. launchd'nin çalıştırdığı küçük wrapper betiği onu her çalıştırmada
  Keychain'den okuyup `ConnectionStrings__Default` olarak veriyor.
- Kurulan dosyalar `~/Library/Application Support/SemtSkoru/ingest/` altında, repo'nun içinde
  değil: macOS'un gizlilik korumaları launchd job'larının `~/Documents`'ı okumasını engelliyor.

### Kurulum

Ön koşul: .NET 10 SDK (`/usr/local/share/dotnet/dotnet`) ve bu repo'nun bir kopyası.

1. Supabase bağlantı dizesini Keychain'e ekle. En güvenlisi Render'daki `ConnectionStrings__Default`
   değerini **elle yazmadan, kopyalayıp** yapıştırmak (adım 1.3'teki anahtar=değer formatı;
   Supabase panelindeki `postgresql://...` URI biçimi de kabul ediliyor). Komut değeri iki kez
   sorar, böylece shell geçmişine ya da diske hiç yazılmaz. `-U` varsa mevcut kaydın üzerine yazar
   (`-U` olmadan, kayıt zaten varsa yeni değer **sessizce kaydedilmez**):

   ```bash
   security add-generic-password -U -s semtskoru-supabase -a "$USER" -w
   ```

   **Şifre yanlışsa tekrar tekrar deneme:** Supabase'in pooler'ı birkaç başarısız girişten sonra
   yeni bağlantıları bir süre engelliyor (`ECIRCUITBREAKER`); engel düzelmiş şifreyi de reddeder.
   Runner bu yüzden her çalıştırmada job'lardan önce **tek bir** giriş denemesi yapıp, başarısızsa
   hiçbir job'a geçmeden duruyor. Bu hatayı görürsen 10-15 dakika bekle, sonra bir kez `--list` ile
   dene.

2. Kur:

   ```bash
   scripts/local-ingestion/install.sh
   ```

   Betik runner'ı Release olarak derleyip `~/Library/Application Support/SemtSkoru/ingest/`'e
   yayınlıyor, wrapper'ı ve `~/Library/LaunchAgents/com.semtskoru.ingest.plist`'i yazıp
   launchd'ye yüklüyor, sonunda da `--list` ile (hiçbir job çalıştırmadan) veritabanı
   bağlantısını kontrol edip her job'ın son başarılı senkronizasyonunu gösteriyor. Keychain
   kaydı yoksa hiçbir şey kurmadan yukarıdaki komutu yazdırıp çıkıyor.

3. İlk veriyi hemen çekmek için (sıradaki 09:00/13:00/19:00'u beklemeden):

   ```bash
   scripts/local-ingestion/run-now.sh
   ```

4. Render'da `Ingestion__IbbJobsEnabled=false` olduğundan emin ol (adım 2.3). Bir sonraki
   deploy/yeniden başlatmada İBB job'ları Render'ın Hangfire'ından silinir.

### Güncelleme

Kodu güncelledikten sonra (`git pull`) aynı kurulum betiğini **yeniden çalıştırmak yeterli**:

```bash
scripts/local-ingestion/install.sh
```

Betik idempotent: mevcut LaunchAgent'ı durdurup runner'ı yeniden yayınlıyor, plist'i yeniden
yazıp yeniden yüklüyor. O an bir çalıştırma sürüyorsa hiçbir şeye dokunmadan çıkıyor; birkaç
dakika sonra tekrar dene. Bağlantı dizesi değişirse: `security add-generic-password -U -s
semtskoru-supabase -a "$USER" -w` (`-U` mevcut kaydı günceller).

### Log ve durum

```bash
tail -f ~/Library/Logs/SemtSkoru/ingest.log
"$HOME/Library/Application Support/SemtSkoru/ingest/run-ingest.sh" --list
launchctl print gui/$(id -u)/com.semtskoru.ingest   # launchd'nin gözünden: state, last exit code
```

Her job için bir `starting` ve bir `OK - N district reading(s) updated from M source record(s)`
ya da `FAILED after Xs - <neden>` satırı var; her çalıştırma `Done: ...` ile bitiyor. Ayrıntılı
hata (stack trace) aynı log'da `IngestionRunFailed` satırında. Veri yine de bayatlarsa
`/health/data` monitörü (bölüm 4) e-posta atar — Mac kapalı kaldıysa ya da internet yoksa ilk
bunu fark edersin.

### Elle çalıştırma

```bash
scripts/local-ingestion/run-now.sh                  # zamanlanmış çalıştırmanın aynısı (--due), arka planda, log'a
scripts/local-ingestion/run-now.sh all              # hepsini şimdi, bu terminalde (--due'yu yok sayar)
scripts/local-ingestion/run-now.sh parking traffic  # yalnızca bunları, bu terminalde
scripts/local-ingestion/run-now.sh --list           # durum, hiçbir şey çalıştırmaz
```

İlki `launchctl kickstart gui/$(id -u)/com.semtskoru.ingest` ile aynı şey. Job adları:
`air-quality`, `parking`, `health-access`, `transit-access`, `green-space`, `traffic`.

### Kaldırma

```bash
scripts/local-ingestion/uninstall.sh
```

LaunchAgent'ı durdurup plist'i ve yayınlanan runner'ı siliyor; Keychain kaydını ve log dosyasını
bırakıyor (silmek için komutları yazdırıyor). **Dikkat:** kaldırdıktan sonra İBB verisini çeken
hiçbir şey kalmaz (Render İBB'ye ulaşamıyor); hava kalitesi ve otopark birkaç gün içinde bayatlar.

## Supabase güvenlik: Row Level Security

Supabase her projede PostgREST'i (kendi otomatik REST API katmanı) varsayılan olarak açık
tutuyor — bu proje hiç kullanmasa da. `public` şemasındaki bir tabloda RLS kapalıysa,
`anon`/`authenticated` rolleri o tabloya bu API'yi hiç atlamadan, doğrudan internetten
erişebiliyor (Supabase'in kendi Security Advisor'ı bunu `ERROR` seviyesinde işaretliyor).
`EnableRowLevelSecurityOnAllTables` migration'ı (2026-09-16) bu yüzden her tabloda RLS'i
policy eklemeden açıyor — `anon`/`authenticated` için tüm erişimi reddediyor, ama bu API'nin
kendi bağlantısını (tablo sahibi rol) etkilemiyor (Postgres'te sahiplik RLS'i baypas eder).

**`spatial_ref_sys` istisnası:** PostGIS'in kendi referans-sistemleri tablosu bu migration'a
dahil değil — canlıda denendi, bu API'nin bağlandığı rol bu tablonun sahibi değil (`must be
owner of table spatial_ref_sys`, Supabase'in kendi internal rolüne ait). Bunu düzeltmek
Supabase dashboard/support tarafında bir işlem gerektiriyor, migration'la yapılamıyor —
Security Advisor'da bu tek kalem "ERROR" olarak görünmeye devam edecek.

## Supabase güvenlik: PostGIS `extension_in_public` / `st_estimatedextent` (kabul edilen sınırlama)

Security Advisor'da iki WARN kalemi var: PostGIS extension'ı `public` şemasında kurulu
(`extension_in_public`) ve PostGIS'in kendi `st_estimatedextent` fonksiyonu (SECURITY
DEFINER) `anon`/`authenticated` tarafından çağrılabilir. İkisi de denendi (2026-09-16),
ikisi de `spatial_ref_sys` ile aynı sınıfta bir Supabase platform kısıtına takıldı:

- **Extension'ı `extensions` şemasına taşıma:** PostGIS varsayılan olarak relocatable değil;
  topluluğun belgelediği tek yöntem `UPDATE pg_extension SET extrelocatable = true ...` ile
  önce bunu açmak. Bu yöntem yerelde gerçek bir Postgres superuser'ıyla (postgis/postgis:17-3.4
  container, prod'daki PostgreSQL 17.6 + PostGIS 3.3.7'ye denk) uçtan uca doğrulandı — 39
  ilçenin gerçek geometrisiyle, taşımadan önce/sonra tüm okuma/yazma/in-memory `Contains()`
  kontrolleri (bu proje SQL tarafında hiç `ST_*` çağırmıyor, sadece geometry/geography kolon
  tipi olarak kullanıyor) birebir aynı sonucu verdi, hiçbir nesne `public`'te kalmadı. Ama
  canlıda `UPDATE pg_extension ...` adımı `permission denied for table pg_extension` ile
  reddedildi — Supabase'in `postgres.<project-ref>` rolü gerçek superuser değil
  (`rolsuper = false`), ve bu tek adım Postgres'te superuser gerektiriyor.
- **`st_estimatedextent`'i `anon`/`authenticated`'dan `REVOKE EXECUTE` ile kapatma:**
  şemayı taşımadan da denendi, ama fonksiyonların sahibi `postgres.<project-ref>` değil,
  Supabase'in kendi `supabase_admin` rolü — `REVOKE` komutu hata vermeden çalışıyor ama
  `WARNING: no privileges could be revoked for "st_estimatedextent"` ile sessizce hiçbir şey
  yapmıyor (sahiplik/GRANT OPTION olmadığı için).

Sonuç: ikisi de bu projenin rolünden değil, Supabase'in proje sahiplerine verdiği yetki
seviyesinden kaynaklanıyor — dashboard/support üzerinden Supabase'in kendisi yapmadıkça
migration'la düzeltilemiyor. `spatial_ref_sys` gibi, Security Advisor'da WARN olarak kalacak;
DROP EXTENSION + yeniden kurulum tek teorik yol ama `CASCADE` 8 tablodaki tüm geometry
kolonlarını düşürür — tek instance'lı, ücretsiz katman, production bir veritabanında bunu
denemek WARN seviyesindeki bir bulgu için orantısız risk.

## Sıra

Supabase → Render → Vercel → GitHub secret. Render'ın CORS origin'i Vercel URL'ine,
Vercel'in API URL'i Render URL'ine bağlı olduğu için ilk deploy'da her ikisini de
geçici/yanlış girip URL'ler netleşince güncellemek normal.

# Order Management API

Prototype REST API untuk order management dengan fokus utama pada **concurrency handling** dan **idempotency**.

Dibangun dengan ASP.NET Core 10 + PostgreSQL.

---

## Ringkasan Keputusan Desain

Tiga masalah concurrency di soal, tiga mekanisme berbeda. Benang merahnya: **jaminan diserahkan ke database, bukan ke kode aplikasi** — kode bisa punya race window, constraint database tidak.

| Masalah | Mekanisme | Kenapa |
|---|---|---|
| Stock jadi minus (Skenario A) | Atomic conditional update — cek dan kurangi dalam satu statement SQL | Tidak ada jeda antara cek dan tulis, jadi tidak ada celah. Tanpa retry loop. |
| Status update bentrok (Skenario B) | Optimistic concurrency via kolom `Version` | Konflik jarang terjadi dan biaya gagal murah, jadi tidak perlu menahan lock di setiap request. |
| Double order saat retry (Skenario C) | Unique constraint pada idempotency key, INSERT sebelum proses | Database yang jadi wasit race, bukan pengecekan di kode yang selalu punya celah. |

Ditambah CHECK constraint `StockQuantity >= 0` sebagai lapis pertahanan terakhir, dan tiga race condition lain yang diidentifikasi sendiri (item duplikat, deadlock multi-item, double restore saat cancel) — dibahas di [bagian tersendiri](#race-condition-lain-yang-diidentifikasi).

Ketiga skenario diverifikasi oleh test yang berjalan di atas PostgreSQL asli, dengan barrier agar request benar-benar berangkat bersamaan.

---

## Daftar Isi

- [Menjalankan Project](#menjalankan-project)
- [Arsitektur](#arsitektur)
- [Pilihan Database](#pilihan-database)
- [Strategi Idempotency](#strategi-idempotency)
- [Concurrency Handling](#concurrency-handling)
- [Race Condition Lain yang Diidentifikasi](#race-condition-lain-yang-diidentifikasi)
- [Error Handling](#error-handling)
- [Logging](#logging)
- [API Endpoints](#api-endpoints)
- [Testing](#testing)
- [Catatan Keterbatasan](#catatan-keterbatasan)

---

## Menjalankan Project

### Prasyarat

- **.NET 10 SDK**
- **PostgreSQL 16+** — lewat Docker (disarankan) atau instalasi lokal
- **EF Core CLI** — install sekali dengan:
  ```bash
  dotnet tool install --global dotnet-ef
  ```
  Setelah instalasi, tutup dan buka ulang terminal agar PATH terbaca.

### Langkah

```bash
git clone https://github.com/Jordi101ok/OrderManagement.git
cd OrderManagement

docker compose up -d

dotnet ef database update -p src/OrderManagement.Infrastructure -s src/OrderManagement.Api

dotnet run --project src/OrderManagement.Api
```

Connection string default di `appsettings.json` sudah cocok dengan `docker-compose.yml`, jadi tidak ada konfigurasi yang perlu diubah. Migration membuat seluruh schema (tabel, index, check constraint) ke schema `oms`.

URL Swagger tercetak di console saat aplikasi start — tambahkan `/swagger` di belakangnya.

### Seed data (opsional, untuk mencoba API)

Jalankan `scripts/seed.sql` terhadap database yang sama. Isinya tiga produk:

| Produk | Stock | Keperluan |
|---|---|---|
| Product X | 15 | Skenario A di soal (dua order @10 unit) |
| Product Y | 100 | Pengujian umum |
| Product Z | 0 | Pengujian penolakan karena stock habis |

### Kalau ada kendala

**Port 5432 sudah dipakai.** Jika di komputer Anda sudah ada PostgreSQL lain yang berjalan, container tidak akan bisa diakses karena instalasi lokal yang menangkap koneksinya. Ubah mapping port di `docker-compose.yml` menjadi `"5433:5432"`, lalu sesuaikan `Port=5433` di `appsettings.json`, dan jalankan ulang `docker compose down && docker compose up -d`.

**Browser menolak sertifikat HTTPS.** Jalankan sekali: `dotnet dev-certs https --trust`

**Menggunakan PostgreSQL lokal, bukan Docker.** Buat database bernama `ordermanagement`, lalu sesuaikan connection string di `appsettings.json` — atau lebih aman lewat user secrets:
```bash
dotnet user-secrets set "ConnectionStrings:Default" "<connection string Anda>" --project src/OrderManagement.Api
```

> **Catatan:** development dilakukan dengan PostgreSQL di Neon (cloud), namun langkah di atas sudah diverifikasi berjalan dari hasil clone bersih menggunakan `docker-compose.yml` yang disertakan.

---

## Arsitektur

Empat project dengan dependency satu arah:

```
Api → Infrastructure → Application → Domain
```

| Project | Isi |
|---|---|
| **Domain** | Entity, enum, aturan transisi status. Tidak mereferensikan apa pun. |
| **Application** | Service, DTO, validator, interface repository, exception. |
| **Infrastructure** | DbContext, migration, implementasi repository. |
| **Api** | Controller, middleware, komposisi DI. |

Interface dideklarasikan di **Application**, implementasinya di **Infrastructure**. Application menentukan kontrak yang dia butuhkan, Infrastructure yang menyesuaikan — bukan sebaliknya.

Konsekuensi praktis: aturan bisnis di Domain tidak bisa menyentuh EF Core atau HTTP karena compiler menolaknya. Batasan antar lapis dijaga compiler, bukan sekadar konvensi penamaan folder.

---

## Pilihan Database

**PostgreSQL.**

Alasannya langsung terkait fokus soal ini:

- **Row-level locking** — dua request yang menyentuh produk berbeda bisa jalan paralel. SQLite mengunci seluruh database saat menulis, sehingga test concurrency akan "lulus" bukan karena desainnya benar, tapi karena database-nya memang tidak bisa paralel.
- **Atomic conditional update** dengan rows-affected yang reliable.
- **CHECK constraint** sebagai jaminan terakhir bahwa stock tidak pernah minus.
- **Unique index** sebagai wasit untuk race pada idempotency key.
- **SQLSTATE spesifik** (`23505`) sehingga unique violation bisa dibedakan dari kegagalan tulis lainnya.

Migration disertakan di `src/OrderManagement.Infrastructure/Migrations/`.

---

## Strategi Idempotency

**Unique constraint pada `IdempotencyRecords.Key`, dengan pola dua fase.**

Klien mengirim header `Idempotency-Key` pada `POST /api/orders`. Header ini opsional — tanpa key, request diproses normal.

Alurnya:

1. **INSERT record dengan state `InProgress` SEBELUM order diproses.**
   Ini kuncinya. Yang kalah race langsung tertolak unique constraint (`23505`).
2. Kalau INSERT berhasil → kita yang pertama, lanjutkan membuat order.
3. Kalau INSERT gagal karena duplikat → baca record pemenang dan bereaksi sesuai state-nya:
   - `Completed` → kembalikan response tersimpan apa adanya, termasuk status code aslinya. Header `Idempotency-Replayed: true` ditambahkan.
   - `InProgress` → pemenang masih memproses. Balas `409` dengan header `Retry-After`.
4. Setelah order berhasil dibuat, record di-update jadi `Completed` beserta response body dan status code-nya.

Diimplementasikan sebagai action filter (`IdempotencyFilter`), bukan di dalam service. Idempotency adalah urusan protokol HTTP — soal header dan replay response — bukan aturan bisnis order. Memisahkannya membuat `OrderService` tetap bisa diuji tanpa HTTP.

### Kenapa bukan pendekatan lain

**Kenapa tidak cek-dulu-baru-insert?** Antara `SELECT` dan `INSERT` ada celah waktu. Dua request bisa sama-sama tidak menemukan key, lalu sama-sama membuat order. Unique constraint memindahkan pengecekan ke dalam operasi tulis itu sendiri, sehingga tidak ada celah.

**Kenapa tidak distributed lock (Redis dll)?** Menambah dependency eksternal dan titik kegagalan baru, padahal database sudah menyediakan jaminan yang persis dibutuhkan.

**Kenapa menyimpan response body?** Agar request ulang mendapat jawaban yang identik dengan request pertama. Mengembalikan `200 OK` kosong untuk request kedua secara teknis "tidak membuat order ganda", tapi memaksa klien menangani dua bentuk respons berbeda untuk operasi yang sama.

**Kenapa ada `RequestHash`?** Untuk mendeteksi key yang sama dipakai dengan payload berbeda. Itu bug di sisi klien, dan mengembalikan order lama yang tidak nyambung akan menyembunyikannya. Kasus ini dibalas `422`.

---

## Concurrency Handling

Tiap skenario mendapat mekanisme yang sesuai sifat masalahnya.

### Skenario A — Concurrent Stock Deduction

**Atomic conditional update.**

```sql
UPDATE oms."Products"
SET "StockQuantity" = "StockQuantity" - @qty
WHERE "Id" = @id AND "StockQuantity" >= @qty
```

Rows affected = 0 berarti stock tidak cukup → `409 INSUFFICIENT_STOCK`.

Pendekatan naif yang dihindari:

```csharp
var product = await db.Products.FindAsync(id);   // baca: 15
if (product.StockQuantity >= 10)                 // ← celah di sini
    product.StockQuantity -= 10;
await db.SaveChangesAsync();
```

Antara baca dan simpan ada jeda. Dua request bisa sama-sama membaca 15, sama-sama lolos pengecekan, dan hasilnya stock jadi -5.

Dengan satu statement, pengecekan dan pengurangan tidak bisa dipisahkan. Postgres mengunci baris selama statement berjalan, jadi request kedua menunggu. Saat gilirannya tiba, stock sudah 5, kondisi tidak terpenuhi, ordernya ditolak.

Hasil untuk kasus di soal (stock 15, dua order @10): satu berhasil, satu ditolak, total terdeduksi 10. Tidak pernah > 15.

**Lapis pertahanan kedua:** CHECK constraint `ck_products_stock_non_negative` di level tabel. Bahkan kalau ada bug di kode atau seseorang menjalankan UPDATE manual, database menolak stock negatif.

**Kenapa tidak pessimistic locking (`SELECT FOR UPDATE`)?** Butuh dua round-trip dan menahan lock lebih lama. Atomic update menyelesaikan hal yang sama dalam satu statement.

**Kenapa tidak optimistic locking untuk stock?** Optimistic cocok kalau konflik jarang. Stock produk populer adalah hot row — konflik sering, dan retry loop akan boros. Atomic update tidak butuh retry sama sekali.

### Skenario B — Concurrent Status Update

**Optimistic concurrency** dengan kolom `Version`.

`Order.Version` dikonfigurasi sebagai concurrency token, sehingga EF Core menghasilkan:

```sql
UPDATE oms."Orders" SET "Status" = @s, "Version" = @newV
WHERE "Id" = @id AND "Version" = @oldV
```

Admin yang kalah mendapati rows affected = 0, EF melempar `DbUpdateConcurrencyException`, dan middleware memetakannya ke `409 CONCURRENT_MODIFICATION` dengan pesan yang jelas.

Ditambah validasi state machine di `OrderStatusTransition` sebelum update — transisi ilegal (misalnya `Delivered → Shipped`) ditolak `409 INVALID_TRANSITION` tanpa menyentuh database.

**Kenapa optimistic di sini?** Dua admin mengupdate order yang sama persis bersamaan itu jarang. Biaya konflik murah (tinggal reload dan coba lagi), dan tidak ada lock yang ditahan. Untuk konflik yang jarang, pessimistic locking membayar ongkos di setiap request untuk masalah yang muncul sesekali.

**Kenapa kolom `Version` manual, bukan `xmin` bawaan Postgres?** `xmin` lebih otomatis — dikelola Postgres sendiri dan tidak butuh kolom tambahan — tapi terikat ke Postgres, dan namanya bentrok dengan kolom sistem saat generate migration. Kolom `Version` eksplisit lebih portable dan lebih mudah dibaca orang lain, dengan konsekuensi harus dinaikkan manual di setiap jalur update.

### Skenario C — Idempotent Create Under Race

Ditangani oleh mekanisme idempotency di atas. Unique constraint menjamin hanya satu INSERT yang lolos, bahkan ketika kedua request tiba sebelum salah satunya sempat commit.

---

## Race Condition Lain yang Diidentifikasi

Selain tiga skenario di atas, berikut race condition yang ditemukan saat menulis kode ini dan bagaimana ditanganinya.

### 1. Item duplikat dalam satu request

**Masalah:** klien mengirim `[{X, 5}, {X, 5}]`. Tanpa penanganan, kode memanggil `TryDeductStockAsync(X, 5)` dua kali dengan pengecekan terpisah. Kalau stock tinggal 7, panggilan pertama lolos (sisa 2), panggilan kedua gagal — order ditolak, padahal seharusnya ditolak sejak awal, dan stock sempat berkurang sebelum rollback.

**Pencegahan:** item digabung per `ProductId` sebelum diproses, sehingga menjadi `{X, 10}` dan pengecekannya dilakukan sekali dengan angka yang benar.

```csharp
var merged = request.Items
    .GroupBy(i => i.ProductId)
    .Select(g => new OrderItemRequest(g.Key, g.Sum(x => x.Quantity)))
```

### 2. Deadlock antar order multi-item

**Masalah:** order A memesan produk [1, 2], order B memesan [2, 1]. Kalau diproses sesuai urutan yang dikirim klien, A mengunci baris 1 lalu menunggu baris 2, sementara B mengunci baris 2 lalu menunggu baris 1. Keduanya saling tunggu sampai Postgres memutus salah satunya dengan deadlock error.

**Pencegahan:** item diurutkan berdasarkan `ProductId` sebelum diproses, sehingga semua transaksi mengunci baris dalam urutan global yang sama. Siklus tunggu menjadi mustahil.

```csharp
.OrderBy(i => i.ProductId)
```

Ini jenis bug yang hanya muncul di produksi di bawah beban, dan sulit direproduksi setelah terjadi.

### 3. Double stock restore saat cancel bersamaan

**Masalah:** dua request cancel untuk order yang sama tiba bersamaan. Keduanya membaca status `Pending`, keduanya lolos pengecekan `CanCancel`. Kalau stock langsung dikembalikan, stock bertambah dua kali lipat dari yang seharusnya.

**Pencegahan:** urutan operasi di dalam transaksi. `SaveChangesAsync()` untuk perubahan status dijalankan **sebelum** stock dikembalikan. Request kedua kalah di pengecekan `Version`, `DbUpdateConcurrencyException` dilempar, transaksi dibatalkan, dan baris restore tidak pernah tereksekusi.

```csharp
order.Status = OrderStatus.Cancelled;
order.Version++;
await uow.SaveChangesAsync(ct);          // ← pemenang ditentukan di sini

foreach (var item in order.Items)         // hanya jalan kalau di atas sukses
    await products.RestoreStockAsync(item.ProductId, item.Quantity, ct);
```

Contoh bahwa urutan operasi di dalam transaksi bisa jadi pembeda antara benar dan salah, meski kodenya terlihat setara.

Diverifikasi oleh `ConcurrentStatusUpdateTests`, yang memeriksa konsistensi antara status akhir dan nilai stock — bukan hanya status code.

### 4. Idempotency record tertinggal di state `InProgress`

**Masalah:** kalau proses crash setelah INSERT record tapi sebelum order selesai dibuat, record tersangkut di `InProgress` selamanya. Semua retry dengan key itu akan terus mendapat `409`.

**Mitigasi saat ini:** klien menerima `Retry-After` dan dapat mencoba lagi dengan key baru.

**Untuk production:** perlu TTL pada record dan background job yang membersihkan record `InProgress` yang lebih tua dari ambang tertentu. Belum diimplementasikan di prototype ini — lihat [Catatan Keterbatasan](#catatan-keterbatasan).

---

## Error Handling

Semua error melewati `ExceptionHandlingMiddleware` dan dikembalikan dalam format yang sama (RFC 7807 Problem Details):

```json
{
  "type": "https://httpstatuses.io/409",
  "title": "INSUFFICIENT_STOCK",
  "status": 409,
  "detail": "Insufficient stock for product 3333... Requested 10, available 3.",
  "correlationId": "0HN7A2K3M9P1Q"
}
```

`title` berisi kode error yang stabil, terpisah dari `detail` yang berisi pesan untuk manusia. Klien bisa bereaksi berdasarkan kode, sementara pesannya bebas diubah tanpa memecahkan integrasi.

| Kondisi | Status | Kode |
|---|---|---|
| Order/produk tidak ditemukan | 404 | `NOT_FOUND` |
| Body tidak bisa di-parse | 400 | — |
| Validasi gagal | 422 | `VALIDATION_ERROR` |
| Stock tidak cukup | 409 | `INSUFFICIENT_STOCK` |
| Transisi status tidak valid | 409 | `INVALID_TRANSITION` |
| Kalah optimistic concurrency | 409 | `CONCURRENT_MODIFICATION` |
| Idempotency key sedang diproses | 409 | `IDEMPOTENCY_IN_PROGRESS` |
| Idempotency key dipakai dengan payload beda | 422 | `IDEMPOTENCY_KEY_REUSED` |
| Error tak terduga | 500 | `INTERNAL_ERROR` |

Response `500` sengaja generik. Detail exception dan stack trace hanya masuk log, tidak pernah dikirim ke klien.

Validasi input memakai FluentValidation, dengan `InvalidModelStateResponseFactory` yang dikustomisasi agar hasilnya mengikuti format yang sama seperti error lain — bukan format `ValidationProblemDetails` bawaan ASP.NET yang berbeda sendiri.

Controller tidak mengandung satu pun `try-catch`. Konsistensi format dijamin oleh middleware, bukan oleh disiplin di setiap endpoint.

---

## Logging

**Serilog** dengan output JSON terstruktur (`CompactJsonFormatter`).

Setiap request mendapat **correlation ID**:

- Diambil dari header `X-Correlation-Id` kalau klien mengirimkannya (memungkinkan penelusuran lintas service), atau digenerate kalau tidak ada.
- Dimasukkan ke `ILogger.BeginScope`, sehingga **setiap baris log** selama request itu otomatis membawanya tanpa perlu dituliskan manual.
- Dikembalikan di response header, sehingga klien bisa menyebutkannya saat melaporkan masalah.
- Disertakan di setiap response error.

Log terstruktur bisa di-query (`CorrelationId = "abc"`), bukan cuma di-grep. Untuk keluhan "tim ops sulit melakukan tracing error", inilah bedanya — satu ID membuka seluruh jejak sebuah request.

Kejadian yang dicatat: order dibuat, perubahan status, kegagalan stock deduction (beserta jumlah yang diminta dan tersedia), replay idempotency, dan seluruh exception.

---

## API Endpoints

| Method | Path | Keterangan |
|---|---|---|
| `POST` | `/api/orders` | Buat order. Mendukung header `Idempotency-Key`. |
| `GET` | `/api/orders/{id}` | Detail order. |
| `GET` | `/api/orders` | List dengan filter `status`, `customerId`, `from`, `to` + pagination. |
| `PATCH` | `/api/orders/{id}/status` | Update status (tervalidasi state machine). |
| `POST` | `/api/orders/{id}/cancel` | Cancel order, stock dikembalikan. |

### Transisi status yang valid

```
Pending   → Confirmed | Cancelled
Confirmed → Shipped   | Cancelled
Shipped   → Delivered
Delivered → (terminal)
Cancelled → (terminal)
```

### Catatan pagination

`ORDER BY "CreatedAt" DESC, "Id"` — tie-breaker pada `Id` diperlukan. Tanpanya, order yang dibuat di detik yang sama bisa muncul di urutan berbeda tiap query, sehingga ada baris yang terlewat atau terduplikasi saat klien berpindah halaman.

`PageSize` dibatasi maksimal 100 agar satu request tidak bisa menarik seluruh tabel.

---

## Testing

Test berjalan di atas **PostgreSQL asli**, bukan in-memory provider. EF Core InMemory tidak punya transaksi, unique constraint, maupun locking — test concurrency di atasnya tidak membuktikan apa pun.

### Menjalankan

Siapkan database test terpisah:

```sql
CREATE DATABASE ordermanagement_test;
```

Connection string dibaca dari environment variable `TEST_DB_CONNECTION`, dengan fallback ke `localhost`. Salin `test.runsettings.example` menjadi `test.runsettings`, sesuaikan isinya, lalu:

- **Visual Studio:** Test → Configure Run Settings → Select Solution Wide runsettings File
- **CLI:** `dotnet test --settings test.runsettings`

`test.runsettings` masuk `.gitignore` karena berisi kredensial.

Schema database test dibuat otomatis oleh `DatabaseFixture` saat test pertama dijalankan. Antar test, isi tabel di-reset dengan **Respawn** sehingga setiap test mulai dari kondisi yang sama.

### Cakupan

| File | Menguji |
|---|---|
| `ConcurrentStockDeductionTests` | Skenario A |
| `ConcurrentStatusUpdateTests` | Skenario B |
| `IdempotentCreateRaceTests` | Skenario C + replay double-click |

**`ConcurrentStockDeductionTests`** berisi dua test. Yang pertama adalah kasus persis dari soal: dua order @10 unit pada stock 15 — tepat satu berhasil, stock berakhir 5. Yang kedua lebih keras: 10 request bersamaan @3 unit pada stock 15, menuntut tepat 5 yang berhasil dan stock berakhir 0. Angka presisi ini akan meleset kalau ada race window sekecil apa pun.

**`ConcurrentStatusUpdateTests`** menjalankan dua admin bersamaan — satu confirm, satu cancel. Selain memeriksa hanya satu yang menang, test ini juga memverifikasi **konsistensi stock dengan status akhir**: kalau menang cancel, stock harus kembali; kalau menang confirm, stock tetap terpakai. Inilah yang membuktikan race condition #3 tertangani.

**`IdempotentCreateRaceTests`** berisi dua test: dua request bersamaan dengan key sama, dan dua request berurutan (kasus double-click).

### Catatan soal assertion

Pada test race idempotency, status code yang diterima request kedua sengaja tidak di-assert secara kaku. Hasilnya bergantung timing: kalau pemenang sudah sempat commit, yang kalah dapat `201` replay; kalau belum, dapat `409` in-progress. Keduanya benar.

Yang di-assert keras adalah hal yang memang harus deterministik: **jumlah order = 1** dan **stock berkurang tepat sekali**. Menuntut status code tertentu akan membuat test flaky — kadang hijau, kadang merah, tanpa ada yang berubah di kode.

### Catatan soal barrier

Semua test concurrency memakai `TaskCompletionSource` sebagai barrier, bukan sekadar `Task.WhenAll`:

```csharp
var t1 = Submit();
var t2 = Submit();
barrier.SetResult();        // baru di sini keduanya dilepas
await Task.WhenAll(t1, t2);
```

Tanpa barrier, request pertama sudah mulai berjalan sebelum baris kedua tereksekusi. Jaraknya kecil, tapi cukup untuk membuat yang pertama selesai duluan — race-nya tidak pernah benar-benar terjadi, dan test lolos tanpa menguji apa pun.

---

## Catatan Keterbatasan

Beberapa hal yang sengaja tidak dikerjakan karena ini prototype, dan akan diperlukan untuk production:

- **Cleanup idempotency record.** Perlu TTL dan background job untuk record yang tersangkut `InProgress` (lihat race condition #4).
- **Authentication & authorization.** Tidak ada sama sekali. Endpoint update status seharusnya terbatas untuk admin.
- **Rate limiting.** Tidak ada.
- **Outbox pattern.** Kalau nanti ada event yang harus dipublikasikan saat order dibuat, mengirimnya langsung di dalam transaksi berisiko — event terkirim tapi transaksi rollback, atau sebaliknya.
- **Retry policy untuk deadlock.** Pengurutan `ProductId` mencegah deadlock yang bisa diprediksi, tapi Postgres masih bisa memutus transaksi karena sebab lain. Production sebaiknya punya retry dengan backoff.
- **Metrics.** Log sudah terstruktur, tapi belum ada instrumentasi untuk latency dan error rate.
- **Unit test untuk state machine.** `OrderStatusTransition` dirancang agar bisa diuji tanpa database, tapi test-nya belum ditulis — prioritas diberikan ke test concurrency yang menjadi fokus soal.

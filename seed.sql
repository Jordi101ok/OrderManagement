-- Seed data untuk development dan demo.
-- Jalankan setelah migration: dotnet ef database update

INSERT INTO oms."Products" ("Id", "Name", "StockQuantity", "Price") VALUES
  ('11111111-1111-1111-1111-111111111111', 'Product X', 15,  50000),
  ('22222222-2222-2222-2222-222222222222', 'Product Y', 100, 25000),
  ('33333333-3333-3333-3333-333333333333', 'Product Z', 0,   75000)
ON CONFLICT ("Id") DO NOTHING;
-- Zakopane - Narty & Snowboard
UPDATE "CompanyInfos" SET "Lat" = 49.2992, "Lon" = 19.9496, "Address" = 'ul. Krupówki 15, 34-500 Zakopane'
WHERE "TenantId" = '547f5df7-a389-44b3-bcc6-090ff2fa92e5';

-- Hel - Surf & SUP
UPDATE "CompanyInfos" SET "Lat" = 54.6083, "Lon" = 18.8003, "Address" = 'ul. Nadmorska 42, 84-150 Hel'
WHERE "TenantId" = 'f1e2d3c4-b5a6-9870-fe12-dc34ba567890';

-- Kraków - BIKE RENTAL
UPDATE "CompanyInfos" SET "Lat" = 50.0614, "Lon" = 19.9372, "Address" = 'ul. Floriańska 8, 31-021 Kraków'
WHERE "TenantId" = 'a1b2c3d4-e5f6-7890-ab12-cd34ef567890';

-- Default Tenant - Warszawa (dodaj CompanyInfo jeśli nie istnieje)
INSERT INTO "CompanyInfos" ("Id", "TenantId", "Lat", "Lon", "Address", "CreatedAtUtc", "UpdatedAtUtc")
SELECT gen_random_uuid(), '440b5767-eb9d-4fcd-a256-fda7e6d074f2', 52.2297, 21.0122, 'ul. Marszałkowska 100, 00-026 Warszawa', NOW(), NOW()
WHERE NOT EXISTS (SELECT 1 FROM "CompanyInfos" WHERE "TenantId" = '440b5767-eb9d-4fcd-a256-fda7e6d074f2');

-- Pokaż wyniki
SELECT t."Name", ci."Lat", ci."Lon", ci."Address" FROM "Tenants" t LEFT JOIN "CompanyInfos" ci ON t."Id" = ci."TenantId";

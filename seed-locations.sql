-- Seed locations for existing tenants
-- First, let's see what tenants exist and update their CompanyInfos with coordinates

-- Update CompanyInfos with sample Polish city coordinates
-- Using popular ski/sport rental locations in Poland

UPDATE "CompanyInfos" ci
SET "Lat" = CASE 
    WHEN t."Name" ILIKE '%zakopane%' THEN 49.2992
    WHEN t."Name" ILIKE '%krak%' THEN 50.0647
    WHEN t."Name" ILIKE '%warszaw%' THEN 52.2297
    WHEN t."Name" ILIKE '%gdańsk%' OR t."Name" ILIKE '%gdansk%' THEN 54.3520
    WHEN t."Name" ILIKE '%poznań%' OR t."Name" ILIKE '%poznan%' THEN 52.4064
    WHEN t."Name" ILIKE '%wrocław%' OR t."Name" ILIKE '%wroclaw%' THEN 51.1079
    WHEN t."Name" ILIKE '%szczyrk%' THEN 49.7181
    WHEN t."Name" ILIKE '%białka%' OR t."Name" ILIKE '%bialka%' THEN 49.3833
    WHEN t."Name" ILIKE '%karpacz%' THEN 50.7761
    WHEN t."Name" ILIKE '%szklarska%' THEN 50.8275
    ELSE 52.0 + (RANDOM() * 2 - 1) -- Random around Poland center
END,
"Lon" = CASE 
    WHEN t."Name" ILIKE '%zakopane%' THEN 19.9496
    WHEN t."Name" ILIKE '%krak%' THEN 19.9450
    WHEN t."Name" ILIKE '%warszaw%' THEN 21.0122
    WHEN t."Name" ILIKE '%gdańsk%' OR t."Name" ILIKE '%gdansk%' THEN 18.6466
    WHEN t."Name" ILIKE '%poznań%' OR t."Name" ILIKE '%poznan%' THEN 16.9252
    WHEN t."Name" ILIKE '%wrocław%' OR t."Name" ILIKE '%wroclaw%' THEN 17.0385
    WHEN t."Name" ILIKE '%szczyrk%' THEN 19.0314
    WHEN t."Name" ILIKE '%białka%' OR t."Name" ILIKE '%bialka%' THEN 20.1000
    WHEN t."Name" ILIKE '%karpacz%' THEN 15.7608
    WHEN t."Name" ILIKE '%szklarska%' THEN 15.5286
    ELSE 19.0 + (RANDOM() * 2 - 1) -- Random around Poland center
END
FROM "Tenants" t
WHERE ci."TenantId" = t."Id"
AND (ci."Lat" IS NULL OR ci."Lat" = 0 OR ci."Lon" IS NULL OR ci."Lon" = 0);

-- For tenants without CompanyInfo, insert new records with coordinates
INSERT INTO "CompanyInfos" ("Id", "TenantId", "Lat", "Lon", "CreatedAtUtc", "UpdatedAtUtc")
SELECT 
    gen_random_uuid(),
    t."Id",
    49.2992 + (ROW_NUMBER() OVER (ORDER BY t."Id") * 0.5), -- Spread around Zakopane area
    19.9496 + (ROW_NUMBER() OVER (ORDER BY t."Id") * 0.3),
    NOW(),
    NOW()
FROM "Tenants" t
WHERE NOT EXISTS (SELECT 1 FROM "CompanyInfos" ci WHERE ci."TenantId" = t."Id");

-- Show results
SELECT t."Name", ci."Lat", ci."Lon" 
FROM "Tenants" t 
LEFT JOIN "CompanyInfos" ci ON t."Id" = ci."TenantId";

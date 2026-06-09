# Bookero vs RentSpot — konsolidacja dwóch analiz (2026-05)

> Maciej i Claude (Fable 5) napisali równolegle analizy Bookero. Ten dokument **konsoliduje obie** — wskazuje rozbieżności, łączy najlepsze fragmenty, daje jednoznaczne rekomendacje. Oryginały: `doc/BOOKERO_VS_RENTSPOT_ANALIZA.md` (Maciej, 686 linii) + `docs/competitor_analysis_bookero.md` (Claude, 473 linie).

## Konsensus — w co obaj wierzymy

Obie analizy zgodne w 5 punktach:

1. **RentSpot ≠ Bookero w bezpośredniej konkurencji.** Bookero to horyzontalny silnik rezerwacji, RentSpot to wertykal dla wypożyczalni sprzętu. Współistnienie możliwe, ale segmenty się tylko częściowo pokrywają.
2. **Główna przewaga RentSpot to głębokość operacyjna**: katalog SKU, kaucje, umowy PDF, hand-over flow z barcode, foto stanu, customer trust scoring. Bookero nic z tego nie ma.
3. **Główna przewaga Bookero to dojrzałość komercyjna**: publiczny cennik, samoobsługa, dokumentacja, plug-and-play, 9 branż obsłużonych w marketingu.
4. **Pozycjonowanie**: RentSpot = "system operacyjny dla wypożyczalni sportowej", nie "lepsze Bookero". Nie konkurujemy 1:1 ceną.
5. **Multi-tenant marketplace** + **AI asystent** to nasze unikalne argumenty których Bookero nie zreplikuje szybko.

## Rozbieżności — dwie analizy widzą inaczej

### Cennik Bookero — różne dane

| Plan | Claude (źródło: bookero.pl/cennik) | Maciej (źródło: bookero.pl publiczna oferta) |
|---|---|---|
| **Najniższy** | Basic 19,90 PLN — 1 pracownik admin | Standard 20 PLN — 1 zasób + 1 admin + 10 SMS/mc |
| **Średni** | Standard 49,90 PLN — do 3 pracowników, SMS od 0,15 | Plus 80 PLN — 5 zasobów + 5 adminów + 100 SMS/mc |
| **Najwyższy** | Premium 89,90 PLN — do 200 pracowników, karnety, QR, Calendar sync | Premium 200 PLN — 20 zasobów + 20 adminów + 300 SMS/mc |
| **Enterprise** | brak danych | indywidualnie |
| **Płatności** | brak danych w mojej analizie | BookeroPay: 1,4% + 1 PLN |

**Najprawdopodobniej Maciej ma rację dla "zasoby" + "administratorzy" jako wymiar pakietowania** (typowy SaaS rezerwacyjny). Moje dane były ze starszej strony cennika gdzie pakietowanie szło po "pracownikach" — Bookero mógł zmienić model.

**Działanie:** ktoś z nas (lub Maciej osobiście) powinien w sezonie sprzedażowym zweryfikować na żywo, najlepiej **screenshotem + datą** w `doc/bookero_pricing_check_2026-05-24.png`. Do tego czasu zakładamy że oba zakresy istnieją.

### Proponowany cennik RentSpot — duże różnice

| Plan | Claude | Maciej | Konsensus rekomendowany |
|---|---|---|---|
| **Free / Pilot** | 0 PLN 60 dni | (brak — Maciej nie sugeruje free) | **0 PLN 60 dni dla Founding Partnerów** (program zamknięty), brak self-service free trial publicznie |
| **Starter** | 39 PLN | 49–79 PLN | **59 PLN** — kompromis. 1 lokalizacja, do 50 produktów, 2 użytkowników, podstawowy checkout, AI asystent read-only |
| **Pro** | 99 PLN | 149–249 PLN | **149 PLN** — 3 lokalizacje, 500 produktów, 10 użytkowników, SMS, PDF, kody, AI write tools, voice mode |
| **Business** | 199 PLN | 399–699 PLN | **399 PLN** — multi-lokalizacja, ∞ pracowników, scoring, integracje, custom branding |
| **Enterprise** | (brak) | indywidualnie | **Tak, indywidualnie** — SLA, migracje, white-label, własna infra |

**Argument Maćka, do którego się przychylam:** RentSpot rozwiązuje **głębszy problem operacyjny** niż Bookero, więc plan wejściowy ma prawo być droższy. Nie powinniśmy konkurować ceną z Bookero Standard 20 PLN — to nasza najszybsza droga do "tani i niedopracowany" pozycjonowania. Lepiej **59 PLN i mocna komunikacja wartości**.

**Marketplace fee** (osobno od subskrypcji): 0% w Pro/Business (lock dla Founding 0% na zawsze), **2–3% w Starter** (zachęta do upgrade).

## Co Maciej widzi a Claude pominął

Konkrety które warto wziąć do roadmapy:

1. **Publiczna strona rezerwacji per-tenant** `/r/{tenantSlug}` (sekcja 13 P1 u Maćka) — Maciej argumentuje że to natywna podstrona, ja proponowałem iframe widget WordPress. **Lepsze: oba** — `/r/{slug}` jako native + iframe wrapper dla embed. Dodam do roadmap'a fazy 11c.
2. **Slot picker** — wybór godzinowy dla rentali godzinowych (kajak 1h, deska SUP 2h). Mam to częściowo (godziny w rezerwacji) ale UX picker'a wymaga dopracowania. Dodam do fazy 8a (BusinessHours).
3. **Płatność za usługę osobno od sprzętu** — w wypożyczalni czasem klient płaci za lekcję + sprzęt (np. nauka windsurfingu). Bookero to obsługuje, my mamy tylko sprzęt. Dodać jako fazę 13.
4. **Domena własna per-tenant** (`riverfun.rentspot.eu` albo `kajaki.riverfun.pl` z DNS CNAME) — Maciej wspomina jako differentator. Mam to tylko jako Enterprise tier.
5. **Bezpieczeństwo dojrzałość techniczna** — Maciej ma osobną sekcję 10 z odniesieniem do audytu Piotra. Dobrze że mamy to w `doc/security/ASVS_STATUS_UPDATE_2026-05.md` osobno.
6. **Lista "Co Bookero robi lepiej"** (sekcja 11) i **"Co RentSpot robi lepiej"** (sekcja 12) — mam u siebie ale Maciej ma bardziej zwięzłe. Warto użyć jego w pitch deck.

## Co Claude ma a Maciej pominął

1. **Battlecard 5 obiekcji** — w mojej sekcji 7 mam scenariusze: "mam już Bookero, po co zmiana", "Bookero tańszy", itp. To **gotowe do użycia w rozmowach z partnerami**. Maciej nie pisał battlecard.
2. **AI asystent jako pełen rozdział** — wymieniam 13+ tools, voice mode, forecast. To jest *unikatowa* przewaga, Maciej tylko marginalnie wspomina.
3. **TL;DR z konkretnym headlinem** ("Bookero 30%, RentSpot 100%") — czytelne, łatwe do zapamiętania.
4. **Konkretna roadmapa techniczna** w `docs/parity_roadmap_bookero.md` — entities, migracje, estimaty per faza. Maciej ma plan biznesowy ale nie techniczny.
5. **Macierz porównawcza 70+ funkcji** — bardziej granularne niż Maciej (który zostawia listy 5-10 punktów per sekcja).

## Rekomendacja — jak dalej

**Najlepsza ścieżka:** dokumenty zostają **oba** w repo. Nie konsolidujemy w jeden — każdy ma inny audytoryjny zwrot:

- **Claude doc (`competitor_analysis_bookero.md`)** = **dla sprzedaży i developerów** — granularne, z battlecard, z roadmapą techniczną
- **Maciej doc (`doc/BOOKERO_VS_RENTSPOT_ANALIZA.md`)** = **dla strategii i pozycjonowania** — szersza perspektywa, biznes plan, pakietowanie

**Ten dokument konsolidacji** zostaje jako referencja kiedy oba widzą inaczej (głównie cennik).

## Akcje do podjęcia z konsolidacji

1. ✅ **Cennik RentSpot rekomendowany**: Starter 59 PLN, Pro 149, Business 399, Enterprise custom. Free 60 dni tylko dla Founding Partnerów.
2. 🟡 **Dodać do roadmapy fazy 8-12**:
   - Faza 8a: dopracować slot picker godzinowy (rozbudowa istniejących `BusinessHours`)
   - Faza 11c: `/r/{slug}` jako native strona + iframe wrapper (nie tylko WordPress plugin)
   - **Nowa faza 13**: płatność za usługę osobno od sprzętu (np. lekcja + sprzęt = 2 line items)
3. 🟡 **Weryfikacja cennika Bookero** — screenshot + data, do `doc/bookero_pricing_check_*.png`
4. 🟢 **Pitch deck do partnerów** — sklejka z sekcji "Co Bookero robi lepiej / Co RentSpot robi lepiej" (Maciej) + battlecard (Claude)

---

*Konsolidacja: 2026-05-24, commit `8bf18b8`. Oba oryginały w repo, ten dokument służy jako bridge.*
